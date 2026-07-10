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
    
    // 实测在用户网络中极速且连接极其稳定的两个国内代理前缀
    private static final String ACCELERATOR_PRIMARY = "https://gh.ddlc.top/";
    private static final String ACCELERATOR_SECONDARY = "https://ghfast.top/";
    
    // 备用域名替换 (KKGitHub)
    private static final String DOMAIN_KK = "kkgithub.com";

    private static final String PREFS_NAME = "yanzi_update_prefs";
    private static final String KEY_DOWNLOADED_VERSION = "downloaded_version";
    private static final String KEY_IS_DOWNLOADING = "is_downloading";

    private static volatile boolean isDownloadCanceled = false;

    /**
     * 异步检测新版本（直接请求 GitHub API）
     */
    public static void checkUpdate(final Activity activity, final boolean isManual) {
        if (activity == null || activity.isFinishing()) return;

        log(activity, "开始检查更新 (" + (isManual ? "手动" : "后台自动") + ")...");

        AsyncTask.THREAD_POOL_EXECUTOR.execute(new Runnable() {
            @Override
            public void run() {
                HttpURLConnection conn = null;
                try {
                    log(activity, "请求 GitHub API: " + GITHUB_RELEASES_API);
                    URL url = new URL(GITHUB_RELEASES_API);
                    conn = (HttpURLConnection) url.openConnection();
                    conn.setRequestMethod("GET");
                    conn.setConnectTimeout(10000);
                    conn.setReadTimeout(10000);
                    conn.setRequestProperty("User-Agent", "YanziClient-Mobile/" + getLocalVersionName(activity));
                    conn.setRequestProperty("Accept", "application/vnd.github+json");

                    int code = conn.getResponseCode();
                    log(activity, "GitHub API 返回状态码: " + code);
                    if (code == 200) {
                        InputStream in = conn.getInputStream();
                        byte[] buffer = new byte[4096];
                        int read;
                        StringBuilder sb = new StringBuilder();
                        while ((read = in.read(buffer)) != -1) {
                            sb.append(new String(buffer, 0, read, "UTF-8"));
                        }
                        in.close();

                        JSONArray releases = new JSONArray(sb.toString());
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
                                    if (assetName.endsWith(".apk")) {
                                        apkDownloadUrl = asset.optString("browser_download_url", "");
                                        break;
                                    }
                                }
                            }

                            if (apkDownloadUrl.isEmpty()) {
                                apkDownloadUrl = "https://github.com/luoluoluo22/yanzi/releases/download/" + tag + "/yanzi-mobile-" + latestVersion + ".apk";
                            }

                            final String finalDownloadUrl = apkDownloadUrl;
                            log(activity, "提取到 APK 下载直链: " + finalDownloadUrl);

                            new Handler(Looper.getMainLooper()).post(new Runnable() {
                                @Override
                                public void run() {
                                    if (activity.isFinishing()) return;
                                    String currentVersion = getLocalVersionName(activity);
                                    log(activity, "版本比对: 当前本地 v" + currentVersion + " , 目标最新 v" + latestVersion);
                                    
                                    if (compareVersions(currentVersion, latestVersion) < 0) {
                                        if (isApkAlreadyDownloaded(activity, latestVersion)) {
                                            log(activity, "检测到本地已存在最新版缓存包，直接弹窗安装。");
                                            showInstallReadyDialog(activity, latestVersion);
                                        } else {
                                            if (isManual) {
                                                showUpdateDialog(activity, latestVersion, finalDownloadUrl, notes);
                                            } else {
                                                log(activity, "静默检查触发，开始后台静默下载。");
                                                startSilentDownload(activity, latestVersion, finalDownloadUrl);
                                            }
                                        }
                                    } else {
                                        log(activity, "当前已是最新版本，无需更新。");
                                        cleanCacheApk(activity);
                                        if (isManual) {
                                            Toast.makeText(activity, "当前已是最新版本 (" + currentVersion + ")", Toast.LENGTH_SHORT).show();
                                        }
                                    }
                                }
                            });
                        } else {
                            throw new Exception("GitHub 上未找到匹配的 Android 发发版");
                        }
                    } else {
                        throw new Exception("HTTP " + code);
                    }
                } catch (final Exception e) {
                    log(activity, "更新检测异常: " + e.getMessage());
                    new Handler(Looper.getMainLooper()).post(new Runnable() {
                        @Override
                        public void run() {
                            if (isManual && !activity.isFinishing()) {
                                Toast.makeText(activity, "检查更新失败: " + e.getMessage(), Toast.LENGTH_SHORT).show();
                            }
                        }
                    });
                } finally {
                    if (conn != null) conn.disconnect();
                }
            }
        });
    }

    private static boolean isApkAlreadyDownloaded(Context context, String latestVersion) {
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
                    downloadAndInstallApk(activity, downloadUrl);
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
    private static void downloadAndInstallApk(final Activity activity, final String originalDownloadUrl) {
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

                // 尝试 1: ddlc.top 镜像 (实测 566 kB/s，极度稳定无超时)
                if (originalDownloadUrl.contains("github.com/")) {
                    String firstUrl = ACCELERATOR_PRIMARY + originalDownloadUrl;
                    log(activity, "尝试 1: 通过 gh.ddlc.top 下载, 链接: " + firstUrl);
                    updateProgressMessage(activity, progressDialog, "使用首选高速节点下载中...");
                    success = performDownload(activity, firstUrl, apkFile, progressDialog);
                }

                // 尝试 2: ghfast.top 镜像 (实测 419 kB/s，极度稳定)
                if (!success && !isDownloadCanceled && originalDownloadUrl.contains("github.com/")) {
                    String secondUrl = ACCELERATOR_SECONDARY + originalDownloadUrl;
                    log(activity, "尝试 2: 通过 ghfast.top 下载, 链接: " + secondUrl);
                    updateProgressMessage(activity, progressDialog, "使用备用高速节点下载中...");
                    success = performDownload(activity, secondUrl, apkFile, progressDialog);
                }

                // 尝试 3: kkgithub 域名替换 (3.1 MB/s，易偶发性闪断)
                if (!success && !isDownloadCanceled && originalDownloadUrl.contains("github.com/")) {
                    String thirdUrl = originalDownloadUrl.replace("github.com", DOMAIN_KK);
                    log(activity, "尝试 3: 通过 kkgithub 替换域名下载, 链接: " + thirdUrl);
                    updateProgressMessage(activity, progressDialog, "尝试使用极速节点下载中...");
                    success = performDownload(activity, thirdUrl, apkFile, progressDialog);
                }

                // 尝试 4: 直连 (最后兜底)
                if (!success && !isDownloadCanceled) {
                    log(activity, "尝试 4: 镜像均失效，尝试原址直连, 链接: " + originalDownloadUrl);
                    updateProgressMessage(activity, progressDialog, "降级为直连官方下载...");
                    success = performDownload(activity, originalDownloadUrl, apkFile, progressDialog);
                }

                final boolean finalSuccess = success;
                new Handler(Looper.getMainLooper()).post(new Runnable() {
                    @Override
                    public void run() {
                        if (!activity.isFinishing()) {
                            progressDialog.dismiss();
                            if (isDownloadCanceled) {
                                cleanCacheApk(activity);
                                return;
                            }
                            if (finalSuccess) {
                                if (!isInstallableApk(activity, apkFile)) {
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
                                installApk(activity, apkFile);
                            } else {
                                log(activity, "应用内更新包下载失败。");
                                Toast.makeText(activity, "下载失败，已为您跳转浏览器下载", Toast.LENGTH_LONG).show();
                                openInBrowser(activity, originalDownloadUrl);
                            }
                        }
                    }
                });
            }
        });
    }

    /**
     * 后台静默下载
     */
    private static void startSilentDownload(final Activity activity, final String latestVersion, final String downloadUrl) {
        final SharedPreferences prefs = activity.getSharedPreferences(PREFS_NAME, 0);
        if (prefs.getBoolean(KEY_IS_DOWNLOADING, false)) return;

        prefs.edit().putBoolean(KEY_IS_DOWNLOADING, true).apply();
        log(activity, "后台静默更新下载启动。");

        AsyncTask.THREAD_POOL_EXECUTOR.execute(new Runnable() {
            @Override
            public void run() {
                File apkFile = new File(activity.getCacheDir(), "yanzi_update.apk");
                boolean success = false;

                // 尝试 1: ddlc.top
                if (downloadUrl.contains("github.com/")) {
                    success = performDownload(activity, ACCELERATOR_PRIMARY + downloadUrl, apkFile, null);
                }
                // 尝试 2: ghfast
                if (!success && downloadUrl.contains("github.com/")) {
                    success = performDownload(activity, ACCELERATOR_SECONDARY + downloadUrl, apkFile, null);
                }
                // 尝试 3: kkgithub
                if (!success && downloadUrl.contains("github.com/")) {
                    success = performDownload(activity, downloadUrl.replace("github.com", DOMAIN_KK), apkFile, null);
                }
                // 尝试 4: 直连
                if (!success) {
                    success = performDownload(activity, downloadUrl, apkFile, null);
                }

                if (success && isInstallableApk(activity, apkFile)) {
                    log(activity, "静默更新包下载成功并通过解析校验。");
                    prefs.edit()
                            .putString(KEY_DOWNLOADED_VERSION, latestVersion)
                            .putBoolean(KEY_IS_DOWNLOADING, false)
                            .apply();

                    new Handler(Looper.getMainLooper()).post(new Runnable() {
                        @Override
                        public void run() {
                            if (!activity.isFinishing()) {
                                showInstallReadyDialog(activity, latestVersion);
                            }
                        }
                    });
                } else {
                    if (success) {
                        log(activity, "静默更新包下载完成但无法解析，已删除坏包。");
                        cleanCacheApk(activity);
                    } else {
                        log(activity, "静默更新包下载失败。");
                    }
                    prefs.edit().putBoolean(KEY_IS_DOWNLOADING, false).apply();
                }
            }
        });
    }

    /**
     * 底层网络下载核心（针对 88M 大文件加大了超时保护时间）
     */
    private static boolean performDownload(Context context, String downloadUrl, File targetFile, final ProgressDialog progressDialog) {
        HttpURLConnection conn = null;
        FileOutputStream out = null;
        InputStream in = null;
        try {
            log(context, "建立下载连接: " + downloadUrl);
            URL url = new URL(downloadUrl);
            conn = (HttpURLConnection) url.openConnection();
            // 大文件下载，将连接超时增加到 20 秒，读取超时增加到 90 秒，防范中途闪断超时
            conn.setConnectTimeout(20000);
            conn.setReadTimeout(90000);
            conn.connect();

            int code = conn.getResponseCode();
            log(context, "下载连接响应状态码: " + code);
            if (code != 200 && code != 206) {
                log(context, "无效的状态码，连接终止");
                return false;
            }

            final int fileLength = conn.getContentLength();
            log(context, "更新文件大小: " + fileLength + " 字节");
            
            in = conn.getInputStream();
            if (targetFile.exists()) {
                targetFile.delete();
            }
            out = new FileOutputStream(targetFile);

            byte[] data = new byte[4096];
            long total = 0;
            int count;
            while ((count = in.read(data)) != -1) {
                if (isDownloadCanceled) {
                    log(context, "下载检测到取消信号，终止数据读取。");
                    return false;
                }
                total += count;
                if (fileLength > 0 && progressDialog != null) {
                    final int progress = (int) (total * 100 / fileLength);
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
            return true;
        } catch (Exception e) {
            log(context, "数据下载流异常: " + e.getMessage());
            return false;
        } finally {
            try {
                if (in != null) in.close();
                if (out != null) out.close();
            } catch (Exception ignored) {}
            if (conn != null) conn.disconnect();
        }
    }

    private static void updateProgressMessage(Context context, final ProgressDialog dialog, final String message) {
        if (dialog == null) return;
        new Handler(Looper.getMainLooper()).post(new Runnable() {
            @Override
            public void run() {
                dialog.setMessage(message);
            }
        });
    }

    private static void installApk(Activity activity, File apkFile) {
        if (apkFile == null || !apkFile.exists()) return;
        if (!isInstallableApk(activity, apkFile)) {
            cleanCacheApk(activity);
            Toast.makeText(activity, "安装包解析失败，请重新下载最新版", Toast.LENGTH_LONG).show();
            return;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            if (!activity.getPackageManager().canRequestPackageInstalls()) {
                log(activity, "安装权限缺失，引导用户前往系统授权面页。");
                Toast.makeText(activity, "请授予“安装未知来源应用”权限以完成升级", Toast.LENGTH_LONG).show();
                Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES);
                intent.setData(Uri.parse("package:" + activity.getPackageName()));
                activity.startActivity(intent);
                return;
            }
        }

        Uri apkUri = FileProvider.getUriForFile(activity, "cc.luoluoluo.yanzi.mobile.fileprovider", apkFile);
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
        return context.getPackageName().equals(info.packageName);
    }

    private static PackageInfo getArchivePackageInfo(Context context, File apkFile) {
        if (context == null || apkFile == null || !apkFile.exists() || apkFile.length() <= 0) {
            return null;
        }

        try {
            return context.getPackageManager().getPackageArchiveInfo(apkFile.getAbsolutePath(), 0);
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
