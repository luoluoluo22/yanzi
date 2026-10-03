package cc.luoluoluo.yanzi.mobile;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;

/** Bounded retries across independent network routes and release sources. */
final class UpdateManifestClient {
    interface Connections {
        HttpURLConnection open(URL url, boolean systemRoute) throws Exception;
    }
    interface Validator { void validate(String body) throws Exception; }
    interface Logger { void log(String message); }

    static String fetch(String[] sources, String userAgent, Connections connections,
                        Validator validator, Logger logger) throws IOException {
        IOException failure = new IOException("No update source available");
        for (String source : sources) {
            for (int route = 0; route < 2; route++) {
                HttpURLConnection connection = null;
                try {
                    logger.log("Update source: " + source + " route=" + (route == 0 ? "system" : "direct"));
                    connection = connections.open(new URL(source), route == 0);
                    connection.setConnectTimeout(10000);
                    connection.setReadTimeout(10000);
                    connection.setRequestProperty("User-Agent", userAgent);
                    connection.setRequestProperty("Accept", "application/json");
                    connection.setRequestProperty("X-Yanzi-Client", "mobile");
                    connection.setRequestProperty("Connection", "close");
                    connection.setUseCaches(false);
                    int status = connection.getResponseCode();
                    if (status != 200) throw new IOException("HTTP " + status);
                    ByteArrayOutputStream body = new ByteArrayOutputStream();
                    try (InputStream input = connection.getInputStream()) {
                        byte[] buffer = new byte[4096];
                        int count;
                        while ((count = input.read(buffer)) != -1) {
                            if (body.size() + count > 2 * 1024 * 1024)
                                throw new IOException("Update manifest exceeds size limit");
                            body.write(buffer, 0, count);
                        }
                    }
                    String result = body.toString("UTF-8");
                    validator.validate(result);
                    return result;
                } catch (Exception error) {
                    failure = new IOException("Update source failed: " + source, error);
                    logger.log("Update attempt failed: " + error.getMessage());
                } finally {
                    if (connection != null) connection.disconnect();
                }
            }
        }
        throw failure;
    }
}
