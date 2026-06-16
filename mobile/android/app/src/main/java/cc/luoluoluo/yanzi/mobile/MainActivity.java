/*
 * Decompiled with CFR 0.152.
 * 
 * Could not load the following classes:
 *  android.app.Activity
 *  android.app.AlertDialog
 *  android.app.AlertDialog$Builder
 *  android.appwidget.AppWidgetManager
 *  android.content.ClipData
 *  android.content.ClipboardManager
 *  android.content.ComponentName
 *  android.content.Context
 *  android.content.Intent
 *  android.content.SharedPreferences
 *  android.content.pm.ShortcutInfo
 *  android.content.pm.ShortcutInfo$Builder
 *  android.content.pm.ShortcutManager
 *  android.graphics.Bitmap
 *  android.graphics.Bitmap$CompressFormat
 *  android.graphics.Bitmap$Config
 *  android.graphics.BitmapFactory
 *  android.graphics.BitmapFactory$Options
 *  android.graphics.Canvas
 *  android.graphics.Color
 *  android.graphics.Matrix
 *  android.graphics.Paint
 *  android.graphics.Paint$Style
 *  android.graphics.Path
 *  android.graphics.RectF
 *  android.graphics.drawable.Drawable
 *  android.graphics.drawable.GradientDrawable
 *  android.graphics.drawable.Icon
 *  android.net.Uri
 *  android.os.Build
 *  android.os.Build$VERSION
 *  android.os.Bundle
 *  android.os.Environment
 *  android.os.Handler
 *  android.os.Looper
 *  android.provider.Settings
 *  android.text.Editable
 *  android.text.TextWatcher
 *  android.util.Base64
 *  android.util.Log
 *  android.view.View
 *  android.view.ViewGroup
 *  android.view.ViewGroup$LayoutParams
 *  android.view.inputmethod.InputMethodManager
 *  android.webkit.JavascriptInterface
 *  android.webkit.WebView
 *  android.widget.Button
 *  android.widget.EditText
 *  android.widget.FrameLayout$LayoutParams
 *  android.widget.GridLayout
 *  android.widget.GridLayout$LayoutParams
 *  android.widget.HorizontalScrollView
 *  android.widget.ImageView
 *  android.widget.LinearLayout
 *  android.widget.LinearLayout$LayoutParams
 *  android.widget.ProgressBar
 *  android.widget.ScrollView
 *  android.widget.TextView
 *  android.widget.Toast
 *  androidx.drawerlayout.widget.DrawerLayout
 *  androidx.drawerlayout.widget.DrawerLayout$LayoutParams
 *  androidx.swiperefreshlayout.widget.SwipeRefreshLayout
 *  org.json.JSONArray
 *  org.json.JSONObject
 */
package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.appwidget.AppWidgetManager;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ShortcutInfo;
import android.content.pm.ShortcutManager;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.Icon;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.provider.OpenableColumns;
import android.database.Cursor;
import android.text.Editable;
import android.text.Selection;
import android.text.Spannable;
import android.text.TextWatcher;
import android.util.Base64;
import android.util.Log;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;
import android.widget.PopupMenu;
import android.widget.TableLayout;
import android.widget.TableRow;
import android.view.Gravity;
import android.graphics.Typeface;
import android.text.Html;
import android.text.TextUtils;
import java.util.ArrayList;
import androidx.drawerlayout.widget.DrawerLayout;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;
import cc.luoluoluo.yanzi.mobile.FloatingWheelService;
import cc.luoluoluo.yanzi.mobile.LanDiscoveryManager;
import cc.luoluoluo.yanzi.mobile.MobileDiagnostics;
import cc.luoluoluo.yanzi.mobile.MobileIconLibrary;
import cc.luoluoluo.yanzi.mobile.PathDrawable;
import cc.luoluoluo.yanzi.mobile.widget.ExtensionsWidgetProvider;
import cc.luoluoluo.yanzi.mobile.widget.YanmWidgetProvider;
import java.io.BufferedReader;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.io.OutputStreamWriter;
import java.lang.reflect.Method;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.json.JSONArray;
import org.json.JSONObject;

public class MainActivity
extends Activity {
    public static Context sContext;
    private static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";
    private static final String CACHE_REMOTE_EXTENSIONS = "cacheRemoteExtensionsJson";
    private static final String CACHE_YANM = "cacheYanmJson";
    private static final int REQUEST_PICK_PHOTO = 4101;
    private static final int REQUEST_CODE_SELECT_IMAGE = 8001;
    private static final int REQUEST_CODE_SELECT_FILE = 8002;
    private static final int REQUEST_CODE_TAKE_PHOTO = 8003;
    private Uri cameraPhotoUri;
    private File cameraPhotoFile;
    private final ArrayList<AttachmentInfo> pendingAttachments = new ArrayList<>();
    private final ArrayList<AttachmentInfo> activeImageAttachments = new ArrayList<>();
    private HorizontalScrollView aiAttachmentScrollView;
    private LinearLayout aiAttachmentContainer;
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private SharedPreferences prefs;
    private String deviceId;
    private ScrollView mainScrollView;
    private EditText baseUrlInput;
    private EditText emailInput;
    private EditText passwordInput;
    private EditText textInput;
    private TextView statusText;
    private EditText mobileExtensionInput;
    private EditText mobileExtensionIdInput;
    private EditText mobileExtensionNameInput;
    private EditText mobileExtensionIconInput;
    private EditText mobileExtensionDescriptionInput;
    private TextView mobileExtensionSectionTitle;
    private TextView mobileExtensionTestResult;
    private LinearLayout mobileExtensionManagerList;
    private LinearLayout extensionList;
    private GridLayout yanmList;
    private LinearLayout yanmTabPage;
    private LinearLayout mobileExtensionTabPage;
    private LinearLayout desktopExtensionTabPage;
    private LinearLayout profileTabPage;
    private LinearLayout aiTabPage;
    private View yanmTabButton;
    private View mobileExtensionTabButton;
    private View aiTabButton;
    private View desktopExtensionTabButton;
    private View profileTabButton;
    private Button loginButton;
    private Button overlayButton;
    private EditText searchDesktopExtensionsInput;
    private LinearLayout aiChatHistory;
    private LinearLayout aiEmptyStateContainer;
    private EditText aiChatInput;
    private TextView aiModelInfoText;
    private Button aiSendButton;
    private Drawable aiSendButtonDefaultBackground;
    private View aiLoadingPlaceholderView;
    private AiMessageInfo currentActiveToolMessageInfo;
    private volatile boolean isAiLoading = false;
    private volatile boolean isAiCancelled = false;
    private volatile HttpURLConnection currentAiConnection = null;
    private JSONArray aiMessagesHistory = new JSONArray();
    private String currentAiSessionId = null;
    private final Object aiHistoryLock = new Object();
    private static final long AI_TOOL_DEBOUNCE_MS = 10000L;
    private final Object aiToolCallLock = new Object();
    private final Map<String, Long> recentAiToolCalls = new HashMap<String, Long>();
    private final Set<String> runningAiToolCalls = new HashSet<String>();
    private DrawerLayout aiDrawerLayout;
    private LinearLayout aiSessionListDrawer;
    private List<RemoteExtension> currentDesktopExtensions = new ArrayList<RemoteExtension>();
    private static final String DEFAULT_SYSTEM_PROMPT = 
            "\u4f60\u662f\u71d5\u5b50\u624b\u673a\u7aef AI \u52a9\u624b\u3002\u4f60\u53ef\u4ee5\u89e3\u7b54\u95ee\u9898\uff0c\u4e5f\u53ef\u4ee5\u8c03\u7528\u672c\u5730\u624b\u673a\u5de5\u5177\u3002\n" +
            "\u4f60\u53ef\u4ee5\u81ea\u4e3b\u5224\u65ad\u662f\u5426\u9700\u8981\u8c03\u7528\u5de5\u5177\u3002\u5982\u679c\u9700\u8981\u8c03\u7528\u5de5\u5177\uff0c\u8bf7\u8f93\u51fa\u4e00\u6bb5\u5305\u88f9\u5728 ```json \u5185\u90e8\u7684 JSON \u4ee3\u7801\u5757\uff1a\n" +
            "```json\n" +
            "{\"tool\": \"\u5de5\u5177\u540d\", \"\u53c2\u6570\u540d\": \"\u53c2\u6570\u503c\"}\n" +
            "```\n" +
            "\n" +
            "\u3010\u5de5\u5177\u8c03\u7528\u793a\u4f8b\u3011\n" +
            "\u7528\u6237\uff1a\u67e5\u770b\u63d2\u4ef6\u5217\u8868\n" +
            "AI\u56de\u590d\uff1a\n" +
            "```json\n" +
            "{\"tool\": \"query_extensions\"}\n" +
            "```\n" +
            "\u7cfb\u7edf\u53cd\u9988\uff1a\n" +
            "[{\"id\": \"ext_calculator\", \"name\": \"\u8ba1\u7b97\u5668\"}, {\"id\": \"ext_weather\", \"name\": \"\u5929\u6c14\u52a9\u624b\"}]\n" +
            "AI\u56de\u590d\uff1a\n" +
            "\u76ee\u524d\u5df2\u5b89\\u88c5\u7684\\u63d2\u4ef6\\u5217\\u8868\\u5982\\u4e0b\uff1a\n" +
            "1. \u8ba1\u7b97\u5668 (ID: ext_calculator)\n" +
            "2. \u5929\u6c14\u52a9\u624b (ID: ext_weather)\n" +
            "\u4f60\u53ef\u4ee5\u544a\u8bc9\u6211\u4f60\u60f3\u6267\\u884c\u54ea\u4e00\u4e2a\u3002\n" +
            "\n" +
            "\u3010\u53ef\\u7528\u5de5\u5177\u5217\\u8868\u3011\n" +
            "1. query_extensions: \u83b7\u53d6\u53ef\u7528\u6269\u5c55\u5217\u8868\u3002\u65e0\\u53c2\u6570\u3002\n" +
            "2. execute_extension: \u6267\u884c\u67d0\u4e2a\u6269\u5c55\u3002\u53c2\u6570: id (\u6269\u5c55ID)\u3002\n" +
            "3. view_yanm: \u67e5\u770b\u71d5\u5e55\u7ec4\u4ef6\u3002\u53c2\u6570: id (\u53ef\u9009\uff0c\u586b\u5165 id \u67e5\u770b\u7ec4\\u4ef6\u8be6\u60c5\uff0c\u4e0d\\u586b\u5219\u67e5\u770b\u6240\u6709\u7ec4\u4ef6\u540d)\u3002\n" +
            "4. update_yanm_component: \u4fee\u6539\u71d5\u5e55\u7ec4\u4ef6\u3002\u53c2\u6570: id (\u7ec4\u4ef6ID), title (\u6807\u9898), html (\u5185\u5bb9)\u3002\n" +
            "5. manage_mobile_extension: \u7ba1\u7406\u624b\u673a\\u6269\u5c55\u3002\u53c2\u6570: action (list/read/create/update/delete), id, name, code, icon, description\u3002\n" +
            "\u3010\u6ce8\u610f\u3011\u5982\u679c\u4f60\u8c03\u7528\u4e86\u5de5\u5177\uff0c\u7cfb\u7edf\u4f1a\\u5728\\u540e\\u53f0\\u771f\\u5b9e\\u6267\\u884c\uff0c\\u5e76\\u5728\\u6267\\u884c\\u5b8c\\u6210\\u540e\\u5c06\\u771f\\u5b9e\\u7684\\u7ed3\\u679c\\u53cd\\u9988\\u7ed9\\u4f60\\uff0c\\u4e4b\\u540e\\u4f60\\u518d\\u6839\\u636e\\u6267\\u884c\\u7ed3\\u679c\\u6765\\u51b3\\u5b9a\\u662f\\u7ee7\\u7eed\\u8c03\\u7528\u5de5\u5177\\u8fd8\\u662f\\u8f93\\u51fa\\u6700\\u7ec8\\u7684\\u81ea\\u7136\\u8bed\\u8a00\\u56de\\u590d\u3002";
    private SwipeRefreshLayout swipeRefresh;
    private final Set<String> expandedComponentIds = new HashSet<String>();
    private final List<String> sortedComponentIds = new ArrayList<String>();
    private android.speech.tts.TextToSpeech textToSpeech;
    private boolean isTtsEnabled = false;
    private boolean isTtsInitialized = false;
    private String pendingSpeakText = null;
    private android.speech.SpeechRecognizer speechRecognizer;
    private android.content.Intent speechRecognizerIntent;
    private boolean isSpeechListening = false;
    private long lastSpeechStartTime = 0;
    private boolean pendingStopSpeech = false;
    private boolean isSpeechActionUp = false;
    private boolean isSpeechFinished = false;
    private android.widget.Button holdToSpeakBtn;
    private android.widget.Button voiceToggleBtn;
    private final Map<String, WebView> activeYanmWebViews = new HashMap<String, WebView>();
    private WebView activeMobileScriptRunner;
    private View photoProgressView;
    private final Handler yanmSyncHandler = new Handler(Looper.getMainLooper());
    private final Handler diagnosticRefreshHandler = new Handler(Looper.getMainLooper());
    private final Runnable diagnosticRefreshRunnable = new Runnable(){

        @Override
        public void run() {
            MainActivity.this.refreshDiagnosticLogFromStore();
            MainActivity.this.diagnosticRefreshHandler.postDelayed((Runnable)this, 1000L);
        }
    };
    private JSONObject currentYanmState;
    private JSONObject currentYanmSnapshot;
    private Runnable pendingYanmSync;
    private final StringBuilder diagnosticLog = new StringBuilder();
    private final android.content.BroadcastReceiver screenshotReceiver = new android.content.BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            Log.d("Yanzi", "BroadcastReceiver onReceive action: " + intent.getAction());
            if ("cc.luoluoluo.yanzi.mobile.SCREENSHOT_SUCCESS".equals(intent.getAction())) {
                String base64Data = intent.getStringExtra("image_base64");
                Log.d("Yanzi", "BroadcastReceiver got base64 data length: " + (base64Data != null ? base64Data.length() : 0));
                if (base64Data != null) {
                    MainActivity.this.addSharedAppScreenshot("float_capture", base64Data);
                }
            }
        }
    };

    protected void onCreate(Bundle savedInstanceState) {
        if (this.getIntent() != null && this.getIntent().hasExtra("run_remote_extension_id")) {
            super.onCreate(savedInstanceState);
            sContext = this;
            this.prefs = this.getSharedPreferences("yanzi-mobile", 0);
            this.deviceId = this.getOrCreateDeviceId();
            String extId = this.getIntent().getStringExtra("run_remote_extension_id");
            String extName = this.getIntent().getStringExtra("run_remote_extension_name");
            if (extId != null && !extId.isEmpty()) {
                this.runRemoteExtensionSilently(extId, extName != null ? extName : extId, null);
            }
            this.finish();
            return;
        }
        super.onCreate(savedInstanceState);
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
            this.registerReceiver(this.screenshotReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.SCREENSHOT_SUCCESS"), 
                Context.RECEIVER_NOT_EXPORTED);
        } else {
            this.registerReceiver(this.screenshotReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.SCREENSHOT_SUCCESS"));
        }
        sContext = this;
        LanDiscoveryManager.discover((Context)this);
        this.prefs = this.getSharedPreferences("yanzi-mobile", 0);
        this.isTtsEnabled = this.prefs.getBoolean("isTtsEnabled", false);
        MobileIconLibrary.init((Context)this);
        this.deviceId = this.getOrCreateDeviceId();
        String expandedJson = this.prefs.getString("expandedComponentIds", "[]");
        try {
            JSONArray arr = new JSONArray(expandedJson);
            this.expandedComponentIds.clear();
            for (int i = 0; i < arr.length(); ++i) {
                this.expandedComponentIds.add(arr.getString(i));
            }
        }
        catch (Exception arr) {
            // empty catch block
        }
        String sortedJson = this.prefs.getString("sortedComponentIds", "[]");
        try {
            JSONArray arr = new JSONArray(sortedJson);
            this.sortedComponentIds.clear();
            for (int i = 0; i < arr.length(); ++i) {
                this.sortedComponentIds.add(arr.getString(i));
            }
        }
        catch (Exception exception) {
            // empty catch block
        }
        MobileDiagnostics.clear((Context)this);
        this.buildUi(MainActivity.extractSharedText(this.getIntent()));
        this.handleExternalAction(this.getIntent());
        this.startFloatingWheelIfPermitted();
        this.initTextToSpeech();
    }

    protected void onResume() {
        super.onResume();
        if (this.overlayButton != null) {
            this.overlayButton.setText((CharSequence)(FloatingWheelService.isRunning ? "\u5173\u95ed\u60ac\u6d6e\u8f6e\u76d8" : "\u6253\u5f00\u60ac\u6d6e\u8f6e\u76d8"));
        }
        LanDiscoveryManager.discover((Context)this);
        this.refreshDiagnosticLogFromStore();
        this.diagnosticRefreshHandler.removeCallbacks(this.diagnosticRefreshRunnable);
        this.diagnosticRefreshHandler.postDelayed(this.diagnosticRefreshRunnable, 1000L);
    }

    protected void onPause() {
        this.diagnosticRefreshHandler.removeCallbacks(this.diagnosticRefreshRunnable);
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        try {
            this.unregisterReceiver(this.screenshotReceiver);
        } catch (Exception e) {}
        if (this.textToSpeech != null) {
            try {
                this.textToSpeech.stop();
                this.textToSpeech.shutdown();
            } catch (Exception ignored) {}
            this.textToSpeech = null;
        }
        this.destroySpeechRecognizer();
        super.onDestroy();
    }

    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        this.setIntent(intent);
        String text = MainActivity.extractSharedText(intent);
        if (text != null && !text.trim().isEmpty()) {
            this.textInput.setText((CharSequence)text);
            this.setStatus("\u5df2\u63a5\u6536\u7cfb\u7edf\u5206\u4eab\u5185\u5bb9\uff0c\u786e\u8ba4\u540e\u53ef\u53d1\u9001\u5230\u7535\u8111\u3002");
        }
        this.handleExternalAction(intent);
    }

    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        Uri uri;
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == 4101 && resultCode == -1 && data != null && (uri = data.getData()) != null) {
            this.sendPhotoToDesktop(uri);
        } else if ((requestCode == REQUEST_CODE_SELECT_IMAGE || requestCode == REQUEST_CODE_SELECT_FILE) && resultCode == -1 && data != null && (uri = data.getData()) != null) {
            this.handleAttachmentSelected(uri, requestCode == REQUEST_CODE_SELECT_IMAGE);
        } else if (requestCode == REQUEST_CODE_TAKE_PHOTO && resultCode == -1) {
            if (this.cameraPhotoUri != null && this.cameraPhotoFile != null && this.cameraPhotoFile.exists()) {
                this.handleCameraPhotoTaken(this.cameraPhotoUri, this.cameraPhotoFile.getName(), this.cameraPhotoFile.length());
            }
        } else if (requestCode == 103 && resultCode == -1 && data != null) {
            ArrayList<String> matches = data.getStringArrayListExtra(android.speech.RecognizerIntent.EXTRA_RESULTS);
            if (matches != null && !matches.isEmpty()) {
                String text = matches.get(0);
                this.aiChatInput.setText((CharSequence)text);
                this.aiChatInput.setSelection(text.length());
            }
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 9001) {
            if (grantResults.length > 0 && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                this.launchCamera();
            } else {
                Toast.makeText(this, "需要相机权限才能拍照", Toast.LENGTH_SHORT).show();
            }
        } else if (requestCode == 102) {
            if (grantResults.length > 0 && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                this.switchToVoiceInput();
            } else {
                Toast.makeText(this, "需要麦克风录音权限才能使用语音输入", Toast.LENGTH_SHORT).show();
            }
        }
    }

    private void takeCameraPhoto() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            if (this.checkSelfPermission(android.Manifest.permission.CAMERA) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                this.requestPermissions(new String[]{android.Manifest.permission.CAMERA}, 9001);
                return;
            }
        }
        this.launchCamera();
    }

    private void launchCamera() {
        Intent intent = new Intent(android.provider.MediaStore.ACTION_IMAGE_CAPTURE);
        if (intent.resolveActivity(this.getPackageManager()) != null) {
            try {
                String timeStamp = new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.getDefault()).format(new Date());
                String imageFileName = "JPEG_" + timeStamp + "_";
                File storageDir = this.getExternalFilesDir(Environment.DIRECTORY_PICTURES);
                this.cameraPhotoFile = File.createTempFile(imageFileName, ".jpg", storageDir);
                
                this.cameraPhotoUri = androidx.core.content.FileProvider.getUriForFile(this, 
                        "cc.luoluoluo.yanzi.mobile.fileprovider", this.cameraPhotoFile);
                
                intent.putExtra(android.provider.MediaStore.EXTRA_OUTPUT, this.cameraPhotoUri);
                this.startActivityForResult(intent, REQUEST_CODE_TAKE_PHOTO);
            } catch (Exception e) {
                Log.e("Yanzi", "Failed to create photo file", e);
                Toast.makeText(this, "拍照初始化失败: " + e.getMessage(), Toast.LENGTH_SHORT).show();
            }
        } else {
            Toast.makeText(this, "未找到相机应用", Toast.LENGTH_SHORT).show();
        }
    }

    private void handleCameraPhotoTaken(Uri uri, String name, long size) {
        String mimeType = "image/jpeg";
        String base64Data = null;
        try (InputStream is = this.getContentResolver().openInputStream(uri)) {
            if (is != null) {
                BitmapFactory.Options options = new BitmapFactory.Options();
                options.inJustDecodeBounds = true;
                BitmapFactory.decodeStream(is, null, options);
                
                int maxDim = Math.max(options.outWidth, options.outHeight);
                int inSampleSize = 1;
                if (maxDim > 1024) {
                    inSampleSize = maxDim / 1024;
                }
                options.inJustDecodeBounds = false;
                options.inSampleSize = inSampleSize;
                
                try (InputStream is2 = this.getContentResolver().openInputStream(uri)) {
                    Bitmap bmp = BitmapFactory.decodeStream(is2, null, options);
                    if (bmp != null) {
                        ByteArrayOutputStream baos = new ByteArrayOutputStream();
                        bmp.compress(Bitmap.CompressFormat.JPEG, 80, baos);
                        byte[] bytes = baos.toByteArray();
                        base64Data = Base64.encodeToString(bytes, Base64.NO_WRAP);
                    }
                }
            }
        } catch (Exception e) {
            Log.e("Yanzi", "Failed to process camera photo", e);
            Toast.makeText(this, "照片加载失败: " + e.getMessage(), Toast.LENGTH_SHORT).show();
            return;
        }

        AttachmentInfo attach = new AttachmentInfo(name, size, mimeType, uri, base64Data, null, true);
        this.pendingAttachments.add(attach);
        this.runOnUiThread(this::refreshAttachmentCards);
    }

    private void startFloatingScreenshot() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            if (!Settings.canDrawOverlays(this)) {
                Toast.makeText(this, "需要悬浮窗权限，请先开启悬浮轮盘以获得授权", Toast.LENGTH_LONG).show();
                try {
                    Intent intent = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, 
                            Uri.parse("package:" + this.getPackageName()));
                    this.startActivity(intent);
                } catch (Exception e) {
                    Log.e("Yanzi", "Failed to start manage overlay settings", e);
                }
                return;
            }
        }
        if (!MobileAccessibilityService.isEnabled()) {
            Toast.makeText(this, "截图失败：请先前往无障碍设置开启 燕子 辅助功能", Toast.LENGTH_LONG).show();
            return;
        }
        
        try {
            Intent intent = new Intent(this, FloatingScreenshotService.class);
            this.startService(intent);
            this.moveTaskToBack(true);
            Toast.makeText(this, "已生成悬浮截图按钮，请切换到合适界面点击截图", Toast.LENGTH_LONG).show();
        } catch (Exception e) {
            Log.e("Yanzi", "Failed to start FloatingScreenshotService", e);
            Toast.makeText(this, "启动悬浮截图服务失败: " + e.getMessage(), Toast.LENGTH_SHORT).show();
        }
    }

    private void addSharedAppScreenshot(String packageName, String base64Data) {
        Log.d("Yanzi", "addSharedAppScreenshot start, package: " + packageName + ", base64 length: " + (base64Data != null ? base64Data.length() : 0));
        String name = "app_share_" + packageName.substring(packageName.lastIndexOf(".") + 1) + ".jpg";
        long size = base64Data.length() * 3L / 4L;
        AttachmentInfo attach = new AttachmentInfo(name, size, "image/jpeg", null, base64Data, null, true);
        this.pendingAttachments.add(attach);
        this.runOnUiThread(() -> {
            Log.d("Yanzi", "addSharedAppScreenshot runOnUiThread running, pending size: " + this.pendingAttachments.size());
            this.refreshAttachmentCards();
            Toast.makeText(this, "成功截取并添加屏幕截图", Toast.LENGTH_SHORT).show();
        });
    }

    private void handleAttachmentSelected(Uri uri, boolean isImage) {
        String name = "未知文件";
        long size = 0;
        String mimeType = this.getContentResolver().getType(uri);
        
        try (Cursor cursor = this.getContentResolver().query(uri, null, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) {
                int nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (nameIndex != -1) {
                    name = cursor.getString(nameIndex);
                }
                int sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE);
                if (sizeIndex != -1) {
                    size = cursor.getLong(sizeIndex);
                }
            }
        } catch (Exception e) {
            Log.e("Yanzi", "Query uri failed", e);
        }
        
        if (name == null || name.isEmpty()) {
            name = uri.getLastPathSegment();
        }
        if (name == null || name.isEmpty()) {
            name = "file_" + System.currentTimeMillis();
        }

        String base64Data = null;
        String textContent = null;

        if (isImage) {
            try (InputStream is = this.getContentResolver().openInputStream(uri)) {
                if (is != null) {
                    BitmapFactory.Options options = new BitmapFactory.Options();
                    options.inJustDecodeBounds = true;
                    BitmapFactory.decodeStream(is, null, options);
                    
                    int width = options.outWidth;
                    int height = options.outHeight;
                    int maxDim = Math.max(width, height);
                    int inSampleSize = 1;
                    if (maxDim > 1024) {
                        inSampleSize = maxDim / 1024;
                    }
                    
                    options.inJustDecodeBounds = false;
                    options.inSampleSize = inSampleSize;
                    
                    try (InputStream is2 = this.getContentResolver().openInputStream(uri)) {
                        Bitmap bmp = BitmapFactory.decodeStream(is2, null, options);
                        if (bmp != null) {
                            ByteArrayOutputStream baos = new ByteArrayOutputStream();
                            bmp.compress(Bitmap.CompressFormat.JPEG, 80, baos);
                            byte[] bytes = baos.toByteArray();
                            base64Data = Base64.encodeToString(bytes, Base64.NO_WRAP);
                        }
                    }
                }
            } catch (Exception e) {
                Log.e("Yanzi", "Failed to process image attachment", e);
                this.setStatus("无法加载图片: " + e.getMessage());
                return;
            }
        } else {
            if (size < 1024 * 1024) {
                try (InputStream is = this.getContentResolver().openInputStream(uri);
                     BufferedReader reader = new BufferedReader(new InputStreamReader(is, StandardCharsets.UTF_8))) {
                    StringBuilder sb = new StringBuilder();
                    String line;
                    while ((line = reader.readLine()) != null) {
                        sb.append(line).append("\n");
                    }
                    textContent = sb.toString();
                } catch (Exception e) {
                    Log.e("Yanzi", "Failed to read file attachment as text", e);
                }
            }
        }

        AttachmentInfo attach = new AttachmentInfo(name, size, mimeType, uri, base64Data, textContent, isImage);
        this.pendingAttachments.add(attach);
        this.runOnUiThread(this::refreshAttachmentCards);
    }

    private void refreshAttachmentCards() {
        if (this.aiAttachmentContainer == null || this.aiAttachmentScrollView == null) {
            return;
        }
        this.aiAttachmentContainer.removeAllViews();
        if (this.pendingAttachments.isEmpty()) {
            this.aiAttachmentScrollView.setVisibility(View.GONE);
            return;
        }
        this.aiAttachmentScrollView.setVisibility(View.VISIBLE);
        
        for (int i = 0; i < this.pendingAttachments.size(); i++) {
            AttachmentInfo attach = this.pendingAttachments.get(i);
            final int index = i;
            
            LinearLayout card = new LinearLayout((Context)this);
            card.setOrientation(0);
            card.setGravity(16);
            card.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
            
            LinearLayout.LayoutParams cardParams = new LinearLayout.LayoutParams(
                    this.dp(160),
                    this.dp(60)
            );
            cardParams.setMargins(0, 0, this.dp(10), 0);
            card.setLayoutParams((ViewGroup.LayoutParams)cardParams);
            
            GradientDrawable cardBg = new GradientDrawable();
            cardBg.setColor(Color.rgb(31, 41, 55));
            cardBg.setCornerRadius((float)this.dp(8));
            card.setBackground((Drawable)cardBg);
            
            if (attach.isImage) {
                ImageView iv = new ImageView((Context)this);
                iv.setScaleType(ImageView.ScaleType.CENTER_CROP);
                if (attach.uri != null) {
                    try (InputStream is = this.getContentResolver().openInputStream(attach.uri)) {
                        BitmapFactory.Options options = new BitmapFactory.Options();
                        options.inSampleSize = 4;
                        Bitmap bmp = BitmapFactory.decodeStream(is, null, options);
                        if (bmp != null) {
                            iv.setImageBitmap(bmp);
                        } else {
                            iv.setImageResource(17301616);
                        }
                    } catch (Exception e) {
                        iv.setImageResource(17301616);
                    }
                } else if (attach.base64Data != null) {
                    try {
                        byte[] decodedString = Base64.decode(attach.base64Data, Base64.DEFAULT);
                        Bitmap bmp = BitmapFactory.decodeByteArray(decodedString, 0, decodedString.length);
                        if (bmp != null) {
                            iv.setImageBitmap(bmp);
                        } else {
                            iv.setImageResource(17301616);
                        }
                    } catch (Exception e) {
                        iv.setImageResource(17301616);
                    }
                } else {
                    iv.setImageResource(17301616);
                }
                card.addView((View)iv, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
            } else {
                TextView fileIcon = new TextView((Context)this);
                fileIcon.setText((CharSequence)"📄");
                fileIcon.setTextSize(24.0f);
                fileIcon.setGravity(17);
                card.addView((View)fileIcon, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
            }
            
            LinearLayout textLayout = new LinearLayout((Context)this);
            textLayout.setOrientation(1);
            textLayout.setGravity(16);
            
            TextView nameTv = new TextView((Context)this);
            nameTv.setText((CharSequence)attach.name);
            nameTv.setTextColor(-1);
            nameTv.setTextSize(12.0f);
            nameTv.setSingleLine(true);
            nameTv.setEllipsize(TextUtils.TruncateAt.END);
            
            TextView sizeTv = new TextView((Context)this);
            String sizeStr = attach.size < 1024 ? attach.size + " B" : (attach.size < 1024 * 1024 ? (attach.size / 1024) + " KB" : String.format(Locale.getDefault(), "%.1f MB", attach.size / (1024.0 * 1024.0)));
            sizeTv.setText((CharSequence)sizeStr);
            sizeTv.setTextColor(Color.rgb(156, 163, 175));
            sizeTv.setTextSize(10.0f);
            
            textLayout.addView((View)nameTv);
            textLayout.addView((View)sizeTv);
            
            LinearLayout.LayoutParams textLayoutParams = new LinearLayout.LayoutParams(0, -2, 1.0f);
            textLayoutParams.setMargins(this.dp(6), 0, this.dp(6), 0);
            card.addView((View)textLayout, (ViewGroup.LayoutParams)textLayoutParams);
            
            TextView deleteBtn = new TextView((Context)this);
            deleteBtn.setText((CharSequence)"✕");
            deleteBtn.setTextColor(Color.rgb(239, 68, 68));
            deleteBtn.setTextSize(16.0f);
            deleteBtn.setPadding(this.dp(4), this.dp(4), this.dp(4), this.dp(4));
            deleteBtn.setClickable(true);
            deleteBtn.setOnClickListener(v -> {
                this.pendingAttachments.remove(index);
                this.refreshAttachmentCards();
            });
            card.addView((View)deleteBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
            
            this.aiAttachmentContainer.addView((View)card);
        }
    }

    private void handleExternalAction(Intent intent) {
        if (intent == null || intent.getAction() == null) {
            return;
        }
        String action = intent.getAction();
        if (action.endsWith(".extensions")) {
            this.selectTab("desktop");
            this.setStatus("\u5df2\u4ece\u60ac\u6d6e\u8f6e\u76d8\u8fdb\u5165\u8fdc\u7a0b\u6269\u5c55\u3002\u70b9\u51fb\u6269\u5c55\u56fe\u6807\u4f1a\u8ba9\u7535\u8111\u7aef\u6267\u884c\u3002");
            this.refreshExtensions(true);
            this.scrollToView((View)this.extensionList);
        } else if (action.endsWith(".pick-photo")) {
            this.selectTab("profile");
            this.setStatus("\u9009\u62e9\u7167\u7247\u540e\u5c06\u53d1\u9001\u5230\u540c\u8d26\u53f7\u7535\u8111\u7aef\u3002");
            this.pickPhotoFromGallery();
        } else if (action.endsWith(".create-mobile-extension")) {
            this.selectTab("mobile");
            this.openMobileExtensionEditor("\u6dfb\u52a0\u624b\u673a\u6269\u5c55\uff1a\u53ef\u7c98\u8d34 AI \u751f\u6210\u7684 mobile-js JSON\uff0c\u4fdd\u5b58\u540e\u8fd0\u884c\u3002");
        } else if (action.endsWith(".run-mobile-extension")) {
            this.selectTab("mobile");
            this.openMobileExtensionEditor("\u8fd0\u884c\u624b\u673a\u6269\u5c55\uff1a\u786e\u8ba4 JSON \u540e\u70b9\u51fb\u201c\u8fd0\u884c\u624b\u673a\u811a\u672c\u201d\u3002");
        } else if (action.endsWith(".compose-text")) {
            this.selectTab("profile");
            this.focusTextComposer("\u4ece\u60ac\u6d6e\u8f6e\u76d8\u8fdb\u5165\u6587\u672c\u53d1\u9001\u3002\u8f93\u5165\u5185\u5bb9\u540e\u70b9\u51fb\u201c\u53d1\u9001\u5230\u7535\u8111\u201d\u3002");
        } else if (action.endsWith(".yanm")) {
            this.selectTab("yanm");
            String targetId = intent.getStringExtra("target_component_id");
            if (targetId != null && !targetId.isEmpty()) {
                this.setStatus("\u5df2\u4ece\u684c\u9762\u5c0f\u90e8\u4ef6\u8fdb\u5165\u624b\u673a\u71d5\u5e55\uff0c\u6b63\u5728\u5b9a\u4f4d...");
                this.refreshYanm(true);
                this.mainScrollView.postDelayed(() -> this.scrollToYanmComponent(targetId), 300L);
            } else {
                this.setStatus("\u5df2\u4ece\u684c\u9762\u8fdb\u5165\u624b\u673a\u71d5\u5e55\u3002");
                this.refreshYanm(true);
                this.scrollToView((View)this.yanmList);
            }
        } else if (action.endsWith(".refresh")) {
            this.setStatus("\u6b63\u5728\u5237\u65b0\u79fb\u52a8\u7aef\u6570\u636e...");
            this.refreshExtensions();
            this.refreshYanm();
        } else if (action.endsWith(".run-remote-extension") || intent.hasExtra("run_remote_extension_id")) {
            String extId = intent.getStringExtra("run_remote_extension_id");
            String extName = intent.getStringExtra("run_remote_extension_name");
            if (extId != null && !extId.isEmpty()) {
                this.selectTab("desktop");
                RemoteExtension tempExt = new RemoteExtension(extId, extName != null ? extName : extId, "", "", "");
                this.runRemoteExtension(tempExt, null);
            }
        }
    }

    private void scrollToYanmComponent(String componentId) {
        if (this.yanmList == null || this.mainScrollView == null) {
            return;
        }
        String targetTag = "yanm_comp_" + componentId;
        for (int i = 0; i < this.yanmList.getChildCount(); ++i) {
            View child = this.yanmList.getChildAt(i);
            if (!targetTag.equals(child.getTag())) continue;
            if (!this.expandedComponentIds.contains(componentId)) {
                this.expandedComponentIds.add(componentId);
                this.prefs.edit().putString("expandedComponentIds", new JSONArray(this.expandedComponentIds).toString()).apply();
                if (this.currentYanmSnapshot != null) {
                    this.renderYanm(this.currentYanmSnapshot);
                } else {
                    this.renderCachedYanm();
                }
                this.mainScrollView.postDelayed(() -> this.scrollToYanmComponent(componentId), 150L);
                return;
            }
            child.post(() -> {
                int[] location = new int[2];
                child.getLocationOnScreen(location);
                int[] scrollLocation = new int[2];
                this.mainScrollView.getLocationOnScreen(scrollLocation);
                int offset = location[1] - scrollLocation[1] + this.mainScrollView.getScrollY() - this.dp(20);
                this.mainScrollView.smoothScrollTo(0, Math.max(0, offset));
                child.setBackgroundColor(Color.rgb((int)40, (int)50, (int)70));
                child.postDelayed(() -> child.setBackgroundColor(Color.rgb((int)30, (int)32, (int)34)), 800L);
            });
            break;
        }
    }

    private void setupAiTabPage() {
        String savedPrompt = this.prefs.getString("aiSystemPrompt", DEFAULT_SYSTEM_PROMPT);
        if (!savedPrompt.contains("\u3010\u5de5\u5177\u8c03\u7528\u793a\u4f8b\u3011")) {
            this.prefs.edit().putString("aiSystemPrompt", DEFAULT_SYSTEM_PROMPT).apply();
        }
        this.aiTabPage.setOrientation(1);
        this.aiTabPage.setBackgroundColor(Color.rgb((int)17, (int)17, (int)17));
        this.aiDrawerLayout = new DrawerLayout((Context)this);
        this.aiDrawerLayout.setFitsSystemWindows(true);
        LinearLayout mainContent = new LinearLayout((Context)this);
        mainContent.setOrientation(1);
        LinearLayout topBar = new LinearLayout((Context)this);
        topBar.setOrientation(0);
        topBar.setPadding(this.dp(16), this.dp(16), this.dp(16), this.dp(16));
        topBar.setGravity(16);
        Button hamburgerBtn = this.button("\u4e09");
        hamburgerBtn.setBackgroundColor(0);
        hamburgerBtn.setTextColor(-1);
        hamburgerBtn.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        hamburgerBtn.setOnClickListener(v -> this.aiDrawerLayout.openDrawer(3));
        topBar.addView((View)hamburgerBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        Button clearBtn = this.button("\ud83e\uddf9");
        clearBtn.setBackgroundColor(0);
        clearBtn.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        clearBtn.setOnClickListener(v -> this.clearAiHistory());
        topBar.addView((View)clearBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        TextView title = this.textView("AI \u52a9\u624b", 20, -1, true);
        title.setGravity(17);
        topBar.addView((View)title, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        Button speakBtn = this.button(this.isTtsEnabled ? "🔊" : "🔇");
        speakBtn.setBackgroundColor(0);
        speakBtn.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        speakBtn.setOnClickListener(v -> this.toggleTtsStatus(speakBtn));
        topBar.addView((View)speakBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        Button aiSettingsBtn = this.button("\u2699\ufe0f");
        aiSettingsBtn.setBackgroundColor(0);
        aiSettingsBtn.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        aiSettingsBtn.setOnClickListener(v -> this.showAiSettingsDialog());
        topBar.addView((View)aiSettingsBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        mainContent.addView((View)topBar);
        ScrollView chatScroll = new ScrollView((Context)this);
        chatScroll.setFillViewport(true);
        this.aiChatHistory = new LinearLayout((Context)this);
        this.aiChatHistory.setOrientation(1);
        this.aiChatHistory.setPadding(this.dp(16), this.dp(8), this.dp(16), this.dp(16));
        chatScroll.addView((View)this.aiChatHistory);
        mainContent.addView((View)chatScroll, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, 0, 1.0f));
        
        this.aiAttachmentScrollView = new HorizontalScrollView((Context)this);
        this.aiAttachmentScrollView.setVisibility(View.GONE);
        this.aiAttachmentScrollView.setBackgroundColor(Color.rgb(26, 26, 26));
        this.aiAttachmentContainer = new LinearLayout((Context)this);
        this.aiAttachmentContainer.setOrientation(0);
        this.aiAttachmentContainer.setGravity(16);
        this.aiAttachmentContainer.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.aiAttachmentScrollView.addView((View)this.aiAttachmentContainer, (ViewGroup.LayoutParams)new FrameLayout.LayoutParams(-1, -1));
        mainContent.addView((View)this.aiAttachmentScrollView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(76)));

        LinearLayout bottomArea = new LinearLayout((Context)this);
        bottomArea.setOrientation(0);
        bottomArea.setPadding(this.dp(12), this.dp(12), this.dp(12), this.dp(12));
        bottomArea.setGravity(80);
        bottomArea.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
        Button attachBtn = this.button("+");
        attachBtn.setBackgroundColor(0);
        attachBtn.setTextColor(Color.rgb((int)200, (int)200, (int)200));
        attachBtn.setTextSize(2, 24.0f);
        attachBtn.setOnClickListener(v -> {
            PopupMenu popup = new PopupMenu((Context)this, attachBtn);
            popup.getMenu().add(0, 1, 0, "添加图片");
            popup.getMenu().add(0, 2, 1, "添加文件");
            popup.getMenu().add(0, 3, 2, "拍照");
            popup.getMenu().add(0, 4, 3, "屏幕截图");
            popup.setOnMenuItemClickListener(item -> {
                if (item.getItemId() == 1) {
                    Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                    intent.setType("image/*");
                    this.startActivityForResult(Intent.createChooser(intent, "选择图片"), REQUEST_CODE_SELECT_IMAGE);
                } else if (item.getItemId() == 2) {
                    Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
                    intent.setType("*/*");
                    this.startActivityForResult(Intent.createChooser(intent, "选择文件"), REQUEST_CODE_SELECT_FILE);
                } else if (item.getItemId() == 3) {
                    this.takeCameraPhoto();
                } else if (item.getItemId() == 4) {
                    this.startFloatingScreenshot();
                }
                return true;
            });
            popup.show();
        });
        bottomArea.addView((View)attachBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(48), this.dp(48)));
        this.holdToSpeakBtn = new Button((Context)this);
        this.holdToSpeakBtn.setText("按住 说话");
        this.holdToSpeakBtn.setTextColor(-1);
        GradientDrawable speakBg = new GradientDrawable();
        speakBg.setColor(Color.rgb((int)59, (int)130, (int)246));
        speakBg.setCornerRadius((float)this.dp(8));
        this.holdToSpeakBtn.setBackground((Drawable)speakBg);
        this.holdToSpeakBtn.setVisibility(8);
        this.holdToSpeakBtn.setOnTouchListener((v, event) -> {
            switch (event.getAction()) {
                case 0: // ACTION_DOWN
                    this.holdToSpeakBtn.setText("松开 结束");
                    GradientDrawable downBg = new GradientDrawable();
                    downBg.setColor(Color.rgb((int)220, (int)38, (int)38));
                    downBg.setCornerRadius((float)this.dp(8));
                    this.holdToSpeakBtn.setBackground((Drawable)downBg);
                    try {
                        android.os.Vibrator vibrator = (android.os.Vibrator) this.getSystemService(Context.VIBRATOR_SERVICE);
                        if (vibrator != null) {
                            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                                vibrator.vibrate(android.os.VibrationEffect.createOneShot(50L, -1));
                            } else {
                                vibrator.vibrate(50L);
                            }
                        }
                    } catch (Exception ignored) {}
                    this.startSpeechRecognition();
                    return true;
                case 1: // ACTION_UP
                case 3: // ACTION_CANCEL
                    this.holdToSpeakBtn.setText("按住 说话");
                    GradientDrawable upBg = new GradientDrawable();
                    upBg.setColor(Color.rgb((int)59, (int)130, (int)246));
                    upBg.setCornerRadius((float)this.dp(8));
                    this.holdToSpeakBtn.setBackground((Drawable)upBg);
                    this.stopSpeechRecognition();
                    return true;
            }
            return false;
        });
        LinearLayout.LayoutParams speakParams = new LinearLayout.LayoutParams(0, this.dp(44), 1.0f);
        speakParams.gravity = 16;
        speakParams.leftMargin = this.dp(4);
        speakParams.rightMargin = this.dp(4);
        bottomArea.addView((View)this.holdToSpeakBtn, (ViewGroup.LayoutParams)speakParams);

        this.aiChatInput = this.multiInput("\u7535\u8111\u6269\u5c55 | \u624b\u673a\u6269\u5c55 | \u71d5\u5e55", "");
        this.aiChatInput.setBackground(null);
        this.aiChatInput.setPadding(this.dp(12), this.dp(10), this.dp(12), this.dp(10));
        this.aiChatInput.setHintTextColor(Color.argb((int)90, (int)255, (int)255, (int)255));
        this.aiChatInput.setMinLines(1);
        this.aiChatInput.setMaxLines(4);
        bottomArea.addView((View)this.aiChatInput, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));

        this.voiceToggleBtn = this.button("🎤");
        this.voiceToggleBtn.setBackgroundColor(0);
        this.voiceToggleBtn.setTextColor(Color.rgb((int)200, (int)200, (int)200));
        this.voiceToggleBtn.setTextSize(2, 20.0f);
        this.voiceToggleBtn.setOnClickListener(v -> {
            if (this.holdToSpeakBtn.getVisibility() == 8) {
                if (this.checkAudioPermission()) {
                    if (android.speech.SpeechRecognizer.isRecognitionAvailable((Context)this)) {
                        this.switchToVoiceInput();
                    } else {
                        Toast.makeText((Context)this, "检测到系统后台语音服务未开启，已为您拉起系统语音输入面板", Toast.LENGTH_SHORT).show();
                        this.startSpeechIntent();
                    }
                }
            } else {
                this.switchToTextInput();
            }
        });
        bottomArea.addView((View)this.voiceToggleBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(48), this.dp(48)));

        this.aiSendButton = this.button("\u53d1\u9001");
        this.aiSendButtonDefaultBackground = this.aiSendButton.getBackground();
        this.aiSendButton.setOnClickListener(v -> this.handleAiSendButtonClick());
        bottomArea.addView((View)this.aiSendButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(70), this.dp(48)));
        mainContent.addView((View)bottomArea);
        this.aiDrawerLayout.addView((View)mainContent, (ViewGroup.LayoutParams)new DrawerLayout.LayoutParams(-1, -1));
        LinearLayout drawerContent = new LinearLayout((Context)this);
        drawerContent.setOrientation(1);
        drawerContent.setBackgroundColor(Color.rgb((int)30, (int)30, (int)30));
        Button newSessionBtn = this.button("\u2795 \u65b0\u5efa\u8bdd\u9898");
        newSessionBtn.setPadding(this.dp(16), this.dp(16), this.dp(16), this.dp(16));
        newSessionBtn.setOnClickListener(v -> {
            this.createNewAiSession();
            this.aiDrawerLayout.closeDrawer(3);
        });
        drawerContent.addView((View)newSessionBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
        ScrollView drawerScroll = new ScrollView((Context)this);
        this.aiSessionListDrawer = new LinearLayout((Context)this);
        this.aiSessionListDrawer.setOrientation(1);
        drawerScroll.addView((View)this.aiSessionListDrawer);
        drawerContent.addView((View)drawerScroll, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, 0, 1.0f));
        DrawerLayout.LayoutParams drawerParams = new DrawerLayout.LayoutParams(this.dp(240), -1);
        drawerParams.gravity = 3;
        this.aiDrawerLayout.addView((View)drawerContent, (ViewGroup.LayoutParams)drawerParams);
        this.aiTabPage.addView((View)this.aiDrawerLayout, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -1));
    }

    private void buildUi(String sharedText) {
        ScrollView scrollView;
        LinearLayout shell = new LinearLayout((Context)this);
        shell.setOrientation(1);
        shell.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
        shell.setFitsSystemWindows(true);
        this.mainScrollView = scrollView = new ScrollView((Context)this);
        LinearLayout root = new LinearLayout((Context)this);
        root.setOrientation(1);
        root.setPadding(this.dp(20), this.dp(24), this.dp(20), this.dp(24));
        scrollView.addView((View)root);
        this.swipeRefresh = new SwipeRefreshLayout((Context)this);
        this.swipeRefresh.addView((View)scrollView);
        this.swipeRefresh.setColorSchemeColors(new int[]{Color.rgb((int)59, (int)130, (int)246)});
        this.swipeRefresh.setProgressBackgroundColorSchemeColor(Color.rgb((int)30, (int)30, (int)30));
        this.swipeRefresh.setOnRefreshListener(() -> {
            this.refreshSettings();
            if (this.yanmTabPage != null && this.yanmTabPage.getVisibility() == 0) {
                this.refreshYanm();
            } else if (this.desktopExtensionTabPage != null && this.desktopExtensionTabPage.getVisibility() == 0) {
                this.refreshExtensions();
            } else {
                this.swipeRefresh.setRefreshing(false);
            }
        });
        this.yanmTabPage = this.createTabPage();
        this.mobileExtensionTabPage = this.createTabPage();
        this.desktopExtensionTabPage = this.createTabPage();
        this.aiTabPage = this.createTabPage();
        this.profileTabPage = this.createTabPage();
        root.addView((View)this.yanmTabPage);
        root.addView((View)this.mobileExtensionTabPage);
        root.addView((View)this.desktopExtensionTabPage);
        root.addView((View)this.profileTabPage);
        TextView yanmTitle = this.textView("\u71d5\u5e55", 28, -1, true);
        this.yanmTabPage.addView((View)yanmTitle);
        this.yanmTabPage.addView((View)this.textView("\u67e5\u770b\u548c\u64cd\u4f5c\u7535\u8111\u7aef\u540c\u6b65\u7684\u71d5\u5e55\u7ec4\u4ef6\u3002", 14, Color.rgb((int)182, (int)194, (int)214), false));
        this.yanmList = new GridLayout((Context)this);
        this.yanmList.setColumnCount(2);
        this.yanmList.setAlignmentMode(0);
        this.yanmList.setUseDefaultMargins(false);
        this.yanmTabPage.addView((View)this.yanmList, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
        this.mobileExtensionTabPage.addView((View)this.textView("\u624b\u673a\u6269\u5c55", 28, -1, true));
        this.mobileExtensionTabPage.addView((View)this.textView("\u7ba1\u7406\u548c\u6d4b\u8bd5\u53ea\u5728\u624b\u673a\u7aef\u8fd0\u884c\u7684 mobile-js \u6269\u5c55\u3002", 14, Color.rgb((int)182, (int)194, (int)214), false));
        this.buildMobileExtensionEditor(this.mobileExtensionTabPage);
        this.desktopExtensionTabPage.addView((View)this.textView("\u7535\u8111\u6269\u5c55", 28, -1, true));
        this.desktopExtensionTabPage.addView((View)this.textView("\u4ece\u624b\u673a\u89e6\u53d1\u540c\u8d26\u53f7\u7535\u8111\u7aef\u5df2\u540c\u6b65\u7684\u6269\u5c55\u3002", 14, Color.rgb((int)182, (int)194, (int)214), false));
        this.searchDesktopExtensionsInput = new EditText((Context)this);
        this.searchDesktopExtensionsInput.setHint((CharSequence)"\u641c\u7d22\u7b5b\u9009\u6269\u5c55...");
        this.searchDesktopExtensionsInput.setTextColor(-1);
        this.searchDesktopExtensionsInput.setHintTextColor(Color.rgb((int)148, (int)163, (int)184));
        this.searchDesktopExtensionsInput.setBackgroundColor(Color.rgb((int)15, (int)23, (int)42));
        this.searchDesktopExtensionsInput.setPadding(this.dp(10), this.dp(8), this.dp(10), this.dp(8));
        this.searchDesktopExtensionsInput.setSingleLine(true);
        LinearLayout.LayoutParams searchParams = new LinearLayout.LayoutParams(-1, -2);
        searchParams.setMargins(0, this.dp(10), 0, this.dp(10));
        this.desktopExtensionTabPage.addView((View)this.searchDesktopExtensionsInput, (ViewGroup.LayoutParams)searchParams);
        this.searchDesktopExtensionsInput.addTextChangedListener(new TextWatcher(){

            public void beforeTextChanged(CharSequence s, int start, int count, int after) {
            }

            public void onTextChanged(CharSequence s, int start, int before, int count) {
                if (MainActivity.this.currentDesktopExtensions != null) {
                    MainActivity.this.renderExtensions(MainActivity.this.currentDesktopExtensions);
                }
            }

            public void afterTextChanged(Editable s) {
            }
        });
        this.extensionList = new LinearLayout((Context)this);
        this.extensionList.setOrientation(1);
        this.desktopExtensionTabPage.addView((View)this.extensionList);
        this.renderCachedExtensions();
        this.profileTabPage.addView((View)this.textView("\u6211\u7684", 28, -1, true));
        this.profileTabPage.addView((View)this.textView("\u767b\u5f55\u3001\u53d1\u9001\u6d88\u606f\u3001\u60ac\u6d6e\u8f6e\u76d8\u548c\u8bca\u65ad\u4fe1\u606f\u3002", 14, Color.rgb((int)182, (int)194, (int)214), false));
        this.baseUrlInput = this.input("\u4e91\u7aef\u5730\u5740", this.prefs.getString("baseUrl", DEFAULT_BASE_URL));
        this.emailInput = this.input("\u90ae\u7bb1", this.prefs.getString("email", ""));
        this.passwordInput = this.input("\u5bc6\u7801", this.prefs.getString("password", ""));
        this.passwordInput.setInputType(129);
        String initialText = sharedText == null || sharedText.trim().isEmpty() ? "hi" : sharedText;
        this.textInput = this.multiInput("\u53d1\u9001\u7ed9\u7535\u8111\u7684\u6587\u672c / \u94fe\u63a5", initialText);
        this.loginButton = this.button("\u767b\u5f55");
        this.statusText = this.textView("", 14, Color.rgb((int)147, (int)197, (int)253), false);
        this.statusText.setTextIsSelectable(true);
        this.statusText.setMinLines(3);
        Button loginSettingsButton = this.button("\u8d26\u53f7");
        this.profileTabPage.addView((View)loginSettingsButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(48)));
        loginSettingsButton.setOnClickListener(v -> {
            LinearLayout dialogLayout = new LinearLayout((Context)this);
            dialogLayout.setOrientation(1);
            dialogLayout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
            dialogLayout.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
            if (this.baseUrlInput.getParent() != null) {
                ((ViewGroup)this.baseUrlInput.getParent()).removeView((View)this.baseUrlInput);
            }
            if (this.emailInput.getParent() != null) {
                ((ViewGroup)this.emailInput.getParent()).removeView((View)this.emailInput);
            }
            if (this.passwordInput.getParent() != null) {
                ((ViewGroup)this.passwordInput.getParent()).removeView((View)this.passwordInput);
            }
            this.baseUrlInput.setVisibility(0);
            dialogLayout.addView((View)this.baseUrlInput);
            dialogLayout.addView((View)this.emailInput);
            dialogLayout.addView((View)this.passwordInput);
            LinearLayout buttonsLayout = new LinearLayout((Context)this);
            buttonsLayout.setOrientation(0);
            buttonsLayout.setPadding(0, this.dp(10), 0, 0);
            Button logoutBtn = this.button("\u9000\u51fa");
            if (this.loginButton.getParent() != null) {
                ((ViewGroup)this.loginButton.getParent()).removeView((View)this.loginButton);
            }
            buttonsLayout.addView((View)this.loginButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
            buttonsLayout.addView((View)logoutBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
            dialogLayout.addView((View)buttonsLayout);
            AlertDialog dialog = new AlertDialog.Builder((Context)this, 16974545).setTitle((CharSequence)"\u8d26\u53f7").setView((View)dialogLayout).setPositiveButton((CharSequence)"\u5173\u95ed", null).show();
            logoutBtn.setOnClickListener(v1 -> {
                this.prefs.edit().putString("token", "").apply();
                this.setStatus("\u5df2\u6e05\u9664\u672c\u5730\u767b\u5f55\u6001\u3002");
                if (this.loginButton != null) {
                    this.loginButton.setEnabled(true);
                }
                dialog.dismiss();
            });
        });
        Button sendTextButton = this.button("\u5411\u7535\u8111\u53d1\u9001\u6587\u672c");
        this.profileTabPage.addView((View)sendTextButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(48)));
        sendTextButton.setOnClickListener(v -> {
            LinearLayout dialogLayout = new LinearLayout((Context)this);
            dialogLayout.setOrientation(1);
            dialogLayout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
            dialogLayout.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
            if (this.textInput.getParent() != null) {
                ((ViewGroup)this.textInput.getParent()).removeView((View)this.textInput);
            }
            dialogLayout.addView((View)this.textInput);
            Button sendBtn = this.button("\u53d1\u9001");
            dialogLayout.addView((View)sendBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(48)));
            AlertDialog dialog = new AlertDialog.Builder((Context)this, 16974545).setTitle((CharSequence)"\u53d1\u9001\u6d88\u606f\u5230\u7535\u8111").setView((View)dialogLayout).setPositiveButton((CharSequence)"\u5173\u95ed", null).show();
            sendBtn.setOnClickListener(v1 -> {
                this.sendToDesktop();
                dialog.dismiss();
            });
        });
        this.profileTabPage.addView((View)this.sectionTitle("\u5168\u5c40\u8f6e\u76d8"));
        LinearLayout wheelButtons = new LinearLayout((Context)this);
        wheelButtons.setOrientation(1);
        this.overlayButton = this.button(FloatingWheelService.isRunning ? "\u5173\u95ed\u60ac\u6d6e\u8f6e\u76d8" : "\u6253\u5f00\u60ac\u6d6e\u8f6e\u76d8");
        Button accessibilityButton = this.button("\u65e0\u969c\u788d\u670d\u52a1");
        wheelButtons.addView((View)this.overlayButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(44)));
        wheelButtons.addView((View)accessibilityButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(44)));
        this.profileTabPage.addView((View)wheelButtons);
        this.profileTabPage.addView((View)this.statusText);
        LinearLayout logButtons = new LinearLayout((Context)this);
        logButtons.setOrientation(0);
        Button copyLogButton = this.button("\u590d\u5236");
        Button clearLogButton = this.button("\u6e05\u7a7a");
        logButtons.addView((View)copyLogButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        logButtons.addView((View)clearLogButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        this.profileTabPage.addView((View)logButtons);
        this.profileTabPage.addView((View)this.textView("\u8bbe\u5907 ID\uff1a" + this.deviceId, 11, Color.rgb((int)100, (int)116, (int)139), false));
        long installTime = 0L;
        try {
            installTime = this.getPackageManager().getPackageInfo((String)this.getPackageName(), (int)0).lastUpdateTime;
        }
        catch (Exception exception) {
            // empty catch block
        }
        if (installTime > 0L) {
            String timeStr = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault()).format(new Date(installTime));
            this.profileTabPage.addView((View)this.textView("\u7f16\u8bd1\u5b89\u88c5\uff1a" + timeStr, 11, Color.rgb((int)100, (int)116, (int)139), false));
        }
        this.loginButton.setOnClickListener(v -> this.loginAndRegister());
        this.overlayButton.setOnClickListener(v -> {
            if (FloatingWheelService.isRunning) {
                this.stopService(new Intent((Context)this, FloatingWheelService.class));
                this.overlayButton.setText((CharSequence)"\u6253\u5f00\u60ac\u6d6e\u8f6e\u76d8");
                this.setStatus("\u60ac\u6d6e\u8f6e\u76d8\u5df2\u5173\u95ed\u3002");
                this.prefs.edit().putBoolean("floatingWheelEnabled", false).apply();
            } else {
                this.startFloatingWheel();
                this.overlayButton.setText((CharSequence)"\u5173\u95ed\u60ac\u6d6e\u8f6e\u76d8");
                this.prefs.edit().putBoolean("floatingWheelEnabled", true).apply();
            }
        });
        accessibilityButton.setOnClickListener(v -> this.openAccessibilitySettings());
        copyLogButton.setOnClickListener(v -> this.copyDiagnostics());
        clearLogButton.setOnClickListener(v -> {
            this.diagnosticLog.setLength(0);
            MobileDiagnostics.clear((Context)this);
            this.statusText.setText((CharSequence)"");
            this.setStatus("\u65e5\u5fd7\u5df2\u6e05\u7a7a\u3002");
        });
        shell.addView((View)this.swipeRefresh, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, 0, 1.0f));
        this.setupAiTabPage();
        this.loadAiHistory();
        shell.addView((View)this.aiTabPage, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, 0, 1.0f));
        shell.addView((View)this.buildBottomTabs(), (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(64)));
        this.setContentView((View)shell);
        this.selectTab("yanm");
        this.setStatus(this.prefs.getString("token", "").trim().isEmpty() ? "\u8bf7\u5148\u767b\u5f55\u71d5\u5b50\u8d26\u53f7\u3002" : "\u5df2\u52a0\u8f7d\u672c\u5730\u767b\u5f55\u6001\u3002");
        this.renderCachedYanm();
        if (!this.prefs.getString("token", "").trim().isEmpty()) {
            if (this.loginButton != null) {
                this.loginButton.setEnabled(false);
            }
            this.refreshExtensions(true);
            this.refreshYanm(true);
        }
    }

    private LinearLayout createTabPage() {
        LinearLayout page = new LinearLayout((Context)this);
        page.setOrientation(1);
        page.setVisibility(8);
        return page;
    }

    private LinearLayout buildBottomTabs() {
        LinearLayout tabs = new LinearLayout((Context)this);
        tabs.setOrientation(0);
        tabs.setGravity(16);
        tabs.setPadding(this.dp(4), this.dp(2), this.dp(4), this.dp(2));
        tabs.setBackgroundColor(Color.rgb((int)17, (int)17, (int)17));
        this.yanmTabButton = this.tabButton("\u71d5\u5e55", 17301591, "yanm");
        this.mobileExtensionTabButton = this.tabButton("\u624b\u673a", 17301558, "mobile");
        this.aiTabButton = this.tabButton("AI", 17301661, "ai");
        this.desktopExtensionTabButton = this.tabButton("\u7535\u8111", 17301578, "desktop");
        this.profileTabButton = this.tabButton("\u6211\u7684", 17301576, "profile");
        tabs.addView(this.yanmTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.mobileExtensionTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.aiTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.desktopExtensionTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.profileTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        return tabs;
    }

    private View tabButton(String text, int iconResId, String key) {
        LinearLayout container = new LinearLayout((Context)this);
        container.setOrientation(1);
        container.setGravity(17);
        container.setPadding(0, this.dp(6), 0, this.dp(6));
        container.setClickable(true);
        container.setFocusable(true);
        ImageView iconView = new ImageView((Context)this);
        iconView.setImageResource(iconResId);
        LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(this.dp(22), this.dp(22));
        iconView.setLayoutParams((ViewGroup.LayoutParams)iconParams);
        TextView textView = new TextView((Context)this);
        textView.setText((CharSequence)text);
        textView.setTextSize(10.0f);
        textView.setGravity(17);
        LinearLayout.LayoutParams textParams = new LinearLayout.LayoutParams(-2, -2);
        textParams.setMargins(0, this.dp(3), 0, 0);
        textView.setLayoutParams((ViewGroup.LayoutParams)textParams);
        container.addView((View)iconView);
        container.addView((View)textView);
        container.setTag((Object)new View[]{iconView, textView});
        container.setOnClickListener(v -> this.selectTab(key));
        return container;
    }

    private void selectTab(String key) {
        if (this.yanmTabPage == null || this.mobileExtensionTabPage == null || this.desktopExtensionTabPage == null || this.profileTabPage == null || this.aiTabPage == null) {
            return;
        }
        boolean isYanm = "yanm".equals(key);
        boolean isMobile = "mobile".equals(key);
        boolean isAi = "ai".equals(key);
        boolean isDesktop = "desktop".equals(key);
        boolean isProfile = "profile".equals(key);
        this.yanmTabPage.setVisibility(isYanm ? 0 : 8);
        this.mobileExtensionTabPage.setVisibility(isMobile ? 0 : 8);
        this.swipeRefresh.setVisibility(isAi ? 8 : 0);
        this.aiTabPage.setVisibility(isAi ? 0 : 8);
        this.desktopExtensionTabPage.setVisibility(isDesktop ? 0 : 8);
        this.profileTabPage.setVisibility(isProfile ? 0 : 8);
        this.styleTabButton(this.yanmTabButton, isYanm);
        this.styleTabButton(this.mobileExtensionTabButton, isMobile);
        this.styleTabButton(this.aiTabButton, isAi);
        this.styleTabButton(this.desktopExtensionTabButton, isDesktop);
        this.styleTabButton(this.profileTabButton, isProfile);
        if (this.mainScrollView != null) {
            this.mainScrollView.post(() -> this.mainScrollView.smoothScrollTo(0, 0));
        }
    }

    private void styleTabButton(View tabView, boolean selected) {
        if (tabView == null) {
            return;
        }
        int color = selected ? Color.rgb((int)34, (int)211, (int)238) : Color.rgb((int)100, (int)116, (int)139);
        View[] tag = (View[])tabView.getTag();
        if (tag != null && tag.length == 2) {
            ImageView iconView = (ImageView)tag[0];
            TextView textView = (TextView)tag[1];
            iconView.setColorFilter(color);
            textView.setTextColor(color);
        }
        GradientDrawable background = new GradientDrawable();
        background.setCornerRadius((float)this.dp(12));
        background.setColor(selected ? Color.argb((int)20, (int)34, (int)211, (int)238) : 0);
        tabView.setBackground((Drawable)background);
    }

    private void focusTextComposer(String status) {
        this.setStatus(status);
        this.textInput.requestFocus();
        this.scrollToView((View)this.textInput);
        this.showKeyboard((View)this.textInput);
    }

    private void buildMobileExtensionEditor(LinearLayout root) {
        LinearLayout header = new LinearLayout((Context)this);
        header.setOrientation(0);
        header.setGravity(16);
        this.mobileExtensionSectionTitle = this.sectionTitle("\u624b\u673a\u6269\u5c55\u7f16\u8f91\u5668");
        header.addView((View)this.mobileExtensionSectionTitle, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        Button promptButton = this.button("\u590d\u5236\u63d0\u793a\u8bcd");
        header.addView((View)promptButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, this.dp(40)));
        root.addView((View)header);
        HorizontalScrollView editorScroll = new HorizontalScrollView((Context)this);
        editorScroll.setHorizontalScrollBarEnabled(false);
        LinearLayout editorRow = new LinearLayout((Context)this);
        editorRow.setOrientation(0);
        editorRow.setPadding(0, 0, this.dp(8), 0);
        LinearLayout helperPanel = this.card();
        helperPanel.setLayoutParams((ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(280), -2));
        helperPanel.addView((View)this.textView("\u624b\u52a8\u8c03\u6574", 16, -1, true));
        helperPanel.addView((View)this.textView("\u4f18\u5148\u505a\u672c\u673a\u53ef\u6267\u884c\u6269\u5c55\uff0c\u518d\u8865\u5145\u53d1\u5230\u7535\u8111\u3002\u6a21\u677f\u70b9\u51fb\u540e\u4f1a\u8986\u76d6\u53f3\u4fa7 JSON \u533a\u3002", 12, Color.rgb((int)182, (int)194, (int)214), false));
        this.mobileExtensionIdInput = this.input("\u6269\u5c55 ID", "mobile-copy-shared-text");
        this.mobileExtensionNameInput = this.input("\u6269\u5c55\u540d\u79f0", "\u590d\u5236\u5f53\u524d\u8f93\u5165");
        this.mobileExtensionIconInput = this.input("\u56fe\u6807", "mdi:content-copy");
        this.mobileExtensionDescriptionInput = this.multiInput("\u63cf\u8ff0", "\u628a\u5f53\u524d\u8f93\u5165\u6846\u5185\u5bb9\u590d\u5236\u5230\u624b\u673a\u526a\u8d34\u677f\u3002");
        this.mobileExtensionDescriptionInput.setMinLines(3);
        helperPanel.addView((View)this.mobileExtensionIdInput);
        helperPanel.addView((View)this.mobileExtensionNameInput);
        helperPanel.addView((View)this.mobileExtensionIconInput);
        helperPanel.addView((View)this.mobileExtensionDescriptionInput);
        LinearLayout helperActions = new LinearLayout((Context)this);
        helperActions.setOrientation(0);
        Button applyMetaButton = this.button("\u5e94\u7528\u5de6\u4fa7\u5b57\u6bb5");
        Button saveDraftButton = this.button("\u4fdd\u5b58\u6269\u5c55");
        helperActions.addView((View)applyMetaButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(42), 1.0f));
        helperActions.addView((View)saveDraftButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(42), 1.0f));
        helperPanel.addView((View)helperActions);
        helperPanel.addView((View)this.textView("\u6a21\u677f\u793a\u4f8b", 15, -1, true));
        helperPanel.addView((View)this.textView("\u672c\u673a\u80fd\u529b\u4f18\u5148\uff1a\u526a\u8d34\u677f\u3001\u6d4f\u89c8\u5668\u3001\u6587\u4ef6\u3001\u7f51\u7edc\u8bf7\u6c42\u3002", 12, Color.rgb((int)103, (int)232, (int)249), false));
        for (MobileExtensionTemplate template : this.buildMobileExtensionTemplates()) {
            Button templateButton = this.button(template.name);
            templateButton.setAllCaps(false);
            templateButton.setOnClickListener(v -> this.replaceDraftWithTemplate(template));
            helperPanel.addView((View)templateButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(42)));
            helperPanel.addView((View)this.textView(template.description, 11, Color.rgb((int)148, (int)163, (int)184), false));
        }
        LinearLayout codePanel = this.card();
        LinearLayout.LayoutParams codeParams = new LinearLayout.LayoutParams(this.dp(460), -2);
        codeParams.setMargins(this.dp(12), this.dp(8), 0, this.dp(8));
        codePanel.setLayoutParams((ViewGroup.LayoutParams)codeParams);
        codePanel.addView((View)this.textView("JSON \u533a", 16, -1, true));
        this.mobileExtensionInput = this.multiInput("\u624b\u673a\u6269\u5c55 JSON / mobile-js", this.prefs.getString("mobileExtensionDraft", this.defaultMobileExtensionJson()));
        this.mobileExtensionInput.setMinLines(18);
        codePanel.addView((View)this.mobileExtensionInput, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
        Button pasteJsonButton = this.button("\u4e00\u952e\u7c98\u8d34 JSON");
        codePanel.addView((View)pasteJsonButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(42)));
        LinearLayout bottomActions = new LinearLayout((Context)this);
        bottomActions.setOrientation(0);
        Button testButton = this.button("\u6d4b\u8bd5\u6269\u5c55");
        Button runButton = this.button("\u4fdd\u5b58\u6269\u5c55");
        bottomActions.addView((View)testButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        bottomActions.addView((View)runButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        codePanel.addView((View)bottomActions);
        this.mobileExtensionTestResult = this.textView("\u6d4b\u8bd5\u7ed3\u679c\u4f1a\u663e\u793a\u5728\u8fd9\u91cc\u3002", 12, Color.rgb((int)148, (int)163, (int)184), false);
        this.mobileExtensionTestResult.setTextIsSelectable(true);
        this.mobileExtensionTestResult.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        this.mobileExtensionTestResult.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
        codePanel.addView((View)this.mobileExtensionTestResult);
        editorRow.addView((View)helperPanel);
        editorRow.addView((View)codePanel);
        editorScroll.addView((View)editorRow);
        root.addView((View)editorScroll);
        this.mobileExtensionManagerList = this.card();
        this.mobileExtensionManagerList.addView((View)this.textView("\u672c\u673a\u624b\u673a\u6269\u5c55", 16, -1, true));
        root.addView((View)this.mobileExtensionManagerList);
        promptButton.setOnClickListener(v -> this.copyMobileExtensionPrompt());
        applyMetaButton.setOnClickListener(v -> this.applyMetadataToDraft());
        saveDraftButton.setOnClickListener(v -> this.saveMobileExtensionDraft());
        pasteJsonButton.setOnClickListener(v -> this.pasteJsonIntoMobileExtensionEditor());
        testButton.setOnClickListener(v -> this.runMobileScript());
        runButton.setOnClickListener(v -> this.saveMobileExtensionDraft());
        this.updateMobileExtensionFieldsFromDraft();
        this.renderLocalMobileExtensions();
    }

    private void openMobileExtensionEditor(String status) {
        this.setStatus(status);
        this.updateMobileExtensionFieldsFromDraft();
        this.mobileExtensionInput.requestFocus();
        this.scrollToView((View)this.mobileExtensionSectionTitle);
        this.showKeyboard((View)this.mobileExtensionInput);
    }

    private void replaceDraftWithTemplate(MobileExtensionTemplate template) {
        this.mobileExtensionInput.setText((CharSequence)template.json);
        this.mobileExtensionInput.setSelection(template.json.length());
        this.mobileExtensionNameInput.setText((CharSequence)template.name);
        this.mobileExtensionDescriptionInput.setText((CharSequence)template.description);
        this.validateMobileExtensionJson(true);
        this.setStatus("\u6a21\u677f\u5df2\u8986\u76d6 JSON\uff1a" + template.name);
    }

    private void pasteJsonIntoMobileExtensionEditor() {
        try {
            String text;
            ClipboardManager manager = (ClipboardManager)this.getSystemService("clipboard");
            ClipData clip = manager == null ? null : manager.getPrimaryClip();
            String value = clip == null || clip.getItemCount() == 0 ? "" : clip.getItemAt(0).coerceToText((Context)this).toString();
            String string = text = value == null ? "" : value.toString().trim();
            if (text.isEmpty()) {
                throw new IllegalStateException("\u526a\u8d34\u677f\u6ca1\u6709 JSON \u5185\u5bb9\u3002");
            }
            this.mobileExtensionInput.setText((CharSequence)"");
            JSONObject json = new JSONObject(text);
            String pretty = json.toString(2);
            this.mobileExtensionInput.setText((CharSequence)pretty);
            this.mobileExtensionInput.setSelection(pretty.length());
            this.updateMobileExtensionFieldsFromDraft();
            this.updateMobileScriptResult("JSON \u683c\u5f0f\u6b63\u786e\uff1a" + MainActivity.firstNonEmpty(json.optString("name"), json.optString("id"), "\u672a\u547d\u540d\u6269\u5c55"), false);
            this.setStatus("\u5df2\u7c98\u8d34\u5e76\u68c0\u6d4b JSON \u683c\u5f0f\u3002");
        }
        catch (Exception ex) {
            this.mobileExtensionInput.setText((CharSequence)"");
            this.updateMobileScriptResult("JSON \u683c\u5f0f\u9519\u8bef\uff1a" + ex.getMessage(), true);
            this.setStatus("\u7c98\u8d34 JSON \u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private boolean validateMobileExtensionJson(boolean updateResult) {
        try {
            JSONObject json = this.parseDraftObject();
            if (updateResult) {
                this.updateMobileScriptResult("JSON \u683c\u5f0f\u6b63\u786e\uff1a" + MainActivity.firstNonEmpty(json.optString("name"), json.optString("id"), "\u672a\u547d\u540d\u6269\u5c55"), false);
            }
            return true;
        }
        catch (Exception ex) {
            if (updateResult) {
                this.updateMobileScriptResult("JSON \u683c\u5f0f\u9519\u8bef\uff1a" + ex.getMessage(), true);
            }
            return false;
        }
    }

    private void applyMetadataToDraft() {
        try {
            JSONObject json = this.parseDraftObject();
            json.put("id", (Object)this.mobileExtensionIdInput.getText().toString().trim());
            json.put("name", (Object)this.mobileExtensionNameInput.getText().toString().trim());
            json.put("description", (Object)this.mobileExtensionDescriptionInput.getText().toString().trim());
            json.put("icon", (Object)this.mobileExtensionIconInput.getText().toString().trim());
            String pretty = json.toString(2);
            this.mobileExtensionInput.setText((CharSequence)pretty);
            this.setStatus("\u5de6\u4fa7\u5b57\u6bb5\u5df2\u5e94\u7528\u5230 JSON\u3002");
        }
        catch (Exception ex) {
            this.setStatus("\u5e94\u7528\u5de6\u4fa7\u5b57\u6bb5\u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private JSONObject parseDraftObject() throws Exception {
        String draft = this.mobileExtensionInput.getText().toString().trim();
        if (draft.isEmpty()) {
            return new JSONObject(this.defaultMobileExtensionJson());
        }
        if (!draft.startsWith("{")) {
            throw new IllegalStateException("\u53f3\u4fa7\u4e0d\u662f JSON \u5bf9\u8c61\uff0c\u65e0\u6cd5\u5e94\u7528\u5b57\u6bb5\u3002");
        }
        return new JSONObject(draft);
    }

    private void updateMobileExtensionFieldsFromDraft() {
        try {
            JSONObject json = this.parseDraftObject();
            this.mobileExtensionIdInput.setText((CharSequence)MainActivity.firstNonEmpty(json.optString("id"), "mobile-copy-shared-text"));
            this.mobileExtensionNameInput.setText((CharSequence)MainActivity.firstNonEmpty(json.optString("name"), "\u590d\u5236\u5f53\u524d\u8f93\u5165"));
            this.mobileExtensionDescriptionInput.setText((CharSequence)MainActivity.firstNonEmpty(json.optString("description"), "\u624b\u673a\u672c\u5730\u6269\u5c55"));
            this.mobileExtensionIconInput.setText((CharSequence)MainActivity.firstNonEmpty(json.optString("icon"), "mdi:content-copy"));
        }
        catch (Exception exception) {
            // empty catch block
        }
    }

    private File resolveMobileScriptFile(String name) throws Exception {
        String value = MainActivity.firstNonEmpty(name, "notes.txt").replace("\\", "_").replace("/", "_").replace("..", "_");
        File dir = this.getExternalFilesDir(Environment.DIRECTORY_DOCUMENTS);
        if (dir == null) {
            dir = new File(this.getFilesDir(), "mobile-script-files");
        }
        if (!dir.exists() && !dir.mkdirs()) {
            throw new IllegalStateException("\u65e0\u6cd5\u521b\u5efa\u624b\u673a\u6269\u5c55\u6587\u4ef6\u76ee\u5f55");
        }
        return new File(dir, value);
    }

    private static String buildJsonErrorResult(String message) {
        try {
            return new JSONObject().put("ok", false).put("error", (Object)MainActivity.firstNonEmpty(message, "unknown error")).toString();
        }
        catch (Exception ignored) {
            return "{\"ok\":false,\"error\":\"unknown error\"}";
        }
    }

    private void scrollToView(View view) {
        if (this.mainScrollView == null || view == null) {
            return;
        }
        this.mainScrollView.post(() -> this.mainScrollView.smoothScrollTo(0, Math.max(0, view.getTop() - this.dp(16))));
    }

    private void showKeyboard(View view) {
        view.postDelayed(() -> {
            InputMethodManager manager = (InputMethodManager)this.getSystemService("input_method");
            if (manager != null) {
                manager.showSoftInput(view, 1);
            }
        }, 250L);
    }

    private void startFloatingWheel() {
        if (!Settings.canDrawOverlays((Context)this)) {
            Intent intent = new Intent("android.settings.action.MANAGE_OVERLAY_PERMISSION", Uri.parse((String)("package:" + this.getPackageName())));
            this.startActivity(intent);
            this.setStatus("\u8bf7\u5148\u5f00\u542f\u201c\u5141\u8bb8\u663e\u793a\u5728\u5176\u4ed6\u5e94\u7528\u4e0a\u5c42\u201d\uff0c\u8fd4\u56de\u540e\u518d\u6b21\u70b9\u51fb\u5f00\u542f\u60ac\u6d6e\u8f6e\u76d8\u3002");
            return;
        }
        Intent intent = new Intent((Context)this, FloatingWheelService.class);
        if (Build.VERSION.SDK_INT >= 26) {
            this.startService(intent);
        } else {
            this.startService(intent);
        }
        this.prefs.edit().putBoolean("floatingWheelEnabled", true).apply();
        this.setStatus("\u60ac\u6d6e\u8f6e\u76d8\u5df2\u542f\u52a8\u3002\u70b9\u51fb\u5c4f\u5e55\u4e0a\u7684\u201c\u71d5\u201d\u6309\u94ae\u6253\u5f00\u624b\u673a\u8f6e\u76d8\u3002");
    }

    private void startFloatingWheelIfPermitted() {
        if (!Settings.canDrawOverlays((Context)this)) {
            return;
        }
        boolean isEnabled = this.prefs.getBoolean("floatingWheelEnabled", true);
        if (!isEnabled) {
            return;
        }
        try {
            this.startService(new Intent((Context)this, FloatingWheelService.class));
        }
        catch (Exception ex) {
            this.setStatus("\u60ac\u6d6e\u8f6e\u76d8\u81ea\u52a8\u542f\u52a8\u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private void openAccessibilitySettings() {
        Intent intent = new Intent("android.settings.ACCESSIBILITY_SETTINGS");
        this.startActivity(intent);
        this.setStatus("\u8bf7\u5728\u65e0\u969c\u788d\u8bbe\u7f6e\u4e2d\u5f00\u542f\u201c\u71d5\u5b50\u79fb\u52a8\u7aef\u201d\uff0c\u7528\u4e8e\u622a\u56fe\u548c\u540e\u7eed\u5168\u5c40\u624b\u52bf\u80fd\u529b\u3002");
    }

    private void copyMobileExtensionPrompt() {
        String prompt = "\u4f60\u6b63\u5728\u4e3a\u71d5\u5b50\u79fb\u52a8\u7aef\u7f16\u5199\u624b\u673a\u6269\u5c55\u3002\u53ea\u5141\u8bb8\u8f93\u51fa JSON\uff0c\u4e0d\u8981\u89e3\u91ca\u3002\\n\u8fd0\u884c\u65f6\u4f7f\u7528 runtime=\\\"mobile-js\\\"\uff0c\u4e0d\u8981\u4f7f\u7528 C#\u3001PowerShell\u3001Windows \u8def\u5f84\u3001WPF \u6216\u684c\u9762 API\u3002\\n\u4f18\u5148\u8bbe\u8ba1\u672c\u673a\u53ef\u6267\u884c\u80fd\u529b\uff0c\u518d\u6309\u9700\u8865\u5145\u53d1\u5230\u7535\u8111\u3002\u53ef\u7528 permissions\uff1aclipboard.read\u3001clipboard.write\u3001browser.open\u3001file.read\u3001file.write\u3001http.request\u3001desktop.message\u3001share.text\u3002\\n\u811a\u672c\u5165\u53e3\u4f7f\u7528 async function run(context)\uff0c\u53ef\u8c03\u7528 context.mobile.toast(text)\u3001getSharedText()\u3001getClipboardText()\u3001setClipboardText(text)\u3001openUrl(url)\u3001pickPhoto()\u3001readTextFile(name)\u3001saveTextFile(name,text)\u3001appendTextFile(name,text)\u3001httpGet(url)\u3001httpPostJson(url,jsonText)\u3001sendToDesktop(text)\u3002\\n\u8f93\u51fa\u5b57\u6bb5\u81f3\u5c11\u5305\u542b id\u3001name\u3001version\u3001category\u3001description\u3001icon\u3001runtime\u3001permissions\u3001script.source\u3002";
        ClipboardManager manager = (ClipboardManager)this.getSystemService("clipboard");
        manager.setPrimaryClip(ClipData.newPlainText((CharSequence)"Yanzi mobile extension prompt", (CharSequence)prompt));
        this.setStatus("\u5df2\u590d\u5236\u624b\u673a\u7aef\u6269\u5c55\u63d0\u793a\u8bcd\u3002");
    }

    private void saveMobileExtensionDraft() {
        try {
            String draft = this.mobileExtensionInput.getText().toString();
            String id = MainActivity.firstNonEmpty(this.mobileExtensionIdInput.getText().toString(), "mobile-extension-draft");
            String name = MainActivity.firstNonEmpty(this.mobileExtensionNameInput.getText().toString(), "\u624b\u673a\u6269\u5c55\u8349\u7a3f");
            if (draft.trim().startsWith("{") && draft.trim().endsWith("}")) {
                JSONObject json = new JSONObject(draft);
                id = MainActivity.firstNonEmpty(json.optString("id"), id);
                name = MainActivity.firstNonEmpty(json.optString("name"), json.optString("displayName"), name);
            }
            this.prefs.edit().putString("mobileExtensionDraft", draft).putString("mobileExtensionDraftId", id).putString("mobileExtensionDraftName", name).apply();
            if (draft.trim().startsWith("{") && draft.trim().endsWith("}")) {
                this.upsertLocalMobileExtension(new JSONObject(draft));
            }
            this.updateMobileExtensionFieldsFromDraft();
            this.renderLocalMobileExtensions();
            this.setStatus("\u624b\u673a\u6269\u5c55\u8349\u7a3f\u5df2\u4fdd\u5b58\uff1a" + name + "\u3002\u53ef\u7ee7\u7eed\u7f16\u8f91\u6216\u6d4b\u8bd5\u3002");
        }
        catch (Exception ex) {
            this.setStatus("\u624b\u673a\u6269\u5c55\u4fdd\u5b58\u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private void runMobileScript() {
        try {
            WebView runner;
            String draft = this.mobileExtensionInput.getText().toString();
            this.prefs.edit().putString("mobileExtensionDraft", draft).apply();
            this.updateMobileExtensionFieldsFromDraft();
            String source = MainActivity.extractMobileScriptSource(draft);
            if (source.trim().isEmpty()) {
                throw new IllegalStateException("\u811a\u672c\u4e3a\u7a7a\u3002");
            }
            this.updateMobileScriptResult("\u6b63\u5728\u6d4b\u8bd5 JSON...", false);
            this.activeMobileScriptRunner = runner = new WebView((Context)this);
            runner.getSettings().setJavaScriptEnabled(true);
            runner.addJavascriptInterface((Object)new MobileJsBridge(), "yanziMobileJsHost");
            String html = this.buildMobileScriptHtml(source);
            runner.loadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
            this.setStatus("\u624b\u673a\u811a\u672c\u5df2\u542f\u52a8\u3002");
        }
        catch (Exception ex) {
            this.updateMobileScriptResult("\u6d4b\u8bd5\u5931\u8d25\uff1a " + ex.getMessage(), true);
            this.setStatus("\u624b\u673a\u811a\u672c\u542f\u52a8\u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private static String extractMobileScriptSource(String draft) throws Exception {
        JSONObject json;
        JSONObject script;
        String text;
        String string = text = draft == null ? "" : draft.trim();
        if (text.startsWith("{") && (script = (json = new JSONObject(text)).optJSONObject("script")) != null) {
            return script.optString("source", "");
        }
        return text;
    }

    private String defaultMobileExtensionJson() {
        return "{\n  \"id\": \"mobile-open-yanzi-site\",\n  \"name\": \"\u6253\u5f00\u71d5\u5b50\u5b98\u7f51\",\n  \"version\": \"0.1.0\",\n  \"category\": \"\u624b\u673a\u6d4f\u89c8\",\n  \"description\": \"\u5728\u624b\u673a\u6d4f\u89c8\u5668\u6253\u5f00\u71d5\u5b50\u5b98\u7f51\u3002\",\n  \"icon\": \"mdi:web\",\n  \"runtime\": \"mobile-js\",\n  \"permissions\": [\"browser.open\"],\n  \"script\": {\n    \"source\": \"async function run(context) {\\n  await context.mobile.openUrl('https://yanzi.luoluoluo.cc');\\n  context.mobile.toast('\u5df2\u6253\u5f00\u71d5\u5b50\u5b98\u7f51');\\n}\"\n  }\n}";
    }

    private void updateMobileScriptResult(String text, boolean isError) {
        if (this.mobileExtensionTestResult == null) {
            return;
        }
        this.mobileExtensionTestResult.setText((CharSequence)(text == null || text.trim().isEmpty() ? "\u6682\u65e0\u6d4b\u8bd5\u7ed3\u679c\u3002" : text));
        this.mobileExtensionTestResult.setTextColor(isError ? Color.rgb((int)248, (int)113, (int)113) : Color.rgb((int)125, (int)211, (int)252));
    }

    private JSONArray readLocalMobileExtensions() {
        try {
            return new JSONArray(this.prefs.getString("mobileExtensions", "[]"));
        }
        catch (Exception ex) {
            return new JSONArray();
        }
    }

    private void upsertLocalMobileExtension(JSONObject json) throws Exception {
        String id = MainActivity.firstNonEmpty(json.optString("id"), "mobile-extension-" + System.currentTimeMillis());
        json.put("id", (Object)id);
        JSONArray array = this.readLocalMobileExtensions();
        JSONArray next = new JSONArray();
        boolean replaced = false;
        for (int i = 0; i < array.length(); ++i) {
            JSONObject item = array.optJSONObject(i);
            if (item == null) continue;
            if (id.equals(item.optString("id"))) {
                next.put((Object)json);
                replaced = true;
                continue;
            }
            next.put((Object)item);
        }
        if (!replaced) {
            next.put((Object)json);
        }
        this.prefs.edit().putString("mobileExtensions", next.toString()).apply();
    }

    private void deleteLocalMobileExtension(String id) {
        JSONArray array = this.readLocalMobileExtensions();
        JSONArray next = new JSONArray();
        for (int i = 0; i < array.length(); ++i) {
            JSONObject item = array.optJSONObject(i);
            if (item == null || id.equals(item.optString("id"))) continue;
            next.put((Object)item);
        }
        this.prefs.edit().putString("mobileExtensions", next.toString()).apply();
        this.renderLocalMobileExtensions();
        this.setStatus("\u5df2\u5220\u9664\u624b\u673a\u6269\u5c55\uff1a" + id);
    }

    private void renderLocalMobileExtensions() {
        if (this.mobileExtensionManagerList == null) {
            return;
        }
        this.mobileExtensionManagerList.removeAllViews();
        this.mobileExtensionManagerList.addView((View)this.textView("\u672c\u673a\u624b\u673a\u6269\u5c55", 16, -1, true));
        JSONArray array = this.readLocalMobileExtensions();
        if (array.length() == 0) {
            this.mobileExtensionManagerList.addView((View)this.textView("\u6682\u65e0\u672c\u673a\u6269\u5c55\u3002\u53ef\u901a\u8fc7\u7a7a\u69fd\u6216\u7f16\u8f91\u5668\u4fdd\u5b58\u3002", 12, Color.rgb((int)148, (int)163, (int)184), false));
            return;
        }
        for (int i = 0; i < array.length(); ++i) {
            JSONObject item = array.optJSONObject(i);
            if (item == null) continue;
            String id = item.optString("id");
            String name = MainActivity.firstNonEmpty(item.optString("name"), item.optString("displayName"), id);
            LinearLayout row = new LinearLayout((Context)this);
            row.setOrientation(0);
            row.setGravity(16);
            TextView title = this.textView(name + "\n" + id, 12, -1, false);
            row.addView((View)title, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
            Button edit = this.button("\u7f16\u8f91");
            Button delete = this.button("\u5220\u9664");
            row.addView((View)edit, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(72), this.dp(40)));
            row.addView((View)delete, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(72), this.dp(40)));
            this.mobileExtensionManagerList.addView((View)row);
            edit.setOnClickListener(v -> {
                String pretty = item.toString();
                try {
                    pretty = item.toString(2);
                }
                catch (Exception exception) {
                    // empty catch block
                }
                this.mobileExtensionInput.setText((CharSequence)pretty);
                this.updateMobileExtensionFieldsFromDraft();
                this.scrollToView((View)this.mobileExtensionSectionTitle);
                this.setStatus("\u6b63\u5728\u7f16\u8f91\u624b\u673a\u6269\u5c55\uff1a" + name);
            });
            delete.setOnClickListener(v -> this.deleteLocalMobileExtension(id));
        }
    }

    private List<MobileExtensionTemplate> buildMobileExtensionTemplates() {
        ArrayList<MobileExtensionTemplate> items = new ArrayList<MobileExtensionTemplate>();
        items.add(new MobileExtensionTemplate("\u53d1\u6d88\u606f\u5230\u7535\u8111", "\u5bf9\u5e94\u6247\u5f62\u83dc\u5355\u201c\u53d1\u6d88\u606f\u201d\uff0c\u9ed8\u8ba4\u628a\u8f93\u5165\u6846\u5185\u5bb9\u53d1\u7ed9\u540c\u8d26\u53f7\u7535\u8111\u3002", MainActivity.mobileTemplateJson("mobile-send-message-to-desktop", "\u53d1\u6d88\u606f\u5230\u7535\u8111", "\u8de8\u7aef\u534f\u540c", "\u628a\u8f93\u5165\u6846\u5185\u5bb9\u53d1\u9001\u5230\u7535\u8111\u3002", "mdi:chat", new String[]{"desktop.message", "share.text"}, "async function run(context) {\n  const text = context.mobile.getSharedText() || 'hi';\n  context.mobile.toast('\u6b63\u5728\u53d1\u9001\u5230\u7535\u8111');\n  context.mobile.sendToDesktop(text);\n}")));
        items.add(new MobileExtensionTemplate("\u53d1\u7167\u7247\u5230\u7535\u8111", "\u5bf9\u5e94\u6247\u5f62\u83dc\u5355\u201c\u53d1\u7167\u7247\u201d\uff0c\u70b9\u51fb\u540e\u9009\u62e9\u672c\u673a\u76f8\u518c\u7167\u7247\u5e76\u53d1\u9001\u3002", MainActivity.mobileTemplateJson("mobile-pick-photo-to-desktop", "\u53d1\u7167\u7247\u5230\u7535\u8111", "\u8de8\u7aef\u534f\u540c", "\u9009\u62e9\u672c\u673a\u76f8\u518c\u7167\u7247\u5e76\u53d1\u9001\u5230\u7535\u8111\u3002", "mdi:image", new String[]{"photo.read", "desktop.message"}, "async function run(context) {\n  context.mobile.toast('\u8bf7\u9009\u62e9\u7167\u7247');\n  await context.mobile.pickPhoto();\n}")));
        items.add(new MobileExtensionTemplate("\u53d1\u622a\u56fe\u5230\u7535\u8111", "\u5bf9\u5e94\u6247\u5f62\u83dc\u5355\u201c\u53d1\u622a\u56fe\u201d\uff0c\u901a\u8fc7\u60ac\u6d6e\u8f6e\u76d8\u622a\u56fe\u5e76\u53d1\u9001\u3002", MainActivity.mobileTemplateJson("mobile-send-screenshot-to-desktop", "\u53d1\u622a\u56fe\u5230\u7535\u8111", "\u8de8\u7aef\u534f\u540c", "\u63d0\u793a\u4f7f\u7528\u60ac\u6d6e\u8f6e\u76d8\u622a\u56fe\u5e76\u53d1\u9001\u5230\u7535\u8111\u3002", "mdi:camera", new String[]{"screen.capture", "desktop.message"}, "async function run(context) {\n  context.mobile.toast('\u8bf7\u4ece\u6247\u5f62\u83dc\u5355\u70b9\u51fb\u53d1\u622a\u56fe');\n}")));
        items.add(new MobileExtensionTemplate("\u6253\u5f00\u71d5\u5b50\u5b98\u7f51", "\u5bf9\u5e94\u6247\u5f62\u83dc\u5355\u201c\u5b98\u7f51\u201d\uff0c\u76f4\u63a5\u6253\u5f00\u71d5\u5b50\u5b98\u7f51\u3002", MainActivity.mobileTemplateJson("mobile-open-yanzi-site", "\u6253\u5f00\u71d5\u5b50\u5b98\u7f51", "\u624b\u673a\u6d4f\u89c8", "\u5728\u624b\u673a\u6d4f\u89c8\u5668\u6253\u5f00\u71d5\u5b50\u5b98\u7f51\u3002", "mdi:web", new String[]{"browser.open"}, "async function run(context) {\n  await context.mobile.openUrl('https://yanzi.luoluoluo.cc.cd');\n  context.mobile.toast('\u5df2\u6253\u5f00\u71d5\u5b50\u5b98\u7f51');\n}")));
        items.add(new MobileExtensionTemplate("\u8fdc\u7a0b\u6269\u5c55\u5165\u53e3", "\u5bf9\u5e94\u6247\u5f62\u83dc\u5355\u201c\u8fdc\u7a0b\u6269\u5c55\u201d\uff0c\u7528\u4e8e\u4ece\u624b\u673a\u8fdb\u5165\u8fdc\u7a0b\u6269\u5c55\u5217\u8868\u3002", MainActivity.mobileTemplateJson("mobile-open-remote-extensions", "\u8fdc\u7a0b\u6269\u5c55\u5165\u53e3", "\u8de8\u7aef\u534f\u540c", "\u63d0\u793a\u4f7f\u7528\u6247\u5f62\u83dc\u5355\u8fdb\u5165\u8fdc\u7a0b\u6269\u5c55\u5217\u8868\u3002", "mdi:monitor-dashboard", new String[]{"desktop.extension"}, "async function run(context) {\n  context.mobile.toast('\u8bf7\u4ece\u6247\u5f62\u83dc\u5355\u70b9\u51fb\u8fdc\u7a0b\u6269\u5c55');\n}")));
        items.add(new MobileExtensionTemplate("\u71d5\u5e55\u5165\u53e3", "\u5bf9\u5e94\u6247\u5f62\u83dc\u5355\u201c\u71d5\u5e55\u201d\uff0c\u7528\u4e8e\u4ece\u624b\u673a\u8fdb\u5165\u71d5\u5e55\u3002", MainActivity.mobileTemplateJson("mobile-open-yanm", "\u71d5\u5e55\u5165\u53e3", "\u624b\u673a\u71d5\u5e55", "\u63d0\u793a\u4f7f\u7528\u6247\u5f62\u83dc\u5355\u8fdb\u5165\u71d5\u5e55\u3002", "mdi:monitor-dashboard", new String[]{"yanm.open"}, "async function run(context) {\n  context.mobile.toast('\u8bf7\u4ece\u6247\u5f62\u83dc\u5355\u70b9\u51fb\u71d5\u5e55');\n}")));
        return items;
    }

    private static String mobileTemplateJson(String id, String name, String category, String description, String icon, String[] permissions, String source) {
        try {
            JSONArray permissionArray = new JSONArray();
            for (String permission : permissions) {
                permissionArray.put((Object)permission);
            }
            return new JSONObject().put("id", (Object)id).put("name", (Object)name).put("version", (Object)"0.1.0").put("category", (Object)category).put("description", (Object)description).put("icon", (Object)icon).put("runtime", (Object)"mobile-js").put("permissions", (Object)permissionArray).put("script", (Object)new JSONObject().put("source", (Object)source)).toString(2);
        }
        catch (Exception ex) {
            return "{}";
        }
    }

    private void loginAndRegister() {
        if (this.loginButton != null) {
            this.loginButton.setEnabled(false);
        }
        this.setStatus("\u6b63\u5728\u767b\u5f55...");
        this.executor.execute(() -> {
            String token;
            String baseUrl = this.normalizedBaseUrl();
            String email = this.emailInput.getText().toString().trim();
            try {
                token = YanziApiClient.login(baseUrl, email, this.passwordInput.getText().toString());
            }
            catch (Exception ex) {
                this.runOnUiThread(() -> {
                    this.setStatus("\u767b\u5f55\u5931\u8d25\uff1a" + ex.getMessage());
                    if (this.loginButton != null) {
                        this.loginButton.setEnabled(true);
                    }
                });
                return;
            }
            this.runOnUiThread(() -> this.setStatus("\u767b\u5f55\u6210\u529f\uff0c\u6b63\u5728\u6ce8\u518c\u624b\u673a\u8bbe\u5907..."));
            try {
                YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                this.prefs.edit().putString("baseUrl", baseUrl).putString("email", email).putString("password", this.passwordInput.getText().toString()).putString("token", token).apply();
                this.runOnUiThread(() -> {
                    this.setStatus("\u767b\u5f55\u6210\u529f\uff0c\u8bbe\u5907\u5df2\u6ce8\u518c\u3002");
                    if (this.loginButton != null) {
                        this.loginButton.setEnabled(true);
                    }
                    this.refreshExtensions();
                    this.refreshYanm();
                });
            }
            catch (Exception ex) {
                this.prefs.edit().putString("baseUrl", baseUrl).putString("email", email).putString("password", this.passwordInput.getText().toString()).putString("token", token).apply();
                this.runOnUiThread(() -> {
                    this.setStatus("\u767b\u5f55\u6210\u529f\uff0c\u4f46\u8bbe\u5907\u6ce8\u518c\u5931\u8d25\uff1a" + ex.getMessage());
                    if (this.loginButton != null) {
                        this.loginButton.setEnabled(true);
                    }
                });
            }
        });
    }

    private void sendToDesktop() {
        this.sendTextValueToDesktop(this.textInput.getText().toString(), "\u6b63\u5728\u53d1\u9001\u5230\u7535\u8111...");
    }

    private void sendTextValueToDesktop(String text, String pendingStatus) {
        this.setStatus(pendingStatus);
        this.executor.execute(() -> {
            try {
                String messageId;
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, this.deviceId, text);
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, this.deviceId, text);
                }
                String sentMessageId = messageId;
                this.runOnUiThread(() -> this.setStatus("\u5df2\u53d1\u9001\u5230\u4e91\u7aef\uff0cmessageId=" + sentMessageId + "\u3002\u7535\u8111\u7aef\u5728\u7ebf\u65f6\u4f1a\u5728 5 \u79d2\u5185\u6536\u5230\u3002"));
            }
            catch (Exception ex) {
                this.runOnUiThread(() -> this.setStatus("\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage()));
            }
        });
    }

    private void pickPhotoFromGallery() {
        try {
            Intent intent = new Intent("android.intent.action.OPEN_DOCUMENT");
            intent.addCategory("android.intent.category.OPENABLE");
            intent.setType("image/*");
            this.startActivityForResult(intent, 4101);
        }
        catch (Exception ex) {
            this.setStatus("\u6253\u5f00\u76f8\u518c\u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private void sendPhotoToDesktop(Uri uri) {
        this.setStatus("\u6b63\u5728\u5904\u7406\u7167\u7247...");
        this.showPhotoProgress("\u6b63\u5728\u53d1\u9001\u7167\u7247...");
        this.executor.execute(() -> {
            try {
                String messageId;
                byte[] jpegBytes = this.readJpegBytesFromUri(uri);
                int[] size = MainActivity.readImageSizeFromJpegBytes(jpegBytes);
                int width = size[0];
                int height = size[1];
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendPhotoToDesktop(baseUrl, token, this.deviceId, jpegBytes, width, height);
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendPhotoToDesktop(baseUrl, token, this.deviceId, jpegBytes, width, height);
                }
                String sentMessageId = messageId;
                this.runOnUiThread(() -> {
                    this.hidePhotoProgress();
                    this.setStatus("\u7167\u7247\u5df2\u53d1\u9001\u5230\u4e91\u7aef\uff0cmessageId=" + sentMessageId + "\u3002\u7535\u8111\u7aef\u5728\u7ebf\u65f6\u4f1a\u5728 5 \u79d2\u5185\u6536\u5230\u3002");
                });
            }
            catch (Exception ex) {
                this.runOnUiThread(() -> {
                    this.hidePhotoProgress();
                    this.setStatus("\u7167\u7247\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage());
                });
            }
        });
    }

    private byte[] readJpegBytesFromUri(Uri uri) throws Exception {
        Bitmap bitmap;
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        try (InputStream stream = this.getContentResolver().openInputStream(uri);){
            BitmapFactory.decodeStream((InputStream)stream, null, (BitmapFactory.Options)bounds);
        }
        int maxEdge = Math.max(bounds.outWidth, bounds.outHeight);
        int sample = 1;
        while (maxEdge / sample > 1600) {
            sample *= 2;
        }
        BitmapFactory.Options decode = new BitmapFactory.Options();
        decode.inSampleSize = Math.max(1, sample);
        try (InputStream stream = this.getContentResolver().openInputStream(uri);){
            bitmap = BitmapFactory.decodeStream((InputStream)stream, null, (BitmapFactory.Options)decode);
        }
        if (bitmap == null) {
            throw new IllegalStateException("\u65e0\u6cd5\u8bfb\u53d6\u56fe\u7247\u5185\u5bb9\u3002");
        }
        try {
            byte[] byArray;
            try (ByteArrayOutputStream output = new ByteArrayOutputStream();){
                bitmap.compress(Bitmap.CompressFormat.JPEG, 90, (OutputStream)output);
                byArray = output.toByteArray();
            }
            return byArray;
        }
        finally {
            bitmap.recycle();
        }
    }

    private static int[] readImageSizeFromJpegBytes(byte[] jpegBytes) {
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inJustDecodeBounds = true;
        BitmapFactory.decodeByteArray((byte[])jpegBytes, (int)0, (int)jpegBytes.length, (BitmapFactory.Options)options);
        return new int[]{Math.max(0, options.outWidth), Math.max(0, options.outHeight)};
    }

    private void refreshExtensions() {
        this.refreshExtensions(false);
    }

    private void refreshExtensions(boolean keepExisting) {
        if (!keepExisting || this.extensionList.getChildCount() == 0) {
            this.extensionList.removeAllViews();
            this.extensionList.addView((View)this.textView("\u6b63\u5728\u8bfb\u53d6\u8d26\u53f7\u6269\u5c55...", 13, Color.rgb((int)148, (int)163, (int)184), false));
        } else {
            this.setStatus("\u6b63\u5728\u540e\u53f0\u5237\u65b0\u7535\u8111\u6269\u5c55...");
        }
        this.executor.execute(() -> {
            try {
                List<RemoteExtension> extensions;
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    extensions = YanziApiClient.fetchRunnableExtensions(baseUrl, token);
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    extensions = YanziApiClient.fetchRunnableExtensions(baseUrl, token);
                }
                List<RemoteExtension> loadedExtensions = extensions;
                this.runOnUiThread(() -> {
                    this.cacheRemoteExtensions(loadedExtensions);
                    this.renderExtensions(loadedExtensions);
                    if (this.swipeRefresh != null) {
                        this.swipeRefresh.setRefreshing(false);
                    }
                });
            }
            catch (Exception ex) {
                this.runOnUiThread(() -> {
                    if (!keepExisting || this.extensionList.getChildCount() == 0) {
                        this.extensionList.removeAllViews();
                        this.extensionList.addView((View)this.textView("\u6269\u5c55\u5217\u8868\u8bfb\u53d6\u5931\u8d25\u3002", 13, Color.rgb((int)248, (int)113, (int)113), false));
                    }
                    this.setStatus("\u6269\u5c55\u5217\u8868\u8bfb\u53d6\u5931\u8d25\uff1a" + ex.getMessage());
                    if (this.swipeRefresh != null) {
                        this.swipeRefresh.setRefreshing(false);
                    }
                });
            }
        });
    }

    private void renderCachedExtensions() {
        List<RemoteExtension> cached = this.readCachedExtensions();
        if (cached.isEmpty()) {
            this.extensionList.removeAllViews();
            this.extensionList.addView((View)this.textView("\u6682\u65e0\u7535\u8111\u6269\u5c55\u7f13\u5b58\u3002\u8fdb\u5165\u540e\u4f1a\u540e\u53f0\u62c9\u53d6\uff0c\u4e5f\u53ef\u70b9\u51fb\u201c\u5237\u65b0\u6269\u5c55\u5217\u8868\u201d\u3002", 13, Color.rgb((int)148, (int)163, (int)184), false));
            return;
        }
        this.renderExtensions(cached);
        this.extensionList.addView((View)this.textView("\u5f53\u524d\u663e\u793a\u7f13\u5b58\uff0c\u540e\u53f0\u4f1a\u81ea\u52a8\u5237\u65b0\u3002", 11, Color.rgb((int)103, (int)232, (int)249), false));
    }

    private void cacheRemoteExtensions(List<RemoteExtension> extensions) {
        try {
            JSONArray array = new JSONArray();
            for (RemoteExtension extension : extensions) {
                array.put((Object)new JSONObject().put("extensionId", (Object)extension.extensionId).put("name", (Object)extension.name).put("description", (Object)extension.description).put("icon", (Object)extension.icon).put("accentHex", (Object)extension.accentHex));
            }
            this.prefs.edit().putString(CACHE_REMOTE_EXTENSIONS, array.toString()).apply();
            this.updateAllAppWidgets();
        }
        catch (Exception exception) {
            // empty catch block
        }
    }

    private List<RemoteExtension> readCachedExtensions() {
        ArrayList<RemoteExtension> items = new ArrayList<RemoteExtension>();
        try {
            JSONArray array = new JSONArray(this.prefs.getString(CACHE_REMOTE_EXTENSIONS, "[]"));
            for (int i = 0; i < array.length(); ++i) {
                JSONObject item = array.optJSONObject(i);
                if (item == null) continue;
                String extensionId = MainActivity.firstNonEmpty(item.optString("extensionId"), item.optString("extension_id"), item.optString("ExtensionId"), item.optString("Extension_id"));
                if (extensionId.isEmpty()) continue;
                String accentHex = MainActivity.firstNonEmpty(item.optString("accentHex"), item.optString("accent_hex"), item.optString("AccentHex"));
                items.add(new RemoteExtension(extensionId, MainActivity.firstNonEmpty(item.optString("name"), item.optString("Name"), extensionId), MainActivity.firstNonEmpty(item.optString("description"), item.optString("Description")), MainActivity.firstNonEmpty(item.optString("icon"), item.optString("Icon")), accentHex));
            }
        }
        catch (Exception exception) {
            // empty catch block
        }
        return items;
    }

    private void renderExtensions(List<RemoteExtension> extensions) {
        this.currentDesktopExtensions = extensions;
        String query = this.searchDesktopExtensionsInput != null && this.searchDesktopExtensionsInput.getText() != null ? this.searchDesktopExtensionsInput.getText().toString().trim().toLowerCase() : "";
        ArrayList<RemoteExtension> filtered = new ArrayList<RemoteExtension>();
        if (query.isEmpty()) {
            filtered.addAll(extensions);
        } else {
            for (RemoteExtension e : extensions) {
                if ((e.name == null || !e.name.toLowerCase().contains(query)) && (e.description == null || !e.description.toLowerCase().contains(query))) continue;
                filtered.add(e);
            }
        }
        this.extensionList.removeAllViews();
        if (filtered.isEmpty()) {
            this.extensionList.addView((View)this.textView("\u6682\u65e0\u53ef\u8fdc\u7a0b\u6267\u884c\u6269\u5c55\u3002", 13, Color.rgb((int)148, (int)163, (int)184), false));
            return;
        }
        GridLayout grid = new GridLayout((Context)this);
        grid.setColumnCount(4);
        this.extensionList.addView((View)grid);
        int screenWidth = this.getResources().getDisplayMetrics().widthPixels;
        int cellWidth = Math.max(this.dp(72), (screenWidth - this.dp(56)) / 4);
        for (RemoteExtension extension : filtered) {
            LinearLayout card = this.iconCard();
            card.setGravity(17);
            card.setOnClickListener(v -> this.runRemoteExtension(extension, (View)card));
            card.setOnLongClickListener(v -> {
                this.showSetWidgetExtensionDialog(extension);
                return true;
            });
            GridLayout.LayoutParams cardParams = new GridLayout.LayoutParams();
            cardParams.width = cellWidth;
            cardParams.height = -2;
            cardParams.setMargins(this.dp(3), this.dp(6), this.dp(3), this.dp(6));
            card.setLayoutParams((ViewGroup.LayoutParams)cardParams);
            Path path = MobileIconLibrary.resolveOrDefault(extension.icon);
            ImageView img = new ImageView((Context)this);
            GradientDrawable gd = new GradientDrawable();
            int baseColor = Color.rgb((int)45, (int)45, (int)45);
            if (extension.accentHex != null && !extension.accentHex.trim().isEmpty()) {
                try {
                    String colorStr = extension.accentHex.trim();
                    if (!colorStr.startsWith("#")) {
                        colorStr = "#" + colorStr;
                    }
                    baseColor = Color.parseColor((String)colorStr);
                }
                catch (Exception colorStr) {
                    // empty catch block
                }
            }
            gd.setColor(baseColor);
            gd.setCornerRadius((float)this.dp(10));
            img.setBackground((Drawable)gd);
            img.setImageDrawable((Drawable)new PathDrawable(path, -1));
            img.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
            ImageView iconView = img;
            LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(this.dp(54), this.dp(54));
            iconParams.setMargins(0, 0, 0, this.dp(1));
            iconParams.gravity = 1;
            card.addView((View)iconView, (ViewGroup.LayoutParams)iconParams);
            TextView name = this.textView(extension.name, 11, -1, false);
            name.setGravity(17);
            name.setMaxLines(2);
            LinearLayout.LayoutParams nameParams = new LinearLayout.LayoutParams(-1, -2);
            nameParams.gravity = 1;
            card.addView((View)name, (ViewGroup.LayoutParams)nameParams);
            grid.addView((View)card);
        }
    }

    private void runRemoteExtension(RemoteExtension extension, View cardView) {
        if (cardView != null) {
            cardView.setEnabled(false);
        }
        ViewGroup cardGroup = cardView != null ? (ViewGroup)cardView : null;
        View originalIcon = cardGroup != null ? cardGroup.getChildAt(0) : null;
        ProgressBar progressBar = new ProgressBar((Context)this, null, 16842873);
        if (originalIcon != null) {
            progressBar.setLayoutParams(originalIcon.getLayoutParams());
        }
        progressBar.setPadding(this.dp(12), this.dp(12), this.dp(12), this.dp(12));
        if (originalIcon != null) {
            originalIcon.setVisibility(8);
        }
        if (cardGroup != null) {
            cardGroup.addView((View)progressBar, 0);
        }
        this.setStatus("\u6b63\u5728\u53d1\u9001\u6269\u5c55\u6267\u884c\u8bf7\u6c42\uff1a" + extension.name);
        Runnable restoreUi = () -> this.runOnUiThread(() -> {
            if (cardGroup != null) {
                cardGroup.removeView((View)progressBar);
            }
            if (originalIcon != null) {
                originalIcon.setVisibility(0);
            }
            if (cardView != null) {
                cardView.setEnabled(true);
            }
        });
        this.executor.execute(() -> {
            try {
                String messageId;
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    messageId = YanziApiClient.runExtensionOnDesktop(baseUrl, token, this.deviceId, this.buildDeviceName(), extension.extensionId, this.textInput.getText().toString());
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    messageId = YanziApiClient.runExtensionOnDesktop(baseUrl, token, this.deviceId, this.buildDeviceName(), extension.extensionId, this.textInput.getText().toString());
                }
                String sentMessageId = messageId;
                this.runOnUiThread(() -> this.setStatus("\u6269\u5c55\u8bf7\u6c42\u5df2\u53d1\u9001\uff0c\u5f00\u59cb\u8f6e\u8be2\u6267\u884c\u72b6\u6001..."));
                boolean finished = false;
                long startTime = System.currentTimeMillis();
                long timeout = 20000L;
                String statusResult = "timeout";
                String execOutput = "";
                while (System.currentTimeMillis() - startTime < timeout) {
                    try {
                        JSONObject msgDetail = YanziApiClient.fetchMessageDetail(baseUrl, token, sentMessageId);
                        String status = msgDetail.optString("status", "pending");
                        if ("completed".equals(status)) {
                            JSONObject execRes;
                            statusResult = "completed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null && (execRes = payloadObj.optJSONObject("executionResult")) != null) {
                                execOutput = execRes.optString("output", "");
                            }
                            finished = true;
                            break;
                        }
                        if ("failed".equals(status)) {
                            JSONObject execRes;
                            statusResult = "failed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null && (execRes = payloadObj.optJSONObject("executionResult")) != null) {
                                execOutput = execRes.optString("output", "");
                            }
                            finished = true;
                            break;
                        }
                        if ("acked".equals(status)) {
                            statusResult = "acked";
                            finished = true;
                            break;
                        }
                    }
                    catch (Exception msgDetail) {
                        // empty catch block
                    }
                    try {
                        Thread.sleep(1000L);
                    }
                    catch (InterruptedException e) {
                        // empty catch block
                        break;
                    }
                }
                restoreUi.run();
                String finalStatus = statusResult;
                String finalOutput = execOutput;
                this.runOnUiThread(() -> {
                    if ("completed".equals(finalStatus)) {
                        new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u6267\u884c\u6210\u529f").setMessage((CharSequence)("\u6269\u5c55 [" + extension.name + "] \u6267\u884c\u6210\u529f\uff01\n\n\u8fd4\u56de\u7ed3\u679c\uff1a\n" + finalOutput)).setPositiveButton((CharSequence)"\u786e\u5b9a", null).show();
                        this.setStatus("\u6269\u5c55\u6267\u884c\u6210\u529f\uff1a" + extension.name);
                    } else if ("failed".equals(finalStatus)) {
                        new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u6267\u884c\u5931\u8d25").setMessage((CharSequence)("\u6269\u5c55 [" + extension.name + "] \u6267\u884c\u5931\u8d25\uff01\n\n\u9519\u8bef\u4fe1\u606f\uff1a\n" + finalOutput)).setPositiveButton((CharSequence)"\u786e\u5b9a", null).show();
                        this.setStatus("\u6269\u5c55\u6267\u884c\u5931\u8d25\uff1a" + extension.name);
                    } else if ("acked".equals(finalStatus)) {
                        new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u6267\u884c\u5b8c\u6210").setMessage((CharSequence)("\u6269\u5c55 [" + extension.name + "] \u5df2\u6267\u884c\u5b8c\u6210\uff08\u672a\u8fd4\u56de\u7ed3\u679c\u6570\u636e\uff09\u3002")).setPositiveButton((CharSequence)"\u786e\u5b9a", null).show();
                        this.setStatus("\u6269\u5c55\u6267\u884c\u5b8c\u6210\uff1a" + extension.name);
                    } else {
                        new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u6267\u884c\u8d85\u65f6").setMessage((CharSequence)("\u6269\u5c55 [" + extension.name + "] \u6267\u884c\u8d85\u65f6\uff0c\u8bf7\u786e\u8ba4\u7535\u8111\u7aef\u662f\u5426\u5df2\u79bb\u7ebf\u3002")).setPositiveButton((CharSequence)"\u786e\u5b9a", null).show();
                        this.setStatus("\u6269\u5c55\u6267\u884c\u8d85\u65f6\uff1a" + extension.name);
                    }
                });
            }
            catch (Exception ex) {
                restoreUi.run();
                this.runOnUiThread(() -> {
                    new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u53d1\u9001\u8bf7\u6c42\u5931\u8d25").setMessage((CharSequence)ex.getMessage()).setPositiveButton((CharSequence)"\u786e\u5b9a", null).show();
                    this.setStatus("\u6269\u5c55\u6267\u884c\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage());
                });
            }
        });
    }

    private void refreshYanm() {
        this.refreshYanm(false);
    }

    private void refreshYanm(boolean keepExisting) {
        if (!keepExisting || this.yanmList.getChildCount() == 0) {
            this.yanmList.removeAllViews();
            this.yanmList.addView((View)this.textView("\u6b63\u5728\u8bfb\u53d6\u71d5\u5e55...", 13, Color.rgb((int)148, (int)163, (int)184), false));
        } else {
            this.setStatus("\u6b63\u5728\u540e\u53f0\u5237\u65b0\u71d5\u5e55...");
        }
        this.executor.execute(() -> {
            try {
                JSONObject yanm;
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    yanm = YanziApiClient.fetchYanmState(baseUrl, token);
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    yanm = YanziApiClient.fetchYanmState(baseUrl, token);
                }
                JSONObject loadedYanm = yanm;
                this.runOnUiThread(() -> {
                    this.prefs.edit().putString(CACHE_YANM, loadedYanm.toString()).apply();
                    this.updateAllAppWidgets();
                    this.renderYanm(loadedYanm);
                    if (this.swipeRefresh != null) {
                        this.swipeRefresh.setRefreshing(false);
                    }
                });
            }
            catch (Exception ex) {
                this.runOnUiThread(() -> {
                    if (!keepExisting || this.yanmList.getChildCount() == 0) {
                        this.yanmList.removeAllViews();
                        this.yanmList.addView((View)this.textView("\u71d5\u5e55\u8bfb\u53d6\u5931\u8d25\u3002", 13, Color.rgb((int)248, (int)113, (int)113), false));
                    }
                    this.setStatus("\u71d5\u5e55\u8bfb\u53d6\u5931\u8d25\uff1a" + ex.getMessage());
                    if (this.swipeRefresh != null) {
                        this.swipeRefresh.setRefreshing(false);
                    }
                });
            }
        });
    }

    private void renderCachedYanm() {
        String cached = this.prefs.getString(CACHE_YANM, "");
        if (cached == null || cached.trim().isEmpty()) {
            this.yanmList.removeAllViews();
            this.yanmList.addView((View)this.textView("\u6682\u65e0\u71d5\u5e55\u7f13\u5b58\u3002\u8fdb\u5165\u540e\u4f1a\u81ea\u52a8\u540e\u53f0\u62c9\u53d6\uff0c\u4e5f\u53ef\u70b9\u51fb\u201c\u5237\u65b0\u201d\u3002", 13, Color.rgb((int)148, (int)163, (int)184), false));
            return;
        }
        try {
            this.renderYanm(new JSONObject(cached));
            this.yanmList.addView((View)this.textView("\u5f53\u524d\u663e\u793a\u7f13\u5b58\uff0c\u540e\u53f0\u4f1a\u81ea\u52a8\u5237\u65b0\u3002", 11, Color.rgb((int)103, (int)232, (int)249), false));
        }
        catch (Exception ex) {
            this.yanmList.removeAllViews();
            this.yanmList.addView((View)this.textView("\u71d5\u5e55\u7f13\u5b58\u4e0d\u53ef\u7528\uff0c\u6b63\u5728\u7b49\u5f85\u5237\u65b0\u3002", 13, Color.rgb((int)148, (int)163, (int)184), false));
        }
    }

    private void saveSortedState() {
        try {
            JSONArray arr = new JSONArray();
            for (String id : this.sortedComponentIds) {
                arr.put((Object)id);
            }
            this.prefs.edit().putString("sortedComponentIds", arr.toString()).apply();
            this.updateAllAppWidgets();
        }
        catch (Exception exception) {
            // empty catch block
        }
    }

    private void saveExpandedState() {
        try {
            JSONArray arr = new JSONArray();
            for (String id : this.expandedComponentIds) {
                arr.put((Object)id);
            }
            this.prefs.edit().putString("expandedComponentIds", arr.toString()).apply();
        }
        catch (Exception exception) {
            // empty catch block
        }
    }

    private void showSortDialog(String componentId, int currentIndex, List<JSONObject> components) {
        if (components.size() <= 1) {
            return;
        }
        CharSequence[] options = new String[]{"\u7f6e\u9876", "\u4e0a\u79fb", "\u4e0b\u79fb", "\u7f6e\u5e95"};
        new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u8c03\u6574\u7ec4\u4ef6\u987a\u5e8f").setItems(options, (dialog, which) -> {
            ArrayList<String> list = new ArrayList<String>();
            for (JSONObject comp : components) {
                String id = MainActivity.firstNonEmpty(comp.optString("id"), comp.optString("Id"), comp.optString("title"), comp.optString("Title"), comp.optString("name"), comp.optString("Name"));
                list.add(id);
            }
            String target = (String)list.remove(currentIndex);
            if (which == 0) {
                list.add(0, target);
            } else if (which == 1) {
                int newIndex = Math.max(0, currentIndex - 1);
                list.add(newIndex, target);
            } else if (which == 2) {
                int newIndex = Math.min(list.size(), currentIndex + 1);
                list.add(newIndex, target);
            } else if (which == 3) {
                list.add(target);
            }
            this.sortedComponentIds.clear();
            this.sortedComponentIds.addAll(list);
            this.saveSortedState();
            if (this.currentYanmSnapshot != null) {
                this.renderYanm(this.currentYanmSnapshot);
            }
        }).show();
    }

    private void refreshSettings() {
        this.executor.execute(() -> {
            try {
                JSONObject settings;
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    settings = YanziApiClient.fetchSettings(baseUrl, token);
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    settings = YanziApiClient.fetchSettings(baseUrl, token);
                }
                JSONObject loadedSettings = settings;
                this.runOnUiThread(() -> {
                    String aiBaseUrl = loadedSettings.optString("aiBaseUrl", "");
                    String aiApiKey = loadedSettings.optString("aiApiKey", "");
                    String aiModel = loadedSettings.optString("aiModel", "");
                    this.prefs.edit().putString("aiBaseUrl", aiBaseUrl).putString("aiApiKey", aiApiKey).putString("aiModel", aiModel).apply();
                    if (this.aiModelInfoText != null) {
                        this.aiModelInfoText.setText((CharSequence)(aiModel.isEmpty() ? "\u5c1a\u672a\u8fde\u63a5\u5230 PC \u7aef AI\uff0c\u4e0b\u62c9\u5237\u65b0\u91cd\u8bd5\u3002" : "\u5f53\u524d AI: " + aiModel));
                    }
                });
            }
            catch (Exception exception) {
                // empty catch block
            }
        });
    }

    private void handleAiSendButtonClick() {
        if (this.isAiLoading) {
            this.cancelAiRequest();
        } else {
            this.sendAiChat();
        }
    }

    private void setAiLoadingState(boolean loading) {
        this.isAiLoading = loading;
        if (this.aiSendButton != null) {
            if (loading) {
                this.aiSendButton.setBackground(null);
                this.aiSendButton.setCompoundDrawablesWithIntrinsicBounds(null, null, this.createStopIconDrawable(), null);
                this.aiSendButton.setText((CharSequence)"");
                this.addAiChatMessage("AI", "\u56de\u590d\u4e2d...", Color.rgb((int)156, (int)163, (int)175), false);
                if (this.aiChatHistory.getChildCount() > 0) {
                    this.aiLoadingPlaceholderView = this.aiChatHistory.getChildAt(this.aiChatHistory.getChildCount() - 1);
                }
            } else {
                this.aiSendButton.setBackground(this.aiSendButtonDefaultBackground);
                this.aiSendButton.setCompoundDrawablesWithIntrinsicBounds(null, null, null, null);
                this.aiSendButton.setText((CharSequence)"\u53d1\u9001");
                if (this.aiLoadingPlaceholderView != null) {
                    this.aiChatHistory.removeView(this.aiLoadingPlaceholderView);
                    this.aiLoadingPlaceholderView = null;
                }
            }
        }
    }

    private void cancelAiRequest() {
        this.isAiCancelled = true;
        HttpURLConnection conn = this.currentAiConnection;
        if (conn != null) {
            this.executor.execute(() -> {
                try {
                    conn.disconnect();
                } catch (Exception ignored) {}
            });
        }
        this.runOnUiThread(() -> {
            this.setAiLoadingState(false);
            this.addAiChatMessage("\u7cfb\u7edf", "\u5df2\u53d6\u6d88 AI \u56de\u590d\u3002", Color.rgb((int)156, (int)163, (int)175), false);
        });
    }

    private void sendAiChat() {
        String text = this.aiChatInput.getText().toString().trim();
        if (text.isEmpty() && this.pendingAttachments.isEmpty()) {
            return;
        }
        
        StringBuilder textWithFiles = new StringBuilder(text);
        ArrayList<AttachmentInfo> imgs = new ArrayList<>();
        
        for (AttachmentInfo attach : this.pendingAttachments) {
            if (attach.isImage) {
                imgs.add(attach);
            } else {
                if (attach.textContent != null) {
                    if (textWithFiles.length() > 0) {
                        textWithFiles.append("\n\n");
                    }
                    textWithFiles.append("[附件: ").append(attach.name).append("]\n```");
                    textWithFiles.append("\n").append(attach.textContent).append("\n```");
                } else {
                    if (textWithFiles.length() > 0) {
                        textWithFiles.append("\n\n");
                    }
                    textWithFiles.append("[附件: ").append(attach.name).append(" (大小: ").append(attach.size).append("字节)]");
                }
            }
        }
        
        for (AttachmentInfo img : imgs) {
            if (textWithFiles.length() > 0) {
                textWithFiles.append("\n");
            }
            textWithFiles.append("[图片: ").append(img.name).append("]");
        }
        
        this.activeImageAttachments.clear();
        this.activeImageAttachments.addAll(imgs);
        
        this.pendingAttachments.clear();
        this.refreshAttachmentCards();
        
        this.sendAiChat(textWithFiles.toString());
    }

    private void sendAiChat(String text) {
        String aiBaseUrl = this.prefs.getString("aiBaseUrl", "");
        if (aiBaseUrl.isEmpty()) {
            this.setStatus("\u8bf7\u5148\u8fde\u63a5 PC \u7aef\u540c\u6b65 AI \u914d\u7f6e\u3002");
            return;
        }
        this.addAiChatMessage("\u6211", text, -1, true);
        this.aiChatInput.setText((CharSequence)"");
        this.isAiCancelled = false;
        this.setAiLoadingState(true);
        this.fetchAiReply();
    }

    private void sendAiSystemFeedback(String toolName, String text) {
        String aiBaseUrl = this.prefs.getString("aiBaseUrl", "");
        if (aiBaseUrl.isEmpty()) {
            this.setStatus("\u8bf7\u5148\u8fde\u63a5 PC \u7aef\u540c\u6b65 AI \u914d\u7f6e\u3002");
            return;
        }
        this.addAiChatMessage("\u7cfb\u7edf\u53cd\u9988:" + toolName, text, -256, true);
        this.isAiCancelled = false;
        this.setAiLoadingState(true);
        this.fetchAiReply();
    }

    private boolean isKnownAiTool(String toolName) {
        return "query_extensions".equals(toolName) || "execute_extension".equals(toolName) || "view_yanm".equals(toolName) || "update_yanm_component".equals(toolName) || "manage_mobile_extension".equals(toolName);
    }

    private String parseToolName(String content) {
        if (content == null) return null;
        String jsonContent = content;
        int startIdx = content.indexOf("```json");
        int endIdx;
        if (startIdx != -1 && (endIdx = content.indexOf("```", startIdx + 7)) != -1) {
            jsonContent = content.substring(startIdx + 7, endIdx).trim();
        }
        if (jsonContent.startsWith("{") && jsonContent.endsWith("}")) {
            try {
                JSONObject toolCall = new JSONObject(jsonContent);
                String tool = toolCall.optString("tool");
                if (tool != null && !tool.isEmpty() && isKnownAiTool(tool)) {
                    return tool;
                }
            }
            catch (Exception exception) {}
        }
        try {
            java.util.regex.Pattern pattern = java.util.regex.Pattern.compile("\"tool\"\\s*:\\s*\"([^\"]+)\"");
            java.util.regex.Matcher matcher = pattern.matcher(jsonContent);
            if (matcher.find()) {
                String tool = matcher.group(1);
                if (tool != null && !tool.isEmpty() && isKnownAiTool(tool)) {
                    return tool;
                }
            }
        }
        catch (Exception exception) {}
        return null;
    }

    private String buildAiToolCallKey(JSONObject toolCall) {
        String toolName = toolCall.optString("tool", "");
        String id = toolCall.optString("id", "");
        String action = toolCall.optString("action", "");
        String title = toolCall.optString("title", "");
        String html = toolCall.optString("html", "");
        String code = toolCall.optString("code", "");
        int htmlHash = html.isEmpty() ? 0 : html.hashCode();
        int codeHash = code.isEmpty() ? 0 : code.hashCode();
        return toolName + "|id=" + id + "|action=" + action + "|title=" + title + "|html=" + htmlHash + "|code=" + codeHash;
    }

    /*
     * WARNING - Removed try catching itself - possible behaviour change.
     */
    private String tryBeginAiToolCall(JSONObject toolCall) {
        long now = System.currentTimeMillis();
        String key = this.buildAiToolCallKey(toolCall);
        Object object = this.aiToolCallLock;
        synchronized (object) {
            Iterator<Map.Entry<String, Long>> it = this.recentAiToolCalls.entrySet().iterator();
            while (it.hasNext()) {
                Map.Entry<String, Long> entry = it.next();
                if (now - entry.getValue() <= 60000L) continue;
                it.remove();
            }
            Long lastRunAt = this.recentAiToolCalls.get(key);
            if (this.runningAiToolCalls.contains(key) || lastRunAt != null && now - lastRunAt < 10000L) {
                return null;
            }
            this.runningAiToolCalls.add(key);
            this.recentAiToolCalls.put(key, now);
        }
        return key;
    }

    /*
     * WARNING - Removed try catching itself - possible behaviour change.
     */
    private void finishAiToolCall(String key) {
        if (key == null) {
            return;
        }
        Object object = this.aiToolCallLock;
        synchronized (object) {
            this.runningAiToolCalls.remove(key);
            this.recentAiToolCalls.put(key, System.currentTimeMillis());
        }
    }

    private void showDuplicateAiToolCall(String toolName, String content) {
        String message = "\u5df2\u62e6\u622a\u77ed\u65f6\u95f4\u91cd\u590d\u5de5\u5177\u8c03\u7528: " + toolName;
        MobileDiagnostics.append((Context)this, message);
        this.runOnUiThread(() -> {
            this.addAiChatMessage("AI", content, Color.rgb((int)167, (int)243, (int)208), true);
            this.addAiChatMessage("\u7cfb\u7edf", message, Color.rgb((int)156, (int)163, (int)175), false);
        });
    }

    /*
     * WARNING - Removed try catching itself - possible behaviour change.
     */
    private JSONArray snapshotAiHistoryForRequest(String sessionId) {
        Object object = this.aiHistoryLock;
        synchronized (object) {
            String stored;
            if (sessionId != null && !sessionId.isEmpty() && (stored = this.prefs.getString("aiMessages_" + sessionId, null)) != null && !stored.trim().isEmpty()) {
                try {
                    JSONArray storedHistory = new JSONArray(stored);
                    if (storedHistory.length() > 0 || this.aiMessagesHistory == null || this.aiMessagesHistory.length() == 0) {
                        return storedHistory;
                    }
                }
                catch (Exception exception) {
                    // empty catch block
                }
            }
            try {
                return new JSONArray(this.aiMessagesHistory == null ? "[]" : this.aiMessagesHistory.toString());
            }
            catch (Exception ignored) {
                return new JSONArray();
            }
        }
    }

    private String summarizeAiMessages(JSONArray messages) {
        if (messages == null) {
            return "count=0";
        }
        StringBuilder sb = new StringBuilder();
        sb.append("count=").append(messages.length()).append(" [");
        int limit = Math.min(messages.length(), 8);
        for (int i = 0; i < limit; ++i) {
            String content;
            String snippet;
            JSONObject m = messages.optJSONObject(i);
            if (m == null) continue;
            if (i > 0) {
                sb.append("; ");
            }
            if ((snippet = (content = m.optString("content", "")).replace('\n', ' ').replace('\r', ' ').trim()).length() > 80) {
                snippet = snippet.substring(0, 80) + "...";
            }
            sb.append(i).append(":").append(m.optString("role", "?")).append("/").append(content.length()).append("=\"").append(snippet).append("\"");
        }
        if (messages.length() > limit) {
            sb.append("; ...");
        }
        sb.append("]");
        return sb.toString();
    }

    private void fetchAiReply() {
        final ArrayList<AttachmentInfo> imagesToSend = new ArrayList<>(this.activeImageAttachments);
        this.activeImageAttachments.clear();
        String aiBaseUrl = this.prefs.getString("aiBaseUrl", "");
        String aiApiKey = this.prefs.getString("aiApiKey", "");
        String aiModel = this.prefs.getString("aiModel", "");
        String requestSessionId = this.currentAiSessionId;
        JSONArray requestHistory = this.snapshotAiHistoryForRequest(requestSessionId);
        this.executor.execute(() -> {
            block43: {
                try {
                    int endIdx;
                    JSONObject msg;
                    JSONObject first;
                    JSONArray messages = new JSONArray();
                    String basePrompt = this.prefs.getString("aiSystemPrompt", DEFAULT_SYSTEM_PROMPT);
                    JSONArray extListPrompt = new JSONArray();
                    if (this.currentDesktopExtensions != null) {
                        for (RemoteExtension e : this.currentDesktopExtensions) {
                            try {
                                JSONObject eObj = new JSONObject();
                                eObj.put("id", (Object)e.extensionId);
                                eObj.put("name", (Object)e.name);
                                extListPrompt.put((Object)eObj);
                            }
                            catch (Exception eObj) {}
                        }
                    }
                    String yanmNamesStr = "[]";
                    try {
                        String yanmStr = this.prefs.getString(CACHE_YANM, "{}");
                        JSONObject yanmObj = new JSONObject(yanmStr);
                        JSONArray yanmList = new JSONArray();
                        Iterator it = yanmObj.keys();
                        while (it.hasNext()) {
                            String k = (String)it.next();
                            JSONObject c = yanmObj.optJSONObject(k);
                            if (c == null) continue;
                            JSONObject simple = new JSONObject();
                            simple.put("id", (Object)k);
                            simple.put("title", (Object)c.optString("title", "\u672a\u547d\u540d"));
                            yanmList.put((Object)simple);
                        }
                        yanmNamesStr = yanmList.toString();
                    }
                    catch (Exception yanmStr) {
                        // empty catch block
                    }
                    String finalPrompt = "\u3010\u7cfb\u7edf\u6307\u4ee4\uff08\u4e25\u683c\u9075\u5b88\uff09\u3011\n" + basePrompt + "\n\u5f53\u524d\u53ef\u7528\u6269\u5c55\u6709:\n" + extListPrompt.toString() + "\n\u5f53\u524d\u71d5\u5e55\u7ec4\u4ef6\u6709:\n" + yanmNamesStr;
                    messages.put((Object)new JSONObject().put("role", (Object)"system").put("content", (Object)finalPrompt));
                    if (requestHistory != null) {
                        for (int i = 0; i < requestHistory.length(); ++i) {
                            JSONObject histMsg = requestHistory.optJSONObject(i);
                            if (histMsg != null) {
                                JSONObject cleanMsg = new JSONObject();
                                cleanMsg.put("role", (Object)histMsg.optString("role"));
                                String contentStr = histMsg.optString("content");
                                if (i == requestHistory.length() - 1 && "user".equals(histMsg.optString("role")) && !imagesToSend.isEmpty()) {
                                    try {
                                        JSONArray contentArr = new JSONArray();
                                        JSONObject textObj = new JSONObject();
                                        textObj.put("type", "text");
                                        textObj.put("text", contentStr);
                                        contentArr.put(textObj);
                                        for (AttachmentInfo img : imagesToSend) {
                                            if (img.base64Data != null) {
                                                JSONObject imgObj = new JSONObject();
                                                imgObj.put("type", "image_url");
                                                JSONObject imgUrlObj = new JSONObject();
                                                String mime = img.mimeType != null ? img.mimeType : "image/jpeg";
                                                imgUrlObj.put("url", "data:" + mime + ";base64," + img.base64Data);
                                                imgObj.put("image_url", imgUrlObj);
                                                contentArr.put(imgObj);
                                            }
                                        }
                                        cleanMsg.put("content", contentArr);
                                    } catch (Exception e) {
                                        cleanMsg.put("content", contentStr);
                                    }
                                } else {
                                    cleanMsg.put("content", contentStr);
                                }
                                messages.put((Object)cleanMsg);
                            }
                        }
                    }
                    JSONObject payload = new JSONObject();
                    if (!aiModel.isEmpty()) {
                        payload.put("model", (Object)aiModel);
                    }
                    payload.put("messages", (Object)messages);
                    MobileDiagnostics.append((Context)this, "AI \u8bf7\u6c42\u4e0a\u4e0b\u6587: session=" + (requestSessionId == null ? "none" : requestSessionId) + ", history=" + this.summarizeAiMessages(requestHistory) + ", payload=" + this.summarizeAiMessages(messages));
                    String endpoint = aiBaseUrl;
                    if (!endpoint.startsWith("http://") && !endpoint.startsWith("https://")) {
                        endpoint = "https://" + endpoint;
                    }
                    if (!endpoint.endsWith("/chat/completions")) {
                        if (!endpoint.endsWith("/")) {
                            endpoint = endpoint + "/";
                        }
                        if (!endpoint.contains("/v1/")) {
                            endpoint = endpoint + "v1/";
                        }
                        endpoint = endpoint + "chat/completions";
                    }

                    int maxRetry = 3;
                    int attempt = 0;
                    boolean success = false;
                    String body = "";

                    while (attempt < maxRetry && !success) {
                        if (this.isAiCancelled) {
                            this.currentAiConnection = null;
                            this.runOnUiThread(() -> this.setAiLoadingState(false));
                            return;
                        }
                        attempt++;
                        HttpURLConnection connection = null;
                        try {
                            connection = (HttpURLConnection)new URL(endpoint).openConnection();
                            this.currentAiConnection = connection;
                            connection.setRequestMethod("POST");
                            connection.setConnectTimeout(60000);
                            connection.setReadTimeout(60000);
                            connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.1.0");
                            connection.setRequestProperty("Accept", "application/json");
                            connection.setDoOutput(true);
                            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                            if (!aiApiKey.isEmpty()) {
                                connection.setRequestProperty("Authorization", "Bearer " + aiApiKey);
                            }
                            try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8);){
                                writer.write(payload.toString());
                            }
                            int responseCode = connection.getResponseCode();

                            if ((responseCode == 502 || responseCode == 504 || responseCode == 500) && attempt < maxRetry) {
                                throw new java.io.IOException("HTTP " + responseCode + " (Vercel cold start?)");
                            }

                            InputStream stream = responseCode >= 200 && responseCode < 300 ? connection.getInputStream() : connection.getErrorStream();
                            StringBuilder builder = new StringBuilder();
                            try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8));){
                                String line;
                                while ((line = reader.readLine()) != null) {
                                    builder.append(line);
                                }
                            }
                            body = builder.toString();
                            if (responseCode < 200 || responseCode >= 300) {
                                throw new IllegalStateException("AI 请求失败 (" + responseCode + "): " + body);
                            }
                            success = true;
                        } catch (Exception ex) {
                            this.currentAiConnection = null;
                            if (connection != null) {
                                try { connection.disconnect(); } catch (Exception ignored) {}
                            }
                            if (this.isAiCancelled) {
                                this.runOnUiThread(() -> this.setAiLoadingState(false));
                                return;
                            }

                            boolean shouldRetry = false;
                            if (ex instanceof java.io.IOException) {
                                shouldRetry = true;
                            } else if (ex.getMessage() != null) {
                                String msgLower = ex.getMessage().toLowerCase();
                                if (msgLower.contains("connection reset") || msgLower.contains("timeout") || msgLower.contains("closed")) {
                                    shouldRetry = true;
                                }
                            }

                            if (shouldRetry && attempt < maxRetry) {
                                final int currentAttempt = attempt;
                                MobileDiagnostics.append((Context)this, "AI 请求失败 (" + ex.getMessage() + ")，正在进行第 " + currentAttempt + " 次重试...");
                                try {
                                    Thread.sleep(1000);
                                } catch (InterruptedException ie) {
                                    Thread.currentThread().interrupt();
                                    this.runOnUiThread(() -> this.setAiLoadingState(false));
                                    return;
                                }
                                continue;
                            }
                            throw ex;
                        }
                    }

                    this.currentAiConnection = null;
                    if (this.isAiCancelled) {
                        this.runOnUiThread(() -> this.setAiLoadingState(false));
                        return;
                    }

                    JSONObject response = new JSONObject(body);
                    JSONArray choices = response.optJSONArray("choices");
                    if (choices == null || choices.length() <= 0 || (first = choices.optJSONObject(0)) == null || (msg = first.optJSONObject("message")) == null) {
                        this.runOnUiThread(() -> this.setAiLoadingState(false));
                        break block43;
                    }
                    String content = msg.optString("content", "").trim();
                    MobileDiagnostics.append((Context)this, "AI \u8fd4\u56de\u7ed3\u679c: " + content);
                    String toolName = this.parseToolName(content);
                    if (toolName != null) {
                        try {
                            String jsonContent = content;
                            int startIdx = content.indexOf("```json");
                            if (startIdx != -1 && (endIdx = content.indexOf("```", startIdx + 7)) != -1) {
                                jsonContent = content.substring(startIdx + 7, endIdx).trim();
                            }
                            jsonContent = jsonContent.trim();
                            JSONObject tempToolCall = null;
                            try {
                                tempToolCall = new JSONObject(jsonContent);
                            } catch (Exception e) {
                                tempToolCall = new JSONObject();
                                tempToolCall.put("tool", (Object)toolName);
                                try {
                                    java.util.regex.Pattern pId = java.util.regex.Pattern.compile("\"id\"\\s*:\\s*\"([^\"]+)\"");
                                    java.util.regex.Matcher mId = pId.matcher(jsonContent);
                                    if (mId.find()) tempToolCall.put("id", (Object)mId.group(1));
                                    
                                    java.util.regex.Pattern pAction = java.util.regex.Pattern.compile("\"action\"\\s*:\\s*\"([^\"]+)\"");
                                    java.util.regex.Matcher mAction = pAction.matcher(jsonContent);
                                    if (mAction.find()) tempToolCall.put("action", (Object)mAction.group(1));
                                } catch (Exception ignored) {}
                            }
                            final JSONObject toolCall = tempToolCall;
                            if (this.isKnownAiTool(toolName)) {
                                String activeToolCallKey = this.tryBeginAiToolCall(toolCall);
                                if (activeToolCallKey == null) {
                                    this.showDuplicateAiToolCall(toolName, content);
                                    return;
                                }
                                if ("query_extensions".equals(toolName)) {
                                    JSONArray extList = new JSONArray();
                                    if (this.currentDesktopExtensions != null) {
                                        for (RemoteExtension e : this.currentDesktopExtensions) {
                                            JSONObject eObj = new JSONObject();
                                            eObj.put("id", (Object)e.extensionId);
                                            eObj.put("name", (Object)e.name);
                                            eObj.put("desc", (Object)e.description);
                                            extList.put((Object)eObj);
                                        }
                                    }
                                    String extStr = extList.toString();
                                    this.runOnUiThread(() -> {
                                        try {
                                            this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:query_extensions", content, Color.rgb((int)167, (int)243, (int)208), true);
                                            this.sendAiSystemFeedback("query_extensions", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u8fd9\u662f\u67e5\u8be2\u5230\u7684\u6269\u5c55\u5217\u8868\uff1a" + extStr);
                                        }
                                        finally {
                                            this.finishAiToolCall(activeToolCallKey);
                                        }
                                    });
                                    return;
                                }
                                if ("execute_extension".equals(toolName)) {
                                    String id = toolCall.optString("id");
                                    this.runOnUiThread(() -> {
                                        this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:execute_extension", content, Color.rgb((int)167, (int)243, (int)208), true);
                                        JSONArray locals = this.readLocalMobileExtensions();
                                        boolean isMobile = false;
                                        for (int i = 0; i < locals.length(); ++i) {
                                            JSONObject item = locals.optJSONObject(i);
                                            if (item == null || !id.equals(item.optString("id"))) continue;
                                            isMobile = true;
                                            String code = item.optString("code");
                                            if (code != null && !code.isEmpty()) {
                                                this.executeMobileScriptHeadless(code, item.optString("name", id), result -> {
                                                    this.finishAiToolCall(activeToolCallKey);
                                                    this.sendAiSystemFeedback("execute_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u6269\u5c55\u6267\u884c\u7ed3\u679c\uff1a" + result + "\n\u8bf7\u6839\u636e\u7ed3\u679c\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\uff0c\u7edd\u5bf9\u4e0d\u8981\u518d\u6b21\u8c03\u7528\u672c\u5de5\u5177\uff01");
                                                });
                                                break;
                                            }
                                            this.finishAiToolCall(activeToolCallKey);
                                            this.sendAiSystemFeedback("execute_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u6269\u5c55\u65e0\u4ee3\u7801\u3002");
                                            break;
                                        }
                                        if (!isMobile) {
                                            this.runRemoteExtensionSilently(id, id, result -> {
                                                this.finishAiToolCall(activeToolCallKey);
                                                this.sendAiSystemFeedback("execute_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u7535\u8111\u7aef\u6267\u884c\u7ed3\u679c\uff1a" + result + "\n\u8bf7\u6839\u636e\u7ed3\u679c\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\uff0c\u7edd\u5bf9\u4e0d\u8981\u518d\u6b21\u8c03\u7528\u672c\u5de5\u5177\uff01");
                                            });
                                        }
                                    });
                                    return;
                                }
                                if ("view_yanm".equals(toolName)) {
                                    String id = toolCall.optString("id");
                                    this.runOnUiThread(() -> {
                                        try {
                                            JSONObject yanmObj;
                                            JSONObject comp;
                                            this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:view_yanm", content, Color.rgb((int)167, (int)243, (int)208), true);
                                            String resultStr = "";
                                            String yanmStr = this.prefs.getString(CACHE_YANM, "{}");
                                            resultStr = id != null && !id.isEmpty() ? ((comp = (yanmObj = new JSONObject(yanmStr)).optJSONObject(id)) != null ? "\u7ec4\u4ef6\u8be6\u60c5: " + comp.toString() : "\u672a\u627e\u5230 ID \u4e3a " + id + " \u7684\u7ec4\u4ef6\u3002") : yanmStr;
                                            this.sendAiSystemFeedback("view_yanm", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u67e5\u8be2\u7ed3\u679c\uff1a" + resultStr + "\n\u8bf7\u6839\u636e\u7ed3\u679c\u5224\u65ad\u662f\u5426\u9700\u8981\u7ee7\u7eed\u8c03\u7528\u5de5\u5177\uff0c\u6216\u8005\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\u3002");
                                        }
                                        catch (Exception e) {
                                            this.sendAiSystemFeedback("view_yanm", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u67e5\u8be2\u7ed3\u679c\uff1a\u89e3\u6790\u5931\u8d25\n\u8bf7\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\u3002");
                                        }
                                        finally {
                                            this.finishAiToolCall(activeToolCallKey);
                                        }
                                    });
                                    return;
                                }
                                if ("update_yanm_component".equals(toolName)) {
                                    String id = toolCall.optString("id");
                                    String title = toolCall.optString("title");
                                    String html = toolCall.optString("html");
                                    this.runOnUiThread(() -> {
                                        this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:update_yanm_component", content, Color.rgb((int)167, (int)243, (int)208), true);
                                        try {
                                            JSONObject yanm = new JSONObject(this.prefs.getString(CACHE_YANM, "{}"));
                                            JSONObject comp = new JSONObject();
                                            comp.put("title", (Object)title);
                                            comp.put("html", (Object)html);
                                            yanm.put(id, (Object)comp);
                                            this.prefs.edit().putString(CACHE_YANM, yanm.toString()).apply();
                                            this.refreshYanm(true);
                                            this.sendAiSystemFeedback("update_yanm_component", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u5df2\u6210\u529f\u66f4\u65b0\u71d5\u5e55\u7ec4\u4ef6 " + id + " \u5e76\u5728\u9875\u9762\u70ed\u5237\u65b0\u663e\u793a\u3002\u8bf7\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u603b\u7ed3\u56de\u590d\u7528\u6237\uff0c\u4e0d\u8981\u518d\u8c03\u7528\u5de5\u5177\u3002");
                                        }
                                        catch (Exception e) {
                                            this.sendAiSystemFeedback("update_yanm_component", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u66f4\u65b0\u71d5\u5e55\u5931\u8d25\uff1a" + e.getMessage());
                                        }
                                        finally {
                                            this.finishAiToolCall(activeToolCallKey);
                                        }
                                    });
                                    return;
                                }
                                if ("manage_mobile_extension".equals(toolName)) {
                                    String action = toolCall.optString("action");
                                    String id = toolCall.optString("id");
                                    this.runOnUiThread(() -> {
                                        this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:manage_mobile_extension", content, Color.rgb((int)167, (int)243, (int)208), true);
                                        try {
                                            if ("list".equals(action)) {
                                                String listStr = this.readLocalMobileExtensions().toString();
                                                this.sendAiSystemFeedback("manage_mobile_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u8fd9\u662f\u672c\u673a\u6269\u5c55\u5217\u8868: " + listStr + "\u3002\u7edd\u5bf9\u4e0d\u53ef\u518d\u6b21\u8f93\u51fa\u5f53\u524d\u52a8\u4f5c\u7684 JSON \u5de5\u5177\u8c03\u7528\uff01");
                                            } else if ("read".equals(action)) {
                                                JSONArray locals = this.readLocalMobileExtensions();
                                                for (int i = 0; i < locals.length(); ++i) {
                                                    JSONObject item = locals.optJSONObject(i);
                                                    if (item == null || !id.equals(item.optString("id"))) continue;
                                                    this.sendAiSystemFeedback("manage_mobile_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u6269\u5c55 " + id + " \u8be6\u60c5: " + item.toString() + "\u3002\u7edd\u5bf9\u4e0d\u53ef\u518d\u6b21\u8f93\u51fa JSON \u5de5\u5177\u8c03\u7528\uff01");
                                                    return;
                                                }
                                                this.sendAiSystemFeedback("manage_mobile_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u672a\u627e\u5230\u6269\u5c55: " + id + "\u3002\u4e0d\u53ef\u8f93\u51fa JSON\u3002");
                                            } else if ("delete".equals(action)) {
                                                this.deleteLocalMobileExtension(id);
                                                this.renderLocalMobileExtensions();
                                                this.sendAiSystemFeedback("manage_mobile_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u5df2\u5220\u9664\u624b\u673a\u6269\u5c55: " + id + "\u3002\u8bf7\u7528\u81ea\u7136\u8bed\u8a00\u5411\u7528\u6237\u53cd\u9988\u7ed3\u679c\uff0c\u7edd\u5bf9\u4e0d\u53ef\u518d\u6b21\u8f93\u51fa\u4efb\u4f55 JSON \u5de5\u5177\u8c03\u7528\uff01");
                                            } else if ("create".equals(action) || "update".equals(action)) {
                                                JSONObject ext = new JSONObject();
                                                ext.put("id", (Object)id);
                                                ext.put("name", (Object)toolCall.optString("name", id));
                                                ext.put("icon", (Object)toolCall.optString("icon", "mdi:puzzle"));
                                                ext.put("description", (Object)toolCall.optString("description", "AI\u751f\u6210\u7684\u6269\u5c55"));
                                                ext.put("code", (Object)toolCall.optString("code", ""));
                                                this.upsertLocalMobileExtension(ext);
                                                this.renderLocalMobileExtensions();
                                                this.sendAiSystemFeedback("manage_mobile_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u5df2\u6210\u529f" + ("create".equals(action) ? "\u521b\u5efa" : "\u66f4\u65b0") + "\u624b\u673a\u6269\u5c55: " + id + "\u3002\u8bf7\u7528\u81ea\u7136\u8bed\u8a00\u5411\u7528\u6237\u53cd\u9988\u7ed3\u679c\uff0c\u7edd\u5bf9\u4e0d\u53ef\u518d\u6b21\u8f93\u51fa\u4efb\u4f55 JSON \u5de5\u5177\u8c03\u7528\uff01");
                                            }
                                        }
                                        catch (Exception e) {
                                            this.sendAiSystemFeedback("manage_mobile_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u6269\u5c55\u7ba1\u7406\u5931\u8d25\uff1a" + e.getMessage() + "\u3002\u4e0d\u53ef\u8f93\u51fa JSON\u3002");
                                        }
                                        finally {
                                            this.finishAiToolCall(activeToolCallKey);
                                        }
                                    });
                                    return;
                                }
                            }
                        }
                        catch (Exception exception) {
                            // empty catch block
                        }
                    }
                    this.runOnUiThread(() -> this.addAiChatMessage("AI", content, Color.rgb((int)167, (int)243, (int)208), true));
                }
                catch (Exception ex) {
                    if (this.isAiCancelled) {
                        this.currentAiConnection = null;
                        this.runOnUiThread(() -> this.setAiLoadingState(false));
                        return;
                    }
                    String errorMsg = ex.getMessage();
                    MobileDiagnostics.append((Context)this, "AI \u8bf7\u6c42\u5931\u8d25: " + errorMsg);
                    this.runOnUiThread(() -> this.addAiChatMessage("\u7cfb\u7edf", "\u9519\u8bef: " + errorMsg, Color.rgb((int)248, (int)113, (int)113), false));
                }
            }
            this.runOnUiThread(() -> this.setAiLoadingState(false));
        });
    }
    private void showPromptEditDialog() {
        LinearLayout dialogLayout = new LinearLayout((Context)this);
        dialogLayout.setOrientation(1);
        dialogLayout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
        dialogLayout.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
        EditText promptInput = this.multiInput("\u7cfb\u7edf\u63d0\u793a\u8bcd", this.prefs.getString("aiSystemPrompt", DEFAULT_SYSTEM_PROMPT));
        dialogLayout.addView((View)promptInput);
        LinearLayout buttonsLayout = new LinearLayout((Context)this);
        buttonsLayout.setOrientation(0);
        buttonsLayout.setPadding(0, this.dp(10), 0, 0);
        Button resetBtn = this.button("\u6062\u590d\u9ed8\u8ba4");
        Button saveBtn = this.button("\u4fdd\u5b58");
        buttonsLayout.addView((View)resetBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        buttonsLayout.addView((View)saveBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        dialogLayout.addView((View)buttonsLayout);
        AlertDialog dialog = new AlertDialog.Builder((Context)this, 16974545).setTitle((CharSequence)"\u7f16\u8f91\u7cfb\u7edf\u63d0\u793a\u8bcd").setView((View)dialogLayout).show();
        resetBtn.setOnClickListener(v -> promptInput.setText((CharSequence)DEFAULT_SYSTEM_PROMPT));
        saveBtn.setOnClickListener(v -> {
            this.prefs.edit().putString("aiSystemPrompt", promptInput.getText().toString()).apply();
            this.setStatus("\u63d0\u793a\u8bcd\u5df2\u4fdd\u5b58\u3002");
            dialog.dismiss();
        });
    }

    private void showAiSettingsDialog() {
        LinearLayout dialogLayout = new LinearLayout((Context)this);
        dialogLayout.setOrientation(1);
        dialogLayout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
        dialogLayout.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
        EditText baseUrlInputLocal = this.input("Base URL", this.prefs.getString("aiBaseUrl", ""));
        EditText apiKeyInputLocal = this.input("API Key", this.prefs.getString("aiApiKey", ""));
        EditText modelInputLocal = this.input("Model", this.prefs.getString("aiModel", ""));
        dialogLayout.addView((View)baseUrlInputLocal);
        dialogLayout.addView((View)apiKeyInputLocal);
        dialogLayout.addView((View)modelInputLocal);
        LinearLayout buttonsLayout = new LinearLayout((Context)this);
        buttonsLayout.setOrientation(0);
        buttonsLayout.setPadding(0, this.dp(10), 0, 0);
        Button pullBtn = this.button("\u62c9\u53d6\u914d\u7f6e");
        Button saveBtn = this.button("\u4fdd\u5b58");
        buttonsLayout.addView((View)pullBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        buttonsLayout.addView((View)saveBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, this.dp(44), 1.0f));
        dialogLayout.addView((View)buttonsLayout);
        AlertDialog dialog = new AlertDialog.Builder((Context)this, 16974545).setTitle((CharSequence)"AI \u914d\u7f6e").setView((View)dialogLayout).show();
        pullBtn.setOnClickListener(v -> {
            this.setStatus("\u6b63\u5728\u62c9\u53d6\u4e91\u7aef\u914d\u7f6e...");
            this.executor.execute(() -> {
                try {
                    JSONObject settings;
                    String baseUrl = this.normalizedBaseUrl();
                    String token = this.requireToken();
                    try {
                        settings = YanziApiClient.fetchSettings(baseUrl, token);
                    }
                    catch (Exception ex) {
                        if (!MainActivity.isUnauthorized(ex)) {
                            throw ex;
                        }
                        token = this.refreshToken();
                        settings = YanziApiClient.fetchSettings(baseUrl, token);
                    }
                    JSONObject loadedSettings = settings;
                    this.runOnUiThread(() -> {
                        baseUrlInputLocal.setText((CharSequence)loadedSettings.optString("aiBaseUrl", ""));
                        apiKeyInputLocal.setText((CharSequence)loadedSettings.optString("aiApiKey", ""));
                        modelInputLocal.setText((CharSequence)loadedSettings.optString("aiModel", ""));
                        this.setStatus("\u62c9\u53d6\u6210\u529f\uff01");
                    });
                }
                catch (Exception ex) {
                    this.runOnUiThread(() -> this.setStatus("\u62c9\u53d6\u5931\u8d25: " + ex.getMessage()));
                }
            });
        });
        saveBtn.setOnClickListener(v -> {
            this.prefs.edit().putString("aiBaseUrl", baseUrlInputLocal.getText().toString().trim()).putString("aiApiKey", apiKeyInputLocal.getText().toString().trim()).putString("aiModel", modelInputLocal.getText().toString().trim()).apply();
            this.setStatus("AI \u914d\u7f6e\u5df2\u4fdd\u5b58\u3002");
            dialog.dismiss();
        });
    }

    /*
     * WARNING - Removed try catching itself - possible behaviour change.
     */
    private void createNewAiSession() {
        String newSessionId;
        this.currentAiSessionId = newSessionId = String.valueOf(System.currentTimeMillis());
        String sessionsJson = this.prefs.getString("aiSessionIds", "[]");
        try {
            JSONArray arr = new JSONArray(sessionsJson);
            arr.put((Object)newSessionId);
            this.prefs.edit().putString("aiSessionIds", arr.toString()).commit();
        }
        catch (Exception exception) {
            // empty catch block
        }
        this.aiChatHistory.removeAllViews();
        Object object = this.aiHistoryLock;
        synchronized (object) {
            this.aiMessagesHistory = new JSONArray();
        }
        this.saveAiHistory();
        if (this.aiChatInput != null) {
            this.aiChatInput.setText((CharSequence)"");
        }
        this.setStatus("\u5df2\u5f00\u542f\u65b0\u4f1a\u8bdd\u3002");
        this.refreshSessionDrawer();
        this.checkShowAiEmptyState();
    }

    /*
     * WARNING - Removed try catching itself - possible behaviour change.
     */
    private void clearAiHistory() {
        this.aiChatHistory.removeAllViews();
        Object object = this.aiHistoryLock;
        synchronized (object) {
            this.aiMessagesHistory = new JSONArray();
        }
        this.saveAiHistory();
        if (this.aiChatInput != null) {
            this.aiChatInput.setText((CharSequence)"");
        }
        this.setStatus("\u5f53\u524d\u4f1a\u8bdd\u5df2\u6e05\u7a7a\u3002");
        this.checkShowAiEmptyState();
    }

    /*
     * WARNING - Removed try catching itself - possible behaviour change.
     */
    private void saveAiHistory() {
        Object object = this.aiHistoryLock;
        synchronized (object) {
            if (this.currentAiSessionId != null) {
                this.prefs.edit().putString("aiMessages_" + this.currentAiSessionId, this.aiMessagesHistory.toString()).commit();
            }
        }
    }

    /*
     * WARNING - Removed try catching itself - possible behaviour change.
     */
    private void loadAiSession(String sessionId) {
        String legacyHistory;
        this.currentAiSessionId = sessionId;
        String historyStr = this.prefs.getString("aiMessages_" + sessionId, "[]");
        if ("[]".equals(historyStr) && (legacyHistory = this.prefs.getString("aiMessagesHistory", "[]")) != null && !"[]".equals(legacyHistory.trim())) {
            historyStr = legacyHistory;
        }
        try {
            JSONArray loadedHistory = new JSONArray(historyStr);
            Object object = this.aiHistoryLock;
            synchronized (object) {
                this.aiMessagesHistory = loadedHistory;
            }
            this.saveAiHistory();
            int len = loadedHistory.length();
            boolean[] merged = new boolean[len];
            this.aiChatHistory.removeAllViews();
            for (int i = 0; i < len; ++i) {
                if (merged[i]) continue;
                JSONObject msg = loadedHistory.getJSONObject(i);
                String role = msg.optString("role");
                String content = msg.optString("content");
                
                String toolName = this.parseToolName(content);
                boolean isToolCall = toolName != null;
                
                if (isToolCall) {
                    String feedbackText = "";
                    for (int j = i + 1; j < Math.min(i + 4, len); ++j) {
                        if (merged[j]) continue;
                        JSONObject nextMsg = loadedHistory.getJSONObject(j);
                        String nextRole = nextMsg.optString("role");
                        boolean nextIsRealUser = nextMsg.optBoolean("is_real_user", false);
                        if (("user".equals(nextRole) && !nextIsRealUser) || "system".equals(nextRole)) {
                            feedbackText = nextMsg.optString("content");
                            merged[j] = true;
                            break;
                        }
                    }
                    if (i > 0 && !merged[i - 1]) {
                        JSONObject prevMsg = loadedHistory.getJSONObject(i - 1);
                        String prevRole = prevMsg.optString("role");
                        if ("system".equals(prevRole)) {
                            merged[i - 1] = true;
                        }
                    }
                    this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:" + toolName, content, Color.rgb((int)167, (int)243, (int)208), false);
                    if (!feedbackText.isEmpty()) {
                        this.updateActiveToolFeedback(feedbackText);
                    }
                    merged[i] = true;
                    continue;
                }
                
                if ("user".equals(role)) {
                    boolean isRealUser = msg.optBoolean("is_real_user", false);
                    if (isRealUser) {
                        this.addAiChatMessage("\u6211", content, -1, false);
                    } else {
                        this.addAiChatMessage("\u7cfb\u7edf\u53cd\u9988", content, -256, false);
                    }
                    merged[i] = true;
                    continue;
                }
                if ("assistant".equals(role)) {
                    this.addAiChatMessage("AI", content, Color.rgb((int)167, (int)243, (int)208), false);
                    merged[i] = true;
                    continue;
                }
                if ("system".equals(role)) {
                    this.addAiChatMessage("\u7cfb\u7edf", content, Color.rgb((int)248, (int)113, (int)113), false);
                    merged[i] = true;
                }
            }
        }
        catch (Exception e) {
            Object object = this.aiHistoryLock;
            synchronized (object) {
                this.aiMessagesHistory = new JSONArray();
            }
        }
        if (this.aiDrawerLayout != null) {
            this.aiDrawerLayout.closeDrawers();
        }
        this.refreshSessionDrawer();
        this.checkShowAiEmptyState();
    }

    private void checkShowAiEmptyState() {
        if (this.aiMessagesHistory == null || this.aiMessagesHistory.length() == 0) {
            String[] prompts;
            this.aiChatHistory.removeAllViews();
            this.aiEmptyStateContainer = new LinearLayout((Context)this);
            this.aiEmptyStateContainer.setOrientation(1);
            this.aiEmptyStateContainer.setGravity(17);
            this.aiEmptyStateContainer.setPadding(0, this.dp(40), 0, this.dp(20));
            TextView title = this.textView("\u71d5\u5b50", 20, -1, true);
            title.setGravity(17);
            this.aiEmptyStateContainer.addView((View)title, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
            TextView subtitle = this.textView("\u4f60\u53ef\u4ee5\u95ee\u6211\u4efb\u4f55\u95ee\u9898\uff0c\u6216\u8005\u6267\u884c\u672c\u673a\u6269\u5c55", 14, Color.rgb((int)156, (int)163, (int)175), false);
            subtitle.setGravity(17);
            subtitle.setPadding(0, this.dp(8), 0, this.dp(24));
            this.aiEmptyStateContainer.addView((View)subtitle, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
            for (String p : prompts = new String[]{"\u67e5\u770b\u63d2\u4ef6\u5217\u8868", "\u67e5\u8be2\u8bbe\u5907\u72b6\u6001", "\u5199\u4e00\u6bb5\u6b22\u8fce\u8bed"}) {
                Button btn = this.button(p);
                btn.setBackgroundColor(Color.argb((int)80, (int)255, (int)255, (int)255));
                btn.setTextColor(-1);
                btn.setOnClickListener(v -> {
                    if (this.aiChatInput != null) {
                        this.aiChatInput.setText((CharSequence)p);
                        this.sendAiChat();
                    }
                });
                LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, this.dp(44));
                params.bottomMargin = this.dp(12);
                this.aiEmptyStateContainer.addView((View)btn, (ViewGroup.LayoutParams)params);
            }
            this.aiChatHistory.addView((View)this.aiEmptyStateContainer, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
        }
    }

    private void loadAiHistory() {
        String sessionsJson = this.prefs.getString("aiSessionIds", "[]");
        try {
            JSONArray arr = new JSONArray(sessionsJson);
            if (arr.length() > 0) {
                this.loadAiSession(arr.getString(arr.length() - 1));
            } else {
                this.createNewAiSession();
            }
        }
        catch (Exception e) {
            this.createNewAiSession();
        }
    }

    private void refreshSessionDrawer() {
        this.runOnUiThread(() -> {
            if (this.aiSessionListDrawer == null) {
                return;
            }
            this.aiSessionListDrawer.removeAllViews();
            String sessionsJson = this.prefs.getString("aiSessionIds", "[]");
            try {
                JSONArray arr = new JSONArray(sessionsJson);
                for (int i = arr.length() - 1; i >= 0; --i) {
                    String sid = arr.getString(i);
                    String sessionName = "\u65b0\u4f1a\u8bdd";
                    String historyStr = this.prefs.getString("aiMessages_" + sid, "[]");
                    try {
                        JSONArray history = new JSONArray(historyStr);
                        for (int j = 0; j < history.length(); ++j) {
                            JSONObject msg = history.getJSONObject(j);
                            String role = msg.optString("role");
                            String content = msg.optString("content");
                            if ("user".equals(role)) {
                                boolean isRealUser = msg.optBoolean("is_real_user", false);
                                if (isRealUser) {
                                    sessionName = content;
                                    break;
                                }
                                if (content.startsWith("\u6267\u884c\u5de5\u5177\u8c03\u7528") || content.contains("\u7cfb\u7edf\u53cd\u9988") || content.startsWith("\u9519\u8bef:")) continue;
                                sessionName = content;
                                break;
                            }
                        }
                    }
                    catch (Exception exception) {
                        // empty catch block
                    }
                    if ("\u65b0\u4f1a\u8bdd".equals(sessionName)) {
                        try {
                            long ts = Long.parseLong(sid);
                            java.text.SimpleDateFormat sdf = new java.text.SimpleDateFormat("MM-dd HH:mm");
                            sessionName = "\u65b0\u4f1a\u8bdd (" + sdf.format(new java.util.Date(ts)) + ")";
                        }
                        catch (Exception exception) {
                            sessionName = "\u65b0\u4f1a\u8bdd";
                        }
                    } else {
                        sessionName = sessionName.replace("\n", " ").replace("\r", " ").trim();
                        if (sessionName.length() > 10) {
                            sessionName = sessionName.substring(0, 10) + "...";
                        }
                    }
                    Button btn = this.button(this.currentAiSessionId != null && this.currentAiSessionId.equals(sid) ? "\u25b6 " + sessionName : sessionName);
                    btn.setPadding(this.dp(16), this.dp(16), this.dp(16), this.dp(16));
                    btn.setBackgroundColor(0);
                    btn.setTextColor(-1);
                    btn.setGravity(19);
                    btn.setOnClickListener(v -> this.loadAiSession(sid));
                    this.aiSessionListDrawer.addView((View)btn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
                }
            }
            catch (Exception exception) {
                // empty catch block
            }
        });
    }

    private void addAiChatMessage(String sender, String text, int color, boolean saveToHistory) {
        TextView tv;
        if (sender != null && sender.startsWith("\u7cfb\u7edf\u53cd\u9988")) {
            String toolName = "";
            if (sender.contains(":")) {
                toolName = sender.substring(sender.indexOf(":") + 1);
            }
            if (this.currentActiveToolMessageInfo != null && this.currentActiveToolMessageInfo.sender.equals("工具调用:" + toolName)) {
                this.updateActiveToolFeedback(text);
                if (saveToHistory) {
                    try {
                        JSONObject msg = new JSONObject();
                        msg.put("role", (Object)"user");
                        msg.put("content", (Object)text);
                        Object object = this.aiHistoryLock;
                        synchronized (object) {
                            this.aiMessagesHistory.put((Object)msg);
                        }
                        this.saveAiHistory();
                        this.refreshSessionDrawer();
                    }
                    catch (Exception exception) {}
                }
                this.currentActiveToolMessageInfo = null;
                return;
            }
        }
        if (this.aiLoadingPlaceholderView != null) {
            this.aiChatHistory.removeView(this.aiLoadingPlaceholderView);
            this.aiLoadingPlaceholderView = null;
        }
        if (this.aiEmptyStateContainer != null) {
            this.aiChatHistory.removeView((View)this.aiEmptyStateContainer);
            this.aiEmptyStateContainer = null;
        }
        String displayText = text;
        if ("AI".equals(sender)) {
            String toolName = parseToolName(text);
            if (toolName != null) {
                String jsonContent = text;
                int startIdx = text.indexOf("```json");
                int endIdx;
                if (startIdx != -1 && (endIdx = text.indexOf("```", startIdx + 7)) != -1) {
                    jsonContent = text.substring(startIdx + 7, endIdx).trim();
                }
                jsonContent = jsonContent.trim();
                try {
                    JSONObject toolCall = new JSONObject(jsonContent);
                    if ("execute_extension".equals(toolName)) {
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: execute_extension (id: " + toolCall.optString("id") + ")";
                    } else if ("update_yanm_component".equals(toolName)) {
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: update_yanm_component (id: " + toolCall.optString("id") + ")";
                    } else if ("manage_mobile_extension".equals(toolName)) {
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: manage_mobile_extension (action: " + toolCall.optString("action") + ", id: " + toolCall.optString("id") + ")";
                    } else if ("view_yanm".equals(toolName)) {
                        String toolId = toolCall.optString("id");
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: view_yanm" + (toolId.isEmpty() ? "" : " (id: " + toolId + ")");
                    } else {
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolName;
                    }
                } catch (Exception ignored) {
                    displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolName;
                }
            }
        }
        LinearLayout msgContainer = new LinearLayout((Context)this);
        msgContainer.setOrientation(1);
        LinearLayout.LayoutParams containerParams = new LinearLayout.LayoutParams(-1, -2);
        containerParams.bottomMargin = this.dp(12);
        int historyIndex = saveToHistory ? this.aiMessagesHistory.length() : -1;
        AiMessageInfo info = new AiMessageInfo(sender, text, historyIndex, msgContainer);
        boolean isUser = "\u6211".equals(sender) || color == -1 && "\u6211".equals(sender);
        boolean isSystem = "\u7cfb\u7edf".equals(sender) || (sender != null && sender.startsWith("\u7cfb\u7edf:"));
        boolean isFeedback = "\u7cfb\u7edf\u53cd\u9988".equals(sender) || (sender != null && sender.startsWith("\u7cfb\u7edf\u53cd\u9988"));
        boolean isToolMsg = sender != null && sender.startsWith("\u5de5\u5177\u8c03\u7528:");
        if (isUser) {
            msgContainer.setGravity(0x800005);
            tv = new TextView((Context)this);
            tv.setText((CharSequence)displayText, TextView.BufferType.SPANNABLE);
            tv.setTextColor(Color.rgb((int)74, (int)222, (int)128));
            tv.setTextSize(2, 14.0f);
            tv.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
            tv.setTextIsSelectable(true);
            GradientDrawable bg = new GradientDrawable();
            bg.setColor(Color.argb((int)30, (int)74, (int)222, (int)128));
            bg.setCornerRadius((float)this.dp(12));
            tv.setBackground((Drawable)bg);
            tv.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            msgContainer.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            msgContainer.addView((View)tv, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
        } else if (isToolMsg) {
            msgContainer.setGravity(0x800003);
            LinearLayout header = new LinearLayout((Context)this);
            header.setOrientation(0);
            header.setGravity(16);
            TextView headerText = new TextView((Context)this);
            String toolLabel = sender.substring(sender.indexOf(":") + 1);
            headerText.setText((CharSequence)("\u25b6 \ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolLabel + " (\u70b9\u51fb\u5c55\u5f00)"));
            int headerColor = Color.rgb((int)167, (int)243, (int)208);
            headerText.setTextColor(headerColor);
            headerText.setTextSize(2, 12.0f);
            headerText.setPadding(0, this.dp(4), 0, this.dp(4));
            header.addView((View)headerText);
            
            LinearLayout contentContainer = new LinearLayout((Context)this);
            contentContainer.setOrientation(1);
            contentContainer.setVisibility(8);
            
            TextView contentText1 = new TextView((Context)this);
            contentText1.setText((CharSequence)("AI \u539f\u59cb\u56de\u590d\uff1a\n" + displayText), TextView.BufferType.SPANNABLE);
            contentText1.setTextColor(Color.rgb(156, 163, 175));
            contentText1.setTextSize(2, 12.0f);
            contentText1.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
            contentText1.setTextIsSelectable(true);
            
            TextView contentText2 = new TextView((Context)this);
            contentText2.setText((CharSequence)("\u7cfb\u7edf\u6267\u884c\u7ed3\u679c\uff0c\u8be6\u60c5\u5982\u4e0b\uff1a\n\u2699\ufe0f \u6b63\u5728\u6267\u884c\u5de5\u5177..."), TextView.BufferType.SPANNABLE);
            contentText2.setTextColor(Color.rgb(156, 163, 175));
            contentText2.setTextSize(2, 12.0f);
            contentText2.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(8));
            contentText2.setTextIsSelectable(true);
            
            contentContainer.addView(contentText1);
            contentContainer.addView(contentText2);
            
            header.setOnClickListener(v -> {
                boolean isHidden = contentContainer.getVisibility() == 8;
                contentContainer.setVisibility(isHidden ? 0 : 8);
                headerText.setText((CharSequence)(isHidden ? "\u25bc \ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolLabel + " (\u70b9\u51fb\u6298\u53e0)" : "\u25b6 \ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolLabel + " (\u70b9\u51fb\u5c55\u5f00)"));
            });
            header.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            contentText1.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            contentText2.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            msgContainer.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            msgContainer.addView((View)header);
            msgContainer.addView((View)contentContainer);
            
            info.feedbackTextView = contentText2;
            this.currentActiveToolMessageInfo = info;
        } else if (isSystem || isFeedback) {
            msgContainer.setGravity(0x800003);
            LinearLayout header = new LinearLayout((Context)this);
            header.setOrientation(0);
            header.setGravity(16);
            TextView headerText = new TextView((Context)this);
            String feedbackTool = "";
            if (sender != null && sender.contains(":")) {
                feedbackTool = sender.substring(sender.indexOf(":") + 1);
            }
            String toolLabel = feedbackTool.isEmpty() ? "\u7cfb\u7edf\u6d88\u606f" : feedbackTool;
            headerText.setText((CharSequence)("\u25b6 \ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolLabel + " (\u70b9\u51fb\u5c55\u5f00)"));
            int headerColor = isFeedback ? Color.rgb((int)234, (int)179, (int)8) : Color.rgb((int)156, (int)163, (int)175);
            headerText.setTextColor(headerColor);
            headerText.setTextSize(2, 12.0f);
            headerText.setPadding(0, this.dp(4), 0, this.dp(4));
            header.addView((View)headerText);
            TextView contentText = new TextView((Context)this);
            contentText.setText((CharSequence)displayText, TextView.BufferType.SPANNABLE);
            contentText.setTextColor(headerColor);
            contentText.setTextSize(2, 12.0f);
            contentText.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(8));
            contentText.setTextIsSelectable(true);
            contentText.setVisibility(8);
            header.setOnClickListener(v -> {
                boolean isHidden = contentText.getVisibility() == 8;
                contentText.setVisibility(isHidden ? 0 : 8);
                headerText.setText((CharSequence)(isHidden ? "\u25bc \ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolLabel + " (\u70b9\u51fb\u6298\u53e0)" : "\u25b6 \ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolLabel + " (\u70b9\u51fb\u5c55\u5f00)"));
            });
            header.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            contentText.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            msgContainer.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            msgContainer.addView((View)header);
            msgContainer.addView((View)contentText);
        } else {
            msgContainer.setGravity(0x800003);
            msgContainer.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            this.renderMarkdownMessage(msgContainer, displayText, -1, info);
        }
        msgContainer.setTag((Object)info);
        this.aiChatHistory.addView((View)msgContainer, (ViewGroup.LayoutParams)containerParams);
        if (saveToHistory) {
            try {
                JSONObject msg = new JSONObject();
                boolean isAiRole = "AI".equals(sender) || (sender != null && sender.startsWith("\u5de5\u5177\u8c03\u7528:"));
                msg.put("role", (Object)("\u6211".equals(sender) || isFeedback ? "user" : (isAiRole ? "assistant" : "system")));
                msg.put("content", (Object)text);
                if ("\u6211".equals(sender)) {
                    msg.put("is_real_user", true);
                }
                Object object = this.aiHistoryLock;
                synchronized (object) {
                    this.aiMessagesHistory.put((Object)msg);
                }
                this.saveAiHistory();
                this.refreshSessionDrawer();
            }
            catch (Exception msg) {
                // empty catch block
            }
        }
        if (this.aiChatHistory.getParent() instanceof ScrollView) {
            ScrollView sv = (ScrollView)this.aiChatHistory.getParent();
            sv.post(() -> {
                int scrollY = sv.getScrollY();
                View child = sv.getChildAt(0);
                if (child != null) {
                    int diff = child.getBottom() - (sv.getHeight() + scrollY);
                    if (diff <= dp(150) || scrollY == 0 || this.aiChatHistory.getChildCount() <= 3) {
                        sv.fullScroll(130);
                    }
                } else {
                    sv.fullScroll(130);
                }
            });
        }
        if (this.isTtsEnabled && "AI".equals(sender) && saveToHistory) {
            String toolName = parseToolName(text);
            if (toolName == null) {
                this.speakText(text);
            }
        }
    }

    private void renderYanm(JSONObject yanm) {
        this.currentYanmSnapshot = yanm;
        this.currentYanmState = MainActivity.firstObject(yanm, "componentState", "ComponentState");
        if (this.currentYanmState == null) {
            this.currentYanmState = new JSONObject();
            try {
                this.currentYanmSnapshot.put("componentState", (Object)this.currentYanmState);
            }
            catch (Exception exception) {
                // empty catch block
            }
        }
        for (WebView webView : this.activeYanmWebViews.values()) {
            if (webView == null) continue;
            try {
                webView.destroy();
            }
            catch (Exception exception) {}
        }
        this.activeYanmWebViews.clear();
        this.yanmList.removeAllViews();
        JSONArray components = MainActivity.firstArray(yanm, "components", "Components");
        if (components == null || components.length() == 0) {
            this.yanmList.addView((View)this.textView("\u6682\u65e0\u71d5\u5e55\u7ec4\u4ef6\u3002", 13, Color.rgb((int)148, (int)163, (int)184), false));
            return;
        }
        ArrayList<JSONObject> sortedList = new ArrayList<JSONObject>();
        ArrayList<JSONObject> remainingList = new ArrayList<JSONObject>();
        for (int i = 0; i < components.length(); ++i) {
            JSONObject comp = components.optJSONObject(i);
            if (comp == null) continue;
            String compId = MainActivity.firstNonEmpty(comp.optString("id"), comp.optString("Id"), comp.optString("title"), comp.optString("Title"), comp.optString("name"), comp.optString("Name"), "comp_" + i);
            int sortedIndex = this.sortedComponentIds.indexOf(compId);
            if (sortedIndex >= 0) {
                sortedList.add(comp);
                continue;
            }
            remainingList.add(comp);
        }
        sortedList.sort((c1, c2) -> {
            String id1 = MainActivity.firstNonEmpty(c1.optString("id"), c1.optString("Id"), c1.optString("title"), c1.optString("Title"), c1.optString("name"), c1.optString("Name"));
            String id2 = MainActivity.firstNonEmpty(c2.optString("id"), c2.optString("Id"), c2.optString("title"), c2.optString("Title"), c2.optString("name"), c2.optString("Name"));
            return Integer.compare(this.sortedComponentIds.indexOf(id1), this.sortedComponentIds.indexOf(id2));
        });
        ArrayList<JSONObject> finalComponents = new ArrayList<JSONObject>(sortedList);
        finalComponents.addAll(remainingList);
        int i = 0;
        while (i < finalComponents.size()) {
            JSONObject component = (JSONObject)finalComponents.get(i);
            String title = MainActivity.firstNonEmpty(component.optString("title"), component.optString("Title"), component.optString("name"), component.optString("Name"), "\u7ec4\u4ef6 " + (i + 1));
            String type = MainActivity.firstNonEmpty(component.optString("type"), component.optString("Type"), component.optString("kind"), component.optString("Kind"), "component");
            String componentId = MainActivity.firstNonEmpty(component.optString("id"), component.optString("Id"), title);
            LinearLayout card = this.card();
            card.setTag((Object)("yanm_comp_" + componentId));
            LinearLayout.LayoutParams cardParams = new LinearLayout.LayoutParams(-1, -2);
            cardParams.setMargins(0, this.dp(8), 0, this.dp(8));
            card.setLayoutParams((ViewGroup.LayoutParams)cardParams);
            LinearLayout headerLayout = new LinearLayout((Context)this);
            headerLayout.setOrientation(0);
            headerLayout.setGravity(16);
            TextView titleView = this.textView(title, 16, -1, true);
            headerLayout.addView((View)titleView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
            String html = MainActivity.firstNonEmpty(component.optString("html"), component.optString("Html"), component.optString("markup"), component.optString("Markup"), component.optString("contentHtml"), component.optString("ContentHtml"));
            TextView arrowView = null;
            if (!html.isEmpty()) {
                boolean isExpanded = this.expandedComponentIds.contains(componentId);
                arrowView = this.textView(isExpanded ? "\u25b2" : "\u25bc", 14, Color.rgb((int)34, (int)211, (int)238), false);
                arrowView.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
                headerLayout.addView((View)arrowView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
            }
            card.addView((View)headerLayout);
            if (!type.isEmpty() && !type.equalsIgnoreCase("component")) {
                card.addView((View)this.textView(type, 11, Color.rgb((int)94, (int)234, (int)212), false));
            }
            if (!html.isEmpty()) {
                LinearLayout previewHost = new LinearLayout((Context)this);
                previewHost.setOrientation(1);
                card.addView((View)previewHost);
                String htmlForPreview = html;
                TextView finalArrow = arrowView;
                if (this.expandedComponentIds.contains(componentId)) {
                    this.toggleYanmPreview(previewHost, htmlForPreview, componentId, title, finalArrow, true);
                }
                card.setOnClickListener(v -> this.toggleYanmPreview(previewHost, htmlForPreview, componentId, title, finalArrow, false));
            } else {
                String summary = MainActivity.summarizeYanmComponent(component);
                card.addView((View)this.textView(summary, 12, Color.rgb((int)182, (int)194, (int)214), false));
            }
            int index = i++;
            ArrayList<JSONObject> finalCompsRef = finalComponents;
            card.setOnLongClickListener(v -> {
                this.showSortDialog(componentId, index, finalCompsRef);
                return true;
            });
            this.yanmList.addView((View)card);
        }
        this.setStatus("\u71d5\u5e55\u5df2\u52a0\u8f7d\uff1a" + finalComponents.size() + " \u4e2a\u7ec4\u4ef6\u3002");
    }

    private void toggleYanmPreview(LinearLayout previewHost, String html, String componentId, String componentTitle, TextView arrowView, boolean forceExpand) {
        boolean isCurrentlyExpanded;
        WebView existingWebView = this.activeYanmWebViews.get(componentId);
        boolean bl = isCurrentlyExpanded = existingWebView != null && previewHost.getChildCount() > 0;
        if (isCurrentlyExpanded && !forceExpand) {
            previewHost.removeAllViews();
            try {
                existingWebView.destroy();
            }
            catch (Exception exception) {
                // empty catch block
            }
            this.activeYanmWebViews.remove(componentId);
            this.expandedComponentIds.remove(componentId);
            this.saveExpandedState();
            if (arrowView != null) {
                arrowView.setText((CharSequence)"\u25bc");
            }
            return;
        }
        if (existingWebView != null) {
            previewHost.removeAllViews();
            try {
                existingWebView.destroy();
            }
            catch (Exception exception) {
                // empty catch block
            }
            this.activeYanmWebViews.remove(componentId);
        }
        WebView webView = new WebView((Context)this);
        this.activeYanmWebViews.put(componentId, webView);
        this.expandedComponentIds.add(componentId);
        this.saveExpandedState();
        if (arrowView != null) {
            arrowView.setText((CharSequence)"\u25b2");
        }
        webView.setBackgroundColor(0);
        webView.setVerticalScrollBarEnabled(false);
        webView.setHorizontalScrollBarEnabled(false);
        webView.getSettings().setJavaScriptEnabled(true);
        webView.getSettings().setDomStorageEnabled(true);
        webView.getSettings().setLoadWithOverviewMode(false);
        webView.getSettings().setUseWideViewPort(false);
        webView.getSettings().setTextZoom(145);
        webView.setInitialScale(145);
        webView.addJavascriptInterface((Object)new YanmMobileBridge(componentId, componentTitle), "yanmMobileHost");
        webView.loadDataWithBaseURL(null, MainActivity.wrapYanmHtml(html, componentId, componentTitle), "text/html", "UTF-8", null);
        previewHost.addView((View)webView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(420)));
    }

    private String requireToken() {
        String token = this.prefs.getString("token", "");
        if (token == null || token.trim().isEmpty()) {
            return this.refreshToken();
        }
        return token;
    }

    private String refreshToken() {
        try {
            String baseUrl = this.normalizedBaseUrl();
            String email = this.prefs.getString("email", "");
            String password = this.prefs.getString("password", "");
            if (email == null || email.trim().isEmpty() || password == null || password.isEmpty()) {
                throw new IllegalStateException("\u8bf7\u5148\u767b\u5f55\u3002");
            }
            String token = YanziApiClient.login(baseUrl, email.trim(), password);
            this.prefs.edit().putString("baseUrl", baseUrl).putString("token", token).apply();
            return token;
        }
        catch (Exception ex) {
            throw new IllegalStateException("\u767b\u5f55\u6001\u5df2\u5931\u6548\uff0c\u8bf7\u91cd\u65b0\u767b\u5f55\uff1a" + ex.getMessage());
        }
    }

    private static boolean isUnauthorized(Exception ex) {
        String message = ex.getMessage();
        return message != null && (message.contains("401") || message.toLowerCase(Locale.ROOT).contains("token expired") || message.toLowerCase(Locale.ROOT).contains("unauthorized"));
    }

    private String normalizedBaseUrl() {
        String value;
        if (this.baseUrlInput != null) {
            value = this.baseUrlInput.getText().toString().trim();
        } else {
            String string = value = this.prefs != null ? this.prefs.getString("baseUrl", DEFAULT_BASE_URL) : DEFAULT_BASE_URL;
        }
        if (value == null || value.trim().isEmpty()) {
            return DEFAULT_BASE_URL;
        }
        int v1Index = value.indexOf("/v1/");
        if (v1Index >= 0) {
            value = value.substring(0, v1Index);
        }
        if (value.endsWith("/health")) {
            value = value.substring(0, value.length() - "/health".length());
        }
        if (value.contains("yanzi.luoluoluo.cc.cd")) {
            value = DEFAULT_BASE_URL;
        }
        while (value.endsWith("/")) {
            value = value.substring(0, value.length() - 1);
        }
        return value.trim().isEmpty() ? DEFAULT_BASE_URL : value;
    }

    private String getOrCreateDeviceId() {
        String existing = this.prefs.getString("deviceId", null);
        if (existing != null && !existing.trim().isEmpty()) {
            return existing;
        }
        String created = "android-" + UUID.randomUUID();
        this.prefs.edit().putString("deviceId", created).apply();
        return created;
    }

    private String buildDeviceName() {
        return MainActivity.buildDeviceDisplayName();
    }

    private static String buildDeviceDisplayName() {
        String marketName = MainActivity.firstNonEmpty(MainActivity.getSystemProperty("ro.product.marketname"), MainActivity.getSystemProperty("ro.vendor.product.marketname"), MainActivity.getSystemProperty("ro.product.vendor.marketname"), MainActivity.getSystemProperty("ro.product.odm.marketname"), MainActivity.getSystemProperty("ro.config.marketing_name"));
        if (!marketName.isEmpty()) {
            return marketName;
        }
        String maker = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.trim();
        String model = Build.MODEL == null ? "" : Build.MODEL.trim();
        String name = (maker + " " + model).trim();
        return name.trim().isEmpty() ? "Android \u624b\u673a" : name;
    }

    private static String getSystemProperty(String key) {
        try {
            Class<?> systemProperties = Class.forName("android.os.SystemProperties");
            Method get = systemProperties.getMethod("get", String.class);
            Object value = get.invoke(null, key);
            return value == null ? "" : value.toString().trim();
        }
        catch (Exception ignored) {
            return "";
        }
    }

    private void setStatus(String status) {
        this.diagnosticLog.setLength(0);
        this.diagnosticLog.append(MobileDiagnostics.append((Context)this, status));
        this.statusText.setText((CharSequence)this.diagnosticLog.toString());
    }

    private void refreshDiagnosticLogFromStore() {
        if (this.statusText == null) {
            return;
        }
        String stored = MobileDiagnostics.get((Context)this);
        if (!stored.equals(this.diagnosticLog.toString())) {
            this.diagnosticLog.setLength(0);
            this.diagnosticLog.append(stored);
            this.statusText.setText((CharSequence)stored);
        }
    }

    private void copyDiagnostics() {
        this.refreshDiagnosticLogFromStore();
        String value = this.diagnosticLog.length() == 0 ? this.statusText.getText().toString() : this.diagnosticLog.toString();
        ClipboardManager manager = (ClipboardManager)this.getSystemService("clipboard");
        manager.setPrimaryClip(ClipData.newPlainText((CharSequence)"Yanzi mobile diagnostics", (CharSequence)value));
        Toast.makeText((Context)this, (CharSequence)"\u5df2\u590d\u5236\u65e5\u5fd7", (int)0).show();
    }

    private void trimDiagnosticLog() {
        int maxLength = 6000;
        if (this.diagnosticLog.length() <= maxLength) {
            return;
        }
        this.diagnosticLog.delete(0, this.diagnosticLog.length() - maxLength);
    }

    private void scheduleYanmCloudSync(String reason) {
        if (this.pendingYanmSync != null) {
            this.yanmSyncHandler.removeCallbacks(this.pendingYanmSync);
        }
        this.pendingYanmSync = () -> this.syncYanmStateToCloud(reason);
        this.yanmSyncHandler.postDelayed(this.pendingYanmSync, 1000L);
        this.setStatus("\u71d5\u5e55\u72b6\u6001\u5f85\u540c\u6b65\u5230\u4e91\u7aef\uff1a" + reason);
    }

    private void syncYanmStateToCloud(String reason) {
        JSONObject snapshot = this.currentYanmSnapshot;
        if (snapshot == null) {
            this.setStatus("\u71d5\u5e55\u540c\u6b65\u8df3\u8fc7\uff1a\u6ca1\u6709\u5b8c\u6574\u5feb\u7167\u3002");
            return;
        }
        this.executor.execute(() -> {
            try {
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    YanziApiClient.putYanmState(baseUrl, token, snapshot);
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    YanziApiClient.putYanmState(baseUrl, token, snapshot);
                }
                this.runOnUiThread(() -> this.setStatus("\u71d5\u5e55\u72b6\u6001\u5df2\u540c\u6b65\u5230\u4e91\u7aef\uff1a" + reason));
            }
            catch (Exception ex) {
                this.runOnUiThread(() -> this.setStatus("\u71d5\u5e55\u72b6\u6001\u540c\u6b65\u5931\u8d25\uff1a" + ex.getMessage()));
            }
        });
    }

    private TextView textView(String text, int sp, int color, boolean bold) {
        TextView view = new TextView((Context)this);
        view.setText((CharSequence)text);
        view.setTextColor(color);
        view.setTextSize((float)sp);
        view.setPadding(0, this.dp(6), 0, this.dp(6));
        if (bold) {
            view.setTypeface(view.getTypeface(), 1);
        }
        return view;
    }

    private TextView sectionTitle(String text) {
        TextView view = this.textView(text, 18, -1, true);
        view.setPadding(0, this.dp(18), 0, this.dp(8));
        return view;
    }

    private LinearLayout card() {
        LinearLayout card = new LinearLayout((Context)this);
        card.setOrientation(1);
        card.setPadding(this.dp(14), this.dp(12), this.dp(14), this.dp(12));
        card.setBackgroundColor(Color.rgb((int)30, (int)30, (int)30));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(-1, -2);
        params.setMargins(0, this.dp(8), 0, this.dp(8));
        card.setLayoutParams((ViewGroup.LayoutParams)params);
        return card;
    }

    private LinearLayout iconCard() {
        LinearLayout card = new LinearLayout((Context)this);
        card.setOrientation(1);
        card.setGravity(17);
        card.setPadding(this.dp(6), this.dp(8), this.dp(6), this.dp(8));
        return card;
    }

    private EditText input(String hint, String value) {
        EditText input = new EditText((Context)this);
        input.setHint((CharSequence)hint);
        input.setText((CharSequence)(value == null ? "" : value));
        input.setSingleLine(true);
        input.setTextColor(-1);
        input.setHintTextColor(Color.rgb((int)148, (int)163, (int)184));
        input.setPadding(this.dp(12), this.dp(10), this.dp(12), this.dp(10));
        return input;
    }

    private EditText multiInput(String hint, String value) {
        EditText input = this.input(hint, value);
        input.setSingleLine(false);
        input.setMinLines(5);
        input.setGravity(48);
        return input;
    }

    private Button button(String text) {
        Button button = new Button((Context)this);
        button.setText((CharSequence)text);
        return button;
    }

    private void showPhotoProgress(String text) {
        this.hidePhotoProgress();
        LinearLayout panel = new LinearLayout((Context)this);
        panel.setOrientation(0);
        panel.setGravity(16);
        panel.setPadding(this.dp(14), this.dp(10), this.dp(14), this.dp(10));
        GradientDrawable background = new GradientDrawable();
        background.setColor(Color.argb((int)238, (int)6, (int)17, (int)31));
        background.setCornerRadius((float)this.dp(16));
        background.setStroke(this.dp(1), Color.argb((int)160, (int)34, (int)211, (int)238));
        panel.setBackground((Drawable)background);
        TextView spinner = this.textView("...", 18, Color.rgb((int)34, (int)211, (int)238), true);
        TextView label = this.textView(text, 14, -1, false);
        label.setPadding(this.dp(10), 0, 0, 0);
        panel.addView((View)spinner, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(34), this.dp(34)));
        panel.addView((View)label, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(this.dp(230), this.dp(56));
        params.gravity = 49;
        params.topMargin = this.dp(72);
        this.photoProgressView = panel;
        this.addContentView(this.photoProgressView, (ViewGroup.LayoutParams)params);
    }

    private void hidePhotoProgress() {
        if (this.photoProgressView != null && this.photoProgressView.getParent() instanceof ViewGroup) {
            ((ViewGroup)this.photoProgressView.getParent()).removeView(this.photoProgressView);
        }
        this.photoProgressView = null;
    }

    private int dp(int value) {
        return (int)((float)value * this.getResources().getDisplayMetrics().density + 0.5f);
    }

    private static String extractSharedText(Intent intent) {
        if (intent == null || !"android.intent.action.SEND".equals(intent.getAction()) || !"text/plain".equals(intent.getType())) {
            return null;
        }
        return intent.getStringExtra("android.intent.extra.TEXT");
    }

    private static String firstNonEmpty(String ... values) {
        for (String value : values) {
            if (value == null || value.trim().isEmpty()) continue;
            return value.trim();
        }
        return "";
    }

    private static JSONArray firstArray(JSONObject object, String ... keys) {
        for (String key : keys) {
            JSONArray value = object.optJSONArray(key);
            if (value == null) continue;
            return value;
        }
        return null;
    }

    private static JSONObject firstObject(JSONObject object, String ... keys) {
        for (String key : keys) {
            JSONObject value = object.optJSONObject(key);
            if (value == null) continue;
            return value;
        }
        return null;
    }

    private static String stripHtml(String html) {
        if (html == null) {
            return "";
        }
        String text = html.replaceAll("(?i)&lt;", "<").replaceAll("(?i)&gt;", ">");
        text = text.replaceAll("(?is)<style[^>]*>.*?</style>", "");
        text = text.replaceAll("(?is)<script[^>]*>.*?</script>", "");
        text = text.replaceAll("(?i)<br\\s*/?>", "\n");
        text = text.replaceAll("(?i)</?(p|div|li|h[1-6]|tr)[^>]*>", "\n");
        text = text.replaceAll("<[^>]*>", "");
        text = text.replaceAll("&nbsp;", " ").replaceAll("&amp;", "&").replaceAll("&quot;", "\"").replaceAll("&#39;", "'");
        text = text.replaceAll("(?m)^[ \t]*\r?\n", "");
        return text.trim();
    }

    private static String summarizeYanmComponent(JSONObject component) {
        String text = MainActivity.firstNonEmpty(component.optString("html"), component.optString("Html"), component.optString("markup"), component.optString("Markup"), component.optString("contentHtml"), component.optString("ContentHtml"), component.optString("text"), component.optString("Text"), component.optString("content"), component.optString("Content"), component.optString("note"), component.optString("Note"), component.optString("description"), component.optString("Description"));
        if (text.isEmpty() && (text = MainActivity.firstNonEmpty(component.optString("title"), component.optString("Title"), component.optString("name"), component.optString("Name"), "")).isEmpty()) {
            text = "\u65e0\u53ef\u7528\u5185\u5bb9";
        }
        text = MainActivity.stripHtml(text);
        return (text = text.replaceAll("\\s+", " ").trim()).length() > 140 ? text.substring(0, 140) + "..." : text;
    }

    private static String wrapYanmHtml(String html, String componentId, String componentTitle) {
        String trimmed = html == null ? "" : html.trim();
        String mobileHead = "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no\" /><style id=\"yanm-mobile-adapter\">html,body{margin:0!important;padding:0!important;background:#07111f!important;color:#fff;min-width:0!important;overflow:auto!important;}body{font-size:18px!important;line-height:1.45!important;-webkit-text-size-adjust:145%;text-size-adjust:145%;}*{box-sizing:border-box;max-width:100%!important;}button,input,textarea,select{font-size:16px!important;}img,svg,canvas,video{max-width:100%!important;height:auto;}</style>";
        String bridge = "<script>(function(){var componentId=" + JSONObject.quote((String)componentId) + ";var componentTitle=" + JSONObject.quote((String)componentTitle) + ";window.yanm=window.yanm||{};window.yanm.componentId=componentId;window.yanm.componentTitle=componentTitle;window.yanmHost=window.yanmHost||{};function emit(d){try{window.dispatchEvent(new CustomEvent('yanm:message',{detail:d||{}}));}catch(e){}}window.yanmHost.getState=function(key){key=String(key||'');var value=String(yanmMobileHost.getState(key)||'');var res={key:key,value:value};emit({type:'host.state',key:key,value:value});return res;};window.yanmHost.setState=function(key,value){key=String(key||'');value=String(value||'');yanmMobileHost.setState(key,value);emit({type:'host.state',key:key,value:value});return {key:key,value:value};};window.yanmHost.requestSystemInfo=function(){var data=JSON.parse(yanmMobileHost.getSystemInfo());data.type='host.systemInfo';emit(data);return data;};window.yanm.invoke=function(method,args){args=args||{};if(method==='state.get')return Promise.resolve(window.yanmHost.getState(args.key));if(method==='state.set')return Promise.resolve(window.yanmHost.setState(args.key,args.value));if(method==='system.info')return Promise.resolve(window.yanmHost.requestSystemInfo());return Promise.reject(new Error('unsupported mobile method '+method));};window.dispatchEvent(new CustomEvent('yanm:message',{detail:{type:'host.ready',componentId:componentId}}));})();</script>";
        if (trimmed.toLowerCase(Locale.ROOT).contains("<html")) {
            String lower = trimmed.toLowerCase(Locale.ROOT);
            int headEnd = lower.indexOf("</head>");
            String withHead = headEnd >= 0 ? trimmed.substring(0, headEnd) + mobileHead + trimmed.substring(headEnd) : trimmed.replaceFirst("(?i)<html[^>]*>", "$0<head>" + mobileHead + "</head>");
            String lowerWithHead = withHead.toLowerCase(Locale.ROOT);
            int bodyEnd = lowerWithHead.lastIndexOf("</body>");
            return bodyEnd >= 0 ? withHead.substring(0, bodyEnd) + bridge + withHead.substring(bodyEnd) : withHead + bridge;
        }
        return "<!doctype html><html><head>" + mobileHead + "</head><body>" + trimmed + bridge + "</body></html>";
    }

    private String buildMobileScriptHtml(String source) {
        return "<!doctype html><html><body><script>window.context={mobile:{toast:function(text){yanziMobileJsHost.toast(String(text||''));},sendToDesktop:function(text){yanziMobileJsHost.sendToDesktop(String(text||''));},done:function(text){yanziMobileJsHost.done(String(text||''));},fail:function(text){yanziMobileJsHost.fail(String(text||''));},getSharedText:function(){return yanziMobileJsHost.getSharedText();},getClipboardText:function(){return Promise.resolve(yanziMobileJsHost.getClipboardText());},setClipboardText:function(text){return Promise.resolve(yanziMobileJsHost.setClipboardText(String(text||'')));},openUrl:function(url){return Promise.resolve(yanziMobileJsHost.openUrl(String(url||'')));},pickPhoto:function(){return Promise.resolve(yanziMobileJsHost.pickPhoto());},readTextFile:function(name){return Promise.resolve(JSON.parse(yanziMobileJsHost.readTextFile(String(name||''))));},saveTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.saveTextFile(String(name||''),String(text||''))));},appendTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.appendTextFile(String(name||''),String(text||''))));},httpGet:function(url){return Promise.resolve(JSON.parse(yanziMobileJsHost.httpGet(String(url||''))));},httpPostJson:function(url,jsonText){return Promise.resolve(JSON.parse(yanziMobileJsHost.httpPostJson(String(url||''),String(jsonText||''))));}}};async function __run(){try{" + source + "\n;if(typeof run==='function'){await run(window.context);}yanziMobileJsHost.done('\u811a\u672c\u6267\u884c\u5b8c\u6210');}catch(e){yanziMobileJsHost.fail(String(e&&e.message?e.message:e));}}__run();</script></body></html>";
    }

    private void executeMobileScriptHeadless(String source, String taskName, ScriptCallback callback) {
        this.runOnUiThread(() -> {
            block2: {
                try {
                    WebView runner = new WebView((Context)this);
                    runner.getSettings().setJavaScriptEnabled(true);
                    runner.addJavascriptInterface((Object)new MobileJsBridge(callback), "yanziMobileJsHost");
                    runner.loadDataWithBaseURL(null, this.buildMobileScriptHtml(source), "text/html", "UTF-8", null);
                }
                catch (Exception e) {
                    this.setStatus("\u540e\u53f0\u811a\u672c\u6267\u884c\u5931\u8d25: " + taskName + " - " + e.getMessage());
                    if (callback == null) break block2;
                    callback.onResult("\u540e\u53f0\u811a\u672c\u6267\u884c\u5931\u8d25: " + e.getMessage());
                }
            }
        });
    }

    private void updateAllAppWidgets() {
        try {
            int[] yanmWidgetIds;
            Context context = this.getApplicationContext();
            AppWidgetManager appWidgetManager = AppWidgetManager.getInstance((Context)context);
            int[] extWidgetIds = appWidgetManager.getAppWidgetIds(new ComponentName(context, ExtensionsWidgetProvider.class));
            if (extWidgetIds.length > 0) {
                Intent updateExtIntent = new Intent(context, ExtensionsWidgetProvider.class);
                updateExtIntent.setAction("android.appwidget.action.APPWIDGET_UPDATE");
                updateExtIntent.putExtra("appWidgetIds", extWidgetIds);
                context.sendBroadcast(updateExtIntent);
            }
            if ((yanmWidgetIds = appWidgetManager.getAppWidgetIds(new ComponentName(context, YanmWidgetProvider.class))).length > 0) {
                Intent updateYanmIntent = new Intent(context, YanmWidgetProvider.class);
                updateYanmIntent.setAction("android.appwidget.action.APPWIDGET_UPDATE");
                updateYanmIntent.putExtra("appWidgetIds", yanmWidgetIds);
                context.sendBroadcast(updateYanmIntent);
            }
        }
        catch (Exception exception) {
            // empty catch block
        }
    }

    private void showSetWidgetExtensionDialog(RemoteExtension extension) {
        CharSequence[] options = new String[]{"\u8bbe\u4e3a\u684c\u9762\u78c1\u8d34 1", "\u8bbe\u4e3a\u684c\u9762\u78c1\u8d34 2", "\u8bbe\u4e3a\u684c\u9762\u78c1\u8d34 3", "\u8bbe\u4e3a\u684c\u9762\u78c1\u8d34 4", "\u521b\u5efa\u684c\u9762\u5feb\u6377\u56fe\u6807", "\u53d6\u6d88\u7f6e\u9876/\u6e05\u9664\u7ed1\u5b9a", "\u590d\u5236\u6269\u5c55 ID"};
        new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u914d\u7f6e\u684c\u9762\u5c0f\u90e8\u4ef6\u5feb\u6377\u6269\u5c55").setItems(options, (dialog, which) -> {
            try {
                ClipboardManager clipboard;
                String prefsKey = "widgetExtensionsOrder";
                String orderJson = this.prefs.getString(prefsKey, "[]");
                JSONArray array = new JSONArray(orderJson);
                while (array.length() < 4) {
                    array.put((Object)"");
                }
                if (which >= 0 && which < 4) {
                    for (int i = 0; i < 4; ++i) {
                        if (!array.optString(i).equals(extension.extensionId)) continue;
                        array.put(i, (Object)"");
                    }
                    array.put(which, (Object)extension.extensionId);
                    this.setStatus("\u5df2\u5c06\u6269\u5c55 " + extension.name + " \u8bbe\u4e3a\u5c0f\u90e8\u4ef6\u5feb\u6377\u952e " + (which + 1));
                } else if (which == 4) {
                    this.createRemoteExtensionShortcut(extension);
                } else if (which == 5) {
                    for (int i = 0; i < 4; ++i) {
                        if (!array.optString(i).equals(extension.extensionId)) continue;
                        array.put(i, (Object)"");
                    }
                    this.setStatus("\u5df2\u53d6\u6d88\u6269\u5c55 " + extension.name + " \u5728\u684c\u9762\u5c0f\u90e8\u4ef6\u7684\u7ed1\u5b9a");
                } else if (which == 6 && (clipboard = (ClipboardManager)this.getSystemService("clipboard")) != null) {
                    ClipData clip = ClipData.newPlainText((CharSequence)"Extension ID", (CharSequence)extension.extensionId);
                    clipboard.setPrimaryClip(clip);
                    Toast.makeText((Context)this, (CharSequence)("\u5df2\u590d\u5236\u6269\u5c55 ID: " + extension.extensionId), (int)0).show();
                    this.setStatus("\u5df2\u590d\u5236\u6269\u5c55 ID: " + extension.extensionId);
                }
                this.prefs.edit().putString(prefsKey, array.toString()).apply();
                this.updateAllAppWidgets();
            }
            catch (Exception ex) {
                this.setStatus("\u5feb\u6377\u952e\u8bbe\u7f6e\u5931\u8d25\uff1a" + ex.getMessage());
                Toast.makeText((Context)this, (CharSequence)("\u8bbe\u7f6e\u5931\u8d25\uff1a" + ex.getMessage()), (int)0).show();
            }
        }).show();
    }

    private void createRemoteExtensionShortcut(RemoteExtension extension) {
        boolean hasShown = this.prefs.getBoolean("hasShownShortcutPermissionGuide", false);
        if (!hasShown) {
            new AlertDialog.Builder((Context)this).setTitle((CharSequence)"\u6dfb\u52a0\u684c\u9762\u56fe\u6807\u63d0\u793a").setMessage((CharSequence)"\u521b\u5efa\u684c\u9762\u5feb\u6377\u56fe\u6807\u9700\u8981\u624b\u673a\u7cfb\u7edf\u7684\u3010\u684c\u9762\u5feb\u6377\u65b9\u5f0f\u3011\u6743\u9650\u3002\n\n\u90e8\u5206\u7cfb\u7edf\uff08\u5982\u5c0f\u7c73\u3001\u7ea2\u7c73\u3001\u6f8e\u6e43OS\u3001\u534e\u4e3a\u7b49\uff09\u9ed8\u8ba4\u4f1a\u7981\u7528\u6b64\u6743\u9650\u3002\n\n\u5982\u679c\u521b\u5efa\u540e\u684c\u9762\u4e0a\u6ca1\u6709\u751f\u6210\u56fe\u6807\uff0c\u8bf7\u70b9\u51fb\u3010\u53bb\u8bbe\u7f6e\u3011\u5f00\u542f\u8be5\u6743\u9650\u3002").setPositiveButton((CharSequence)"\u53bb\u8bbe\u7f6e", (dialog, which) -> {
                this.prefs.edit().putBoolean("hasShownShortcutPermissionGuide", true).apply();
                try {
                    Intent intent = new Intent("android.settings.APPLICATION_DETAILS_SETTINGS");
                    intent.setData(Uri.parse((String)("package:" + this.getPackageName())));
                    this.startActivity(intent);
                }
                catch (Exception ex) {
                    Toast.makeText((Context)this, (CharSequence)"\u65e0\u6cd5\u81ea\u52a8\u6253\u5f00\u8bbe\u7f6e\uff0c\u8bf7\u624b\u52a8\u4e3a\u201c\u71d5\u5b50\u79fb\u52a8\u7aef\u201d\u5f00\u542f\u5feb\u6377\u65b9\u5f0f\u6743\u9650", (int)1).show();
                }
            }).setNegativeButton((CharSequence)"\u6211\u77e5\u9053\u4e86", (dialog, which) -> {
                this.prefs.edit().putBoolean("hasShownShortcutPermissionGuide", true).apply();
                this.performShortcutCreation(extension);
            }).show();
        } else {
            this.performShortcutCreation(extension);
        }
    }

    private void performShortcutCreation(RemoteExtension extension) {
        block12: {
            try {
                if (Build.VERSION.SDK_INT >= 26) {
                    ShortcutManager shortcutManager = (ShortcutManager)this.getSystemService(ShortcutManager.class);
                    if (shortcutManager != null && shortcutManager.isRequestPinShortcutSupported()) {
                        Intent shortcutIntent = new Intent((Context)this, MainActivity.class);
                        shortcutIntent.setAction("android.intent.action.VIEW");
                        shortcutIntent.putExtra("run_remote_extension_id", extension.extensionId);
                        shortcutIntent.putExtra("run_remote_extension_name", extension.name);
                        shortcutIntent.addFlags(0x14000000);
                        int size = 192;
                        Bitmap bitmap = Bitmap.createBitmap((int)size, (int)size, (Bitmap.Config)Bitmap.Config.ARGB_8888);
                        Canvas canvas = new Canvas(bitmap);
                        Paint bgPaint = new Paint(1);
                        bgPaint.setStyle(Paint.Style.FILL);
                        int baseColor = Color.rgb((int)45, (int)45, (int)45);
                        if (extension.accentHex != null && !extension.accentHex.trim().isEmpty()) {
                            try {
                                String colorStr = extension.accentHex.trim();
                                if (!colorStr.startsWith("#")) {
                                    colorStr = "#" + colorStr;
                                }
                                baseColor = Color.parseColor((String)colorStr);
                            }
                            catch (Exception colorStr) {
                                // empty catch block
                            }
                        }
                        bgPaint.setColor(baseColor);
                        float radius = (float)size * 0.22f;
                        canvas.drawRoundRect(new RectF(0.0f, 0.0f, (float)size, (float)size), radius, radius, bgPaint);
                        Path iconPath = MobileIconLibrary.resolveOrDefault(extension.icon);
                        if (iconPath != null) {
                            Path path = new Path(iconPath);
                            RectF bounds = new RectF();
                            path.computeBounds(bounds, true);
                            if (bounds.width() > 0.0f && bounds.height() > 0.0f) {
                                float targetSize = (float)size * 0.52f;
                                float scale = targetSize / Math.max(bounds.width(), bounds.height());
                                Matrix matrix = new Matrix();
                                matrix.postTranslate(-bounds.centerX(), -bounds.centerY());
                                matrix.postScale(scale, scale);
                                matrix.postTranslate((float)size / 2.0f, (float)size / 2.0f);
                                path.transform(matrix);
                                Paint iconPaint = new Paint(1);
                                iconPaint.setStyle(Paint.Style.FILL);
                                iconPaint.setColor(-1);
                                canvas.drawPath(path, iconPaint);
                            }
                        }
                        Icon icon = Icon.createWithBitmap((Bitmap)bitmap);
                        ShortcutInfo shortcutInfo = new ShortcutInfo.Builder((Context)this, "ext_" + extension.extensionId).setShortLabel((CharSequence)extension.name).setLongLabel((CharSequence)extension.name).setIcon(icon).setIntent(shortcutIntent).build();
                        boolean success = shortcutManager.requestPinShortcut(shortcutInfo, null);
                        if (success) {
                            this.setStatus("\u5df2\u5411\u7cfb\u7edf\u53d1\u9001\u521b\u5efa\u8bf7\u6c42\uff1a" + extension.name);
                            Toast.makeText((Context)this, (CharSequence)("\u5df2\u5411\u7cfb\u7edf\u53d1\u9001\u521b\u5efa\u8bf7\u6c42\uff1a" + extension.name), (int)0).show();
                        } else {
                            this.setStatus("\u7cfb\u7edf\u62d2\u7edd\u4e86\u5feb\u6377\u65b9\u5f0f\u521b\u5efa\u8bf7\u6c42");
                            Toast.makeText((Context)this, (CharSequence)"\u7cfb\u7edf\u62d2\u7edd\u4e86\u5feb\u6377\u65b9\u5f0f\u521b\u5efa\u8bf7\u6c42(\u8bf7\u68c0\u67e5\u5feb\u6377\u65b9\u5f0f\u6743\u9650)", (int)1).show();
                        }
                        break block12;
                    }
                    this.setStatus("\u5f53\u524d\u7cfb\u7edf\u6216\u684c\u9762\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f");
                    Toast.makeText((Context)this, (CharSequence)"\u5f53\u524d\u7cfb\u7edf\u6216\u684c\u9762\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f", (int)0).show();
                    break block12;
                }
                this.setStatus("\u5f53\u524d\u7cfb\u7edf\u7248\u672c\u8f83\u4f4e\uff0c\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f");
                Toast.makeText((Context)this, (CharSequence)"\u5f53\u524d\u7cfb\u7edf\u7248\u672c\u8f83\u4f4e\uff0c\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f", (int)0).show();
            }
            catch (Exception ex) {
                this.setStatus("\u521b\u5efa\u5feb\u6377\u65b9\u5f0f\u5931\u8d25\uff1a" + ex.getMessage());
                Toast.makeText((Context)this, (CharSequence)("\u521b\u5efa\u5931\u8d25\uff1a" + ex.getMessage()), (int)1).show();
            }
        }
    }

    private void runRemoteExtensionSilently(String extensionId, String extensionName, ScriptCallback callback) {
        Toast.makeText((Context)this.getApplicationContext(), (CharSequence)("\u71d5\u5b50\uff1a\u6b63\u5728\u6267\u884c [" + extensionName + "]..."), (int)0).show();
        this.executor.execute(() -> {
            try {
                String messageId;
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    messageId = YanziApiClient.runExtensionOnDesktop(baseUrl, token, this.deviceId, this.buildDeviceName(), extensionId, "");
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    messageId = YanziApiClient.runExtensionOnDesktop(baseUrl, token, this.deviceId, this.buildDeviceName(), extensionId, "");
                }
                String sentMessageId = messageId;
                boolean finished = false;
                long startTime = System.currentTimeMillis();
                long timeout = 20000L;
                String statusResult = "timeout";
                String execOutput = "";
                while (System.currentTimeMillis() - startTime < timeout) {
                    try {
                        JSONObject msgDetail = YanziApiClient.fetchMessageDetail(baseUrl, token, sentMessageId);
                        String status = msgDetail.optString("status", "pending");
                        if ("completed".equals(status)) {
                            JSONObject execRes;
                            statusResult = "completed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null && (execRes = payloadObj.optJSONObject("executionResult")) != null) {
                                execOutput = execRes.optString("output", "");
                            }
                            finished = true;
                            break;
                        }
                        if ("failed".equals(status)) {
                            JSONObject execRes;
                            statusResult = "failed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null && (execRes = payloadObj.optJSONObject("executionResult")) != null) {
                                execOutput = execRes.optString("output", "");
                            }
                            finished = true;
                            break;
                        }
                        if ("acked".equals(status)) {
                            statusResult = "acked";
                            finished = true;
                            break;
                        }
                    }
                    catch (Exception msgDetail) {
                        // empty catch block
                    }
                    try {
                        Thread.sleep(1000L);
                    }
                    catch (InterruptedException e) {
                        // empty catch block
                        break;
                    }
                }
                String finalStatus = statusResult;
                String finalOutput = execOutput;
                new Handler(Looper.getMainLooper()).post(() -> {
                    if ("completed".equals(finalStatus)) {
                        String cleanOut = finalOutput.trim();
                        String showMsg = "\u6269\u5c55 [" + extensionName + "] \u6267\u884c\u6210\u529f\uff01";
                        if (!cleanOut.isEmpty()) {
                            showMsg = showMsg + "\n\u7ed3\u679c: " + (cleanOut.length() > 60 ? cleanOut.substring(0, 60) + "..." : cleanOut);
                        }
                        Toast.makeText((Context)this.getApplicationContext(), (CharSequence)showMsg, (int)1).show();
                    } else if ("failed".equals(finalStatus)) {
                        Toast.makeText((Context)this.getApplicationContext(), (CharSequence)("\u6269\u5c55 [" + extensionName + "] \u6267\u884c\u5931\u8d25\uff01\n\u9519\u8bef: " + finalOutput), (int)1).show();
                    } else if ("acked".equals(finalStatus)) {
                        Toast.makeText((Context)this.getApplicationContext(), (CharSequence)("\u6269\u5c55 [" + extensionName + "] \u5df2\u6267\u884c\u5b8c\u6210"), (int)0).show();
                        Toast.makeText((Context)this.getApplicationContext(), (CharSequence)("\u6269\u5c55 [" + extensionName + "] \u6267\u884c\u8d85\u65f6\uff0c\u8bf7\u5728\u7535\u8111\u7aef\u786e\u8ba4"), (int)1).show();
                    }
                    if (callback != null) {
                        if ("completed".equals(finalStatus)) {
                            callback.onResult(finalOutput.isEmpty() ? "\u6267\u884c\u6210\u529f\u65e0\u8f93\u51fa" : finalOutput);
                        } else if ("failed".equals(finalStatus)) {
                            callback.onResult("\u6267\u884c\u5931\u8d25\uff1a" + finalOutput);
                        } else {
                            callback.onResult("\u72b6\u6001: " + finalStatus);
                        }
                    }
                });
            }
            catch (Exception ex) {
                new Handler(Looper.getMainLooper()).post(() -> {
                    Toast.makeText((Context)this.getApplicationContext(), (CharSequence)("\u6267\u884c\u5931\u8d25\uff1a" + ex.getMessage()), (int)1).show();
                    if (callback != null) {
                        callback.onResult("\u6267\u884c\u5f02\u5e38\uff1a" + ex.getMessage());
                    }
                });
            }
        });
    }

    private static interface ScriptCallback {
        public void onResult(String var1);
    }

    private static final class RemoteExtension {
        final String extensionId;
        final String name;
        final String description;
        final String icon;
        final String accentHex;

        RemoteExtension(String extensionId, String name, String description, String icon, String accentHex) {
            this.extensionId = extensionId;
            this.name = name;
            this.description = description;
            this.icon = icon == null ? "" : icon;
            this.accentHex = accentHex == null ? "" : accentHex;
        }

        RemoteExtension(String extensionId, String name, String description, String icon) {
            this(extensionId, name, description, icon, "");
        }

        String iconText() {
            String value = this.icon.trim();
            if (value.startsWith("mdi:")) {
                String namePart = value.substring(4).replace("-", " ").trim();
                return namePart.isEmpty() ? "\u71d5" : namePart.substring(0, 1).toUpperCase(Locale.ROOT);
            }
            String base = this.name.trim().isEmpty() ? this.extensionId : this.name.trim();
            return base.isEmpty() ? "\u71d5" : base.substring(0, 1).toUpperCase(Locale.ROOT);
        }
    }

    private static final class MobileExtensionTemplate {
        final String name;
        final String description;
        final String json;

        MobileExtensionTemplate(String name, String description, String json) {
            this.name = name;
            this.description = description;
            this.json = json;
        }
    }

    private class MobileJsBridge {
        private ScriptCallback callback;

        public MobileJsBridge() {
        }

        public MobileJsBridge(ScriptCallback callback) {
            this.callback = callback;
        }

        @JavascriptInterface
        public void toast(String text) {
            MainActivity.this.runOnUiThread(() -> Toast.makeText((Context)MainActivity.this, (CharSequence)text, (int)0).show());
        }

        @JavascriptInterface
        public void sendToDesktop(String text) {
            MainActivity.this.runOnUiThread(() -> MainActivity.this.sendTextValueToDesktop(text, "\u624b\u673a\u811a\u672c\u6b63\u5728\u53d1\u9001\u5230\u7535\u8111..."));
        }

        @JavascriptInterface
        public String getSharedText() {
            return MainActivity.this.textInput == null ? "" : MainActivity.this.textInput.getText().toString();
        }

        @JavascriptInterface
        public String getClipboardText() {
            ClipboardManager manager = (ClipboardManager)MainActivity.this.getSystemService("clipboard");
            if (manager == null || manager.getPrimaryClip() == null || manager.getPrimaryClip().getItemCount() == 0) {
                return "";
            }
            CharSequence value = manager.getPrimaryClip().getItemAt(0).coerceToText((Context)MainActivity.this);
            return value == null ? "" : value.toString();
        }

        @JavascriptInterface
        public String setClipboardText(String text) {
            ClipboardManager manager = (ClipboardManager)MainActivity.this.getSystemService("clipboard");
            if (manager != null) {
                manager.setPrimaryClip(ClipData.newPlainText((CharSequence)"Yanzi mobile script", (CharSequence)(text == null ? "" : text)));
            }
            return text == null ? "" : text;
        }

        @JavascriptInterface
        public String openUrl(String url) {
            MainActivity.this.runOnUiThread(() -> {
                Intent intent = new Intent("android.intent.action.VIEW", Uri.parse((String)url));
                MainActivity.this.startActivity(intent);
            });
            return url;
        }

        @JavascriptInterface
        public String pickPhoto() {
            MainActivity.this.runOnUiThread(() -> MainActivity.this.pickPhotoFromGallery());
            return "ok";
        }

        /*
         * Enabled aggressive exception aggregation
         */
        @JavascriptInterface
        public String readTextFile(String name) {
            try {
                File file = MainActivity.this.resolveMobileScriptFile(name);
                if (!file.exists()) {
                    return new JSONObject().put("ok", false).put("error", (Object)"\u6587\u4ef6\u4e0d\u5b58\u5728").put("path", (Object)file.getAbsolutePath()).toString();
                }
                try (FileInputStream stream = new FileInputStream(file);){
                    String string;
                    try (ByteArrayOutputStream output = new ByteArrayOutputStream();){
                        int read;
                        byte[] buffer = new byte[4096];
                        while ((read = stream.read(buffer)) >= 0) {
                            output.write(buffer, 0, read);
                        }
                        string = new JSONObject().put("ok", true).put("path", (Object)file.getAbsolutePath()).put("text", (Object)output.toString(StandardCharsets.UTF_8.name())).toString();
                    }
                    return string;
                }
            }
            catch (Exception ex) {
                return MainActivity.buildJsonErrorResult(ex.getMessage());
            }
        }

        @JavascriptInterface
        public String saveTextFile(String name, String text) {
            return this.writeTextFile(name, text, false);
        }

        @JavascriptInterface
        public String appendTextFile(String name, String text) {
            return this.writeTextFile(name, text, true);
        }

        @JavascriptInterface
        public String httpGet(String url) {
            return this.runHttpRequest("GET", url, null, null);
        }

        @JavascriptInterface
        public String httpPostJson(String url, String jsonText) {
            return this.runHttpRequest("POST", url, jsonText, "application/json; charset=utf-8");
        }

        @JavascriptInterface
        public void done(String text) {
            MainActivity.this.runOnUiThread(() -> {
                MainActivity.this.updateMobileScriptResult(text, false);
                MainActivity.this.setStatus(text);
                if (this.callback != null) {
                    this.callback.onResult(text);
                }
            });
        }

        @JavascriptInterface
        public void fail(String text) {
            MainActivity.this.runOnUiThread(() -> {
                MainActivity.this.updateMobileScriptResult("\u6d4b\u8bd5\u5931\u8d25\uff1a " + text, true);
                MainActivity.this.setStatus("\u624b\u673a\u811a\u672c\u6267\u884c\u5931\u8d25\uff1a" + text);
            });
        }

        private String writeTextFile(String name, String text, boolean append) {
            try {
                File file = MainActivity.this.resolveMobileScriptFile(name);
                try (FileOutputStream stream = new FileOutputStream(file, append);){
                    stream.write((text == null ? "" : text).getBytes(StandardCharsets.UTF_8));
                }
                return new JSONObject().put("ok", true).put("path", (Object)file.getAbsolutePath()).put("bytes", file.length()).toString();
            }
            catch (Exception ex) {
                return MainActivity.buildJsonErrorResult(ex.getMessage());
            }
        }

        /*
         * WARNING - Removed try catching itself - possible behaviour change.
         */
        private String runHttpRequest(String method, String url, String body, String contentType) {
            HttpURLConnection connection = null;
            try {
                connection = (HttpURLConnection)new URL(url).openConnection();
                connection.setRequestMethod(method);
                connection.setConnectTimeout(15000);
                connection.setReadTimeout(15000);
                connection.setRequestProperty("Accept", "application/json, text/plain, */*");
                connection.setRequestProperty("User-Agent", "YanziMobile/1.0");
                if (body != null) {
                    connection.setDoOutput(true);
                    connection.setRequestProperty("Content-Type", contentType == null ? "text/plain; charset=utf-8" : contentType);
                    try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8);){
                        writer.write(body);
                    }
                }
                int status = connection.getResponseCode();
                String responseBody = this.readConnectionBody(connection);
                String string = new JSONObject().put("ok", status >= 200 && status < 300).put("status", status).put("body", (Object)responseBody).toString();
                return string;
            }
            catch (Exception ex) {
                String string = MainActivity.buildJsonErrorResult(ex.getMessage());
                return string;
            }
            finally {
                if (connection != null) {
                    connection.disconnect();
                }
            }
        }

        private String readConnectionBody(HttpURLConnection connection) throws Exception {
            InputStream stream;
            InputStream inputStream = stream = connection.getResponseCode() >= 200 && connection.getResponseCode() < 300 ? connection.getInputStream() : connection.getErrorStream();
            if (stream == null) {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8));){
                String line;
                while ((line = reader.readLine()) != null) {
                    builder.append(line);
                }
            }
            return builder.toString();
        }
    }

    private final class YanmMobileBridge {
        private final String componentId;
        private final String componentTitle;

        YanmMobileBridge(String componentId, String componentTitle) {
            this.componentId = componentId;
            this.componentTitle = componentTitle;
        }

        @JavascriptInterface
        public String getState(String key) {
            JSONObject state = MainActivity.this.currentYanmState == null ? new JSONObject() : MainActivity.this.currentYanmState;
            return state.optString(key, "");
        }

        @JavascriptInterface
        public void setState(String key, String value) {
            try {
                if (MainActivity.this.currentYanmState == null) {
                    MainActivity.this.currentYanmState = new JSONObject();
                }
                MainActivity.this.currentYanmState.put(key, (Object)value);
                if (MainActivity.this.currentYanmSnapshot == null) {
                    MainActivity.this.currentYanmSnapshot = new JSONObject();
                }
                MainActivity.this.currentYanmSnapshot.put("componentState", (Object)MainActivity.this.currentYanmState);
                MainActivity.this.runOnUiThread(() -> {
                    MainActivity.this.setStatus("\u71d5\u5e55\u72b6\u6001\u5df2\u5728\u624b\u673a\u7aef\u66f4\u65b0\uff1a" + this.componentTitle + " / " + key);
                    MainActivity.this.scheduleYanmCloudSync(this.componentTitle + " / " + key);
                });
            }
            catch (Exception exception) {
                // empty catch block
            }
        }

        @JavascriptInterface
        public String getSystemInfo() {
            try {
                return new JSONObject().put("machineName", (Object)MainActivity.buildDeviceDisplayName()).put("osVersion", (Object)("Android " + Build.VERSION.RELEASE)).put("isNetworkAvailable", true).put("time", (Object)new SimpleDateFormat("HH:mm", Locale.getDefault()).format(new Date())).put("componentId", (Object)this.componentId).toString();
            }
            catch (Exception ex) {
                return "{}";
            }
        }
    }

    public static final class YanziApiClient {
        static String login(String baseUrl, String email, String password) throws Exception {
            JSONObject payload = new JSONObject().put("email", (Object)email).put("password", (Object)password);
            return YanziApiClient.postJson(baseUrl, "/v1/auth/login", payload, null, "\u767b\u5f55").getString("accessToken");
        }

        static void registerDevice(String baseUrl, String token, String deviceId, String displayName) throws Exception {
            JSONObject capabilities = new JSONObject().put("shareText", true).put("sendToDesktop", true);
            JSONObject payload = new JSONObject().put("deviceId", (Object)deviceId).put("platform", (Object)"android").put("displayName", (Object)displayName).put("capabilities", (Object)capabilities);
            YanziApiClient.postJson(baseUrl, "/v1/me/devices", payload, token, "\u8bbe\u5907\u6ce8\u518c");
        }

        static String sendTextToDesktop(String baseUrl, String token, String sourceDeviceId, String text) throws Exception {
            JSONObject payload = new JSONObject().put("sourceDeviceId", (Object)sourceDeviceId).put("targetPlatform", (Object)"desktop").put("kind", (Object)"text").put("title", (Object)"\u624b\u673a\u53d1\u6765\u6d88\u606f").put("text", (Object)text).put("payload", (Object)new JSONObject().put("source", (Object)"android").put("sourceDeviceName", (Object)MainActivity.buildDeviceDisplayName()).put("createdAt", System.currentTimeMillis()));
            return YanziApiClient.postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "\u53d1\u9001\u6d88\u606f").optString("messageId", "unknown");
        }

        static String sendPhotoToDesktop(String baseUrl, String token, String sourceDeviceId, byte[] jpegBytes, int width, int height) throws Exception {
            String base64 = Base64.encodeToString((byte[])jpegBytes, (int)2);
            String screenshotDataUrl = "base64," + base64;
            return YanziApiClient.postScreenshotDirectMessage(baseUrl, token, sourceDeviceId, screenshotDataUrl, jpegBytes.length, width, height);
        }

        private static String postScreenshotDirectMessage(String baseUrl, String token, String sourceDeviceId, String screenshotDataUrl, int bytes, int width, int height) throws Exception {
            JSONObject payload = new JSONObject().put("sourceDeviceId", (Object)sourceDeviceId).put("targetPlatform", (Object)"desktop").put("kind", (Object)"screenshot").put("title", (Object)"\u624b\u673a\u7167\u7247").put("text", (Object)("\u624b\u673a\u7167\u7247\uff1a" + width + "x" + height)).put("payload", (Object)new JSONObject().put("source", (Object)"android-mobile").put("sourceDeviceName", (Object)MainActivity.buildDeviceDisplayName()).put("screenshotMime", (Object)"image/jpeg").put("screenshotWidth", width).put("screenshotHeight", height).put("screenshotBytes", bytes).put("screenshotDataUrl", (Object)screenshotDataUrl).put("expiresAt", System.currentTimeMillis() + 2592000000L).put("createdAt", System.currentTimeMillis()));
            return YanziApiClient.postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "\u53d1\u9001\u7167\u7247").optString("messageId", "unknown");
        }

        private static WebDavConfig fetchWebDavConfig(String baseUrl, String token) throws Exception {
            JSONObject json = YanziApiClient.getJson(baseUrl, "/v1/sync/webdav-config", token, "\u8bfb\u53d6 WebDAV");
            WebDavConfig config = new WebDavConfig();
            config.serverUrl = json.optString("serverUrl", "https://dav.jianguoyun.com/dav/");
            config.rootPath = json.optString("rootPath", "/yanzi");
            config.username = json.optString("username", "");
            config.password = json.optString("password", "");
            if (!json.optBoolean("enabled", false) || config.username.trim().isEmpty() || config.password.trim().isEmpty()) {
                throw new IllegalStateException("\u8d26\u53f7\u672a\u914d\u7f6e\u53ef\u7528\u7684 WebDAV\u3002");
            }
            return config;
        }

        private static String uploadMobilePhotoToWebDav(WebDavConfig config, byte[] bytes) throws Exception {
            String day = new SimpleDateFormat("yyyyMMdd", Locale.ROOT).format(new Date());
            String fileName = "mobile-photo-" + day + "-" + UUID.randomUUID().toString().replace("-", "") + ".jpg";
            YanziApiClient.putWebDavBytes(config, fileName, bytes, "image/jpeg");
            return fileName;
        }

        private static void putWebDavBytes(WebDavConfig config, String relativePath, byte[] bytes, String contentType) throws Exception {
            HttpURLConnection connection = YanziApiClient.openWebDav(config, relativePath);
            connection.setRequestMethod("PUT");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(30000);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", contentType);
            connection.setFixedLengthStreamingMode(bytes.length);
            connection.connect();
            try (OutputStream output = connection.getOutputStream();){
                output.write(bytes);
            }
            String body = YanziApiClient.readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                throw new IllegalStateException("WebDAV \u4e0a\u4f20\u5931\u8d25\uff0cHTTP " + connection.getResponseCode() + "\uff1a" + body);
            }
        }

        private static HttpURLConnection openWebDav(WebDavConfig config, String relativePath) throws Exception {
            String root;
            String server;
            String string = server = config.serverUrl == null ? "" : config.serverUrl.trim();
            if (!server.endsWith("/")) {
                server = server + "/";
            }
            String string2 = root = config.rootPath == null ? "" : config.rootPath.trim();
            if (!root.startsWith("/")) {
                root = "/" + root;
            }
            if (!root.endsWith("/")) {
                root = root + "/";
            }
            String path = root + relativePath;
            while (path.contains("//")) {
                path = path.replace("//", "/");
            }
            URL url = new URL(server + path.substring(1));
            HttpURLConnection connection = (HttpURLConnection)url.openConnection();
            connection.setRequestProperty("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            String userpass = (config.username == null ? "" : config.username) + ":" + (config.password == null ? "" : config.password);
            String encoded = Base64.encodeToString((byte[])userpass.getBytes(StandardCharsets.UTF_8), (int)2);
            connection.setRequestProperty("Authorization", "Basic " + encoded);
            return connection;
        }

        public static String runExtensionOnDesktop(String baseUrl, String token, String sourceDeviceId, String sourceDeviceName, String extensionId, String inputText) throws Exception {
            JSONObject payload = new JSONObject().put("sourceDeviceId", (Object)sourceDeviceId).put("targetPlatform", (Object)"desktop").put("kind", (Object)"run-extension").put("title", (Object)"\u624b\u673a\u8bf7\u6c42\u6267\u884c\u6269\u5c55").put("text", (Object)(inputText == null ? "" : inputText)).put("payload", (Object)new JSONObject().put("source", (Object)"android").put("sourceDeviceName", (Object)sourceDeviceName).put("extensionId", (Object)extensionId).put("createdAt", System.currentTimeMillis()));
            return YanziApiClient.postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "\u6267\u884c\u6269\u5c55").optString("messageId", "unknown");
        }

        public static JSONObject fetchMessageDetail(String baseUrl, String token, String messageId) throws Exception {
            return YanziApiClient.getJson(baseUrl, "/v1/me/mobile/messages/" + YanziApiClient.encodePath(messageId), token, "\u83b7\u53d6\u6d88\u606f\u8be6\u60c5");
        }

        static List<RemoteExtension> fetchRunnableExtensions(String baseUrl, String token) throws Exception {
            JSONObject payload = YanziApiClient.getJson(baseUrl, "/v1/me/extensions", token, "\u8bfb\u53d6\u6269\u5c55\u5217\u8868");
            JSONArray items = payload.optJSONArray("items");
            ArrayList<RemoteExtension> result = new ArrayList<RemoteExtension>();
            if (items == null) {
                return result;
            }
            for (int i = 0; i < items.length(); ++i) {
                String extensionId;
                JSONObject item = items.optJSONObject(i);
                if (item == null || item.optInt("enabled", 1) == 0 || (extensionId = MainActivity.firstNonEmpty(new String[]{item.optString("extension_id"), item.optString("extensionId"), item.optString("ExtensionId"), item.optString("Extension_id")})).isEmpty() || "yanzi-webdav-settings".equals(extensionId) || "yanzi-webdav-setting".equals(extensionId) || "yanzi-quickpanel-settings".equals(extensionId) || "yanzi-quickpanel-setting".equals(extensionId) || "yanzi-personal-sync-settings".equals(extensionId) || "yanzi-personal-sync-setting".equals(extensionId) || "yanzi-ai-settings".equals(extensionId) || "yanzi-ai-setting".equals(extensionId) || "yanzi-general-settings".equals(extensionId) || "yanzi-general-setting".equals(extensionId)) continue;
                try {
                    JSONObject detail = YanziApiClient.getJson(baseUrl, "/v1/extensions/" + YanziApiClient.encodePath(extensionId), token, "\u8bfb\u53d6\u6269\u5c55\u8be6\u60c5");
                    JSONObject manifest = detail.optJSONObject("manifest");
                    String name = MainActivity.firstNonEmpty(new String[]{detail.optString("display_name"), detail.optString("displayName"), detail.optString("DisplayName"), detail.optString("name"), detail.optString("Name"), manifest == null ? "" : manifest.optString("name"), manifest == null ? "" : manifest.optString("Name"), manifest == null ? "" : manifest.optString("display_name"), manifest == null ? "" : manifest.optString("displayName"), manifest == null ? "" : manifest.optString("DisplayName"), extensionId});
                    String description = MainActivity.firstNonEmpty(new String[]{detail.optString("description"), detail.optString("Description"), manifest == null ? "" : manifest.optString("description"), manifest == null ? "" : manifest.optString("Description")});
                    String icon = MainActivity.firstNonEmpty(new String[]{detail.optString("icon"), detail.optString("Icon"), manifest == null ? "" : manifest.optString("icon"), manifest == null ? "" : manifest.optString("Icon")});
                    String accentHex = MainActivity.firstNonEmpty(new String[]{detail.optString("accent_hex"), detail.optString("accentHex"), detail.optString("AccentHex"), manifest == null ? "" : manifest.optString("accent_hex"), manifest == null ? "" : manifest.optString("accentHex"), manifest == null ? "" : manifest.optString("AccentHex")});
                    result.add(new RemoteExtension(extensionId, name, description, icon, accentHex));
                    continue;
                }
                catch (Exception ignored) {
                    result.add(new RemoteExtension(extensionId, extensionId, "\u6269\u5c55\u8be6\u60c5\u6682\u4e0d\u53ef\u7528\uff0c\u4ecd\u53ef\u5c1d\u8bd5\u8fdc\u7a0b\u6267\u884c\u3002", "", ""));
                }
            }
            return result;
        }

        static JSONObject fetchYanmState(String baseUrl, String token) throws Exception {
            JSONObject payload = YanziApiClient.getJson(baseUrl, "/v1/me/yanm-state", token, "\u8bfb\u53d6\u71d5\u5e55");
            JSONObject yanm = payload.optJSONObject("yanm");
            if (yanm == null) {
                throw new IllegalStateException("\u8d26\u53f7\u4e91\u7aef\u6ca1\u6709\u71d5\u5e55\u6570\u636e\u3002");
            }
            return yanm;
        }

        static JSONObject fetchSettings(String baseUrl, String token) throws Exception {
            JSONObject payload = YanziApiClient.getJson(baseUrl, "/v1/settings", token, "\u8bfb\u53d6\u914d\u7f6e");
            JSONObject settings = payload.optJSONObject("settings");
            if (settings == null) {
                throw new IllegalStateException("\u672a\u80fd\u83b7\u53d6\u5230\u4e91\u7aef\u914d\u7f6e\u3002");
            }
            return settings;
        }

        static JSONObject putYanmState(String baseUrl, String token, JSONObject yanm) throws Exception {
            JSONObject payload = new JSONObject().put("updatedAtUtc", (Object)new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT).format(new Date())).put("yanm", (Object)yanm);
            return YanziApiClient.putJson(baseUrl, "/v1/me/yanm-state", payload, token, "\u540c\u6b65\u71d5\u5e55");
        }

        private static JSONObject putJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            if (YanziApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = sContext != null ? LanDiscoveryManager.getLanBaseUrl(sContext) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = sContext != null ? LanDiscoveryManager.getLanApiToken(sContext) : LanDiscoveryManager.cachedLanApiToken;
                        JSONObject result = YanziApiClient.doRequest(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "PUT", payload, 1500);
                        YanziApiClient.handleLanSuccess(action, path);
                        return result;
                    }
                    catch (Exception e) {
                        YanziApiClient.handleLanFailure(action, e);
                    }
                }
            }
            return YanziApiClient.doRequest(baseUrl, path, token, action, "PUT", payload, 15000);
        }

        private static JSONObject postJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            if (YanziApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = sContext != null ? LanDiscoveryManager.getLanBaseUrl(sContext) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = sContext != null ? LanDiscoveryManager.getLanApiToken(sContext) : LanDiscoveryManager.cachedLanApiToken;
                        JSONObject result = YanziApiClient.doRequest(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "POST", payload, 1500);
                        YanziApiClient.handleLanSuccess(action, path);
                        return result;
                    }
                    catch (Exception e) {
                        YanziApiClient.handleLanFailure(action, e);
                    }
                }
            }
            return YanziApiClient.doRequest(baseUrl, path, token, action, "POST", payload, 15000);
        }

        private static JSONObject getJson(String baseUrl, String path, String token, String action) throws Exception {
            if (YanziApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = sContext != null ? LanDiscoveryManager.getLanBaseUrl(sContext) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = sContext != null ? LanDiscoveryManager.getLanApiToken(sContext) : LanDiscoveryManager.cachedLanApiToken;
                        JSONObject result = YanziApiClient.doRequest(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "GET", null, 1500);
                        YanziApiClient.handleLanSuccess(action, path);
                        return result;
                    }
                    catch (Exception e) {
                        YanziApiClient.handleLanFailure(action, e);
                    }
                }
            }
            return YanziApiClient.doRequest(baseUrl, path, token, action, "GET", null, 15000);
        }

        private static boolean shouldUseLan(String path) {
            return !path.startsWith("/v1/auth/login");
        }

        private static void handleLanSuccess(String action, String path) {
            if (sContext != null) {
                MobileDiagnostics.append(sContext, "\u5c40\u57df\u7f51\u76f4\u8fde\u6210\u529f(" + action + "): " + path);
            }
        }

        private static void handleLanFailure(String action, Exception e) {
            String message = e.getMessage() == null ? e.toString() : e.getMessage();
            Log.w((String)"ApiClient", (String)("LAN fallback failed: " + message));
            if (sContext != null) {
                MobileDiagnostics.append(sContext, "\u5c40\u57df\u7f51\u76f4\u8fde\u5931\u8d25(" + action + ")\uff0c\u5df2\u56de\u9000\u516c\u7f51\uff1a" + message);
                LanDiscoveryManager.clearLanBaseUrl(sContext);
            } else {
                LanDiscoveryManager.cachedLanBaseUrl = null;
                LanDiscoveryManager.cachedLanApiToken = null;
            }
        }

        private static JSONObject doRequest(String baseUrl, String path, String token, String action, String method, JSONObject payload, int timeoutMs) throws Exception {
            HttpURLConnection connection = (HttpURLConnection)new URL(baseUrl + path).openConnection();
            connection.setRequestMethod(method);
            connection.setConnectTimeout(timeoutMs);
            connection.setReadTimeout(timeoutMs);
            connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.1.0");
            connection.setRequestProperty("Accept", "application/json");
            if (payload != null) {
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            }
            if (token != null && !token.trim().isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }
            if (payload != null) {
                try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8);){
                    writer.write(payload.toString());
                }
            }
            String body = YanziApiClient.readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                String message = body;
                try {
                    message = new JSONObject(body).optString("message", body);
                }
                catch (Exception exception) {
                    // empty catch block
                }
                throw new IllegalStateException(YanziApiClient.formatError(action, path, connection.getResponseCode(), message));
            }
            return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
        }

        private static String encodePath(String value) {
            return value.replace(" ", "%20").replace("/", "%2F");
        }

        private static String formatError(String action, String path, int statusCode, String message) {
            String trimmed;
            String string = trimmed = message == null ? "" : message.trim();
            if (statusCode == 404 && trimmed.toLowerCase().contains("route not found")) {
                return action + "\u63a5\u53e3\u4e0d\u5b58\u5728\uff0c\u8bf7\u786e\u8ba4\u4e91\u7aef\u5730\u5740\u662f " + MainActivity.DEFAULT_BASE_URL + "\uff0c\u5e76\u786e\u8ba4 Worker \u5df2\u53d1\u5e03\u79fb\u52a8\u7aef\u63a5\u53e3\uff1a" + path;
            }
            if (trimmed.isEmpty()) {
                return action + "\u5931\u8d25\uff0cHTTP " + statusCode;
            }
            return trimmed;
        }

        private static String readBody(HttpURLConnection connection) throws Exception {
            InputStream stream = connection.getResponseCode() >= 200 && connection.getResponseCode() < 300 ? connection.getInputStream() : connection.getErrorStream();
            StringBuilder builder = new StringBuilder();
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8));){
                String line;
                while ((line = reader.readLine()) != null) {
                    builder.append(line);
                }
            }
            return builder.toString();
        }

        private static final class WebDavConfig {
            String serverUrl;
            String rootPath;
            String username;
            String password;

            private WebDavConfig() {
            }
        }
    }

    private static class AttachmentInfo {
        String name;
        long size;
        String mimeType;
        Uri uri;
        String base64Data;
        String textContent;
        boolean isImage;

        AttachmentInfo(String name, long size, String mimeType, Uri uri, String base64Data, String textContent, boolean isImage) {
            this.name = name;
            this.size = size;
            this.mimeType = mimeType;
            this.uri = uri;
            this.base64Data = base64Data;
            this.textContent = textContent;
            this.isImage = isImage;
        }
    }

    private static class AiMessageInfo {
        String sender;
        String text;
        int historyIndex;
        View view;
        TextView feedbackTextView;
        AiMessageInfo(String sender, String text, int historyIndex, View view) {
            this.sender = sender;
            this.text = text;
            this.historyIndex = historyIndex;
            this.view = view;
        }
    }

    private void showAiMessageMenu(AiMessageInfo info) {
        PopupMenu popup = new PopupMenu((Context)this, info.view);
        popup.getMenu().add(0, 1, 0, (CharSequence)"\u590d\u5236");
        popup.getMenu().add(0, 2, 1, (CharSequence)"\u9009\u62e9\u6587\u672c");
        
        boolean isUserMsg = "\u6211".equals(info.sender);
        boolean isAiMsg = "AI".equals(info.sender);
        
        if (isAiMsg && info.historyIndex != -1) {
            popup.getMenu().add(0, 3, 2, (CharSequence)"\u91cd\u65b0\u751f\u6210");
        }
        if (info.historyIndex != -1) {
            popup.getMenu().add(0, 4, 3, (CharSequence)"\u4fee\u6539");
        }
        if (isUserMsg && info.historyIndex != -1) {
            popup.getMenu().add(0, 5, 4, (CharSequence)"\u91cd\u53d1");
        }
        if (info.historyIndex != -1) {
            popup.getMenu().add(0, 6, 5, (CharSequence)"\u5220\u9664");
        }
        popup.getMenu().add(0, 7, 6, (CharSequence)"朗读文本");
        
        popup.setOnMenuItemClickListener(item -> {
            switch (item.getItemId()) {
                case 1:
                    ClipboardManager manager = (ClipboardManager)this.getSystemService("clipboard");
                    if (manager != null) {
                        manager.setPrimaryClip(ClipData.newPlainText("AI Message", info.text));
                        this.setStatus("\u5df2\u590d\u5236\u6d88\u606f\u5230\u526a\u8d34\u677f\u3002");
                    }
                    break;
                case 2:
                    if (info.view instanceof ViewGroup) {
                        TextView tv = this.findTextViewInContainer((ViewGroup) info.view);
                        if (tv != null) {
                            tv.setOnLongClickListener(null);
                            info.view.setOnLongClickListener(null);
                            tv.setVisibility(0);
                            ViewGroup vg = (ViewGroup) info.view;
                            if (vg.getChildCount() >= 2 && vg.getChildAt(0) instanceof ViewGroup) {
                                ViewGroup header = (ViewGroup) vg.getChildAt(0);
                                if (header.getChildCount() > 0 && header.getChildAt(0) instanceof TextView) {
                                    TextView ht = (TextView) header.getChildAt(0);
                                    String curTxt = ht.getText().toString();
                                    if (curTxt.contains("\u25b6")) {
                                        ht.setText((CharSequence)curTxt.replace("\u25b6", "\u25bc"));
                                    }
                                }
                            }
                            tv.requestFocus();
                            tv.performLongClick();
                            tv.setOnLongClickListener(v -> {
                                this.showAiMessageMenu(info);
                                return true;
                            });
                            info.view.setOnLongClickListener(v -> {
                                this.showAiMessageMenu(info);
                                return true;
                            });
                        }
                    }
                    break;
                case 3:
                    this.regenerateAiMessage(info);
                    break;
                case 4:
                    this.aiChatInput.setText((CharSequence)info.text);
                    this.aiChatInput.setSelection(info.text.length());
                    this.deleteAiMessageAndFollowing(info);
                    break;
                case 5:
                    this.deleteAiMessageAndFollowing(info);
                    this.sendAiChat(info.text);
                    break;
                case 6:
                    this.deleteSingleAiMessage(info);
                    break;
                case 7:
                    this.speakText(info.text);
                    break;
            }
            return true;
        });
        popup.show();
    }

    private TextView findTextViewInContainer(ViewGroup container) {
        if (container == null) return null;
        for (int i = 0; i < container.getChildCount(); i++) {
            View child = container.getChildAt(i);
            if (child instanceof TextView) {
                TextView tv = (TextView) child;
                if (tv.isTextSelectable()) {
                    return tv;
                }
            } else if (child instanceof ViewGroup) {
                TextView tv = findTextViewInContainer((ViewGroup) child);
                if (tv != null) return tv;
            }
        }
        return null;
    }

    private void updateActiveToolFeedback(String feedbackText) {
        if (this.currentActiveToolMessageInfo != null && this.currentActiveToolMessageInfo.feedbackTextView != null) {
            this.currentActiveToolMessageInfo.feedbackTextView.setText((CharSequence)("\u7cfb\u7edf\u6267\u884c\u7ed3\u679c\uff0c\u8be6\u60c5\u5982\u4e0b\uff1a\n" + feedbackText), TextView.BufferType.SPANNABLE);
        }
    }

    private void regenerateAiMessage(AiMessageInfo info) {
        this.deleteAiMessageAndFollowing(info);
        this.isAiCancelled = false;
        this.setAiLoadingState(true);
        this.fetchAiReply();
    }

    private void deleteSingleAiMessage(AiMessageInfo info) {
        this.aiChatHistory.removeView(info.view);
        if (info.historyIndex != -1) {
            Object object = this.aiHistoryLock;
            synchronized (object) {
                if (this.aiMessagesHistory != null && info.historyIndex < this.aiMessagesHistory.length()) {
                    this.aiMessagesHistory.remove(info.historyIndex);
                }
            }
            this.saveAiHistory();
            this.refreshSessionDrawer();
            int childCount = this.aiChatHistory.getChildCount();
            for (int i = 0; i < childCount; ++i) {
                View child = this.aiChatHistory.getChildAt(i);
                Object tag = child.getTag();
                if (!(tag instanceof AiMessageInfo)) continue;
                AiMessageInfo childInfo = (AiMessageInfo)tag;
                if (childInfo.historyIndex <= info.historyIndex) continue;
                childInfo.historyIndex--;
            }
        }
    }

    private void deleteAiMessageAndFollowing(AiMessageInfo info) {
        int viewIndex = this.aiChatHistory.indexOfChild(info.view);
        if (viewIndex != -1) {
            int childCount = this.aiChatHistory.getChildCount();
            for (int i = childCount - 1; i >= viewIndex; --i) {
                this.aiChatHistory.removeViewAt(i);
            }
        }
        if (info.historyIndex != -1) {
            Object object = this.aiHistoryLock;
            synchronized (object) {
                if (this.aiMessagesHistory != null && info.historyIndex < this.aiMessagesHistory.length()) {
                    JSONArray newHistory = new JSONArray();
                    for (int i = 0; i < info.historyIndex; ++i) {
                        newHistory.put(this.aiMessagesHistory.opt(i));
                    }
                    this.aiMessagesHistory = newHistory;
                }
            }
            this.saveAiHistory();
            this.refreshSessionDrawer();
        }
    }

    private Drawable createStopIconDrawable() {
        return new Drawable() {
            private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
            {
                paint.setColor(Color.WHITE);
            }
            @Override
            public void draw(Canvas canvas) {
                android.graphics.Rect bounds = getBounds();
                float size = dp(20);
                float left = bounds.centerX() - size / 2;
                float top = bounds.centerY() - size / 2;
                RectF rectF = new RectF(left, top, left + size, top + size);
                canvas.drawRoundRect(rectF, dp(4), dp(4), paint);
            }
            @Override
            public void setAlpha(int alpha) { paint.setAlpha(alpha); }
            @Override
            public void setColorFilter(android.graphics.ColorFilter colorFilter) { paint.setColorFilter(colorFilter); }
            @Override
            public int getOpacity() { return -3; }
        };
    }

    private void renderMarkdownMessage(LinearLayout container, String text, int textColor, AiMessageInfo info) {
        if (text == null || text.isEmpty()) return;
        
        String[] lines = text.split("\n", -1);
        ArrayList<String> currentTextBlock = new ArrayList<>();
        ArrayList<String> currentTableBlock = new ArrayList<>();
        
        for (String line : lines) {
            String trimmed = line.trim();
            boolean isTable = trimmed.startsWith("|") && trimmed.endsWith("|");
            
            if (isTable) {
                if (!currentTextBlock.isEmpty()) {
                    renderNormalTextBlock(container, TextUtils.join("\n", currentTextBlock), textColor, info);
                    currentTextBlock.clear();
                }
                currentTableBlock.add(line);
            } else {
                if (!currentTableBlock.isEmpty()) {
                    renderTableBlock(container, currentTableBlock);
                    currentTableBlock.clear();
                }
                currentTextBlock.add(line);
            }
        }
        
        if (!currentTextBlock.isEmpty()) {
            renderNormalTextBlock(container, TextUtils.join("\n", currentTextBlock), textColor, info);
        }
        if (!currentTableBlock.isEmpty()) {
            renderTableBlock(container, currentTableBlock);
        }
    }

    private void renderNormalTextBlock(LinearLayout container, String text, int textColor, AiMessageInfo info) {
        if (text == null || text.trim().isEmpty()) return;
        TextView tv = new TextView(container.getContext());
        tv.setText(parseMarkdownToSpanned(text), TextView.BufferType.SPANNABLE);
        tv.setTextColor(textColor);
        tv.setTextSize(2, 14.0f);
        tv.setPadding(dp(4), dp(4), dp(4), dp(4));
        tv.setTextIsSelectable(true);
        tv.setOnLongClickListener(v -> {
            this.showAiMessageMenu(info);
            return true;
        });
        
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT, 
                LinearLayout.LayoutParams.WRAP_CONTENT);
        container.addView(tv, params);
    }

    private void renderTableBlock(LinearLayout container, ArrayList<String> tableLines) {
        Context context = container.getContext();
        HorizontalScrollView hsv = new HorizontalScrollView(context);
        hsv.setFillViewport(true);
        
        LinearLayout.LayoutParams hsvParams = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, 
                LinearLayout.LayoutParams.WRAP_CONTENT);
        hsvParams.setMargins(0, dp(8), 0, dp(8));
        hsv.setLayoutParams(hsvParams);
        
        TableLayout tableLayout = new TableLayout(context);
        tableLayout.setStretchAllColumns(true);
        
        GradientDrawable tableBg = new GradientDrawable();
        tableBg.setColor(Color.rgb(17, 24, 39));
        tableBg.setCornerRadius(dp(8));
        tableBg.setStroke(dp(1), Color.rgb(75, 85, 99));
        tableLayout.setBackground(tableBg);
        tableLayout.setPadding(dp(1), dp(1), dp(1), dp(1));
        
        TableLayout.LayoutParams tableParams = new TableLayout.LayoutParams(
                TableLayout.LayoutParams.MATCH_PARENT, 
                TableLayout.LayoutParams.WRAP_CONTENT);
        tableLayout.setLayoutParams(tableParams);
        
        boolean isHeader = true;
        int rowIndex = 0;
        
        for (String line : tableLines) {
            String trimmed = line.trim();
            if (trimmed.replaceAll("[|\\s-:]", "").isEmpty()) {
                continue;
            }
            
            String[] rawCells = trimmed.split("\\|", -1);
            if (rawCells.length <= 1) continue;
            
            int startCell = trimmed.startsWith("|") ? 1 : 0;
            int endCell = trimmed.endsWith("|") ? rawCells.length - 1 : rawCells.length;
            
            ArrayList<String> cells = new ArrayList<>();
            for (int c = startCell; c < endCell; c++) {
                cells.add(rawCells[c].trim());
            }
            
            TableRow row = new TableRow(context);
            
            GradientDrawable rowBg = new GradientDrawable();
            if (isHeader) {
                rowBg.setColor(Color.rgb(30, 41, 59));
            } else {
                rowBg.setColor(rowIndex % 2 == 0 ? Color.rgb(17, 24, 39) : Color.rgb(31, 41, 55));
            }
            row.setBackground(rowBg);
            
            for (String cellText : cells) {
                TextView cellTv = new TextView(context);
                cellTv.setText(parseMarkdownToSpanned(cellText), TextView.BufferType.SPANNABLE);
                cellTv.setTextSize(2, 12.0f);
                cellTv.setPadding(dp(10), dp(8), dp(10), dp(8));
                cellTv.setGravity(Gravity.CENTER);
                
                if (isHeader) {
                    cellTv.setTextColor(Color.WHITE);
                    cellTv.setTypeface(null, Typeface.BOLD);
                } else {
                    cellTv.setTextColor(Color.rgb(229, 231, 235));
                }
                row.addView(cellTv);
            }
            
            tableLayout.addView(row);
            isHeader = false;
            rowIndex++;
        }
        
        hsv.addView(tableLayout);
        container.addView(hsv);
    }

    private android.text.Spanned parseMarkdownToSpanned(String text) {
        if (text == null) return new android.text.SpannableString("");
        String escaped = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
        
        escaped = escaped.replaceAll("\\*\\*(.*?)\\*\\*", "<b>$1</b>");
        escaped = escaped.replaceAll("\\*(.*?)\\*", "<i>$1</i>");
        escaped = escaped.replaceAll("__(.*?)__", "<u>$1</u>");
        escaped = escaped.replaceAll("`(.*?)`", "<tt><font color=\"#4ADE80\">$1</font></tt>");
        escaped = escaped.replaceAll("(?m)^\\s*-\\s+(.*)$", "&bull; $1");
        escaped = escaped.replaceAll("(?m)^\\s*\\*\\s+(.*)$", "&bull; $1");
        escaped = escaped.replace("\n", "<br/>");
        
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.N) {
            return Html.fromHtml(escaped, Html.FROM_HTML_MODE_LEGACY);
        } else {
            return Html.fromHtml(escaped);
        }
    }

    private void toggleTtsStatus(Button speakBtn) {
        this.isTtsEnabled = !this.isTtsEnabled;
        this.prefs.edit().putBoolean("isTtsEnabled", this.isTtsEnabled).apply();
        speakBtn.setText(this.isTtsEnabled ? "🔊" : "🔇");
        if (!this.isTtsEnabled) {
            if (this.textToSpeech != null) {
                try {
                    this.textToSpeech.stop();
                } catch (Exception ignored) {}
            }
        } else {
            this.initTextToSpeech();
        }
        Toast.makeText((Context)this, this.isTtsEnabled ? "已开启语音朗读" : "已关闭语音朗读", Toast.LENGTH_SHORT).show();
    }

    private void switchToVoiceInput() {
        this.holdToSpeakBtn.setVisibility(0); // VISIBLE
        this.aiChatInput.setVisibility(8);    // GONE
        this.voiceToggleBtn.setText("⌨️");
        this.hideKeyboard((View)this.aiChatInput);
    }

    private void switchToTextInput() {
        this.holdToSpeakBtn.setVisibility(8); // GONE
        this.aiChatInput.setVisibility(0);    // VISIBLE
        this.voiceToggleBtn.setText("🎤");
    }

    private boolean checkAudioPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            if (this.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                this.requestPermissions(new String[]{android.Manifest.permission.RECORD_AUDIO}, 102);
                return false;
            }
        }
        return true;
    }

    private void destroySpeechRecognizer() {
        final android.speech.SpeechRecognizer recognizerToDestroy = this.speechRecognizer;
        this.speechRecognizer = null;
        this.isSpeechListening = false;
        this.pendingStopSpeech = false;
        
        if (recognizerToDestroy != null) {
            new android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(() -> {
                try {
                    recognizerToDestroy.cancel();
                    recognizerToDestroy.destroy();
                    Log.d("YanziVoice", "SpeechRecognizer destroyed asynchronously");
                } catch (Exception e) {
                    Log.e("YanziVoice", "Failed to destroy SpeechRecognizer asynchronously", e);
                }
            }, 100L);
        }
    }

    private void startSpeechRecognition() {
        try {
            this.destroySpeechRecognizer(); // 确保重置状态
            this.lastSpeechStartTime = System.currentTimeMillis();
            this.pendingStopSpeech = false;
            this.isSpeechActionUp = false;
            this.isSpeechFinished = false;
            android.content.ComponentName comp = this.findAvailableSpeechService();
            if (comp != null) {
                this.initSpeechRecognizer(comp);
                if (this.speechRecognizer != null) {
                    this.isSpeechListening = false;
                    this.speechRecognizer.startListening(this.speechRecognizerIntent);
                } else {
                    this.startSpeechIntent();
                    this.switchToTextInput();
                }
            } else {
                this.startSpeechIntent();
                this.switchToTextInput();
            }
        } catch (Exception e) {
            Log.e("YanziVoice", "Failed to start speech recognition", e);
            this.startSpeechIntent();
            this.switchToTextInput();
        }
    }

    private void stopSpeechRecognition() {
        try {
            this.isSpeechActionUp = true;
            long duration = System.currentTimeMillis() - this.lastSpeechStartTime;
            if (duration < 500) {
                Log.d("YanziVoice", "Speech duration too short: " + duration + "ms, cancelling");
                this.destroySpeechRecognizer();
                this.switchToTextInput();
                Toast.makeText((Context)this, "说话时间太短", Toast.LENGTH_SHORT).show();
                return;
            }
            if (this.isSpeechFinished) {
                Log.d("YanziVoice", "Speech already finished when ActionUp, switching UI and destroying");
                this.switchToTextInput();
                this.destroySpeechRecognizer();
            } else {
                if (this.speechRecognizer != null) {
                    if (this.isSpeechListening) {
                        Log.d("YanziVoice", "Speech is listening, stopping immediately");
                        this.speechRecognizer.stopListening();
                        this.pendingStopSpeech = false;
                    } else {
                        Log.d("YanziVoice", "Speech not listening yet, marking pendingStop");
                        this.pendingStopSpeech = true;
                    }
                }
            }
        } catch (Exception e) {
            Log.e("YanziVoice", "Failed to stop speech recognition", e);
            this.switchToTextInput();
            this.destroySpeechRecognizer();
        }
    }

    private void startSpeechIntent() {
        android.content.Intent intent = new android.content.Intent(android.speech.RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE_MODEL, android.speech.RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault());
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_PROMPT, "请说话...");
        try {
            this.startActivityForResult(intent, 103);
        } catch (android.content.ActivityNotFoundException a) {
            Toast.makeText((Context)this, "您的设备不支持语音识别输入", Toast.LENGTH_SHORT).show();
        }
    }

    private android.content.ComponentName findAvailableSpeechService() {
        try {
            android.content.Intent serviceIntent = new android.content.Intent("android.speech.RecognitionService");
            List<android.content.pm.ResolveInfo> services = getPackageManager().queryIntentServices(serviceIntent, 0);
            if (services == null || services.isEmpty()) {
                return null;
            }

            // 主流厂商推荐语音引擎包名关键字列表（按偏好排序）
            String[] preferredPkgs = {
                "com.miui.voiceassist",
                "com.xiaomi.mibrain.speech",
                "com.huawei.vassistant",
                "com.huawei.speechservice",
                "com.coloros.speechservice",
                "com.heytap.speechservice",
                "com.vivo.speechsuite",
                "com.vivo.vassistant",
                "com.iflytek.speechcloud",
                "com.iflytek.speechsuite",
                "com.iflytek.tts",
                "com.baidu.speech",
                "com.google.android.tts",
                "com.google.android.googleassistant"
            };

            // 已知不是真实语音识别服务（会导致静默挂死）的黑名单
            String[] blacklistPkgs = {
                "com.arlosoft.macrodroid",
                "net.dinglisch.android.taskerm"
            };

            // 1. 优先寻找大厂及主流引擎
            for (String pref : preferredPkgs) {
                for (android.content.pm.ResolveInfo ri : services) {
                    android.content.pm.ServiceInfo si = ri.serviceInfo;
                    if (si != null && si.packageName.equalsIgnoreCase(pref)) {
                        Log.d("YanziVoice", "Found preferred speech service: " + si.packageName + "/" + si.name);
                        return new android.content.ComponentName(si.packageName, si.name);
                    }
                }
            }

            // 2. 查找包含 speech/voice 等关键词且不在黑名单中的引擎
            for (android.content.pm.ResolveInfo ri : services) {
                android.content.pm.ServiceInfo si = ri.serviceInfo;
                if (si != null) {
                    boolean isBlacklisted = false;
                    for (String black : blacklistPkgs) {
                        if (si.packageName.toLowerCase().contains(black.toLowerCase())) {
                            isBlacklisted = true;
                            break;
                        }
                    }
                    if (isBlacklisted) continue;

                    String pkg = si.packageName.toLowerCase();
                    String name = si.name.toLowerCase();
                    if (pkg.contains("speech") || pkg.contains("voice") || pkg.contains("recogni") ||
                        name.contains("speech") || name.contains("voice") || name.contains("recogni")) {
                        Log.d("YanziVoice", "Found keyword-matched speech service: " + si.packageName + "/" + si.name);
                        return new android.content.ComponentName(si.packageName, si.name);
                    }
                }
            }

            // 3. 兜底寻找第一个非黑名单中的引擎
            for (android.content.pm.ResolveInfo ri : services) {
                android.content.pm.ServiceInfo si = ri.serviceInfo;
                if (si != null) {
                    boolean isBlacklisted = false;
                    for (String black : blacklistPkgs) {
                        if (si.packageName.toLowerCase().contains(black.toLowerCase())) {
                            isBlacklisted = true;
                            break;
                        }
                    }
                    if (!isBlacklisted) {
                        Log.d("YanziVoice", "Fallback to first non-blacklisted speech service: " + si.packageName + "/" + si.name);
                        return new android.content.ComponentName(si.packageName, si.name);
                    }
                }
            }
        } catch (Exception e) {
            Log.e("YanziVoice", "Error querying speech services", e);
        }
        return null;
    }

    private void initSpeechRecognizer(android.content.ComponentName comp) {
        this.speechRecognizer = android.speech.SpeechRecognizer.createSpeechRecognizer((Context)this, comp);
        this.speechRecognizerIntent = new Intent(android.speech.RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        this.speechRecognizerIntent.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE_MODEL, android.speech.RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        this.speechRecognizerIntent.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault());
        this.speechRecognizerIntent.putExtra(android.speech.RecognizerIntent.EXTRA_PARTIAL_RESULTS, false);
        this.speechRecognizer.setRecognitionListener(new android.speech.RecognitionListener() {
            @Override
            public void onReadyForSpeech(Bundle params) {
                Log.d("YanziVoice", "onReadyForSpeech");
                MainActivity.this.isSpeechListening = true;
                if (MainActivity.this.pendingStopSpeech) {
                    Log.d("YanziVoice", "onReadyForSpeech: pendingStop is true, stopping now");
                    try {
                        if (MainActivity.this.speechRecognizer != null) {
                            MainActivity.this.speechRecognizer.stopListening();
                        }
                    } catch (Exception e) {
                        Log.e("YanziVoice", "Failed to stop pending speech", e);
                    }
                    MainActivity.this.pendingStopSpeech = false;
                }
            }

            @Override
            public void onBeginningOfSpeech() {
                Log.d("YanziVoice", "onBeginningOfSpeech");
                MainActivity.this.isSpeechListening = true;
                if (MainActivity.this.pendingStopSpeech) {
                    Log.d("YanziVoice", "onBeginningOfSpeech: pendingStop is true, stopping now");
                    try {
                        if (MainActivity.this.speechRecognizer != null) {
                            MainActivity.this.speechRecognizer.stopListening();
                        }
                    } catch (Exception e) {
                        Log.e("YanziVoice", "Failed to stop pending speech", e);
                    }
                    MainActivity.this.pendingStopSpeech = false;
                }
            }

            @Override
            public void onRmsChanged(float rmsdB) {}

            @Override
            public void onBufferReceived(byte[] buffer) {}

            @Override
            public void onEndOfSpeech() {
                Log.d("YanziVoice", "onEndOfSpeech");
                MainActivity.this.isSpeechListening = false;
            }

            @Override
            public void onError(int error) {
                Log.e("YanziVoice", "Speech recognition error: " + error);
                MainActivity.this.isSpeechFinished = true;
                String msg = getSpeechErrorMsg(error);
                if (error == android.speech.SpeechRecognizer.ERROR_NO_MATCH) {
                    Toast.makeText((Context)MainActivity.this, "未检测到有效语音", Toast.LENGTH_SHORT).show();
                } else {
                    Toast.makeText((Context)MainActivity.this, "识别失败: " + msg, Toast.LENGTH_SHORT).show();
                }
                if (MainActivity.this.isSpeechActionUp) {
                    MainActivity.this.switchToTextInput();
                    MainActivity.this.destroySpeechRecognizer();
                }
            }

            @Override
            public void onResults(Bundle results) {
                MainActivity.this.isSpeechFinished = true;
                ArrayList<String> matches = results.getStringArrayList(android.speech.SpeechRecognizer.RESULTS_RECOGNITION);
                if (matches != null && !matches.isEmpty()) {
                    String text = matches.get(0);
                    Log.d("YanziVoice", "onResults: " + text);
                    MainActivity.this.aiChatInput.setText((CharSequence)text);
                    MainActivity.this.aiChatInput.setSelection(text.length());
                }
                if (MainActivity.this.isSpeechActionUp) {
                    MainActivity.this.switchToTextInput();
                    MainActivity.this.destroySpeechRecognizer();
                }
            }

            @Override
            public void onPartialResults(Bundle partialResults) {}

            @Override
            public void onEvent(int eventType, Bundle params) {}
        });
    }

    private String getSpeechErrorMsg(int error) {
        switch (error) {
            case 3: // ERROR_AUDIO
                return "音频录制错误";
            case 5: // ERROR_CLIENT
                return "客户端错误";
            case 9: // ERROR_INSUFFICIENT_PERMISSIONS
                return "权限不足";
            case 2: // ERROR_NETWORK
                return "网络错误";
            case 1: // ERROR_NETWORK_TIMEOUT
                return "网络超时";
            case 7: // ERROR_NO_MATCH
                return "没有匹配的语音";
            case 8: // ERROR_RECOGNIZER_BUSY
                return "识别引擎忙，请重试";
            case 4: // ERROR_SERVER
                return "服务器错误";
            case 6: // ERROR_SPEECH_TIMEOUT
                return "未检测到语音输入";
            default:
                return "错误码 " + error;
        }
    }

    private void initTextToSpeech() {
        if (this.textToSpeech != null) return;
        this.textToSpeech = new android.speech.tts.TextToSpeech((Context)this, status -> {
            if (status == android.speech.tts.TextToSpeech.SUCCESS) {
                int result = this.textToSpeech.setLanguage(Locale.CHINA);
                if (result == android.speech.tts.TextToSpeech.LANG_MISSING_DATA || result == android.speech.tts.TextToSpeech.LANG_NOT_SUPPORTED) {
                    this.textToSpeech.setLanguage(Locale.getDefault());
                    if (this.textToSpeech.setLanguage(Locale.getDefault()) == android.speech.tts.TextToSpeech.LANG_NOT_SUPPORTED) {
                        runOnUiThread(() -> Toast.makeText(this, "TTS 引擎不支持中文或缺省语言包", Toast.LENGTH_LONG).show());
                    }
                }
                this.isTtsInitialized = true;
                Log.d("YanziTTS", "TTS Initialization Success");
                if (this.pendingSpeakText != null) {
                    this.speakText(this.pendingSpeakText);
                    this.pendingSpeakText = null;
                }
            } else {
                this.isTtsInitialized = false;
                Log.e("YanziTTS", "TTS Initialization Failed");
                runOnUiThread(() -> Toast.makeText(this, "TTS 初始化失败，状态码: " + status, Toast.LENGTH_LONG).show());
            }
        });
    }

    private void speakText(String text) {
        if (text == null || text.trim().isEmpty()) return;
        this.initTextToSpeech();
        if (this.textToSpeech == null) {
            Toast.makeText((Context)this, "朗读失败：TTS 引擎未初始化或不可用", Toast.LENGTH_SHORT).show();
            return;
        }
        String cleaned = text.replaceAll("[\\*#`_~>\\[\\]\\(\\)-]", " ").trim();
        if (!this.isTtsInitialized) {
            this.pendingSpeakText = cleaned;
            Log.d("YanziTTS", "TTS not initialized yet, queueing text");
            Toast.makeText((Context)this, "语音引擎正在初始化，请稍候...", Toast.LENGTH_SHORT).show();
            return;
        }
        try {
            int result;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
                result = this.textToSpeech.speak(cleaned, android.speech.tts.TextToSpeech.QUEUE_FLUSH, null, "YanziTTSCall");
            } else {
                result = this.textToSpeech.speak(cleaned, android.speech.tts.TextToSpeech.QUEUE_FLUSH, null);
            }
            if (result == android.speech.tts.TextToSpeech.ERROR) {
                Toast.makeText((Context)this, "朗读失败：TTS 播放接口返回错误 (ERROR)", Toast.LENGTH_SHORT).show();
            } else {
                Toast.makeText((Context)this, "开始朗读...", Toast.LENGTH_SHORT).show();
            }
        } catch (Exception e) {
            Log.e("YanziTTS", "Speak failed", e);
            Toast.makeText((Context)this, "朗读发生异常: " + e.getMessage(), Toast.LENGTH_SHORT).show();
        }
    }

    private void hideKeyboard(View view) {
        android.view.inputmethod.InputMethodManager imm = (android.view.inputmethod.InputMethodManager) this.getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null) {
            imm.hideSoftInputFromWindow(view.getWindowToken(), 0);
        }
    }
}
