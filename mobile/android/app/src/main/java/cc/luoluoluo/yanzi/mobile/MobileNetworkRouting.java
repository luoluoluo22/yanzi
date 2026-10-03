package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.util.Log;

import java.net.HttpURLConnection;
import java.net.URL;

final class MobileNetworkRouting {
    private static Context applicationContext;
    static void initialize(Context context) { applicationContext = context.getApplicationContext(); }
    private static final String TAG = "MobileNetworkRouting";

    private MobileNetworkRouting() {
    }

    static HttpURLConnection openLanConnection(URL url) throws Exception {
        if (url.getHost().equals("127.0.0.1") || url.getHost().equals("localhost")) return openLanPlainConnection(url);
        Context context = applicationContext != null ? applicationContext : MainActivity.sContext;
        return new SecureLanConnection(url, SecureLanConnection.load(context, LanDiscoveryManager.getLanDeviceId(context)));
    }
    static HttpURLConnection openLanPlainConnection(URL url) throws Exception {
        Context context = applicationContext != null ? applicationContext : MainActivity.sContext;
        if (context != null && !"127.0.0.1".equals(url.getHost()) && !"localhost".equals(url.getHost())) {
            ConnectivityManager manager = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (manager != null) for (Network network : manager.getAllNetworks()) {
                NetworkCapabilities caps = manager.getNetworkCapabilities(network);
                if (caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
                        && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN))
                    return (HttpURLConnection) network.openConnection(url);
            }
        }
        return (HttpURLConnection) url.openConnection();
    }

    static boolean isLanUrl(URL url) {
        String host = url.getHost();
        return host.equals("localhost") || host.equals("127.0.0.1") || host.startsWith("192.168.")
                || host.startsWith("10.") || host.matches("172\\.(1[6-9]|2[0-9]|3[01])\\..*");
    }

    static void bindLanSocket(java.net.DatagramSocket socket) throws Exception {
        Context context = applicationContext != null ? applicationContext : MainActivity.sContext;
        if (context == null) return;
        ConnectivityManager manager = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
        if (manager == null) return;
        for (Network network : manager.getAllNetworks()) {
            NetworkCapabilities caps = manager.getNetworkCapabilities(network);
            if (caps != null && caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
                    && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)) {
                network.bindSocket(socket);
                return;
            }
        }
    }

    static HttpURLConnection openCloudConnection(URL url) throws Exception {
        Network directNetwork = findPreferredDirectNetwork();
        if (directNetwork != null) {
            Log.i(TAG, "Routing cloud request outside active VPN: " + url.getPath());
            return (HttpURLConnection) directNetwork.openConnection(url);
        }
        return (HttpURLConnection) url.openConnection();
    }

    static Network findPreferredDirectNetwork() {
        Context context = applicationContext != null ? applicationContext : MainActivity.sContext;
        if (context == null) {
            return null;
        }

        try {
            ConnectivityManager manager =
                    (ConnectivityManager) context.getSystemService(
                            Context.CONNECTIVITY_SERVICE);
            if (manager == null) {
                return null;
            }

            Network activeNetwork = manager.getActiveNetwork();
            NetworkCapabilities activeCapabilities =
                    activeNetwork == null
                            ? null
                            : manager.getNetworkCapabilities(activeNetwork);

            if (activeCapabilities == null
                    || !activeCapabilities.hasTransport(
                            NetworkCapabilities.TRANSPORT_VPN)) {
                return null;
            }

            for (Network network : manager.getAllNetworks()) {
                NetworkCapabilities capabilities =
                        manager.getNetworkCapabilities(network);
                if (capabilities == null) {
                    continue;
                }

                boolean internet = capabilities.hasCapability(
                        NetworkCapabilities.NET_CAPABILITY_INTERNET);
                boolean notVpn = capabilities.hasCapability(
                        NetworkCapabilities.NET_CAPABILITY_NOT_VPN);
                boolean physical =
                        capabilities.hasTransport(
                                NetworkCapabilities.TRANSPORT_WIFI)
                                || capabilities.hasTransport(
                                        NetworkCapabilities.TRANSPORT_CELLULAR);

                if (internet && notVpn && physical) {
                    return network;
                }
            }
        }
        catch (Exception ex) {
            Log.w(
                    TAG,
                    "Unable to resolve underlying network: "
                            + ex.getMessage());
        }

        return null;
    }
}
