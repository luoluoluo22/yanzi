package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.ProgressDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.graphics.Color;
import android.net.Uri;
import android.os.AsyncTask;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import androidx.core.content.FileProvider;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;

public final class UpdateManager {
    private static final String GITHUB_RELEASES_API = "https://api.github.com/repos/luoluoluo22/yanzi/releases";
    private static final String PUBLIC_RELEASES_API = "https://sync.luoluoluo.cc.cd/downloads/android/releases.json";
    
    private static final String PREFS_NAME = "yanzi_update_prefs";
    private static final String KEY_DOWNLOADED_VERSION = "downloaded_version";
    private static final String KEY_IS_DOWNLOADING = "is_downloading";
    private static final String KEY_DOWNLOAD_STARTED_AT = "download_started_at";

    private static final java.util.concurrent.atomic.AtomicBoolean checking = new java.util.concurrent.atomic.AtomicBoolean();
    private static final java.util.concurrent.atomic.AtomicBoolean downloading = new java.util.concurrent.atomic.AtomicBoolean();
    private static final java.util.List<BusyButton> checkingButtons = new java.util.ArrayList<>();
    private static volatile boolean manualCheckRequested;
    private static volatile boolean isDownloadCanceled = false;
    private static volatile boolean pendingInstallPermission = false;
    private static final java.util.concurrent.ConcurrentHashMap<String, String> updateHashes = new java.util.concurrent.ConcurrentHashMap<>();

    public static void resumePendingInstall(Activity activity) {
        if (pendingInstallPermission && (Build.VERSION.SDK_INT < 26 || activity.getPackageManager().canRequestPackageInstalls())) {
            pendingInstallPermission = false;
            installApk(activity, new File(activity.getCacheDir(), "yanzi_update.apk"));
        }
    }

    /**
     * 异步检测新版本（公网清单与 GitHub 备用源）
     */
    public static void checkUpdate(final Activity activity, final boolean isManual) {
        checkUpdate(activity, isManual, null);
    }

    static void checkUpdate(final Activity activity, final boolean isManual, final BusyButton button) {
        if (activity == null || activity.isFinishing() || activity.isDestroyed()) return;
        if (!checking.compareAndSet(false, true)) {
            if (isManual) manualCheckRequested = true;
            if (button != null && button.begin("正在检查更新…")) checkingButtons.add(button);
            if (isManual) Toast.makeText(activity, "正在检查更新，请稍候", Toast.LENGTH_SHORT).show();
            return;
        }
        manualCheckRequested = isManual;
        if (button != null && button.begin("正在检查更新…")) checkingButtons.add(button);
        log(activity, "开始检查更新 (" + (isManual ? "手动" : "后台自动") + ")...");

        AsyncTask.THREAD_POOL_EXECUTOR.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    log(activity, "请求公网更新清单: " + PUBLIC_RELEASES_API);
                    String manifest = UpdateManifestClient.fetch(
                            new String[]{PUBLIC_RELEASES_API, GITHUB_RELEASES_API},
                            "YanziClient-Mobile/" + getLocalVersionName(activity),
                            (url, systemRoute) -> systemRoute
                                    ? (HttpURLConnection) url.openConnection()
                                    : MobileNetworkRouting.openCloudConnection(url),
                            body -> { new JSONArray(body); },
                            message -> log(activity, message));
                    JSONArray releases = new JSONArray(manifest);
                        JSONObject latestAndroidRelease = null;
                        
                        for (int i = 0; i < releases.length(); i++) {
                            JSONObject release = releases.getJSONObject(i);
                            String tagName = release.optString("tag_name", "");
                            boolean isDraft = release.optBoolean("draft", false);
                            if (tagName.startsWith("android-v") && !isDraft) {
                                latestAndroidRelease = release;
                                break;
                            }
                        }

                        if (latestAndroidRelease != null) {
                            final String tag = latestAndroidRelease.optString("tag_name", "");
                            final String latestVersion = tag.replace("android-v", "");
                            final String notes = latestAndroidRelease.optString("body", "");
                            
                            log(activity, "探测到最新 Android 版本: v" + latestVersion + " (Tag: " + tag + ")");

                            String apkDownloadUrl = "";
                            JSONArray assets = latestAndroidRelease.optJSONArray("assets");
                            if (assets != null) {
                                for (int j = 0; j < assets.length(); j++) {
                                    JSONObject asset = assets.getJSONObject(j);
                                    String assetName = asset.optString("name", "");
                                    if (assetName.endsWith(".apk") && assetName.contains("-dev.apk") == activity.getPackageName().endsWith(".dev")) {
                                        apkDownloadUrl = asset.optString("browser_download_url", "");
                                        String digest = asset.optString("digest", "");
                                        if (digest.matches("sha256:[0-9a-fA-F]{64}")) updateHashes.put(latestVersion, digest.substring(7).toLowerCase(java.util.Locale.ROOT));
                                        break;
                                    }
                                }
                            }

                            if (apkDownloadUrl.isEmpty() && activity.getPackageName().endsWith(".dev")) return;
                            if (apkDownloadUrl.isEmpty()) {
                                apkDownloadUrl = "https://github.com/luoluoluo22/yanzi/releases/download/" + tag + "/yanzi-mobile-" + latestVersion + ".apk";
                            }

                            final String finalDownloadUrl = apkDownloadUrl;
                            log(activity, "提取到 APK 下载直链: " + finalDownloadUrl);

                            new Handler(Looper.getMainLooper()).post(new Runnable() {
                                @Override
                                public void run() {
                                    if (activity.isFinishing() || activity.isDestroyed()) return;
                                    boolean manualCheck = isManual || manualCheckRequested;
                                    String currentVersion = getLocalVersionName(activity);
                                    log(activity, "版本比对: 当前本地 v" + currentVersion + " , 目标最新 v" + latestVersion);
                                    
                                    if (compareVersions(currentVersion, latestVersion) < 0) {
                                        if (isApkAlreadyDownloaded(activity, latestVersion)) {
                                            log(activity, "检测到本地已存在最新版缓存包，直接弹窗安装。");
                                            showInstallReadyDialog(activity, latestVersion);
                                        } else {
                                            if (manualCheck) {
                                                showUpdateDialog(activity, latestVersion, finalDownloadUrl, notes);
                                            } else {
                                                log(activity, "静默检查触发，开始后台静默下载。");
                                                startSilentDownload(activity, latestVersion, finalDownloadUrl);
                                            }
                                        }
                                    } else {
                                        log(activity, "当前已是最新版本，无需更新。");
                                        if (!downloading.get()) cleanCacheApk(activity);
                                        if (manualCheck) {
                                            Toast.makeText(activity, "当前已是最新版本 (" + currentVersion + ")", Toast.LENGTH_SHORT).show();
                                        }
                                    }
                                }
                            });
                        } else {
                            throw new Exception("更新清单未找到匹配的 Android 发布版");
                        }
                } catch (final Exception e) {
                    log(activity, "更新检测异常: " + e.getMessage());
                    new Handler(Looper.getMainLooper()).post(new Runnable() {
                        @Override
                        public void run() {
                            if ((isManual || manualCheckRequested) && !activity.isFinishing() && !activity.isDestroyed()) {
                                Toast.makeText(activity, "检查更新失败: " + e.getMessage(), Toast.LENGTH_SHORT).show();
                            }
                        }
                    });
                } finally {
                    new Handler(Looper.getMainLooper()).post(() -> {
                        checking.set(false);
                        manualCheckRequested = false;
                        for (BusyButton pending : checkingButtons) pending.finish();
                        checkingButtons.clear();
                    });
                }
            }
        });
    }

    private static boolean isApkAlreadyDownloaded(Context context, String latestVersion) {
        if (downloading.get()) return false;
        File apkFile = new File(context.getCacheDir(), "yanzi_update.apk");
        if (!apkFile.exists()) return false;

        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, 0);
        String downloadedVer = prefs.getString(KEY_DOWNLOADED_VERSION, "");
        if (!downloadedVer.equals(latestVersion)) return false;

        PackageInfo info = getArchivePackageInfo(context, apkFile);
        if (info == null || !latestVersion.equals(info.versionName)) {
            log(context, "已缓存更新包无法解析或版本不匹配，删除缓存后重新下载。");
            cleanCacheApk(context);
            return false;
        }

        return true;
    }

    private static void showInstallReadyDialog(final Activity activity, final String latestVersion) {
        LinearLayout dialogLayout = new LinearLayout(activity);
        dialogLayout.setOrientation(LinearLayout.VERTICAL);
        int padding = dp(activity, 20);
        dialogLayout.setPadding(padding, padding, padding, padding);
        dialogLayout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);

        TextView titleView = new TextView(activity);
        titleView.setText("更新已准备就绪");
        titleView.setTextSize(18f);
        titleView.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
        titleView.setPadding(0, 0, 0, dp(activity, 12));
        dialogLayout.addView(titleView);

        TextView notesView = new TextView(activity);
        notesView.setText("新版本 v" + latestVersion + " 已在后台安全下载完成。点击立即升级安装！");
        notesView.setTextSize(14f);
        notesView.setTextColor(ThemeConfig.COLOR_TEXT_SECONDARY);
        notesView.setPadding(0, 0, 0, dp(activity, 24));
        dialogLayout.addView(notesView);

        final AlertDialog dialog = new AlertDialog.Builder(activity, 16974545)
                .setView(dialogLayout)
                .setCancelable(true)
                .create();

        LinearLayout buttonsLayout = new LinearLayout(activity);
        buttonsLayout.setOrientation(LinearLayout.HORIZONTAL);
        buttonsLayout.setGravity(Gravity.END);

        TextView btnCancel = new TextView(activity);
        btnCancel.setText("稍后");
        btnCancel.setTextColor(ThemeConfig.COLOR_TEXT_MUTED);
        btnCancel.setTextSize(14f);
        btnCancel.setPadding(dp(activity, 16), dp(activity, 8), dp(activity, 16), dp(activity, 8));
        btnCancel.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                dialog.dismiss();
            }
        });
        buttonsLayout.addView(btnCancel);

        TextView btnInstall = new TextView(activity);
        btnInstall.setText("立即安装");
        btnInstall.setTextColor(Color.rgb(59, 130, 246));
        btnInstall.setTextSize(14f);
        btnInstall.setPadding(dp(activity, 16), dp(activity, 8), dp(activity, 16), dp(activity, 8));
        btnInstall.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                dialog.dismiss();
                File apkFile = new File(activity.getCacheDir(), "yanzi_update.apk");
                installApk(activity, apkFile);
            }
        });
        buttonsLayout.addView(btnInstall);

        dialogLayout.addView(buttonsLayout);
        dialog.show();
    }

    private static void showUpdateDialog(final Activity activity, final String latestVersion, final String downloadUrl, String notes) {
        LinearLayout dialogLayout = new LinearLayout(activity);
        dialogLayout.setOrientation(LinearLayout.VERTICAL);
        int padding = dp(activity, 20);
        dialogLayout.setPadding(padding, padding, padding, padding);
        dialogLayout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);

        TextView titleView = new TextView(activity);
        titleView.setText("发现新版本 v" + latestVersion);
        titleView.setTextSize(18f);
        titleView.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
        titleView.setPadding(0, 0, 0, dp(activity, 12));
        dialogLayout.addView(titleView);

        TextView notesView = new TextView(activity);
        notesView.setText(notes != null && !notes.trim().isEmpty() ? notes : "优化了部分系统细节，修复了一些已知问题。");
        notesView.setTextSize(14f);
        notesView.setTextColor(ThemeConfig.COLOR_TEXT_SECONDARY);
        notesView.setPadding(0, 0, 0, dp(activity, 24));
        dialogLayout.addView(notesView);

        final AlertDialog dialog = new AlertDialog.Builder(activity, 16974545)
                .setView(dialogLayout)
                .setCancelable(true)
                .create();

        LinearLayout buttonsLayout = new LinearLayout(activity);
        buttonsLayout.setOrientation(LinearLayout.HORIZONTAL);
        buttonsLayout.setGravity(Gravity.END);

        // 1. 稍后
        TextView btnCancel = new TextView(activity);
        btnCancel.setText("稍后");
        btnCancel.setTextColor(ThemeConfig.COLOR_TEXT_MUTED);
        btnCancel.setTextSize(14f);
        btnCancel.setPadding(dp(activity, 12), dp(activity, 8), dp(activity, 12), dp(activity, 8));
        btnCancel.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                dialog.dismiss();
            }
        });
        buttonsLayout.addView(btnCancel);

        // 2. 浏览器下载
        TextView btnBrowser = new TextView(activity);
        btnBrowser.setText("浏览器下载");
        btnBrowser.setTextColor(ThemeConfig.COLOR_TEXT_SECONDARY);
        btnBrowser.setTextSize(14f);
        btnBrowser.setPadding(dp(activity, 12), dp(activity, 8), dp(activity, 12), dp(activity, 8));
        btnBrowser.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                dialog.dismiss();
                log(activity, "用户选择浏览器下载更新，下载链接: " + downloadUrl);
                openInBrowser(activity, downloadUrl);
            }
        });
        buttonsLayout.addView(btnBrowser);

        // 3. 立即更新
        TextView btnUpdate = new TextView(activity);
        btnUpdate.setText("立即更新");
        btnUpdate.setTextColor(Color.rgb(59, 130, 246));
        btnUpdate.setTextSize(14f);
        btnUpdate.setPadding(dp(activity, 12), dp(activity, 8), dp(activity, 12), dp(activity, 8));
        btnUpdate.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                dialog.dismiss();
                if (downloadUrl.toLowerCase().endsWith(".apk") || downloadUrl.contains("/releases/download/")) {
                    downloadAndInstallApk(activity, latestVersion, downloadUrl);
                } else {
                    openInBrowser(activity, downloadUrl);
                }
            }
        });
        buttonsLayout.addView(btnUpdate);

        dialogLayout.addView(buttonsLayout);
        dialog.show();
    }

    /**
     * 前台下载实现 (优化了下载源的排序和 Socket 超时设置)
     */
    private static void downloadAndInstallApk(final Activity activity, final String latestVersion, final String originalDownloadUrl) {
        if (!downloading.compareAndSet(false, true)) {
            Toast.makeText(activity, "更新包正在下载，请稍候", Toast.LENGTH_SHORT).show();
            return;
        }
        isDownloadCanceled = false;
        log(activity, "用户触发立即更新，启动前台下载线程。");

        final ProgressDialog progressDialog = new ProgressDialog(activity);
        progressDialog.setProgressStyle(ProgressDialog.STYLE_HORIZONTAL);
        progressDialog.setTitle("正在下载更新");
        progressDialog.setMessage("准备下载中...");
        progressDialog.setMax(100);
        progressDialog.setCancelable(true); // 设为可取消
        progressDialog.setOnCancelListener(new DialogInterface.OnCancelListener() {
            @Override
            public void onCancel(DialogInterface dialog) {
                isDownloadCanceled = true;
                log(activity, "前台更新下载被用户手动取消。");
                Toast.makeText(activity, "下载已取消", Toast.LENGTH_SHORT).show();
            }
        });
        progressDialog.show();

        AsyncTask.THREAD_POOL_EXECUTOR.execute(new Runnable() {
            @Override
            public void run() {
                File apkFile = new File(activity.getCacheDir(), "yanzi_update.apk");
                boolean success = false;
                try { success = downloadFromNodes(activity, latestVersion, originalDownloadUrl, apkFile, progressDialog); }
                catch (Exception e) { log(activity, "更新下载异常: " + e.getMessage()); }
                final boolean finalSuccess = success;
                if (isDownloadCanceled) cleanCacheApk(activity);
                else if (success) activity.getSharedPreferences(PREFS_NAME, 0).edit()
                        .putString(KEY_DOWNLOADED_VERSION, latestVersion).apply();
                new Handler(Looper.getMainLooper()).post(new Runnable() {
                    @Override
                    public void run() {
                        try {
                        if (!activity.isFinishing() && !activity.isDestroyed()) {
                            progressDialog.dismiss();
                            if (isDownloadCanceled) {
                                cleanCacheApk(activity);
                                return;
                            }
                            if (finalSuccess) {
                                if (!apkFile.exists()) {
                                    log(activity, "更新包下载完成但无法解析，已删除坏包。");
                                    cleanCacheApk(activity);
                                    Toast.makeText(activity, "更新包校验失败，已为您跳转浏览器下载", Toast.LENGTH_LONG).show();
                                    openInBrowser(activity, originalDownloadUrl);
                                    return;
                                }

                                log(activity, "更新包下载成功并通过解析校验，正在拉起系统安装器。");
                                activity.getSharedPreferences(PREFS_NAME, 0).edit()
                                        .putString(KEY_DOWNLOADED_VERSION, getDownloadedVersionFromApk(activity, apkFile))
                                        .apply();
                                installVerifiedApk(activity, apkFile);
                            } else {
                                log(activity, "应用内更新包下载失败。");
                                Toast.makeText(activity, "下载失败，已为您跳转浏览器下载", Toast.LENGTH_LONG).show();
                                openInBrowser(activity, originalDownloadUrl);
                            }
                        }
                        } finally { downloading.set(false); }
                    }
                });
            }
        });
    }

    /**
     * 后台静默下载
     */
    private static void startSilentDownload(final Activity activity, final String latestVersion, final String downloadUrl) {
        if (!downloading.compareAndSet(false, true)) return;
        isDownloadCanceled = false;
        final SharedPreferences prefs = activity.getSharedPreferences(PREFS_NAME, 0);
        prefs.edit()
                .putBoolean(KEY_IS_DOWNLOADING, true)
                .putLong(KEY_DOWNLOAD_STARTED_AT, System.currentTimeMillis())
                .apply();
        log(activity, "后台静默更新下载启动。");

        AsyncTask.THREAD_POOL_EXECUTOR.execute(new Runnable() {
            @Override
            public void run() {
                File apkFile = new File(activity.getCacheDir(), "yanzi_update.apk");
                try {
                boolean success = downloadFromNodes(activity, latestVersion, downloadUrl, apkFile, null);

                if (success) {
                    log(activity, "静默更新包下载成功并通过解析校验。");
                    prefs.edit()
                            .putString(KEY_DOWNLOADED_VERSION, latestVersion)
                            .putBoolean(KEY_IS_DOWNLOADING, false)
                            .remove(KEY_DOWNLOAD_STARTED_AT)
                            .apply();

                    new Handler(Looper.getMainLooper()).post(new Runnable() {
                        @Override
                        public void run() {
                            if (!activity.isFinishing() && !activity.isDestroyed()) {
                                showInstallReadyDialog(activity, latestVersion);
                            }
                        }
                    });
                } else {
                    log(activity, "静默更新包下载失败。");
                }
                } catch (Exception e) { log(activity, "静默更新异常: " + e.getMessage());
                } finally {
                    prefs.edit().putBoolean(KEY_IS_DOWNLOADING, false).remove(KEY_DOWNLOAD_STARTED_AT).apply();
                    downloading.set(false);
                }
            }
        });
    }

    /** Shared foreground/background node selection and package validation. */
    private static boolean downloadFromNodes(Context context, String latestVersion, String original, File target, ProgressDialog dialog) {
        updateProgressMessage(context, dialog, "正在检测可用下载节点…");
        java.util.List<UpdateDownloadNodes.Node> nodes = UpdateDownloadNodes.select(original,
                (url, systemRoute) -> systemRoute ? (HttpURLConnection) url.openConnection() : MobileNetworkRouting.openCloudConnection(url),
                () -> isDownloadCanceled, message -> log(context, message));
        for (UpdateDownloadNodes.Node node : nodes) {
            if (isDownloadCanceled) return false;
            updateProgressMessage(context, dialog, "使用" + node.name + "下载中…");
            if (performDownloadAttempt(context, node.url, target, dialog, node.systemRoute)) {
                PackageInfo archive = getArchivePackageInfo(context, target);
                if (archive != null && latestVersion.equals(archive.versionName) && isInstallableApk(context, target)) return true;
                log(context, "节点安装包校验失败，继续尝试其他可用节点: " + node.name);
                target.delete();
            }
        }
        return false;
    }

    private static boolean performDownloadAttempt(Context context, String downloadUrl, File targetFile,
                                                  ProgressDialog progressDialog, boolean systemRoute) {
        HttpURLConnection conn = null;
        FileOutputStream out = null;
        InputStream in = null;
        File tempFile = new File(targetFile.getAbsolutePath() + ".part");
        try {
            log(context, "建立下载连接: " + downloadUrl);
            URL url = new URL(downloadUrl);
            conn = !systemRoute && downloadUrl.startsWith("https://sync.luoluoluo.cc.cd/")
                    ? MobileNetworkRouting.openCloudConnection(url) : (HttpURLConnection) url.openConnection();
            conn.setRequestProperty("User-Agent", "YanziClient-Mobile/" + getLocalVersionName(context));
            conn.setRequestProperty("X-Yanzi-Client", "mobile");
            conn.setRequestProperty("Connection", "close");
            // A stalled node must fail promptly so another validated route can take over.
            conn.setConnectTimeout(8000);
            conn.setReadTimeout(20000);
            conn.connect();

            int code = conn.getResponseCode();
            log(context, "下载连接响应状态码: " + code);
            if (code != 200) {
                log(context, "无效的状态码，连接终止");
                return false;
            }

            final int fileLength = conn.getContentLength();
            String contentType = conn.getContentType();
            log(context, "更新文件大小: " + fileLength + " 字节");
            log(context, "更新文件类型: " + contentType);
            if (contentType != null && (contentType.toLowerCase(java.util.Locale.ROOT).contains("text/") || contentType.toLowerCase(java.util.Locale.ROOT).contains("json"))) return false;
            if (fileLength > 0 && (fileLength < 1024 * 1024 || fileLength > 200 * 1024 * 1024)) {
                log(context, "下载内容过小，疑似错误页，连接终止");
                return false;
            }
            
            in = conn.getInputStream();
            if (targetFile.exists()) {
                targetFile.delete();
            }
            if (tempFile.exists()) {
                tempFile.delete();
            }
            out = new FileOutputStream(tempFile);

            byte[] data = new byte[65536];
            long total = 0;
            int lastProgress = -1;
            int count;
            while ((count = in.read(data)) != -1) {
                if (isDownloadCanceled) {
                    log(context, "下载检测到取消信号，终止数据读取。");
                    return false;
                }
                total += count;
                if (total > 200L * 1024 * 1024) return false;
                final int progress = fileLength > 0 ? (int) (total * 100 / fileLength) : 0;
                if (fileLength > 0 && progressDialog != null && progress != lastProgress) {
                    lastProgress = progress;
                    new Handler(Looper.getMainLooper()).post(new Runnable() {
                        @Override
                        public void run() {
                            progressDialog.setProgress(progress);
                        }
                    });
                }
                out.write(data, 0, count);
            }
            log(context, "数据文件读取完毕，共写入 " + total + " 字节。");
            try {
                out.flush();
                out.close();
                out = null;
            } catch (Exception ignored) {}

            if (fileLength > 0 && total != fileLength) {
                log(context, "下载文件长度不匹配，expected=" + fileLength + ", actual=" + total);
                tempFile.delete();
                return false;
            }

            if (!tempFile.renameTo(targetFile)) {
                log(context, "临时更新包重命名失败。");
                tempFile.delete();
                return false;
            }

            return true;
        } catch (Exception e) {
            log(context, "数据下载流异常: " + e.getMessage());
            return false;
        } finally {
            try {
                if (in != null) in.close();
                if (out != null) out.close();
            } catch (Exception ignored) {}
            if (!targetFile.exists() && tempFile.exists()) {
                tempFile.delete();
            }
            if (conn != null) conn.disconnect();
        }
    }

    private static void updateProgressMessage(Context context, final ProgressDialog dialog, final String message) {
        if (dialog == null) return;
        new Handler(Looper.getMainLooper()).post(new Runnable() {
            @Override
            public void run() {
                dialog.setProgress(0);
                dialog.setMessage(message);
            }
        });
    }

    private static void installApk(Activity activity, File apkFile) {
        if (apkFile == null || !apkFile.exists()) return;
        if (activity.isFinishing() || activity.isDestroyed()) return;
        if (!downloading.compareAndSet(false, true)) {
            Toast.makeText(activity, "更新包正在处理，请稍候", Toast.LENGTH_SHORT).show();
            return;
        }
        ProgressDialog preparing = ProgressDialog.show(activity, "准备安装", "正在校验安装包…", true, false);
        AsyncTask.THREAD_POOL_EXECUTOR.execute(() -> {
            boolean valid = isInstallableApk(activity, apkFile);
            new Handler(Looper.getMainLooper()).post(() -> {
                try {
                    if (activity.isFinishing() || activity.isDestroyed()) return;
                    preparing.dismiss();
                    if (valid) installVerifiedApk(activity, apkFile);
                    else {
                        cleanCacheApk(activity);
                        Toast.makeText(activity, "安装包校验失败，请重新下载最新版", Toast.LENGTH_LONG).show();
                    }
                } finally { downloading.set(false); }
            });
        });
    }

    private static void installVerifiedApk(Activity activity, File apkFile) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            if (!activity.getPackageManager().canRequestPackageInstalls()) {
                log(activity, "安装权限缺失，引导用户前往系统授权面页。");
                Toast.makeText(activity, "请授予“安装未知来源应用”权限以完成升级", Toast.LENGTH_LONG).show();
                Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES);
                pendingInstallPermission = true;
                intent.setData(Uri.parse("package:" + activity.getPackageName()));
                activity.startActivity(intent);
                return;
            }
        }

        Uri apkUri = FileProvider.getUriForFile(activity, BuildConfig.APPLICATION_ID + ".fileprovider", apkFile);
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setDataAndType(apkUri, "application/vnd.android.package-archive");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        activity.startActivity(intent);
    }

    private static void openInBrowser(Context context, String url) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
        } catch (Exception e) {
            Toast.makeText(context, "无法打开浏览器", Toast.LENGTH_SHORT).show();
        }
    }

    public static int compareVersions(String version1, String version2) {
        if (version1 == null || version2 == null) return 0;
        String[] levels1 = version1.split("\\.");
        String[] levels2 = version2.split("\\.");
        int length = Math.max(levels1.length, levels2.length);
        for (int i = 0; i < length; i++) {
            int v1 = i < levels1.length ? Integer.parseInt(levels1[i].replaceAll("\\D", "")) : 0;
            int v2 = i < levels2.length ? Integer.parseInt(levels2[i].replaceAll("\\D", "")) : 0;
            if (v1 < v2) return -1;
            if (v1 > v2) return 1;
        }
        return 0;
    }

    private static String getLocalVersionName(Context context) {
        try {
            PackageInfo pInfo = context.getPackageManager().getPackageInfo(context.getPackageName(), 0);
            return pInfo.versionName;
        } catch (Exception e) {
            return "0.1.0";
        }
    }

    private static String getDownloadedVersionFromApk(Activity activity, File apkFile) {
        PackageInfo info = getArchivePackageInfo(activity, apkFile);
        if (info != null && info.versionName != null) {
            return info.versionName;
        }

        return getLocalVersionName(activity);
    }

    private static boolean isInstallableApk(Context context, File apkFile) {
        PackageInfo info = getArchivePackageInfo(context, apkFile);
        if (info == null) return false;
        if (!context.getPackageName().equals(info.packageName)) return false;
        try {
            String expectedHash = updateHashes.get(info.versionName);
            if (expectedHash != null && !expectedHash.equals(MobileAttachmentClient.hash(apkFile))) {
                log(context, "更新包 SHA256 校验失败");
                return false;
            }
            PackageInfo installed = context.getPackageManager().getPackageInfo(context.getPackageName(), android.content.pm.PackageManager.GET_SIGNATURES);
            if (info.versionCode <= installed.versionCode || info.signatures == null || installed.signatures == null) return false;
            return java.util.Arrays.equals(info.signatures, installed.signatures);
        } catch (Exception e) { return false; }
    }

    private static PackageInfo getArchivePackageInfo(Context context, File apkFile) {
        if (context == null || apkFile == null || !apkFile.exists() || apkFile.length() <= 0) {
            return null;
        }

        try {
            return context.getPackageManager().getPackageArchiveInfo(apkFile.getAbsolutePath(), android.content.pm.PackageManager.GET_SIGNATURES);
        } catch (Exception e) {
            log(context, "安装包解析校验异常: " + e.getMessage());
            return null;
        }
    }

    private static void cleanCacheApk(Context context) {
        try {
            File apkFile = new File(context.getCacheDir(), "yanzi_update.apk");
            if (apkFile.exists()) {
                apkFile.delete();
            }
            context.getSharedPreferences(PREFS_NAME, 0).edit().clear().apply();
            File partFile = new File(context.getCacheDir(), "yanzi_update.apk.part");
            if (partFile.exists()) {
                partFile.delete();
            }
        } catch (Exception ignored) {}
    }

    private static int dp(Context context, int value) {
        float density = context.getResources().getDisplayMetrics().density;
        return (int) (value * density + 0.5f);
    }

    private static void log(Context context, String message) {
        android.util.Log.d("YanziUpdate", message);
        try {
            MobileDiagnostics.append(context, "[更新检测] " + message);
        } catch (Exception ignored) {}
    }
}
