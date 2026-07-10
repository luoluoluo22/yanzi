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
import android.media.AudioManager;
import android.media.ToneGenerator;
import java.util.ArrayList;
import androidx.drawerlayout.widget.DrawerLayout;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;
import cc.luoluoluo.yanzi.mobile.FloatingWheelService;
import cc.luoluoluo.yanzi.mobile.LanDiscoveryManager;
import cc.luoluoluo.yanzi.mobile.MobileDiagnostics;
import cc.luoluoluo.yanzi.mobile.MobileIconLibrary;
import cc.luoluoluo.yanzi.mobile.PathDrawable;
import cc.luoluoluo.yanzi.mobile.widget.ExtensionsWidgetProvider;
import cc.luoluoluo.yanzi.mobile.widget.YanmWidgetData;
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
import org.vosk.Model;
import org.vosk.Recognizer;
import org.vosk.android.SpeechService;
import org.vosk.android.StorageService;
import java.lang.reflect.Method;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.time.Instant;
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
    public static MainActivity sInstance;
    private static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";
    private static final String CACHE_REMOTE_EXTENSIONS = "cacheRemoteExtensionsJson";
    private static final String CACHE_YANM = "cacheYanmJson";
    private static final int REQUEST_PICK_PHOTO = 4101;
    private static final int REQUEST_CODE_SELECT_IMAGE = 8001;
    private static final int REQUEST_CODE_SELECT_FILE = 8002;
    private static final int REQUEST_CODE_TAKE_PHOTO = 8003;
    private static final long DESKTOP_ONLINE_WINDOW_MS = 2 * 60 * 1000L;
    private Uri cameraPhotoUri;
    private File cameraPhotoFile;
    private final ArrayList<AttachmentInfo> pendingAttachments = new ArrayList<>();
    private final ArrayList<AttachmentInfo> activeImageAttachments = new ArrayList<>();
    private HorizontalScrollView aiAttachmentScrollView;
    private LinearLayout aiAttachmentContainer;
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private SharedPreferences prefs;
    private String deviceId;
    private androidx.core.widget.NestedScrollView mainScrollView;
    private EditText baseUrlInput;
    private EditText emailInput;
    private EditText passwordInput;
    private EditText textInput;
    private TextView statusText;
    private final android.content.BroadcastReceiver yanmSyncReceiver = new android.content.BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            MainActivity.this.runOnUiThread(() -> {
                MainActivity.this.setStatus("\u5c40\u57df\u7f51\u6536\u5230\u71d5\u5e55\u66f4\u65b0\u901a\u77e5\uff0c\u6b63\u5728\u540c\u6b65...");
                MainActivity.this.refreshYanm(true);
            });
        }
    };
    private final android.content.BroadcastReceiver chatMessageReceiver = new android.content.BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            String msg = intent.getStringExtra("message");
            android.util.Log.i("MainActivity", "Received CHAT_MESSAGE broadcast, msg=" + msg);
            if (msg != null) {
                MainActivity.this.runOnUiThread(() -> {
                    MainActivity.this.renderChatMessage("desktop", "text", msg, true);
                });
            }
        }
    };
    private EditText mobileExtensionInput;
    private EditText mobileExtensionIdInput;
    private EditText mobileExtensionNameInput;
    private EditText mobileExtensionIconInput;
    private EditText mobileExtensionDescriptionInput;
    private TextView mobileExtensionSectionTitle;
    private TextView mobileExtensionTestResult;
    private LinearLayout mobileExtensionListView;
    private LinearLayout mobileExtensionEditorView;
    private GridLayout mobileExtensionGrid;
    private LinearLayout extensionList;
    private GridLayout yanmList;
    private LinearLayout yanmTabPage;
    private LinearLayout mobileExtensionTabPage;
    private LinearLayout desktopExtensionTabPage;
    private LinearLayout profileTabPage;
    private android.widget.ImageView profileAvatarView;
    private android.widget.TextView profileNameView;
    private android.widget.TextView profileSubtextView;
    private final android.os.Handler autoCloudUpdateHandler = new android.os.Handler(android.os.Looper.getMainLooper());
    private Runnable autoCloudUpdateRunnable;
    private LinearLayout aiTabPage;
    private View yanmTabButton;
    private View mobileExtensionTabButton;
    private View aiTabButton;
    private View desktopExtensionTabButton;
    private View profileTabButton;
    private View desktopConnectionDot;
    private android.os.Handler connectionCheckHandler;
    private Runnable connectionCheckRunnable;
    private boolean isDesktopConnected = false;
    private String desktopConnectionType = "";
    private LinearLayout offlineHintView;
    private LinearLayout mainDesktopContentLayout;
    private TextView tvDesktopConnectionStatus;
    private TextView tvDesktopOfflineTitle;
    private TextView tvDesktopOfflineDesc;
    private String desktopOfflineTitle = "电脑端未上线";
    private String desktopOfflineDesc = "请确认电脑端程序已开启并在运行中";
    private androidx.viewpager.widget.ViewPager desktopViewPager;
    private HorizontalScrollView breadcrumbsScrollView;
    private LinearLayout breadcrumbsLayout;
    private EditText fsSearchInput;
    private boolean isFsUploading = false;
    private Button loginButton;
    private AlertDialog accountDialog;
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
    private static final int SPEECH_COMPLETE_SILENCE_MS = 3000;
    private static final int SPEECH_POSSIBLY_COMPLETE_SILENCE_MS = 3000;
    private static final int SPEECH_MINIMUM_LENGTH_MS = 15000;
    private static final int SPEECH_MAX_RESULTS = 3;
    private static final int SPEECH_MAX_CONTINUATION_COUNT = 20;
    private static final String WAKE_MODEL_ASSET_NAME = "vosk-model-small-cn-0.22";
    private static final String WAKE_MODEL_TARGET_NAME = "wake-model-cn";
    private static final String WAKE_GRAMMAR = "[\"燕子 燕子\", \"[unk]\"]";
    private final Object aiToolCallLock = new Object();
    private final Map<String, Long> recentAiToolCalls = new HashMap<String, Long>();
    private final Set<String> runningAiToolCalls = new HashSet<String>();
    private String currentPath = null;
    private TextView tvCurrentPath = null;
    private LinearLayout fileListLayout = null;
    private TextView tvShellOutput = null;
    private EditText etShellInput = null;
    private androidx.core.widget.NestedScrollView shellScrollView = null;
    private final java.util.List<String> shellHistory = new java.util.ArrayList<>();
    private int shellHistoryIndex = -1;
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
            "\u76ee\u524d\u5df2\u5b89\\u88c5\u7684\\u63d2\u4ef6\\u5217\u8868\u5982\u4e0b\uff1a\n" +
            "1. \u8ba1\u7b97\u5668 (ID: ext_calculator)\n" +
            "2. \u5929\u6c14\u52a9\u624b (ID: ext_weather)\n" +
            "\u4f60\u53ef\u4ee5\u544a\u8bc9\u6211\u4f60\u60f3\u6267\\u884c\u54ea\u4e00\u4e2a\u3002\n" +
            "\n" +
            "\u3010\u53ef\u7528\u5de5\u5177\u5217\u8868\u3011\n" +
            "1. query_extensions: \u83b7\u53d6\u53ef\u7528\u6269\u5c55\u5217\u8868\u3002\u65e0\u53c2\u6570\u3002\n" +
            "2. execute_extension: \u6267\u884c\u67d0\u4e2a\u6269\u5c55\u3002\u53c2\u6570: id (\u6269\u5c55ID)\u3002\n" +
            "3. view_yanm: 查看燕幕组件清单/前端结构。参数: id 可选，includeHtml 可选。id 为空时只返回组件 id、标题、stateKey 和数据长度，绝不返回完整 HTML 或正文；只有明确要看前端代码时才传 includeHtml:true。\n" +
            "4. view_yanm_state: 查看燕幕组件后端数据 componentState。参数: id 可选，stateKey 可选。不填 stateKey 时自动使用组件的 stateKey；修改便签、待办等正文前先用它确认 key/value。\n" +
            "5. update_yanm_state: 修改燕幕组件后端数据 componentState。参数: id 可选，stateKey 可选，value。它只改正文/数据，不改前端 HTML、布局和燕幕启用状态。【格式约束】便签的值为纯文本字符串；待办的值为 JSON 数组字符串，其 Item 必须为 {\"text\":\"任务\",\"done\":false}，键名必须是 text 和 done，不要用 title 或 completed。\n" +
            "6. update_yanm_component: 仅修改燕幕组件前端结构。参数: id，mode 必须为 frontend，title 可选，html 可选。不要用它修改便签正文。\n" +
            "7. manage_mobile_extension: \u7ba1\u7406\u624b\u673a\u6269\u5c55\u3002\u53c2\u6570: action (list/read/create/update/delete), id, name, code, icon, description\u3002\u3010\u91cd\u8981\u3011\u624b\u673a\u6269\u5c55\u8fd0\u884c\u5728\u0020\u006d\u006f\u0062\u0069\u006c\u0065\u002d\u006a\u0073\u0020\u7684\u0020\u004a\u0061\u0076\u0061\u0053\u0063\u0072\u0069\u0070\u0074\u0020\u73af\u5883\u4e2d\uff0c\u4e25\u7981\u4f7f\u7528\u0020\u0043\u0023\u3001\u0050\u006f\u0077\u0065\u0072\u0053\u0068\u0065\u006c\u006c\u0020\u6216\u0020\u0057\u0069\u006e\u0064\u006f\u0077\u0073\u0020\u684c\u9762\u0020\u0041\u0050\u0049\u3002\u5f53\u521b\u5efa\u6216\u66f4\u65b0\u6269\u5c55\u65f6\uff0c\u0063\u006f\u0064\u0065\u0020\u53c2\u6570\u5fc5\u987b\u662f\u7ebf\u6027\u3001\u7b80\u6d01\u7684\u0020\u004a\u0053\u0020\u4ee3\u7801\uff0c\u811a\u672c\u5165\u53e3\u4e3a\u0020\u0061\u0073\u0079\u006e\u0063\u0020\u0066\u0075\u006e\u0063\u0074\u0069\u006f\u006e\u0020\u0072\u0075\u006e\u0028\u0063\u006f\u006e\u0074\u0065\u0078\u0074\u0029\uff0c\u53ef\u8c03\u7528\u0020\u0063\u006f\u006e\u0074\u0065\u0078\u0074\u002e\u006d\u006f\u0062\u0069\u006c\u0065\u002e\u006f\u0070\u0065\u006e\u0055\u0072\u006c\u0028\u0075\u0072\u006c\u0029\u3001\u0074\u006f\u0061\u0073\u0074\uff0c\u8c03\u7528\u0020\u0063\u006f\u006e\u0074\u0065\u0078\u0074\u002e\u006d\u006f\u0062\u0069\u006c\u0065\u002e\u0073\u0065\u006e\u0064\u0054\u006f\u0044\u0065\u0073\u006b\u0074\u006f\u0070\u0028\u0074\u0065\u0078\u0074\u0029\u3002\n" +
            "8. execute_command: \u5728\u7535\u8111\u7aef\u6267\u884c\u547d\u4ee4\u884c\u547d\u4ee4\u3002\u53c2\u6570: command (\u8981\u6267\u884c\u7684\u547d\u4ee4\u6587\u672c)\u3002\u3010\u91cd\u8981\u3011\u7535\u8111\u7aef\u5df2\u9ed8\u8ba4\u5728\u0020\u0050\u006f\u0077\u0065\u0072\u0053\u0068\u0065\u006c\u006c\u0020\u0035\u002e\u0031\u0020\u73af\u5883\u4e2d\u6267\u884c\u547d\u4ee4\uff0c\u8bf7\u76f4\u63a5\u8f93\u5165\u0020\u0050\u006f\u0077\u0065\u0072\u0053\u0068\u0065\u006c\u006c\u0020\u7684\u0020\u0043\u006d\u0064\u006c\u0065\u0074\u0020\u6216\u8868\u8fbe\u5f0f\uff0c\u4e25\u7981\u5916\u5c42\u5d4c\u5957\u8c03\u7528\u0020\u0070\u006f\u0077\u0065\u0072\u0073\u0068\u0065\u006c\u006c\u3001\u0070\u006f\u0077\u0065\u0072\u0073\u0068\u0065\u006c\u006c\u002e\u0065\u0078\u0065\u0020\u002d\u0043\u006f\u006d\u006d\u0061\u006e\u0064\u0020\u6216\u0020\u0063\u006d\u0064\u0020\u002f\u0063\uff0c\u907f\u514d\u8f6c\u4e49\u9519\u8bef\u548c\u6267\u884c\u8d85\u65f6\u3002\n" +
            "9. execute_mobile_command: \u5728\u624b\u673a\u672c\u5730\u6267\u884c\u0020\u004c\u0069\u006e\u0075\u0078\u0020\u547d\u4ee4\u3002\u53c2\u6570\uff1a\u0063\u006f\u006d\u006d\u0061\u006e\u0064\u0020\uff08\u8981\u6267\u884c\u7684\u547d\u4ee4\u6587\u672c\uff09\u3002\u3010\u91cd\u8981\u3011\u624b\u673a\u7aef\u672c\u5730\u65e0\u0020\u0072\u006f\u006f\u0074\u0020\u6743\u9650\uff0c\u53ea\u80fd\u6267\u884c\u666e\u901a\u7684\u0020\u004c\u0069\u006e\u0075\u0078\u0020\u547d\u4ee4\uff08\u4f8b\u5982\u0020\u006c\u0073\u002c\u0020\u0070\u006d\u0020\u006c\u0069\u0073\u0074\u0020\u0070\u0061\u0063\u006b\u0061\u0067\u0065\u0073\u002c\u0020\u0067\u0065\u0074\u0070\u0072\u006f\u0070\u0020\u7b49\uff09\uff0c\u4e25\u7981\u6267\u884c\u4efb\u4f55\u7834\u574f\u7cfb\u7edf\u5b89\u5168\u7684\u547d\u4ee4\u3002\n" +
            "\u3010\u6ce8\u610f\u3011\u5982\u679c\u4f60\u8c03\u7528\u4e86\u5de5\u5177\uff0c\u7cfb\u7edf\u4f1a\u5728\u540e\u53f0\u771f\u5b9e\u6267\u884c\uff0c\u5e76\u5728\u6267\u884c\u5b8c\u6210\u540e\u5c06\u771f\u5b9e\u7684\u7ed3\u679c\u53cd\u9988\u7ed9\u4f60\uff0c\u4e4b\u540e\u4f60\u518d\u6839\u636e\u6267\u884c\u7ed3\u679c\u6765\u51b3\u5b9a\u662f\u7ee7\u7eed\u8c03\u7528\u5de5\u5177\u8fd8\u662f\u8f93\u51fa\u6700\u7ec8\u7684\u81ea\u7136\u8bed\u8a00\u56de\u590d\u3002";
    private SwipeRefreshLayout swipeRefresh;
    private final Set<String> expandedComponentIds = new HashSet<String>();
    private final List<String> sortedComponentIds = new ArrayList<String>();
    private android.speech.tts.TextToSpeech textToSpeech;
    private boolean isTtsEnabled = false;
    private boolean isTtsInitialized = false;
    private boolean isTtsSpeaking = false;
    private String pendingSpeakText = null;
    private android.speech.SpeechRecognizer speechRecognizer;
    private android.content.Intent speechRecognizerIntent;
    private boolean isSpeechListening = false;
    private long lastSpeechStartTime = 0;
    private boolean pendingStopSpeech = false;
    private boolean isSpeechActionUp = false;
    private boolean isSpeechFinished = false;
    private final Handler speechRestartHandler = new Handler(Looper.getMainLooper());
    private Runnable pendingSpeechRestartRunnable;
    private final StringBuilder speechAccumulatedText = new StringBuilder();
    private int speechContinuationCount = 0;
    private Model wakeModel;
    private SpeechService wakeSpeechService;
    private boolean isWakeListeningEnabled = false;
    private boolean isWakeListeningActive = false;
    private boolean isWakeModelLoading = false;
    private boolean isWakeTriggeredSpeech = false;
    private boolean isWakeTriggeredSpeechSessionActive = false;
    private android.widget.ImageView wakeToggleBtn;
    private Button ttsStopButton;
    private android.widget.Button holdToSpeakBtn;
    private android.widget.Button chatHoldToSpeakBtn;
    private boolean isChatVoiceActive;
    private android.widget.ImageView chatVoiceToggleBtn;
    private android.widget.ImageView chatAttachBtn;
    private android.widget.ImageView voiceToggleBtn;
    private final java.util.Set<String> failedSpeechPackages = new java.util.HashSet<String>();
    private String currentSpeechPackage = null;
    private int speechRetryCount = 0;
    private android.widget.Button btnShowExtensions;
    private android.widget.Button btnShowChat;
    private LinearLayout chatContainerLayout;
    private LinearLayout chatMessageListLayout;
    private EditText chatInputEditText;
    private Button chatSendButton;
    private Button chatPhotoButton;
    private Button chatFileButton;
    private TextView flatLogTv;
    private androidx.core.widget.NestedScrollView flatLogScrollView;
    private android.widget.Button btnShowFileManager;
    private android.widget.Button btnShowShell;
    private android.view.View extensionsContainer;
    private android.view.View fileManagerContainer;
    private android.view.View shellContainer;
    private int currentSubTabIndex = 0;
    private android.widget.Button btnShowMobileExtensions;
    private android.widget.Button btnShowMobileDocs;
    private android.widget.Button btnShowMobileShell;
    private android.widget.LinearLayout mobileSubTabBar;
    private int currentMobileSubTab = 0;
    private boolean isEditingMobileExtension = false;
    private androidx.core.widget.NestedScrollView mobileDocsScrollView;
    private android.widget.LinearLayout mobileDocsContainer;
    private android.widget.LinearLayout mobileShellContainer;
    private android.widget.TextView tvMobileShellLog;
    private androidx.core.widget.NestedScrollView svMobileShellLog;
    private android.widget.EditText etMobileShellInput;
    private androidx.viewpager.widget.ViewPager mobileViewPager;
    private android.view.GestureDetector tabGestureDetector;

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
    private Runnable pendingYanmComponentStateSync;
    private final Map<String, String> pendingYanmComponentStateUpdates = new HashMap<String, String>();
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
        if (this.getIntent() != null) {
            if (this.getIntent().hasExtra("run_remote_extension_id")) {
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
            if (this.getIntent().hasExtra("run_mobile_extension_id")) {
                super.onCreate(savedInstanceState);
                sContext = this;
                this.prefs = this.getSharedPreferences("yanzi-mobile", 0);
                this.deviceId = this.getOrCreateDeviceId();
                String extId = this.getIntent().getStringExtra("run_mobile_extension_id");
                String extName = this.getIntent().getStringExtra("run_mobile_extension_name");
                if (extId != null && !extId.isEmpty()) {
                    this.runLocalMobileExtensionByIdSilently(extId, extName != null ? extName : extId);
                }
                return;
            }
        }
        super.onCreate(savedInstanceState);
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.LOLLIPOP) {
            android.view.Window window = this.getWindow();
            int themeColor = Color.rgb(17, 17, 17);
            window.setStatusBarColor(themeColor);
            window.setNavigationBarColor(themeColor);
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.M) {
                android.view.View decorView = window.getDecorView();
                int flags = decorView.getSystemUiVisibility();
                flags &= ~android.view.View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
                if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.O) {
                    flags &= ~android.view.View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
                }
                decorView.setSystemUiVisibility(flags);
            }
        }
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
            this.registerReceiver(this.screenshotReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.SCREENSHOT_SUCCESS"), 
                Context.RECEIVER_NOT_EXPORTED);
        } else {
            this.registerReceiver(this.screenshotReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.SCREENSHOT_SUCCESS"));
        }
        sContext = this;
        sInstance = this;
        LanDiscoveryManager.discover((Context)this);
        this.startService(new Intent((Context)this, FloatingWheelService.class));
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
        this.connectionCheckHandler = new android.os.Handler(android.os.Looper.getMainLooper());
        this.connectionCheckRunnable = new Runnable() {
            @Override
            public void run() {
                MainActivity.this.checkConnectionAsync();
                MainActivity.this.connectionCheckHandler.postDelayed(this, 8000L);
            }
        };
        this.autoCloudUpdateRunnable = new Runnable() {
            @Override
            public void run() {
                boolean autoUpdate = MainActivity.this.prefs.getBoolean("auto_cloud_update", false);
                if (autoUpdate) {
                    MainActivity.this.refreshYanm(true);
                }
                int interval = MainActivity.this.prefs.getInt("auto_cloud_update_interval", 60);
                if (interval < 10) {
                    interval = 10;
                }
                MainActivity.this.autoCloudUpdateHandler.postDelayed(this, (long)interval * 1000L);
            }
        };
    }

    protected void onResume() {
        super.onResume();
        if (this.overlayButton != null) {
            this.overlayButton.setText((CharSequence)(FloatingWheelService.isRunning ? "\u5173\u95ed\u60ac\u6d6e\u8f6e\u76d8" : "\u6253\u5f00\u60ac\u6d6e\u8f6e\u76d8"));
        }
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU) {
            this.registerReceiver(this.yanmSyncReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.SYNC_YANM"), 
                Context.RECEIVER_NOT_EXPORTED);
            this.registerReceiver(this.chatMessageReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.CHAT_MESSAGE"), 
                Context.RECEIVER_NOT_EXPORTED);
        } else {
            this.registerReceiver(this.yanmSyncReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.SYNC_YANM"));
            this.registerReceiver(this.chatMessageReceiver, 
                new android.content.IntentFilter("cc.luoluoluo.yanzi.mobile.CHAT_MESSAGE"));
        }
        LanDiscoveryManager.discover((Context)this);
        this.syncClipboard();
        this.refreshDiagnosticLogFromStore();
        this.diagnosticRefreshHandler.removeCallbacks(this.diagnosticRefreshRunnable);
        this.diagnosticRefreshHandler.postDelayed(this.diagnosticRefreshRunnable, 1000L);
        if (this.isWakeListeningEnabled && !this.isWakeTriggeredSpeech) {
            this.startWakeListening();
        }
        if (this.connectionCheckHandler != null && this.connectionCheckRunnable != null) {
            this.connectionCheckHandler.post(this.connectionCheckRunnable);
        }
        if (this.prefs.getBoolean("auto_cloud_update", false)) {
            this.refreshYanm(true);
            this.autoCloudUpdateHandler.removeCallbacks(this.autoCloudUpdateRunnable);
            this.autoCloudUpdateHandler.postDelayed(this.autoCloudUpdateRunnable, 1000L);
        }
        this.updateProfileHeader();
        this.loadChatHistory();
    }

    private void syncClipboard() {
        this.executor.execute(() -> {
            try {
                String token = this.prefs.getString("token", "").trim();
                if (token.isEmpty()) return;
                
                String baseUrl = this.normalizedBaseUrl();
                
                final String[] localTextHolder = new String[]{""};
                final boolean[] hasClipHolder = new boolean[]{false};
                this.runOnUiThread(() -> {
                    try {
                        android.content.ClipboardManager cm = (android.content.ClipboardManager) this.getSystemService(Context.CLIPBOARD_SERVICE);
                        if (cm != null && cm.hasPrimaryClip()) {
                            android.content.ClipData data = cm.getPrimaryClip();
                            if (data != null && data.getItemCount() > 0) {
                                CharSequence text = data.getItemAt(0).getText();
                                if (text != null) {
                                    localTextHolder[0] = text.toString();
                                }
                            }
                        }
                    } catch (Exception ignored) {}
                    hasClipHolder[0] = true;
                });
                
                int waits = 0;
                while (!hasClipHolder[0] && waits < 10) {
                    Thread.sleep(50);
                    waits++;
                }
                
                String localText = localTextHolder[0];
                String lastSyncedText = this.prefs.getString("last_synced_clipboard", "");
                
                boolean writeToPc = false;
                if (!localText.isEmpty() && !localText.equals(lastSyncedText)) {
                    writeToPc = true;
                }
                
                JSONObject payload = new JSONObject()
                    .put("text", (Object)localText)
                    .put("write", writeToPc);
                    
                JSONObject res = YanziApiClient.postJson(baseUrl, "/v1/clipboard/sync", payload, token, "\u540c\u6b65\u526a\u8d34\u677f");
                String pcText = res.optString("text", "");
                
                if (!pcText.isEmpty() && !pcText.equals(localText)) {
                    this.runOnUiThread(() -> {
                        try {
                            android.content.ClipboardManager cm = (android.content.ClipboardManager) this.getSystemService(Context.CLIPBOARD_SERVICE);
                            if (cm != null) {
                                android.content.ClipData clip = android.content.ClipData.newPlainText("Yanzi Sync", pcText);
                                cm.setPrimaryClip(clip);
                                Toast.makeText(this.getApplicationContext(), "\u526a\u8d34\u677f\u5df2\u540c\u6b65\u81ea\u7535\u8111\u7aef", Toast.LENGTH_SHORT).show();
                            }
                        } catch (Exception ignored) {}
                    });
                    this.prefs.edit().putString("last_synced_clipboard", pcText).apply();
                } else if (writeToPc) {
                    this.prefs.edit().putString("last_synced_clipboard", localText).apply();
                }
            } catch (Exception e) {
                Log.e("YanziClipboard", "Sync clipboard error", e);
            }
        });
    }

    private void loadFileList(String targetPath) {
        this.runOnUiThread(() -> {
            if (this.fileListLayout != null) {
                this.fileListLayout.removeAllViews();
                this.fileListLayout.addView((View)this.textView("\u6b63\u5728\u52a0\u8f7d\u6587\u4ef6\u5217\u8868...", 14, Color.rgb(148, 163, 184), false));
            }
        });
        
        this.executor.execute(() -> {
            try {
                String token = this.prefs.getString("token", "").trim();
                if (token.isEmpty()) return;
                
                String baseUrl = this.normalizedBaseUrl();
                JSONObject payload = new JSONObject().put("path", (Object)targetPath);
                JSONObject res = YanziApiClient.postJson(baseUrl, "/v1/fs/list", payload, token, "\u83b7\u53d6\u6587\u4ef6\u5217\u8868");
                
                String processedPath = res.optString("path", "");
                JSONArray items = res.optJSONArray("items");
                
                this.runOnUiThread(() -> {
                    this.currentPath = processedPath;
                    if (this.tvCurrentPath != null) {
                        this.tvCurrentPath.setText((CharSequence)(processedPath.isEmpty() ? "\u5f53\u524d\u8def\u5f84: [\u76d8\u7b26\u6839\u89c6\u5b9a]" : "\u5f53\u524d\u8def\u5f84: " + processedPath));
                    }
                    this.renderBreadcrumbs(processedPath);
                    
                    if (this.fileListLayout == null) return;
                    this.fileListLayout.removeAllViews();
                    
                    if (items == null || items.length() == 0) {
                        this.fileListLayout.addView((View)this.textView("\u6b64\u6587\u4ef6\u5939\u4e3a\u7a7a\u3002", 14, Color.rgb(148, 163, 184), false));
                        return;
                    }
                    
                    for (int i = 0; i < items.length(); ++i) {
                        JSONObject item = items.optJSONObject(i);
                        if (item == null) continue;
                        
                        String name = item.optString("name", "");
                        boolean isDir = item.optBoolean("isDir", false);
                        long size = item.optLong("size", 0L);
                        
                        LinearLayout row = new LinearLayout((Context)this);
                        row.setOrientation(0);
                        row.setGravity(16);
                        row.setPadding(0, this.dp(8), 0, this.dp(8));
                        row.setClickable(true);
                        row.setTag((Object)name);
                        
                        ImageView ivIcon = new ImageView((Context)this);
                        String iconName = isDir ? "folder" : "file-document-outline";
                        int iconColor = isDir ? Color.rgb(34, 211, 238) : Color.rgb(200, 200, 200);
                        ivIcon.setImageDrawable((android.graphics.drawable.Drawable)new PathDrawable(MobileIconLibrary.resolveOrDefault(iconName), iconColor));
                        
                        row.addView((View)ivIcon, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(24), this.dp(24)));
                        
                        LinearLayout textContainer = new LinearLayout((Context)this);
                        textContainer.setOrientation(1);
                        
                        TextView tvName = this.textView(name, 14, Color.WHITE, false);
                        tvName.setEllipsize(android.text.TextUtils.TruncateAt.END);
                        tvName.setSingleLine(true);
                        textContainer.addView((View)tvName);
                        
                        if (!isDir) {
                            String sizeStr = size < 1024 ? size + " B" : (size < 1024 * 1024 ? (size / 1024) + " KB" : (size / (1024 * 1024)) + " MB");
                            TextView tvSize = this.textView(sizeStr, 11, Color.rgb(148, 163, 184), false);
                            textContainer.addView((View)tvSize);
                        }
                        
                        LinearLayout.LayoutParams tcParams = new LinearLayout.LayoutParams(0, -2, 1.0f);
                        tcParams.leftMargin = this.dp(10);
                        row.addView((View)textContainer, (ViewGroup.LayoutParams)tcParams);
                        
                        if (isDir) {
                            row.setOnClickListener(v -> {
                                String separator = processedPath.endsWith("\\") || processedPath.endsWith("/") ? "" : "\\";
                                String nextPath = processedPath.isEmpty() ? name : processedPath + separator + name;
                                this.loadFileList(nextPath);
                            });
                        } else {
                            Button btnOpen = new Button((Context)this);
                            btnOpen.setText((CharSequence)"打开");
                            btnOpen.setTextColor(-1);
                            btnOpen.setBackgroundColor(Color.rgb(30, 41, 59));
                            btnOpen.setTextSize(11f);
                            btnOpen.setAllCaps(false);
                            
                            btnOpen.setOnClickListener(v -> {
                                String separator = processedPath.endsWith("\\") || processedPath.endsWith("/") ? "" : "\\";
                                String fullFilePath = processedPath + separator + name;
                                boolean isText = this.isTextFile(name);
                                
                                if (isText) {
                                    Toast.makeText(this.getApplicationContext(), "正在加载文件内容...", Toast.LENGTH_SHORT).show();
                                    this.executor.execute(() -> {
                                        try {
                                            String readToken = this.prefs.getString("token", "").trim();
                                            String readBaseUrl = this.normalizedBaseUrl();
                                            JSONObject readPayload = new JSONObject().put("path", (Object)fullFilePath);
                                            JSONObject readRes = YanziApiClient.postJson(readBaseUrl, "/v1/fs/read", readPayload, readToken, "读取文件");
                                            if (readRes.optBoolean("ok", false)) {
                                                String content = readRes.optString("content", "");
                                                this.runOnUiThread(() -> {
                                                    this.showTextEditorDialog(fullFilePath, name, content);
                                                });
                                            } else {
                                                String error = readRes.optString("error", "未知错误");
                                                this.runOnUiThread(() -> {
                                                    Toast.makeText(this.getApplicationContext(), "加载失败: " + error, Toast.LENGTH_LONG).show();
                                                });
                                            }
                                        } catch (Exception ex) {
                                            this.runOnUiThread(() -> {
                                                Toast.makeText(this.getApplicationContext(), "加载失败: " + ex.getMessage(), Toast.LENGTH_LONG).show();
                                            });
                                        }
                                    });
                                } else {
                                    Toast.makeText(this.getApplicationContext(), "非文本文件，正在电脑上打开...", Toast.LENGTH_SHORT).show();
                                    this.executor.execute(() -> {
                                        try {
                                            String runToken = this.prefs.getString("token", "").trim();
                                            String runBaseUrl = this.normalizedBaseUrl();
                                            JSONObject runPayload = new JSONObject().put("command", (Object)("Start-Process \"" + fullFilePath + "\""));
                                            YanziApiClient.postJson(runBaseUrl, "/v1/shell/run", runPayload, runToken, "打开文件");
                                        } catch (Exception ex) {
                                            Log.e("YanziFS", "Run file error", ex);
                                        }
                                    });
                                }
                            });
                            
                            row.addView((View)btnOpen, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(60), this.dp(30)));
                        }
                        
                        View divider = new View((Context)this);
                        divider.setBackgroundColor(Color.rgb(30, 41, 59));
                        
                        this.fileListLayout.addView((View)row);
                        this.fileListLayout.addView(divider, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(1)));
                    }
                    if (this.fsSearchInput != null) {
                        this.filterFsList(this.fsSearchInput.getText().toString());
                    }
                    this.adjustViewPagerHeight();
                });
            } catch (Exception e) {
                this.runOnUiThread(() -> {
                    if (this.fileListLayout != null) {
                        this.fileListLayout.removeAllViews();
                        this.fileListLayout.addView((View)this.textView("\u52a0\u8f6d\u5931\u8d25: " + e.getMessage(), 14, Color.RED, false));
                    }
                    this.adjustViewPagerHeight();
                });
            }
        });
    }

    protected void onPause() {
        this.diagnosticRefreshHandler.removeCallbacks(this.diagnosticRefreshRunnable);
        this.autoCloudUpdateHandler.removeCallbacks(this.autoCloudUpdateRunnable);
        this.stopWakeListening(false);
        this.destroySpeechRecognizer();
        if (this.connectionCheckHandler != null && this.connectionCheckRunnable != null) {
            this.connectionCheckHandler.removeCallbacks(this.connectionCheckRunnable);
        }
        try {
            this.unregisterReceiver(this.yanmSyncReceiver);
        } catch (Exception ignored) {}
        try {
            this.unregisterReceiver(this.chatMessageReceiver);
        } catch (Exception ignored) {}
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
        this.releaseWakeListening();
        if (sInstance == this) {
            sInstance = null;
        }
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
        if (this.isFsUploading) {
            this.isFsUploading = false;
            if (resultCode == -1) {
                if (requestCode == 2001 || requestCode == 2002) {
                    if (data != null && data.getData() != null) {
                        this.uploadFileToPc(data.getData());
                    }
                } else if (requestCode == REQUEST_CODE_TAKE_PHOTO) {
                    if (this.cameraPhotoUri != null && this.cameraPhotoFile != null && this.cameraPhotoFile.exists()) {
                        this.uploadFileToPc(this.cameraPhotoUri);
                    }
                }
            }
            return;
        }
        if (requestCode == 4101 && resultCode == -1 && data != null && (uri = data.getData()) != null) {
            this.sendPhotoToDesktop(uri);
        } else if (requestCode == 4103 && resultCode == -1 && data != null && (uri = data.getData()) != null) {
            this.sendPhotoToDesktopChat(uri);
        } else if (requestCode == 4102 && resultCode == -1 && data != null && (uri = data.getData()) != null) {
            this.sendFileToDesktopChat(uri);
        } else if (requestCode == 4104 && resultCode == -1) {
            if (this.cameraPhotoUri != null && this.cameraPhotoFile != null && this.cameraPhotoFile.exists()) {
                this.sendPhotoToDesktopChat(this.cameraPhotoUri);
            }
        } else if ((requestCode == REQUEST_CODE_SELECT_IMAGE || requestCode == REQUEST_CODE_SELECT_FILE) && resultCode == -1 && data != null && (uri = data.getData()) != null) {
            this.handleAttachmentSelected(uri, requestCode == REQUEST_CODE_SELECT_IMAGE);
        } else if (requestCode == REQUEST_CODE_TAKE_PHOTO && resultCode == -1) {
            if (this.cameraPhotoUri != null && this.cameraPhotoFile != null && this.cameraPhotoFile.exists()) {
                this.handleCameraPhotoTaken(this.cameraPhotoUri, this.cameraPhotoFile.getName(), this.cameraPhotoFile.length());
            }
        } else if (requestCode == 103) {
            if (resultCode == -1 && data != null) {
                ArrayList<String> matches = data.getStringArrayListExtra(android.speech.RecognizerIntent.EXTRA_RESULTS);
                if (matches != null && !matches.isEmpty()) {
                    String text = matches.get(0);
                    if (this.isChatVoiceActive) {
                        this.chatInputEditText.setText((CharSequence)text);
                        this.chatInputEditText.setSelection(text.length());
                        this.handleSendChatMessageClick();
                    } else {
                        this.aiChatInput.setText((CharSequence)text);
                        this.aiChatInput.setSelection(text.length());
                        this.handleAiSendButtonClick();
                    }
                }
            }
            if (this.isWakeTriggeredSpeech || this.isWakeTriggeredSpeechSessionActive) {
                this.isWakeTriggeredSpeech = false;
                this.isWakeTriggeredSpeechSessionActive = false;
                this.restartWakeListeningLater();
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
        } else if (requestCode == 9002) {
            if (grantResults.length > 0 && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                this.launchCameraForChat();
            } else {
                Toast.makeText(this, "需要相机权限才能拍照", Toast.LENGTH_SHORT).show();
            }
        } else if (requestCode == 102) {
            if (grantResults.length > 0 && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                if (this.isChatVoiceActive) {
                    this.switchToChatVoiceInput();
                } else {
                    this.switchToVoiceInput();
                }
            } else {
                Toast.makeText(this, "需要麦克风录音权限才能使用语音输入", Toast.LENGTH_SHORT).show();
            }
        } else if (requestCode == 104) {
            if (grantResults.length > 0 && grantResults[0] == android.content.pm.PackageManager.PERMISSION_GRANTED) {
                this.isWakeListeningEnabled = true;
                this.startWakeListening();
            } else {
                this.isWakeListeningEnabled = false;
                this.updateWakeToggleButton();
                Toast.makeText(this, "需要麦克风录音权限才能监听唤醒词", Toast.LENGTH_SHORT).show();
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
        } else if (intent.hasExtra("run_mobile_extension_id")) {
            String extId = intent.getStringExtra("run_mobile_extension_id");
            String extName = intent.getStringExtra("run_mobile_extension_name");
            if (extId != null && !extId.isEmpty()) {
                this.runLocalMobileExtensionByIdSilently(extId, extName != null ? extName : extId);
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
        if (!savedPrompt.contains("\u3010\u5de5\u5177\u8c03\u7528\u793a\u4f8b\u3011") || !savedPrompt.contains("PowerShell 5.1") || !savedPrompt.contains("mobile-js") || !savedPrompt.contains("view_yanm_state") || !savedPrompt.contains("update_yanm_state") || !savedPrompt.contains("\u3010\u683c\u5f0f\u7ea6\u675f\u3011")) {
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
        ImageView hamburgerBtn = new ImageView((Context)this);
        hamburgerBtn.setClickable(true);
        hamburgerBtn.setFocusable(true);
        hamburgerBtn.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        hamburgerBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("menu"), Color.WHITE));
        hamburgerBtn.setOnClickListener(v -> this.aiDrawerLayout.openDrawer(3));
        topBar.addView((View)hamburgerBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        ImageView clearBtn = new ImageView((Context)this);
        clearBtn.setClickable(true);
        clearBtn.setFocusable(true);
        clearBtn.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        clearBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("delete"), Color.WHITE));
        clearBtn.setOnClickListener(v -> this.clearAiHistory());
        topBar.addView((View)clearBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        TextView title = this.textView("AI 助手", 20, -1, true);
        title.setGravity(17);
        topBar.addView((View)title, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        ImageView speakBtn = new ImageView((Context)this);
        speakBtn.setClickable(true);
        speakBtn.setFocusable(true);
        speakBtn.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        speakBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault(this.isTtsEnabled ? "volume-high" : "volume-mute"), Color.WHITE));
        speakBtn.setOnClickListener(v -> this.toggleTtsStatus(speakBtn));
        topBar.addView((View)speakBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        this.wakeToggleBtn = new ImageView((Context)this);
        this.wakeToggleBtn.setClickable(true);
        this.wakeToggleBtn.setFocusable(true);
        this.wakeToggleBtn.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        this.wakeToggleBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("hearing"), Color.WHITE));
        this.wakeToggleBtn.setColorFilter(Color.rgb(148, 163, 184));
        this.wakeToggleBtn.setOnClickListener(v -> this.toggleWakeListening());
        topBar.addView((View)this.wakeToggleBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(44), this.dp(44)));
        ImageView aiSettingsBtn = new ImageView((Context)this);
        aiSettingsBtn.setClickable(true);
        aiSettingsBtn.setFocusable(true);
        aiSettingsBtn.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        aiSettingsBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("settings"), Color.WHITE));
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

        LinearLayout bottomShell = new LinearLayout((Context)this);
        bottomShell.setOrientation(1);
        bottomShell.setBackgroundColor(Color.rgb((int)22, (int)22, (int)22));
        this.ttsStopButton = this.button("停止朗读");
        this.ttsStopButton.setTextColor(Color.rgb((int)248, (int)250, (int)252));
        GradientDrawable stopTtsBg = new GradientDrawable();
        stopTtsBg.setColor(Color.rgb((int)127, (int)29, (int)29));
        stopTtsBg.setCornerRadius((float)this.dp(8));
        this.ttsStopButton.setBackground((Drawable)stopTtsBg);
        this.ttsStopButton.setVisibility(View.GONE);
        this.ttsStopButton.setOnClickListener(v -> this.stopTtsPlayback(true));
        LinearLayout.LayoutParams stopTtsParams = new LinearLayout.LayoutParams(-1, this.dp(38));
        stopTtsParams.leftMargin = this.dp(12);
        stopTtsParams.rightMargin = this.dp(12);
        stopTtsParams.topMargin = this.dp(8);
        bottomShell.addView((View)this.ttsStopButton, (ViewGroup.LayoutParams)stopTtsParams);

        LinearLayout bottomArea = new LinearLayout((Context)this);
        bottomArea.setOrientation(0);
        bottomArea.setPadding(this.dp(12), this.dp(12), this.dp(12), this.dp(12));
        bottomArea.setGravity(80);
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
        this.aiChatInput.setTextSize(14.0f);
        this.aiChatInput.setBackground(null);
        this.aiChatInput.setPadding(this.dp(12), this.dp(10), this.dp(12), this.dp(10));
        this.aiChatInput.setHintTextColor(Color.argb((int)90, (int)255, (int)255, (int)255));
        this.aiChatInput.setMinLines(1);
        this.aiChatInput.setMaxLines(4);
        bottomArea.addView((View)this.aiChatInput, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));

        this.voiceToggleBtn = new ImageView((Context)this);
        this.voiceToggleBtn.setClickable(true);
        this.voiceToggleBtn.setFocusable(true);
        this.voiceToggleBtn.setPadding(this.dp(12), this.dp(12), this.dp(12), this.dp(12));
        this.voiceToggleBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("microphone"), Color.rgb(200, 200, 200)));
        this.voiceToggleBtn.setOnClickListener(v -> {
            if (this.holdToSpeakBtn.getVisibility() == 8) {
                if (this.checkAudioPermission()) {
                    if (this.isBackendSpeechRecognizerWorkable()) {
                        this.switchToVoiceInput();
                    } else {
                        Toast.makeText((Context)this, "拉起系统语音输入...", Toast.LENGTH_SHORT).show();
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
        bottomShell.addView((View)bottomArea);
        mainContent.addView((View)bottomShell);
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
        androidx.core.widget.NestedScrollView scrollView;
        LinearLayout shell = new LinearLayout((Context)this);
        shell.setOrientation(1);
        shell.setBackgroundColor(ThemeConfig.COLOR_BACKGROUND);
        shell.setFitsSystemWindows(true);
        this.mainScrollView = scrollView = new androidx.core.widget.NestedScrollView((Context)this);
        LinearLayout root = new LinearLayout((Context)this);
        root.setOrientation(1);
        root.setPadding(this.dp(20), this.dp(24), this.dp(20), this.dp(24));
        scrollView.addView((View)root);
        this.swipeRefresh = new SwipeRefreshLayout((Context)this) {
            private float startX;
            private float startY;
            private int touchSlop = android.view.ViewConfiguration.get(getContext()).getScaledTouchSlop();

            @Override
            public boolean onInterceptTouchEvent(android.view.MotionEvent ev) {
                switch (ev.getAction()) {
                    case android.view.MotionEvent.ACTION_DOWN:
                        startX = ev.getX();
                        startY = ev.getY();
                        break;
                    case android.view.MotionEvent.ACTION_MOVE:
                        float diffX = Math.abs(ev.getX() - startX);
                        float diffY = Math.abs(ev.getY() - startY);
                        
                        // 1. 如果水平滑动位移明显大于垂直位移，说明是左右滑动切换 Tab，不予拦截
                        if (diffX > touchSlop && diffX > diffY) {
                            return false;
                        }
                        
                        // 2. 增加下滑距离判定：下拉距离不到 30dp 时，不予拦截，给子 View 自主滚动机会
                        if (diffY < MainActivity.this.dp(30)) {
                            return false;
                        }
                        
                        // 3. 终端内部滑动：如果是终端页面，且终端 ScrollView 还可以向下滚动，则禁止下拉刷新拦截
                        if (MainActivity.this.desktopExtensionTabPage != null && 
                            MainActivity.this.desktopExtensionTabPage.getVisibility() == android.view.View.VISIBLE) {
                            if (MainActivity.this.currentSubTabIndex == 2 && MainActivity.this.shellScrollView != null) {
                                if (MainActivity.this.shellScrollView.canScrollVertically(-1)) {
                                    return false;
                                }
                            }
                        }
                        break;
                }
                return super.onInterceptTouchEvent(ev);
            }
        };
        this.swipeRefresh.addView((View)scrollView);
        this.swipeRefresh.setColorSchemeColors(new int[]{Color.rgb((int)59, (int)130, (int)246)});
        this.swipeRefresh.setProgressBackgroundColorSchemeColor(Color.rgb((int)30, (int)30, (int)30));
        this.swipeRefresh.setOnRefreshListener(() -> {
            YanziApiClient.sLanFailedThisSession = false;
            this.refreshSettings();
            if (this.yanmTabPage != null && this.yanmTabPage.getVisibility() == 0) {
                this.refreshYanm();
            } else if (this.desktopExtensionTabPage != null && this.desktopExtensionTabPage.getVisibility() == 0) {
                this.refreshExtensions();
            } else if (this.mobileExtensionTabPage != null && this.mobileExtensionTabPage.getVisibility() == 0) {
                this.syncMobileExtensionsFromCloud();
                this.swipeRefresh.postDelayed(() -> this.swipeRefresh.setRefreshing(false), 800L);
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
        LinearLayout yanmHeader = new LinearLayout((Context)this);
        yanmHeader.setOrientation(LinearLayout.HORIZONTAL);
        yanmHeader.setGravity(Gravity.CENTER_VERTICAL);
        
        TextView yanmTitle = this.textView("\u71d5\u5e55", 28, -1, true);
        LinearLayout.LayoutParams titleParams = new LinearLayout.LayoutParams(0, -2, 1.0f);
        yanmHeader.addView((View)yanmTitle, (ViewGroup.LayoutParams)titleParams);
        
        Button btnSyncLog = new Button((Context)this);
        btnSyncLog.setText((CharSequence)"\u540c\u6b65\u8bb0\u5f55");
        btnSyncLog.setTextColor(Color.rgb(34, 211, 238));
        btnSyncLog.setBackgroundColor(Color.TRANSPARENT);
        btnSyncLog.setTextSize(14f);
        btnSyncLog.setAllCaps(false);
        this.yanmTabPage.addView((View)yanmHeader);
        
        this.yanmTabPage.addView((View)this.textView("\u67e5\u770b\u548c\u64cd\u4f5c\u7535\u8111\u7aef\u540c\u6b65\u7684\u71d5\u5e55\u7ec4\u4ef6\u3002", 14, Color.rgb((int)182, (int)194, (int)214), false));
        this.yanmList = new GridLayout((Context)this);
        this.yanmList.setColumnCount(1);
        this.yanmList.setAlignmentMode(0);
        this.yanmList.setUseDefaultMargins(false);
        this.yanmTabPage.addView((View)this.yanmList, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));

        LinearLayout flatLogPanel = new LinearLayout((Context)this);
        flatLogPanel.setOrientation(LinearLayout.VERTICAL);
        flatLogPanel.setPadding(0, this.dp(16), 0, 0);
        
        LinearLayout flatLogHeader = new LinearLayout((Context)this);
        flatLogHeader.setOrientation(LinearLayout.HORIZONTAL);
        flatLogHeader.setGravity(Gravity.CENTER_VERTICAL);
        flatLogHeader.setPadding(0, 0, 0, this.dp(8));
        
        TextView flatLogTitle = this.textView("\u540c\u6b65\u4e0e\u8fde\u63a5\u65e5\u5fd7", 16, ThemeConfig.COLOR_TEXT_PRIMARY, true);
        flatLogHeader.addView((View)flatLogTitle, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        
        Button btnCopyLog = this.button("\u590d\u5236");
        btnCopyLog.setTextSize(12f);
        btnCopyLog.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
        btnCopyLog.setOnClickListener(v -> {
            String logText = this.getYanmSyncLogs();
            ClipboardManager manager = (ClipboardManager)this.getSystemService("clipboard");
            if (manager != null) {
                manager.setPrimaryClip(ClipData.newPlainText("logs", logText));
                Toast.makeText(this.getApplicationContext(), "\u65e5\u5fd7\u5df2\u590d\u5236", Toast.LENGTH_SHORT).show();
            }
        });
        
        Button btnClearLog = this.button("\u6e05\u7a7a");
        btnClearLog.setTextSize(12f);
        btnClearLog.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
        btnClearLog.setOnClickListener(v -> {
            MobileDiagnostics.clear((Context)this);
            this.flatLogTv.setText("");
            Toast.makeText(this.getApplicationContext(), "\u65e5\u5fd7\u5df2\u6e05\u7a7a", Toast.LENGTH_SHORT).show();
        });
        
        LinearLayout.LayoutParams btnLp = new LinearLayout.LayoutParams(this.dp(60), this.dp(32));
        btnLp.leftMargin = this.dp(8);
        flatLogHeader.addView((View)btnCopyLog, (ViewGroup.LayoutParams)btnLp);
        flatLogHeader.addView((View)btnClearLog, (ViewGroup.LayoutParams)btnLp);
        flatLogPanel.addView((View)flatLogHeader);
        
        this.flatLogScrollView = new androidx.core.widget.NestedScrollView((Context)this);
        this.flatLogScrollView.setBackgroundColor(ThemeConfig.COLOR_BACKGROUND);
        this.flatLogScrollView.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        
        GradientDrawable gdLog = new GradientDrawable();
        gdLog.setColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        gdLog.setCornerRadius((float)this.dp(8));
        this.flatLogScrollView.setBackground((Drawable)gdLog);
        
        this.flatLogTv = new TextView((Context)this);
        this.flatLogTv.setTextSize(11f);
        this.flatLogTv.setTextColor(ThemeConfig.COLOR_TEXT_SECONDARY);
        this.flatLogTv.setTypeface(Typeface.MONOSPACE);
        this.flatLogTv.setText((CharSequence)this.getYanmSyncLogs());
        this.flatLogScrollView.addView((View)this.flatLogTv);
        
        flatLogPanel.addView((View)this.flatLogScrollView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(180)));
        this.yanmTabPage.addView((View)flatLogPanel);
        // 手机端子 Tab 栏
        this.mobileSubTabBar = new LinearLayout((Context)this);
        mobileSubTabBar.setOrientation(0);
        mobileSubTabBar.setGravity(16);
        mobileSubTabBar.setPadding(this.dp(16), this.dp(16), this.dp(16), this.dp(8));
        
        this.btnShowMobileExtensions = new android.widget.Button((Context)this);
        this.btnShowMobileExtensions.setText((CharSequence)"\u6269\u5c55"); // "扩展"
        this.btnShowMobileExtensions.setTextColor(Color.rgb(148, 163, 184));
        this.btnShowMobileExtensions.setBackgroundColor(Color.TRANSPARENT);
        this.btnShowMobileExtensions.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.btnShowMobileExtensions.setAllCaps(false);
        this.btnShowMobileExtensions.setTextSize(13f);
        
        this.btnShowMobileDocs = new android.widget.Button((Context)this);
        this.btnShowMobileDocs.setText((CharSequence)"\u6587\u6863"); // "文档"
        this.btnShowMobileDocs.setTextColor(Color.rgb(148, 163, 184));
        this.btnShowMobileDocs.setBackgroundColor(Color.TRANSPARENT);
        this.btnShowMobileDocs.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.btnShowMobileDocs.setAllCaps(false);
        this.btnShowMobileDocs.setTextSize(13f);
        
        this.btnShowMobileShell = new android.widget.Button((Context)this);
        this.btnShowMobileShell.setText((CharSequence)"\u624b\u673a\u7ec8\u7aef"); // "手机终端"
        this.btnShowMobileShell.setTextColor(Color.rgb(148, 163, 184));
        this.btnShowMobileShell.setBackgroundColor(Color.TRANSPARENT);
        this.btnShowMobileShell.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.btnShowMobileShell.setAllCaps(false);
        this.btnShowMobileShell.setTextSize(13f);
        
        LinearLayout.LayoutParams mobileBtnParams = new LinearLayout.LayoutParams(-2, this.dp(36));
        mobileBtnParams.rightMargin = this.dp(8);
        mobileSubTabBar.addView((View)this.btnShowMobileExtensions, (ViewGroup.LayoutParams)mobileBtnParams);
        mobileSubTabBar.addView((View)this.btnShowMobileDocs, (ViewGroup.LayoutParams)mobileBtnParams);
        mobileSubTabBar.addView((View)this.btnShowMobileShell, (ViewGroup.LayoutParams)mobileBtnParams);
        
        this.btnShowMobileExtensions.setOnClickListener(v -> this.selectMobileSubTab(0));
        this.btnShowMobileDocs.setOnClickListener(v -> this.selectMobileSubTab(1));
        this.btnShowMobileShell.setOnClickListener(v -> this.selectMobileSubTab(2));
        
        this.mobileExtensionTabPage.addView((View)mobileSubTabBar);

        this.mobileExtensionListView = new LinearLayout((Context)this);
        this.mobileExtensionListView.setOrientation(1);
        this.mobileExtensionListView.setPadding(this.dp(16), this.dp(8), this.dp(16), this.dp(16));
        
        this.mobileExtensionEditorView = new LinearLayout((Context)this);
        this.mobileExtensionEditorView.setOrientation(1);
        this.mobileExtensionEditorView.setPadding(this.dp(6), this.dp(12), this.dp(6), this.dp(12));
        this.mobileExtensionEditorView.setVisibility(View.GONE);
        
        this.mobileDocsScrollView = new androidx.core.widget.NestedScrollView((Context)this);
        this.mobileDocsScrollView.setNestedScrollingEnabled(true);
        this.mobileDocsScrollView.setPadding(this.dp(16), this.dp(8), this.dp(16), this.dp(16));
        
        this.mobileDocsContainer = new android.widget.LinearLayout((Context)this);
        this.mobileDocsContainer.setOrientation(1);
        this.mobileDocsScrollView.addView((View)this.mobileDocsContainer, (ViewGroup.LayoutParams)new android.widget.FrameLayout.LayoutParams(-1, -2));
        
        this.mobileShellContainer = new android.widget.LinearLayout((Context)this);
        this.mobileShellContainer.setOrientation(1);
        this.mobileShellContainer.setPadding(this.dp(16), this.dp(8), this.dp(16), this.dp(16));

        // 新建并配置 ViewPager
        this.mobileViewPager = new androidx.viewpager.widget.ViewPager((Context)this);
        this.mobileViewPager.setId(android.view.View.generateViewId());
        
        final java.util.List<View> mobilePages = new java.util.ArrayList<>();
        mobilePages.add(this.mobileExtensionListView);
        mobilePages.add(this.mobileDocsScrollView);
        mobilePages.add(this.mobileShellContainer);
        
        this.mobileViewPager.setAdapter(new androidx.viewpager.widget.PagerAdapter() {
            @Override
            public int getCount() {
                return mobilePages.size();
            }
            @Override
            public boolean isViewFromObject(View view, Object object) {
                return view == object;
            }
            @Override
            public Object instantiateItem(ViewGroup container, int position) {
                View page = mobilePages.get(position);
                container.addView(page);
                return page;
            }
            @Override
            public void destroyItem(ViewGroup container, int position, Object object) {
                container.removeView((View)object);
            }
        });
        
        this.mobileViewPager.addOnPageChangeListener(new androidx.viewpager.widget.ViewPager.OnPageChangeListener() {
            @Override
            public void onPageScrolled(int position, float positionOffset, int positionOffsetPixels) {}
            
            @Override
            public void onPageSelected(int position) {
                MainActivity.this.selectMobileSubTab(position);
            }
            
            @Override
            public void onPageScrollStateChanged(int state) {}
        });

        int mobileScreenHeight = this.getResources().getDisplayMetrics().heightPixels;
        int mobilePagerHeight = mobileScreenHeight - this.dp(160);
        if (mobilePagerHeight < this.dp(400)) {
            mobilePagerHeight = this.dp(500);
        }
        this.mobileExtensionTabPage.addView((View)this.mobileViewPager, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, mobilePagerHeight));
        this.mobileExtensionTabPage.addView((View)this.mobileExtensionEditorView);
        
        // 渲染文档内容
        this.buildMobileDocsView(this.mobileDocsContainer);
        // 渲染终端内容
        this.buildMobileShellView(this.mobileShellContainer);
        
        // 默认选中第一个子 Tab
        this.selectMobileSubTab(0);
        
        // 渲染 List 页面头部 (移除了大标题，只保留新建按钮，置右排布)
        LinearLayout listHeader = new LinearLayout((Context)this);
        listHeader.setOrientation(0);
        listHeader.setGravity(5); // Gravity.RIGHT is 5
        listHeader.setPadding(0, 0, 0, this.dp(12));
        
        // 加一个漂亮的“新建”按钮在主列表右上角
        Button newExtBtn = this.button("\u65b0\u5efa\u6269\u5c55");
        newExtBtn.setOnClickListener(v -> {
            this.isEditingMobileExtension = true;
            this.mobileExtensionInput.setText((CharSequence)this.defaultMobileExtensionJson());
            this.updateMobileExtensionFieldsFromDraft();
            // 在编辑状态下，隐藏 ViewPager，显示编辑界面
            this.mobileViewPager.setVisibility(View.GONE);
            if (this.mobileSubTabBar != null) this.mobileSubTabBar.setVisibility(View.GONE);
            this.mobileExtensionEditorView.setVisibility(View.VISIBLE);
            this.setStatus("\u65b0\u5efa\u6269\u5c55\u8349\u7a3f");
        });
        listHeader.addView((View)newExtBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, this.dp(40)));
        this.mobileExtensionListView.addView((View)listHeader);
        
        // 网格展示容器
        this.mobileExtensionGrid = new GridLayout((Context)this);
        this.mobileExtensionGrid.setColumnCount(4); // 4 列网格
        this.mobileExtensionGrid.setAlignmentMode(0);
        this.mobileExtensionGrid.setUseDefaultMargins(true);
        this.mobileExtensionListView.addView((View)this.mobileExtensionGrid, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
        
        // 在编辑二级界面最上面，增加一个返回导航栏！
        LinearLayout editorNavBar = new LinearLayout((Context)this);
        editorNavBar.setOrientation(0);
        editorNavBar.setGravity(16);
        editorNavBar.setPadding(0, this.dp(8), 0, this.dp(16));
        Button backBtn = this.button("\u2190");
        backBtn.setOnClickListener(v -> {
            this.isEditingMobileExtension = false;
            this.mobileExtensionEditorView.setVisibility(View.GONE);
            this.mobileViewPager.setVisibility(View.VISIBLE);
            if (this.mobileSubTabBar != null) this.mobileSubTabBar.setVisibility(View.VISIBLE);
            this.setStatus("\u5df2\u8fd4\u56de\u624b\u673a\u6269\u5c55\u5217\u8868");
        });
        editorNavBar.addView((View)backBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, this.dp(40)));
        TextView navTitle = this.textView("  \u7f16\u8f91\u624b\u673a\u6269\u5c55", 18, -1, true);
        editorNavBar.addView((View)navTitle);
        this.mobileExtensionEditorView.addView((View)editorNavBar);
        
        this.buildMobileExtensionEditor(this.mobileExtensionEditorView);
        // 子 Tab 条
        LinearLayout subTabBar = new LinearLayout((Context)this);
        subTabBar.setOrientation(0);
        subTabBar.setGravity(16);
        subTabBar.setPadding(0, 0, 0, this.dp(12));
        
        this.btnShowChat = new Button((Context)this);
        this.btnShowChat.setText((CharSequence)"聊天");
        this.btnShowChat.setTextColor(Color.rgb(34, 211, 238));
        this.btnShowChat.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.btnShowChat.setAllCaps(false);
        this.btnShowChat.setTextSize(13f);
        
        GradientDrawable activeBg = new GradientDrawable();
        activeBg.setCornerRadius((float)this.dp(8));
        activeBg.setColor(Color.argb(20, 34, 211, 238));
        this.btnShowChat.setBackground((android.graphics.drawable.Drawable)activeBg);

        this.btnShowExtensions = new Button((Context)this);
        this.btnShowExtensions.setText((CharSequence)"电脑扩展");
        this.btnShowExtensions.setTextColor(Color.rgb(148, 163, 184));
        this.btnShowExtensions.setBackgroundColor(Color.TRANSPARENT);
        this.btnShowExtensions.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.btnShowExtensions.setAllCaps(false);
        this.btnShowExtensions.setTextSize(13f);
        
        this.btnShowFileManager = new Button((Context)this);
        this.btnShowFileManager.setText((CharSequence)"文件管理");
        this.btnShowFileManager.setTextColor(Color.rgb(148, 163, 184));
        this.btnShowFileManager.setBackgroundColor(Color.TRANSPARENT);
        this.btnShowFileManager.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.btnShowFileManager.setAllCaps(false);
        this.btnShowFileManager.setTextSize(13f);

        this.btnShowShell = new Button((Context)this);
        this.btnShowShell.setText((CharSequence)"PowerShell");
        this.btnShowShell.setTextColor(Color.rgb(148, 163, 184));
        this.btnShowShell.setBackgroundColor(Color.TRANSPARENT);
        this.btnShowShell.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
        this.btnShowShell.setAllCaps(false);
        this.btnShowShell.setTextSize(13f);
        
        LinearLayout.LayoutParams btnParams = new LinearLayout.LayoutParams(-2, this.dp(36));
        btnParams.rightMargin = this.dp(8);
        subTabBar.addView((View)this.btnShowChat, (ViewGroup.LayoutParams)btnParams);
        subTabBar.addView((View)this.btnShowExtensions, (ViewGroup.LayoutParams)btnParams);
        subTabBar.addView((View)this.btnShowFileManager, (ViewGroup.LayoutParams)btnParams);
        subTabBar.addView((View)this.btnShowShell, (ViewGroup.LayoutParams)btnParams);
        
        LinearLayout desktopHeader = new LinearLayout((Context)this);
        desktopHeader.setOrientation(0);
        desktopHeader.setGravity(16);
        desktopHeader.setPadding(0, 0, 0, this.dp(10));
        
        TextView tvTitle = this.textView("电脑", 28, -1, true);
        desktopHeader.addView((View)tvTitle);
        
        this.tvDesktopConnectionStatus = new TextView((Context)this);
        this.tvDesktopConnectionStatus.setTextSize(14f);
        this.tvDesktopConnectionStatus.setPadding(this.dp(8), this.dp(6), 0, 0);
        this.tvDesktopConnectionStatus.setTextColor(Color.rgb(148, 163, 184));
        desktopHeader.addView((View)this.tvDesktopConnectionStatus);
        
        this.desktopExtensionTabPage.addView((View)desktopHeader);
        
        this.offlineHintView = new LinearLayout((Context)this);
        this.offlineHintView.setOrientation(1);
        this.offlineHintView.setGravity(17);
        this.offlineHintView.setPadding(0, this.dp(100), 0, this.dp(100));

        this.tvDesktopOfflineTitle = new TextView((Context)this);
        this.tvDesktopOfflineTitle.setText(this.desktopOfflineTitle);
        this.tvDesktopOfflineTitle.setTextColor(Color.rgb(148, 163, 184));
        this.tvDesktopOfflineTitle.setTextSize(16f);
        this.tvDesktopOfflineTitle.setGravity(17);

        this.tvDesktopOfflineDesc = new TextView((Context)this);
        this.tvDesktopOfflineDesc.setText(this.desktopOfflineDesc);
        this.tvDesktopOfflineDesc.setTextColor(Color.rgb(100, 116, 139));
        this.tvDesktopOfflineDesc.setTextSize(13f);
        this.tvDesktopOfflineDesc.setGravity(17);
        this.tvDesktopOfflineDesc.setPadding(0, this.dp(8), 0, 0);

        this.offlineHintView.addView((View)this.tvDesktopOfflineTitle);
        this.offlineHintView.addView((View)this.tvDesktopOfflineDesc);
        this.desktopExtensionTabPage.addView((View)this.offlineHintView);
        
        this.mainDesktopContentLayout = new LinearLayout((Context)this);
        this.mainDesktopContentLayout.setOrientation(1);
        this.mainDesktopContentLayout.setVisibility(8);
        this.mainDesktopContentLayout.addView((View)subTabBar);
        this.desktopExtensionTabPage.addView((View)this.mainDesktopContentLayout);
        
        LinearLayout extensionsContainer = new LinearLayout((Context)this);
        this.extensionsContainer = extensionsContainer;
        extensionsContainer.setOrientation(1);
        
        LinearLayout fileManagerContainer = new LinearLayout((Context)this);
        this.fileManagerContainer = fileManagerContainer;
        fileManagerContainer.setOrientation(1);

        LinearLayout shellContainer = new LinearLayout((Context)this);
        this.shellContainer = shellContainer;
        shellContainer.setOrientation(1);
        
        int screenHeight = this.getResources().getDisplayMetrics().heightPixels;
        int pagerHeight = screenHeight - this.dp(200);
        if (pagerHeight < this.dp(400)) {
            pagerHeight = this.dp(500);
        }
        
        this.desktopViewPager = new androidx.viewpager.widget.ViewPager((Context)this);
        this.desktopViewPager.setId(android.view.View.generateViewId());
        
        LinearLayout chatContainer = this.buildChatContainer();
        
        final List<View> pages = new java.util.ArrayList<>();
        pages.add(chatContainer);
        pages.add(extensionsContainer);
        pages.add(fileManagerContainer);
        pages.add(shellContainer);
        
        this.desktopViewPager.setAdapter(new androidx.viewpager.widget.PagerAdapter() {
            @Override
            public int getCount() {
                return pages.size();
            }
            @Override
            public boolean isViewFromObject(View view, Object object) {
                return view == object;
            }
            @Override
            public Object instantiateItem(ViewGroup container, int position) {
                View page = pages.get(position);
                container.addView(page);
                return page;
            }
            @Override
            public void destroyItem(ViewGroup container, int position, Object object) {
                container.removeView((View)object);
            }
        });
        
        this.desktopViewPager.addOnPageChangeListener(new androidx.viewpager.widget.ViewPager.OnPageChangeListener() {
            @Override
            public void onPageScrolled(int position, float positionOffset, int positionOffsetPixels) {}
            
            @Override
            public void onPageSelected(int position) {
                MainActivity.this.selectSubTab(position);
            }
            
            @Override
            public void onPageScrollStateChanged(int state) {}
        });
        
        this.btnShowChat.setOnClickListener(v -> this.selectSubTab(0));
        this.btnShowExtensions.setOnClickListener(v -> this.selectSubTab(1));
        this.btnShowFileManager.setOnClickListener(v -> this.selectSubTab(2));
        this.btnShowShell.setOnClickListener(v -> this.selectSubTab(3));
        
        this.mainDesktopContentLayout.addView((View)this.desktopViewPager, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, pagerHeight));

        LinearLayout extensionsSearchRow = new LinearLayout((Context)this);
        extensionsSearchRow.setOrientation(0);
        extensionsSearchRow.setGravity(16);
        LinearLayout.LayoutParams searchRowParams = new LinearLayout.LayoutParams(-1, -2);
        searchRowParams.setMargins(0, this.dp(10), 0, this.dp(10));
        
        this.searchDesktopExtensionsInput = new EditText((Context)this);
        this.searchDesktopExtensionsInput.setHint((CharSequence)"\u641c\u7d22\u7b5b\u9009\u6269\u5c55...");
        this.searchDesktopExtensionsInput.setTextColor(-1);
        this.searchDesktopExtensionsInput.setHintTextColor(Color.rgb((int)148, (int)163, (int)184));
        this.searchDesktopExtensionsInput.setBackgroundColor(Color.rgb((int)15, (int)23, (int)42));
        this.searchDesktopExtensionsInput.setPadding(this.dp(10), this.dp(8), this.dp(10), this.dp(8));
        this.searchDesktopExtensionsInput.setSingleLine(true);
        
        Button btnSearchExtensions = new Button((Context)this);
        btnSearchExtensions.setText((CharSequence)"搜索");
        btnSearchExtensions.setTextColor(-1);
        btnSearchExtensions.setBackgroundColor(Color.rgb(30, 41, 59));
        btnSearchExtensions.setAllCaps(false);
        
        LinearLayout.LayoutParams inputParams = new LinearLayout.LayoutParams(0, -2, 1.0f);
        inputParams.rightMargin = this.dp(8);
        extensionsSearchRow.addView((View)this.searchDesktopExtensionsInput, (ViewGroup.LayoutParams)inputParams);
        extensionsSearchRow.addView((View)btnSearchExtensions, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(70), this.dp(36)));
        extensionsContainer.addView((View)extensionsSearchRow);
        
        this.searchDesktopExtensionsInput.addTextChangedListener(new TextWatcher(){
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {}
            public void onTextChanged(CharSequence s, int start, int before, int count) {
                if (MainActivity.this.currentDesktopExtensions != null) {
                    MainActivity.this.renderExtensions(MainActivity.this.currentDesktopExtensions);
                }
            }
            public void afterTextChanged(Editable s) {}
        });
        btnSearchExtensions.setOnClickListener(v -> {
            if (MainActivity.this.currentDesktopExtensions != null) {
                MainActivity.this.renderExtensions(MainActivity.this.currentDesktopExtensions);
            }
            MainActivity.this.hideKeyboard((View)MainActivity.this.searchDesktopExtensionsInput);
        });
        
        this.extensionList = new LinearLayout((Context)this);
        this.extensionList.setOrientation(1);
        extensionsContainer.addView((View)this.extensionList);
        this.renderCachedExtensions();

        // YanShell UI (Termux-style)
        LinearLayout shellPanel = new LinearLayout((Context)this);
        shellPanel.setOrientation(1);
        shellPanel.setPadding(0, this.dp(8), 0, this.dp(16));
        
        // 顶部小指示栏（macOS 终端窗口风格小圆点 + 标题）
        LinearLayout shellTitleBar = new LinearLayout((Context)this);
        shellTitleBar.setOrientation(0);
        shellTitleBar.setGravity(16);
        shellTitleBar.setPadding(0, 0, 0, this.dp(6));
        
        int[] dotColors = {Color.rgb(239, 68, 68), Color.rgb(245, 158, 11), Color.rgb(34, 197, 94)};
        for (int c : dotColors) {
            View dot = new View((Context)this);
            android.graphics.drawable.GradientDrawable gd = new android.graphics.drawable.GradientDrawable();
            gd.setColor(c);
            gd.setShape(android.graphics.drawable.GradientDrawable.OVAL);
            dot.setBackground(gd);
            LinearLayout.LayoutParams dp = new LinearLayout.LayoutParams(this.dp(8), this.dp(8));
            dp.rightMargin = this.dp(6);
            shellTitleBar.addView(dot, dp);
        }
        
        TextView shellTitle = this.textView("PowerShell", 14, Color.rgb(156, 163, 175), true);
        shellTitle.setTypeface(Typeface.MONOSPACE);
        shellTitleBar.addView(shellTitle);
        shellPanel.addView(shellTitleBar);
        
        // 1. 输出局部 ScrollView (sv) 在上方
        androidx.core.widget.NestedScrollView sv = new androidx.core.widget.NestedScrollView((Context)this);
        sv.setNestedScrollingEnabled(true);
        this.shellScrollView = sv; // 保存成员变量
        this.tvShellOutput = new TextView((Context)this);
        this.tvShellOutput.setBackgroundColor(-16777216); // 纯黑背景
        this.tvShellOutput.setTextColor(-16711936); // 终端绿字
        this.tvShellOutput.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        this.tvShellOutput.setTypeface(Typeface.MONOSPACE);
        this.tvShellOutput.setTextSize(11f);
        this.tvShellOutput.setText((CharSequence)"等待命令输入...");
        this.tvShellOutput.setVisibility(8);
        
        sv.addView((View)this.tvShellOutput);
        LinearLayout.LayoutParams outputParams = new LinearLayout.LayoutParams(-1, this.dp(350));
        shellPanel.addView((View)sv, (ViewGroup.LayoutParams)outputParams);
        
        // 2. 输入行在下方
        LinearLayout shellInputRow = new LinearLayout((Context)this);
        shellInputRow.setOrientation(0);
        shellInputRow.setGravity(16);
        shellInputRow.setPadding(this.dp(4), this.dp(4), this.dp(4), this.dp(4));
        shellInputRow.setBackgroundColor(Color.rgb(15, 23, 42)); // 极深灰底框
        
        TextView tvPrompt = new TextView((Context)this);
        tvPrompt.setText("PS > ");
        tvPrompt.setTextColor(Color.rgb(34, 211, 238)); // 青色高亮提示符
        tvPrompt.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        tvPrompt.setTextSize(13f);
        shellInputRow.addView(tvPrompt);
        
        this.etShellInput = new EditText((Context)this);
        this.etShellInput.setHint((CharSequence)"输入命令...");
        this.etShellInput.setTextColor(-1);
        this.etShellInput.setHintTextColor(Color.rgb(100, 116, 139));
        this.etShellInput.setBackgroundColor(Color.TRANSPARENT);
        this.etShellInput.setPadding(this.dp(6), this.dp(6), this.dp(6), this.dp(6));
        this.etShellInput.setSingleLine(true);
        this.etShellInput.setTypeface(Typeface.MONOSPACE);
        this.etShellInput.setTextSize(13f);
        this.etShellInput.setImeOptions(android.view.inputmethod.EditorInfo.IME_ACTION_SEND);
        
        Button btnRunShell = new Button((Context)this);
        btnRunShell.setText((CharSequence)"执行");
        btnRunShell.setTextColor(-1);
        btnRunShell.setBackgroundColor(Color.rgb(30, 41, 59));
        btnRunShell.setAllCaps(false);
        btnRunShell.setTextSize(11f);
        
        LinearLayout.LayoutParams shellInputParams = new LinearLayout.LayoutParams(0, -2, 1.0f);
        shellInputParams.rightMargin = this.dp(6);
        shellInputRow.addView((View)this.etShellInput, (ViewGroup.LayoutParams)shellInputParams);
        shellInputRow.addView((View)btnRunShell, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(55), this.dp(30)));
        
        LinearLayout.LayoutParams inputRowParams = new LinearLayout.LayoutParams(-1, -2);
        inputRowParams.topMargin = this.dp(6);
        shellPanel.addView((View)shellInputRow, inputRowParams);
        
        // 3. 快捷辅助按键栏 (Extra Keys)
        HorizontalScrollView extraKeysScroll = new HorizontalScrollView((Context)this);
        extraKeysScroll.setHorizontalScrollBarEnabled(false);
        LinearLayout extraKeysLayout = new LinearLayout((Context)this);
        extraKeysLayout.setOrientation(0);
        extraKeysLayout.setGravity(16);
        extraKeysScroll.addView((View)extraKeysLayout);
        
        String[] keys = {"TAB", "Ctrl+C", "↑", "↓", "CLS", "HELP"};
        for (String key : keys) {
            Button kBtn = new Button((Context)this);
            kBtn.setText((CharSequence)key);
            kBtn.setTextColor(Color.rgb(200, 200, 200));
            kBtn.setBackgroundColor(Color.rgb(30, 41, 59));
            kBtn.setAllCaps(false);
            kBtn.setTextSize(11f);
            kBtn.setPadding(this.dp(10), 0, this.dp(10), 0);
            
            LinearLayout.LayoutParams kp = new LinearLayout.LayoutParams(-2, this.dp(28));
            kp.rightMargin = this.dp(6);
            extraKeysLayout.addView((View)kBtn, (ViewGroup.LayoutParams)kp);
            
            if ("TAB".equals(key)) {
                kBtn.setOnClickListener(v -> {
                    String currentText = this.etShellInput.getText().toString();
                    if (currentText.isEmpty()) {
                        this.etShellInput.setText("Get-");
                        this.etShellInput.setSelection(4);
                    } else {
                        int cursor = this.etShellInput.getSelectionStart();
                        this.etShellInput.getText().insert(cursor, " ");
                    }
                });
            } else if ("Ctrl+C".equals(key)) {
                kBtn.setOnClickListener(v -> {
                    this.etShellInput.setText("");
                });
            } else if ("↑".equals(key)) {
                kBtn.setOnClickListener(v -> {
                    if (this.shellHistoryIndex > 0) {
                        this.shellHistoryIndex--;
                        this.etShellInput.setText(this.shellHistory.get(this.shellHistoryIndex));
                        this.etShellInput.setSelection(this.etShellInput.getText().length());
                    }
                });
            } else if ("↓".equals(key)) {
                kBtn.setOnClickListener(v -> {
                    if (this.shellHistoryIndex < this.shellHistory.size() - 1) {
                        this.shellHistoryIndex++;
                        this.etShellInput.setText(this.shellHistory.get(this.shellHistoryIndex));
                        this.etShellInput.setSelection(this.etShellInput.getText().length());
                    } else {
                        this.shellHistoryIndex = this.shellHistory.size();
                        this.etShellInput.setText("");
                    }
                });
            } else if ("CLS".equals(key)) {
                kBtn.setOnClickListener(v -> {
                    this.tvShellOutput.setText("");
                    this.tvShellOutput.setVisibility(8);
                    this.adjustViewPagerHeight();
                });
            } else if ("HELP".equals(key)) {
                kBtn.setOnClickListener(v -> {
                    this.tvShellOutput.setVisibility(0);
                    this.tvShellOutput.setTextColor(Color.rgb(34, 211, 238));
                    String helpText = "PowerShell 常用极客指令备忘：\n" +
                                     "====================================\n" +
                                     "Get-Process  : 查看运行中的进程\n" +
                                     "Get-Service  : 查看系统服务状态\n" +
                                     "Get-Content <文件> : 查看文本内容\n" +
                                     "ls / dir     : 列出当前目录下文件\n" +
                                     "ipconfig     : 查看电脑网卡配置\n" +
                                     "ping <主机>   : 测试网络连通性\n" +
                                     "Get-Date     : 获取当前系统时间\n" +
                                     "====================================\n" +
                                     "提示：你也可以在输入框输入任何命令后直接按软键盘发送键执行！";
                    this.tvShellOutput.setText(helpText);
                    this.adjustViewPagerHeight();
                    this.shellScrollView.post(() -> this.shellScrollView.fullScroll(android.view.View.FOCUS_DOWN));
                });
            }
        }
        
        LinearLayout.LayoutParams ekParams = new LinearLayout.LayoutParams(-1, -2);
        ekParams.topMargin = this.dp(6);
        shellPanel.addView((View)extraKeysScroll, (ViewGroup.LayoutParams)ekParams);
        shellContainer.addView((View)shellPanel);
        
        // 软键盘 Enter 直接执行
        this.etShellInput.setOnEditorActionListener((v, actionId, event) -> {
            if (actionId == android.view.inputmethod.EditorInfo.IME_ACTION_SEND || 
                actionId == android.view.inputmethod.EditorInfo.IME_ACTION_DONE) {
                btnRunShell.performClick();
                return true;
            }
            return false;
        });
        
        btnRunShell.setOnClickListener(v -> {
            String cmd = this.etShellInput.getText().toString().trim();
            if (cmd.isEmpty()) return;
            
            // 记录命令历史
            if (this.shellHistory.isEmpty() || !this.shellHistory.get(this.shellHistory.size() - 1).equals(cmd)) {
                this.shellHistory.add(cmd);
            }
            this.shellHistoryIndex = this.shellHistory.size();
            
            this.tvShellOutput.setVisibility(0);
            this.tvShellOutput.setTextColor(-256);
            this.tvShellOutput.setText((CharSequence)("正在执行命令...\n> " + cmd));
            this.adjustViewPagerHeight();
            this.shellScrollView.post(() -> this.shellScrollView.fullScroll(android.view.View.FOCUS_DOWN));
            
            this.executor.execute(() -> {
                try {
                    String baseUrl = this.normalizedBaseUrl();
                    String token = this.requireToken();
                    JSONObject payload = new JSONObject().put("command", (Object)cmd);
                    JSONObject res = YanziApiClient.postJson(baseUrl, "/v1/shell/run", payload, token, "执行命令");
                    String output = res.optString("output", "");
                    int exitCode = res.optInt("exitCode", 0);
                    this.runOnUiThread(() -> {
                        this.tvShellOutput.setTextColor(exitCode == 0 ? -16711936 : -65536);
                        this.tvShellOutput.setText((CharSequence)(output.isEmpty() ? "命令执行完毕，无输出内容。" : output));
                        this.adjustViewPagerHeight();
                        this.shellScrollView.post(() -> this.shellScrollView.fullScroll(android.view.View.FOCUS_DOWN));
                    });
                } catch (Exception ex) {
                    this.runOnUiThread(() -> {
                        this.tvShellOutput.setTextColor(-65536);
                        this.tvShellOutput.setText((CharSequence)("\u547d\u4ee4\u6267\u884c\u5931\u8d25: " + ex.getMessage()));
                        this.adjustViewPagerHeight();
                    });
                }
            });
        });

        // YanPath UI
        LinearLayout fsPanel = new LinearLayout((Context)this);
        fsPanel.setOrientation(1);
        fsPanel.addView((View)this.textView("文件管理 (YanPath)", 16, -1, true));
        
        LinearLayout pathRow = new LinearLayout((Context)this);
        pathRow.setOrientation(0);
        pathRow.setGravity(16);
        pathRow.setPadding(0, this.dp(4), 0, this.dp(8));
        
        Button btnRoot = new Button((Context)this);
        btnRoot.setText((CharSequence)"根");
        btnRoot.setTextColor(-1);
        btnRoot.setBackgroundColor(Color.rgb(30, 41, 59));
        btnRoot.setAllCaps(false);
        btnRoot.setTextSize(12f);
        
        Button btnBack = new Button((Context)this);
        btnBack.setText((CharSequence)"返回");
        btnBack.setTextColor(-1);
        btnBack.setBackgroundColor(Color.rgb(30, 41, 59));
        btnBack.setAllCaps(false);
        btnBack.setTextSize(12f);
        
        pathRow.addView((View)btnRoot, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(45), this.dp(32)));
        LinearLayout.LayoutParams backParams = new LinearLayout.LayoutParams(this.dp(60), this.dp(32));
        backParams.leftMargin = this.dp(6);
        backParams.rightMargin = this.dp(6);
        pathRow.addView((View)btnBack, (ViewGroup.LayoutParams)backParams);
        
        this.breadcrumbsScrollView = new HorizontalScrollView((Context)this);
        this.breadcrumbsScrollView.setHorizontalScrollBarEnabled(false);
        this.breadcrumbsLayout = new LinearLayout((Context)this);
        this.breadcrumbsLayout.setOrientation(0);
        this.breadcrumbsLayout.setGravity(16);
        this.breadcrumbsScrollView.addView((View)this.breadcrumbsLayout, new ViewGroup.LayoutParams(-2, -1));
        
        pathRow.addView((View)this.breadcrumbsScrollView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        fsPanel.addView((View)pathRow);
        
        LinearLayout fsSearchRow = new LinearLayout((Context)this);
        fsSearchRow.setOrientation(0);
        fsSearchRow.setGravity(16);
        LinearLayout.LayoutParams fsSearchParams = new LinearLayout.LayoutParams(-1, -2);
        fsSearchParams.setMargins(0, this.dp(6), 0, this.dp(6));
        
        this.fsSearchInput = new EditText((Context)this);
        this.fsSearchInput.setHint((CharSequence)"输入关键字筛选当前目录...");
        this.fsSearchInput.setTextColor(-1);
        this.fsSearchInput.setHintTextColor(Color.rgb(100, 116, 139));
        this.fsSearchInput.setBackgroundColor(Color.rgb(15, 23, 42));
        this.fsSearchInput.setPadding(this.dp(10), this.dp(8), this.dp(10), this.dp(8));
        this.fsSearchInput.setSingleLine(true);
        this.fsSearchInput.setTextSize(13f);
        
        Button btnFsSearch = new Button((Context)this);
        btnFsSearch.setText((CharSequence)"搜索");
        btnFsSearch.setTextColor(-1);
        btnFsSearch.setBackgroundColor(Color.rgb(30, 41, 59));
        btnFsSearch.setAllCaps(false);
        
        LinearLayout.LayoutParams fsInputParams = new LinearLayout.LayoutParams(0, -2, 1.0f);
        fsInputParams.rightMargin = this.dp(8);
        fsSearchRow.addView((View)this.fsSearchInput, (ViewGroup.LayoutParams)fsInputParams);
        fsSearchRow.addView((View)btnFsSearch, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(this.dp(70), this.dp(36)));
        fsPanel.addView((View)fsSearchRow);
        
        this.fsSearchInput.addTextChangedListener(new android.text.TextWatcher() {
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {}
            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
                MainActivity.this.filterFsList(s.toString());
            }
            @Override
            public void afterTextChanged(android.text.Editable s) {}
        });
        btnFsSearch.setOnClickListener(v -> {
            MainActivity.this.filterFsList(this.fsSearchInput.getText().toString());
            this.hideKeyboard((View)this.fsSearchInput);
        });
        
        LinearLayout uploadRow = new LinearLayout((Context)this);
        uploadRow.setOrientation(0);
        uploadRow.setGravity(16);
        uploadRow.setPadding(0, this.dp(4), 0, this.dp(8));
        
        Button btnUploadFile = new Button((Context)this);
        btnUploadFile.setText((CharSequence)"上传文件");
        btnUploadFile.setTextColor(-1);
        btnUploadFile.setBackgroundColor(Color.rgb(30, 41, 59));
        btnUploadFile.setAllCaps(false);
        btnUploadFile.setTextSize(11f);
        
        Button btnUploadPhoto = new Button((Context)this);
        btnUploadPhoto.setText((CharSequence)"上传照片");
        btnUploadPhoto.setTextColor(-1);
        btnUploadPhoto.setBackgroundColor(Color.rgb(30, 41, 59));
        btnUploadPhoto.setAllCaps(false);
        btnUploadPhoto.setTextSize(11f);
        
        Button btnCamera = new Button((Context)this);
        btnCamera.setText((CharSequence)"拍照");
        btnCamera.setTextColor(-1);
        btnCamera.setBackgroundColor(Color.rgb(30, 41, 59));
        btnCamera.setAllCaps(false);
        btnCamera.setTextSize(11f);
        
        LinearLayout.LayoutParams btnUploadParams = new LinearLayout.LayoutParams(0, this.dp(34), 1.0f);
        btnUploadParams.rightMargin = this.dp(6);
        uploadRow.addView((View)btnUploadFile, (ViewGroup.LayoutParams)btnUploadParams);
        uploadRow.addView((View)btnUploadPhoto, (ViewGroup.LayoutParams)btnUploadParams);
        btnUploadParams.rightMargin = 0;
        uploadRow.addView((View)btnCamera, (ViewGroup.LayoutParams)btnUploadParams);
        fsPanel.addView((View)uploadRow);
        
        btnUploadFile.setOnClickListener(v -> this.startFsUploadFile());
        btnUploadPhoto.setOnClickListener(v -> this.startFsUploadPhoto());
        btnCamera.setOnClickListener(v -> this.startFsTakePhoto());
        
        this.fileListLayout = new LinearLayout((Context)this);
        this.fileListLayout.setOrientation(1);
        fsPanel.addView((View)this.fileListLayout);
        fileManagerContainer.addView((View)fsPanel);
        
        btnRoot.setOnClickListener(v -> this.loadFileList(""));
        btnBack.setOnClickListener(v -> {
            if (this.currentPath == null || this.currentPath.isEmpty()) return;
            java.io.File file = new java.io.File(this.currentPath);
            String parent = file.getParent();
            if (parent == null) {
                this.loadFileList("");
            } else {
                this.loadFileList(parent);
            }
        });

        this.baseUrlInput = this.input("\u4e91\u7aef\u5730\u5740", this.prefs.getString("baseUrl", DEFAULT_BASE_URL));
        this.emailInput = this.input("\u90ae\u7bb1", this.prefs.getString("email", ""));
        this.passwordInput = this.input("\u5bc6\u7801", this.prefs.getString("password", ""));
        this.passwordInput.setInputType(129);
        String initialText = sharedText == null || sharedText.trim().isEmpty() ? "hi" : sharedText;
        this.textInput = this.multiInput("\u53d1\u9001\u7ed9\u7535\u8111\u7684\u6587\u672c / \u94fe\u63a5", initialText);
        this.loginButton = this.button("\u767b\u5f55");
        this.loginButton.setOnClickListener(v -> this.loginAndRegister());
        this.statusText = this.textView("", 14, Color.rgb((int)147, (int)197, (int)253), false);
        this.statusText.setTextIsSelectable(true);
        this.statusText.setMinLines(3);
        this.overlayButton = this.button(FloatingWheelService.isRunning ? "\u5173\u95ed\u60ac\u6d6e\u8f6e\u76d8" : "\u6253\u5f00\u60ac\u6d6e\u8f6e\u76d8");
        Button accessibilityButton = this.button("\u65e0\u969c\u788d\u670d\u52a1");

        this.setupProfileHeader();
        boolean autoUpdate = this.prefs.getBoolean("auto_cloud_update", false);
        LinearLayout itemCloud = this.createSwitchListItem("\u542f\u52a8\u65f6\u81ea\u52a8\u540c\u6b65\u71d5\u5e55", autoUpdate, (buttonView, isChecked) -> {
            this.prefs.edit().putBoolean("auto_cloud_update", isChecked).apply();
            this.setStatus(isChecked ? "\u5df2\u542f\u7528\u542f\u52a8\u65f6\u81ea\u52a8\u540c\u6b65" : "\u5df2\u5173\u95ed\u542f\u52a8\u65f6\u81ea\u52a8\u540c\u6b65");
        });
        
        LinearLayout group1 = this.createListGroup(itemCloud);
        this.profileTabPage.addView((View)group1);
        
        boolean wheelEnabled = this.prefs.getBoolean("floatingWheelEnabled", true);
        LinearLayout itemWheel = this.createSwitchListItem("\u60ac\u6d6e\u8f6e\u76d8", wheelEnabled, (buttonView, isChecked) -> {
            this.prefs.edit().putBoolean("floatingWheelEnabled", isChecked).apply();
            this.startService(new Intent((Context)this, FloatingWheelService.class));
            if (isChecked) {
                this.startFloatingWheel();
                this.setStatus("\u60ac\u6d6e\u8f6e\u76d8\u5df2\u5f00\u542f\u3002");
                this.overlayButton.setText((CharSequence)"\u5173\u95ed\u60ac\u6d6e\u8f6e\u76d8");
            } else {
                this.setStatus("\u60ac\u6d6e\u8f6e\u76d8\u5df2\u5173\u95ed\u3002");
                this.overlayButton.setText((CharSequence)"\u6253\u5f00\u60ac\u6d6e\u8f6e\u76d8");
            }
        });
        
        LinearLayout itemAccessibility = this.createListItem("\u65e0\u969c\u788d\u670d\u52a1", null, () -> this.openAccessibilitySettings());
        
        String currentVer = "0.2.17";
        try {
            currentVer = this.getPackageManager().getPackageInfo(this.getPackageName(), 0).versionName;
        } catch (Exception ignored) {}
        LinearLayout itemCheckUpdate = this.createListItem("\u68c0\u67e5\u66f4\u65b0", "v" + currentVer, () -> {
            UpdateManager.checkUpdate(MainActivity.this, true);
        });
        
        LinearLayout group2 = this.createListGroup(itemWheel, itemAccessibility, itemCheckUpdate);
        this.profileTabPage.addView((View)group2);

        LinearLayout runLogPanel = new LinearLayout((Context)this);
        runLogPanel.setOrientation(LinearLayout.VERTICAL);
        runLogPanel.setPadding(0, this.dp(16), 0, 0);
        
        LinearLayout runLogHeader = new LinearLayout((Context)this);
        runLogHeader.setOrientation(LinearLayout.HORIZONTAL);
        runLogHeader.setGravity(Gravity.CENTER_VERTICAL);
        runLogHeader.setPadding(0, 0, 0, this.dp(8));
        
        TextView runLogTitle = this.textView("\u8fd0\u884c\u65e5\u5fd7", 16, ThemeConfig.COLOR_TEXT_PRIMARY, true);
        runLogHeader.addView((View)runLogTitle, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        
        Button btnCopyRunLog = this.button("\u590d\u5236");
        btnCopyRunLog.setTextSize(12f);
        btnCopyRunLog.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
        btnCopyRunLog.setOnClickListener(v -> {
            this.copyDiagnostics();
            Toast.makeText(this.getApplicationContext(), "\u65e5\u5fd7\u5df2\u590d\u5236", Toast.LENGTH_SHORT).show();
        });
        
        Button btnClearRunLog = this.button("\u6e05\u7a7a");
        btnClearRunLog.setTextSize(12f);
        btnClearRunLog.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
        btnClearRunLog.setOnClickListener(v -> {
            this.diagnosticLog.setLength(0);
            MobileDiagnostics.clear((Context)this);
            this.statusText.setText((CharSequence)"");
            this.setStatus("\u65e5\u5fd7\u5df2\u6e05\u7a7a\u3002");
            Toast.makeText(this.getApplicationContext(), "\u65e5\u5fd7\u5df2\u6e05\u7a7a", Toast.LENGTH_SHORT).show();
        });
        
        LinearLayout.LayoutParams runBtnLp = new LinearLayout.LayoutParams(this.dp(60), this.dp(32));
        runBtnLp.leftMargin = this.dp(8);
        runLogHeader.addView((View)btnCopyRunLog, (ViewGroup.LayoutParams)runBtnLp);
        runLogHeader.addView((View)btnClearRunLog, (ViewGroup.LayoutParams)runBtnLp);
        runLogPanel.addView((View)runLogHeader);
        
        androidx.core.widget.NestedScrollView runLogScroll = new androidx.core.widget.NestedScrollView((Context)this);
        runLogScroll.setBackgroundColor(ThemeConfig.COLOR_BACKGROUND);
        runLogScroll.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        
        GradientDrawable gdRunLog = new GradientDrawable();
        gdRunLog.setColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        gdRunLog.setCornerRadius((float)this.dp(8));
        runLogScroll.setBackground((Drawable)gdRunLog);
        
        this.statusText.setTextSize(11f);
        this.statusText.setTextColor(ThemeConfig.COLOR_TEXT_SECONDARY);
        this.statusText.setTypeface(Typeface.MONOSPACE);
        this.statusText.setText((CharSequence)this.diagnosticLog.toString());
        runLogScroll.addView((View)this.statusText);
        
        runLogPanel.addView((View)runLogScroll, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(150)));
        this.profileTabPage.addView((View)runLogPanel);
        
        TextView tvAbout = new TextView((Context)this);
        tvAbout.setTextSize(12f);
        tvAbout.setTextColor(Color.rgb(100, 116, 139));
        tvAbout.setGravity(Gravity.CENTER);
        
        long installTime = 0L;
        try {
            installTime = this.getPackageManager().getPackageInfo((String)this.getPackageName(), (int)0).lastUpdateTime;
        } catch (Exception e) {}
        String timeStr = "";
        if (installTime > 0L) {
            timeStr = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault()).format(new Date(installTime));
        }
        String aboutText = "\u8bbe\u5907 ID: " + this.deviceId + "\n\u7f16\u8bd1\u5b89\u88c5: " + timeStr;
        tvAbout.setText((CharSequence)aboutText);
        tvAbout.setPadding(0, this.dp(16), 0, this.dp(24));
        this.profileTabPage.addView((View)tvAbout);
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
        new android.os.Handler(android.os.Looper.getMainLooper()).postDelayed(() -> {
            UpdateManager.checkUpdate(MainActivity.this, false);
        }, 2000L);
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
        tabs.setBackgroundColor(ThemeConfig.COLOR_BACKGROUND);
        this.yanmTabButton = this.tabButton("燕幕", "dashboard", "yanm");
        this.mobileExtensionTabButton = this.tabButton("手机", "cellphone", "mobile");
        this.aiTabButton = this.tabButton("AI", "chat", "ai");
        this.desktopExtensionTabButton = this.tabButton("电脑", "laptop", "desktop");
        this.profileTabButton = this.tabButton("我的", "account", "profile");
        tabs.addView(this.yanmTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.mobileExtensionTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.aiTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.desktopExtensionTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        tabs.addView(this.profileTabButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -1, 1.0f));
        return tabs;
    }

    private View tabButton(String text, String iconName, String key) {
        LinearLayout container = new LinearLayout((Context)this);
        container.setOrientation(1);
        container.setGravity(17);
        container.setPadding(0, this.dp(6), 0, this.dp(6));
        container.setClickable(true);
        container.setFocusable(true);
        ImageView iconView = new ImageView((Context)this);
        Path path = MobileIconLibrary.resolveOrDefault(iconName);
        iconView.setImageDrawable(new PathDrawable(path, Color.WHITE));
        LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(this.dp(22), this.dp(22));
        iconView.setLayoutParams((ViewGroup.LayoutParams)iconParams);
        TextView textView = new TextView((Context)this);
        textView.setText((CharSequence)text);
        textView.setTextSize(10.0f);
        textView.setGravity(17);
        LinearLayout.LayoutParams textParams = new LinearLayout.LayoutParams(-2, -2);
        textParams.setMargins(0, this.dp(3), 0, 0);
        textView.setLayoutParams((ViewGroup.LayoutParams)textParams);
        if ("desktop".equals(key)) {
            FrameLayout iconWrapper = new FrameLayout((Context)this);
            iconWrapper.setLayoutParams(new LinearLayout.LayoutParams(-2, -2));
            iconWrapper.addView((View)iconView);
            View dot = new View((Context)this);
            FrameLayout.LayoutParams dotParams = new FrameLayout.LayoutParams(this.dp(7), this.dp(7));
            dotParams.gravity = 53;
            dot.setLayoutParams((ViewGroup.LayoutParams)dotParams);
            GradientDrawable dotBg = new GradientDrawable();
            dotBg.setShape(GradientDrawable.OVAL);
            dotBg.setColor(Color.rgb(34, 197, 94));
            dot.setBackground((Drawable)dotBg);
            dot.setVisibility(View.GONE);
            iconWrapper.addView(dot);
            this.desktopConnectionDot = dot;
            container.addView((View)iconWrapper);
        } else {
            container.addView((View)iconView);
        }
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
        if (isDesktop) {
            this.checkConnectionAsync();
        }
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
        
        // 1. 手动调整卡片
        LinearLayout helperPanel = this.card();
        LinearLayout.LayoutParams helperParams = new LinearLayout.LayoutParams(-1, -2);
        helperParams.setMargins(0, this.dp(8), 0, this.dp(8));
        helperPanel.setLayoutParams((ViewGroup.LayoutParams)helperParams);
        this.mobileExtensionIdInput = this.input("\u6269\u5c55 ID", "mobile-copy-shared-text");
        this.mobileExtensionNameInput = this.input("\u6269\u5c55\u540d\u79f0", "\u590d\u5236\u5f53\u524d\u8f93\u5165");
        this.mobileExtensionIconInput = this.input("\u56fe\u6807", "mdi:content-copy");
        this.mobileExtensionDescriptionInput = this.multiInput("\u63cf\u8ff0", "\u628a\u5f53\u524d\u8f93\u5165\u6846\u5185\u5bb9\u590d\u5236\u5230\u624b\u673a\u526a\u8d34\u677f\u3002");
        this.mobileExtensionDescriptionInput.setMinLines(3);
        helperPanel.addView((View)this.mobileExtensionIdInput);
        helperPanel.addView((View)this.mobileExtensionNameInput);
        helperPanel.addView((View)this.mobileExtensionIconInput);
        helperPanel.addView((View)this.mobileExtensionDescriptionInput);
        Button saveDraftButton = this.button("\u4fdd\u5b58\u6269\u5c55");
        helperPanel.addView((View)saveDraftButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(42)));
        root.addView((View)helperPanel);
        
        // 2. JSON 区卡片
        LinearLayout codePanel = this.card();
        LinearLayout.LayoutParams codeParams = new LinearLayout.LayoutParams(-1, -2);
        codeParams.setMargins(0, this.dp(8), 0, this.dp(8));
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
        root.addView((View)codePanel);
        
        // 3. 模板示例卡片（默认折叠）
        LinearLayout templatePanel = this.card();
        LinearLayout.LayoutParams templateParams = new LinearLayout.LayoutParams(-1, -2);
        templateParams.setMargins(0, this.dp(8), 0, this.dp(8));
        templatePanel.setLayoutParams((ViewGroup.LayoutParams)templateParams);
        TextView templateHeader = this.textView("\u6a21\u677f\u793a\u4f8b (\u70b9\u51fb\u5c55\u5f00 \u25bd)", 15, -1, true);
        templateHeader.setPadding(0, this.dp(10), 0, this.dp(10));
        templatePanel.addView((View)templateHeader);
        LinearLayout templateContainer = new LinearLayout((Context)this);
        templateContainer.setOrientation(1);
        templateContainer.setVisibility(View.GONE);
        templateContainer.addView((View)this.textView("\u672c\u673a\u80fd\u529b\u4f18\u5148\uff1a\u526a\u8d34\u677f\u3001\u6d4f\u89c8\u5668\u3001\u6587\u4ef6\u3001\u7f51\u7edc\u8bf7\u6c42\u3002", 12, Color.rgb((int)103, (int)232, (int)249), false));
        for (MobileExtensionTemplate template : this.buildMobileExtensionTemplates()) {
            Button templateButton = this.button(template.name);
            templateButton.setAllCaps(false);
            templateButton.setOnClickListener(v -> this.replaceDraftWithTemplate(template));
            templateContainer.addView((View)templateButton, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(42)));
            templateContainer.addView((View)this.textView(template.description, 11, Color.rgb((int)148, (int)163, (int)184), false));
        }
        templateHeader.setOnClickListener(v -> {
            if (templateContainer.getVisibility() == View.GONE) {
                templateContainer.setVisibility(View.VISIBLE);
                templateHeader.setText("\u6a21\u677f\u793a\u4f8b (\u70b9\u51fb\u6298\u53e0 \u25b3)");
            } else {
                templateContainer.setVisibility(View.GONE);
                templateHeader.setText("\u6a21\u677f\u793a\u4f8b (\u70b9\u51fb\u5c55\u5f00 \u25bd)");
            }
        });
        templatePanel.addView((View)templateContainer);
        root.addView((View)templatePanel);
        
        promptButton.setOnClickListener(v -> this.copyMobileExtensionPrompt());
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
            String inputId = this.mobileExtensionIdInput.getText().toString().trim();
            String inputName = this.mobileExtensionNameInput.getText().toString().trim();
            String inputIcon = this.mobileExtensionIconInput.getText().toString().trim();
            String inputDesc = this.mobileExtensionDescriptionInput.getText().toString().trim();

            if (draft.trim().startsWith("{") && draft.trim().endsWith("}")) {
                JSONObject json = new JSONObject(draft);
                if (!inputId.isEmpty()) json.put("id", inputId);
                if (!inputName.isEmpty()) {
                    json.put("name", inputName);
                    json.put("displayName", inputName);
                }
                if (!inputIcon.isEmpty()) json.put("icon", inputIcon);
                if (!inputDesc.isEmpty()) json.put("description", inputDesc);
                draft = json.toString(2);
                this.mobileExtensionInput.setText(draft);
            }

            String id = MainActivity.firstNonEmpty(inputId, "mobile-extension-draft");
            String name = MainActivity.firstNonEmpty(inputName, "\u624b\u673a\u6269\u5c55\u8349\u7a3f");
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
            String inputId = this.mobileExtensionIdInput.getText().toString().trim();
            String inputName = this.mobileExtensionNameInput.getText().toString().trim();
            String inputIcon = this.mobileExtensionIconInput.getText().toString().trim();
            String inputDesc = this.mobileExtensionDescriptionInput.getText().toString().trim();

            if (draft.trim().startsWith("{") && draft.trim().endsWith("}")) {
                JSONObject json = new JSONObject(draft);
                if (!inputId.isEmpty()) json.put("id", inputId);
                if (!inputName.isEmpty()) {
                    json.put("name", inputName);
                    json.put("displayName", inputName);
                }
                if (!inputIcon.isEmpty()) json.put("icon", inputIcon);
                if (!inputDesc.isEmpty()) json.put("description", inputDesc);
                draft = json.toString(2);
                this.mobileExtensionInput.setText(draft);
            }

            this.prefs.edit().putString("mobileExtensionDraft", draft).apply();
            this.updateMobileExtensionFieldsFromDraft();
            String source = MainActivity.extractMobileScriptSource(draft);
            if (source.trim().isEmpty()) {
                throw new IllegalStateException("\u811a\u672c\u4e3a\u7a7a\u3002");
            }
            this.updateMobileScriptResult("\u6b63\u5728\u6d4b\u8bd5...", false);
            this.activeMobileScriptRunner = runner = new WebView((Context)this);
            runner.getSettings().setJavaScriptEnabled(true);
            runner.addJavascriptInterface((Object)new MobileJsBridge(), "yanziMobileJsHost");
            String html = this.buildMobileScriptHtml(source);
            runner.loadDataWithBaseURL("http://localhost/", html, "text/html", "UTF-8", null);
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
        this.pushMobileExtensionsToCloud();
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
        this.pushMobileExtensionsToCloud();
    }

    private void renderLocalMobileExtensions() {
        if (this.mobileExtensionGrid == null) {
            return;
        }
        this.mobileExtensionGrid.removeAllViews();
        
        JSONArray array = this.readLocalMobileExtensions();
        if (array.length() == 0) {
            TextView emptyTv = this.textView("\u6682\u65e0\u672c\u673a\u6269\u5c55\u3002", 12, Color.rgb((int)148, (int)163, (int)184), false);
            emptyTv.setPadding(this.dp(16), this.dp(16), this.dp(16), this.dp(16));
            this.mobileExtensionGrid.addView((View)emptyTv);
            return;
        }
        
        for (int i = 0; i < array.length(); ++i) {
            JSONObject item = array.optJSONObject(i);
            if (item == null) continue;
            String id = item.optString("id");
            String name = MainActivity.firstNonEmpty(item.optString("name"), item.optString("displayName"), id);
            
            LinearLayout card = new LinearLayout((Context)this);
            card.setOrientation(1);
            card.setGravity(17);
            
            GridLayout.LayoutParams params = new GridLayout.LayoutParams();
            params.width = this.dp(80);
            params.height = this.dp(110);
            params.setMargins(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
            card.setLayoutParams((ViewGroup.LayoutParams)params);
            
            String iconName = item.optString("icon", "mdi:play");
            if (iconName.startsWith("mdi:")) {
                iconName = iconName.substring(4);
            }
            android.graphics.Path path = MobileIconLibrary.resolveOrDefault(iconName);
            
            LinearLayout iconLayout = new LinearLayout((Context)this);
            iconLayout.setGravity(17);
            GradientDrawable iconBg = new GradientDrawable();
            iconBg.setShape(GradientDrawable.OVAL);
            
            int colorIndex = Math.abs(id.hashCode()) % 5;
            int iconBgColor = Color.rgb(59, 130, 246);
            if (colorIndex == 1) iconBgColor = Color.rgb(16, 185, 129);
            else if (colorIndex == 2) iconBgColor = Color.rgb(239, 68, 68);
            else if (colorIndex == 3) iconBgColor = Color.rgb(245, 158, 11);
            else if (colorIndex == 4) iconBgColor = Color.rgb(139, 92, 246);
            
            iconBg.setColor(iconBgColor);
            iconLayout.setBackground((Drawable)iconBg);
            
            LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(this.dp(52), this.dp(52));
            iconParams.bottomMargin = this.dp(6);
            iconLayout.setLayoutParams((ViewGroup.LayoutParams)iconParams);
            
            ImageView iconImg = new ImageView((Context)this);
            iconImg.setImageDrawable((Drawable)new PathDrawable(path, Color.WHITE));
            iconImg.setPadding(this.dp(13), this.dp(13), this.dp(13), this.dp(13));
            iconLayout.addView((View)iconImg, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -1));
            
            TextView nameTv = new TextView((Context)this);
            nameTv.setText((CharSequence)name);
            nameTv.setTextColor(Color.WHITE);
            nameTv.setTextSize(2, 12.0f);
            nameTv.setGravity(17);
            nameTv.setSingleLine(true);
            nameTv.setEllipsize(android.text.TextUtils.TruncateAt.END);
            nameTv.setPadding(this.dp(4), 0, this.dp(4), 0);
            
            card.addView((View)iconLayout);
            card.addView((View)nameTv);
            
            card.setOnClickListener(v -> {
                String code = item.optString("code");
                if (code == null || code.isEmpty()) {
                    JSONObject script = item.optJSONObject("script");
                    if (script != null) {
                        code = script.optString("source");
                    }
                }
                if (code != null && !code.isEmpty()) {
                    this.setStatus("\u6b63\u5728\u6267\u884c\u6269\u5c55\uff1a" + name);
                    this.executeMobileScriptHeadless(code, name, result -> {
                        this.setStatus("\u6269\u5c55\u6267\u884c\u7ed3\u679c\uff1a" + result);
                    });
                } else {
                    this.setStatus("\u6269\u5c55\u65e0\u53ef\u6267\u884c\u4ee3\u7801");
                }
            });
            
            card.setOnLongClickListener(v -> {
                PopupMenu popup = new PopupMenu((Context)this, (View)card);
                popup.getMenu().add(0, 1, 0, (CharSequence)"\u6267\u884c");
                popup.getMenu().add(0, 2, 1, (CharSequence)"\u7f16\u8f91");
                popup.getMenu().add(0, 3, 2, (CharSequence)"\u5220\u9664");
                popup.getMenu().add(0, 4, 3, (CharSequence)"\u6dfb\u52a0\u5230\u684c\u9762"); // "添加到桌面"
                popup.getMenu().add(0, 5, 4, (CharSequence)"\u6dfb\u52a0\u5230\u71d5\u73af"); // "添加到燕环"
                popup.setOnMenuItemClickListener(menuItem -> {
                    if (menuItem.getItemId() == 1) {
                        card.performClick();
                    } else if (menuItem.getItemId() == 2) {
                        String pretty = item.toString();
                        try { pretty = item.toString(2); } catch (Exception e) {}
                        this.mobileExtensionInput.setText((CharSequence)pretty);
                        this.updateMobileExtensionFieldsFromDraft();
                        this.isEditingMobileExtension = true;
                        if (this.mobileViewPager != null) this.mobileViewPager.setVisibility(View.GONE);
                        if (this.mobileSubTabBar != null) this.mobileSubTabBar.setVisibility(View.GONE);
                        if (this.mobileExtensionEditorView != null) this.mobileExtensionEditorView.setVisibility(View.VISIBLE);
                        this.scrollToView((View)this.mobileExtensionSectionTitle);
                        this.setStatus("\u6b63\u5728\u7f16\u8f91\u6269\u5c55\uff1a" + name);
                    } else if (menuItem.getItemId() == 3) {
                        this.deleteLocalMobileExtension(id);
                    } else if (menuItem.getItemId() == 4) {
                        this.createLocalMobileExtensionShortcut(id, name, item.optString("icon", "mdi:play"));
                    } else if (menuItem.getItemId() == 5) {
                        this.addLocalMobileExtensionToWheel(id, name);
                    }
                    return true;
                });
                popup.show();
                return true;
            });
            
            this.mobileExtensionGrid.addView((View)card);
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
            String token = "";
            String username = "";
            String baseUrl = this.normalizedBaseUrl();
            String email = this.emailInput.getText().toString().trim();
            try {
                JSONObject loginRes = YanziApiClient.loginResponse(baseUrl, email, this.passwordInput.getText().toString());
                token = loginRes.getString("accessToken");
                username = loginRes.optString("username", "");
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
            final String finalToken = token;
            final String finalUsername = username;
            this.runOnUiThread(() -> this.setStatus("\u767b\u5f55\u6210\u529f\uff0c\u6b63\u5728\u6ce8\u518c\u624b\u673a\u8bbe\u5907..."));
            try {
                YanziApiClient.registerDevice(baseUrl, finalToken, this.deviceId, this.buildDeviceName());
                this.prefs.edit().putString("baseUrl", baseUrl).putString("email", email).putString("password", this.passwordInput.getText().toString()).putString("token", finalToken).putString("username", finalUsername).apply();
                this.runOnUiThread(() -> {
                    this.setStatus("\u767b\u5f55\u6210\u529f\uff0c\u8bbe\u5907\u5df2\u6ce8\u518c\u3002");
                    if (this.loginButton != null) {
                        this.loginButton.setEnabled(true);
                    }
                    this.refreshExtensions();
                    this.refreshYanm();
                    this.updateProfileHeader();
                    if (MainActivity.this.accountDialog != null) {
                        MainActivity.this.accountDialog.dismiss();
                        MainActivity.this.accountDialog = null;
                    }
                });
            }
            catch (Exception ex) {
                this.prefs.edit().putString("baseUrl", baseUrl).putString("email", email).putString("password", this.passwordInput.getText().toString()).putString("token", finalToken).putString("username", finalUsername).apply();
                this.runOnUiThread(() -> {
                    this.setStatus("\u767b\u5f55\u6210\u529f\uff0c\u4f46\u8bbe\u5907\u6ce8\u518c\u5931\u8d25\uff1a" + ex.getMessage());
                    if (this.loginButton != null) {
                        this.loginButton.setEnabled(true);
                    }
                    this.updateProfileHeader();
                    if (MainActivity.this.accountDialog != null) {
                        MainActivity.this.accountDialog.dismiss();
                        MainActivity.this.accountDialog = null;
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
                LanDiscoveryManager.discoverNow((Context)this);
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
        this.adjustViewPagerHeight();
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
                    MobileDiagnostics.append((Context)this, "读取云端燕幕状态成功");
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
                    MobileDiagnostics.append((Context)this, "读取云端燕幕状态失败：" + ex.getMessage());
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

    @Override
    public boolean dispatchTouchEvent(android.view.MotionEvent ev) {
        if (this.tabGestureDetector != null) {
            this.tabGestureDetector.onTouchEvent(ev);
        }
        return super.dispatchTouchEvent(ev);
    }

    private void selectSubTab(int index) {
        if (index < 0 || index > 3) return;
        this.currentSubTabIndex = index;
        
        this.runOnUiThread(() -> {
            if (this.desktopViewPager != null && this.desktopViewPager.getCurrentItem() != index) {
                this.desktopViewPager.setCurrentItem(index, true);
            }
            
            android.graphics.drawable.GradientDrawable activeBg = new android.graphics.drawable.GradientDrawable();
            activeBg.setCornerRadius((float)this.dp(8));
            activeBg.setColor(Color.argb(20, 34, 211, 238));
            
            if (this.btnShowChat != null) {
                this.btnShowChat.setTextColor(Color.rgb(148, 163, 184));
                this.btnShowChat.setBackgroundColor(Color.TRANSPARENT);
            }
            if (this.btnShowExtensions != null) {
                this.btnShowExtensions.setTextColor(Color.rgb(148, 163, 184));
                this.btnShowExtensions.setBackgroundColor(Color.TRANSPARENT);
            }
            if (this.btnShowFileManager != null) {
                this.btnShowFileManager.setTextColor(Color.rgb(148, 163, 184));
                this.btnShowFileManager.setBackgroundColor(Color.TRANSPARENT);
            }
            if (this.btnShowShell != null) {
                this.btnShowShell.setTextColor(Color.rgb(148, 163, 184));
                this.btnShowShell.setBackgroundColor(Color.TRANSPARENT);
            }
            
            if (index == 0) {
                if (this.btnShowChat != null) {
                    this.btnShowChat.setTextColor(Color.rgb(34, 211, 238));
                    this.btnShowChat.setBackground((android.graphics.drawable.Drawable)activeBg);
                }
            } else if (index == 1) {
                if (this.btnShowExtensions != null) {
                    this.btnShowExtensions.setTextColor(Color.rgb(34, 211, 238));
                    this.btnShowExtensions.setBackground((android.graphics.drawable.Drawable)activeBg);
                }
            } else if (index == 2) {
                if (this.btnShowFileManager != null) {
                    this.btnShowFileManager.setTextColor(Color.rgb(34, 211, 238));
                    this.btnShowFileManager.setBackground((android.graphics.drawable.Drawable)activeBg);
                }
                if (this.currentPath == null) {
                    this.loadFileList("");
                }
            } else if (index == 3) {
                if (this.btnShowShell != null) {
                    this.btnShowShell.setTextColor(Color.rgb(34, 211, 238));
                    this.btnShowShell.setBackground((android.graphics.drawable.Drawable)activeBg);
                }
            }
            this.adjustViewPagerHeight();
        });
    }

    private void adjustViewPagerHeight() {
        if (this.desktopViewPager == null) return;
        int index = this.desktopViewPager.getCurrentItem();
        View view = null;
        if (index == 0) {
            view = this.chatContainerLayout;
        } else if (index == 1) {
            view = this.extensionsContainer;
        } else if (index == 2) {
            view = this.fileManagerContainer;
        } else if (index == 3) {
            view = this.shellContainer;
        }
        if (view == null) return;
        
        final View finalView = view;
        this.desktopViewPager.post(() -> {
            try {
                int width = this.desktopViewPager.getWidth();
                if (width <= 0) {
                    width = this.getResources().getDisplayMetrics().widthPixels - this.dp(32);
                }
                int widthSpec = View.MeasureSpec.makeMeasureSpec(width, View.MeasureSpec.EXACTLY);
                int heightSpec = View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED);
                finalView.measure(widthSpec, heightSpec);
                int measuredHeight = finalView.getMeasuredHeight();
                int minHeight = this.dp(300);
                int targetHeight = Math.max(minHeight, measuredHeight);
                ViewGroup.LayoutParams lp = this.desktopViewPager.getLayoutParams();
                if (lp != null && lp.height != targetHeight) {
                    lp.height = targetHeight;
                    this.desktopViewPager.setLayoutParams(lp);
                }
            } catch (Exception e) {
                // ignore
            }
        });
    }

    private void checkConnectionAsync() {
        this.executor.execute(() -> {
            boolean connected = false;
            String type = "";
            String offlineTitle = "电脑端未上线";
            String offlineDesc = "请确认电脑端程序已开启并在运行中";
            String lanBaseUrl = cc.luoluoluo.yanzi.mobile.LanDiscoveryManager.getLanBaseUrl((Context)this);
            if (lanBaseUrl == null) {
                lanBaseUrl = cc.luoluoluo.yanzi.mobile.LanDiscoveryManager.cachedLanBaseUrl;
            }
            if (lanBaseUrl != null) {
                try {
                    String cleanUrl = lanBaseUrl;
                    if (cleanUrl.endsWith("/")) {
                        cleanUrl = cleanUrl.substring(0, cleanUrl.length() - 1);
                    }
                    java.net.URL url = new java.net.URL(cleanUrl + "/health");
                    java.net.HttpURLConnection conn = (java.net.HttpURLConnection) url.openConnection();
                    conn.setRequestMethod("GET");
                    conn.setConnectTimeout(2000);
                    conn.setReadTimeout(2000);
                    int code = conn.getResponseCode();
                    if (code == 200 || code == 401) {
                        connected = true;
                        type = "lan";
                    }
                    conn.disconnect();
                } catch (Exception e) {
                }
            }
            if (!connected) {
                String token = this.prefs != null ? this.prefs.getString("token", "").trim() : "";
                if (token.isEmpty()) {
                    offlineTitle = "请先登录账号";
                    offlineDesc = "登录后才能通过云端设备表确认电脑端是否在线。";
                } else {
                    try {
                        DesktopCloudPresence presence = this.fetchDesktopCloudPresence(this.normalizedBaseUrl(), token);
                        if (presence.online) {
                            connected = true;
                            type = "cloud";
                        } else {
                            offlineTitle = presence.title;
                            offlineDesc = presence.description;
                        }
                    } catch (Exception e) {
                        offlineTitle = "云端不可达";
                        offlineDesc = "无法确认电脑端是否启动：" + MainActivity.shortMessage(e);
                    }
                }
            }
            final boolean finalConnected = connected;
            final String finalType = type;
            final String finalOfflineTitle = offlineTitle;
            final String finalOfflineDesc = offlineDesc;
            this.runOnUiThread(() -> {
                this.isDesktopConnected = finalConnected;
                this.desktopConnectionType = finalType;
                this.desktopOfflineTitle = finalOfflineTitle;
                this.desktopOfflineDesc = finalOfflineDesc;
                this.updateConnectionUi();
            });
        });
    }

    private void updateConnectionUi() {
        if (this.desktopConnectionDot != null) {
            this.desktopConnectionDot.setVisibility(this.isDesktopConnected ? View.VISIBLE : View.GONE);
        }
        if (this.isDesktopConnected) {
            if (this.offlineHintView != null) {
                this.offlineHintView.setVisibility(View.GONE);
            }
            if (this.mainDesktopContentLayout != null) {
                this.mainDesktopContentLayout.setVisibility(View.VISIBLE);
            }
            if (this.tvDesktopConnectionStatus != null) {
                if ("lan".equals(this.desktopConnectionType)) {
                    this.tvDesktopConnectionStatus.setText(" (局域网)");
                    this.tvDesktopConnectionStatus.setTextColor(Color.rgb(34, 197, 94));
                } else {
                    this.tvDesktopConnectionStatus.setText(" (云端在线)");
                    this.tvDesktopConnectionStatus.setTextColor(Color.rgb(34, 211, 238));
                }
            }
        } else {
            if (this.offlineHintView != null) {
                this.offlineHintView.setVisibility(View.VISIBLE);
            }
            if (this.mainDesktopContentLayout != null) {
                this.mainDesktopContentLayout.setVisibility(View.GONE);
            }
            if (this.tvDesktopConnectionStatus != null) {
                this.tvDesktopConnectionStatus.setText(" (未上线)");
                this.tvDesktopConnectionStatus.setTextColor(Color.rgb(239, 68, 68));
            }
            if (this.tvDesktopOfflineTitle != null) {
                this.tvDesktopOfflineTitle.setText(this.desktopOfflineTitle);
            }
            if (this.tvDesktopOfflineDesc != null) {
                this.tvDesktopOfflineDesc.setText(this.desktopOfflineDesc);
            }
        }
    }

    private DesktopCloudPresence fetchDesktopCloudPresence(String baseUrl, String token) throws Exception {
        JSONObject payload = YanziApiClient.doRequest(baseUrl, "/v1/me/devices", token, "读取电脑在线状态", "GET", null, 5000);
        JSONArray items = payload.optJSONArray("items");
        long newestSeenAtMs = 0L;
        String newestDisplayName = "";
        if (items != null) {
            for (int i = 0; i < items.length(); ++i) {
                JSONObject item = items.optJSONObject(i);
                if (item == null || !"desktop".equalsIgnoreCase(item.optString("platform", ""))) {
                    continue;
                }
                long seenAtMs = MainActivity.parseIsoTimeMs(item.optString("lastSeenAt", ""));
                if (seenAtMs > newestSeenAtMs) {
                    newestSeenAtMs = seenAtMs;
                    newestDisplayName = item.optString("displayName", item.optString("deviceId", "电脑端"));
                }
            }
        }
        if (newestSeenAtMs <= 0L) {
            return DesktopCloudPresence.offline("未发现电脑端设备", "请在电脑端登录同一账号，并保持燕子电脑端程序运行。");
        }
        long ageMs = Math.max(0L, System.currentTimeMillis() - newestSeenAtMs);
        if (ageMs <= DESKTOP_ONLINE_WINDOW_MS) {
            return DesktopCloudPresence.online(newestDisplayName);
        }
        return DesktopCloudPresence.offline("电脑端疑似未启动", newestDisplayName + " 最后在线 " + MainActivity.formatRelativeDuration(ageMs) + "，请确认电脑端程序已开启并联网。");
    }

    private static long parseIsoTimeMs(String value) {
        if (value == null || value.trim().isEmpty()) {
            return 0L;
        }
        try {
            return Instant.parse(value.trim()).toEpochMilli();
        }
        catch (Exception ex) {
            return 0L;
        }
    }

    private static String formatRelativeDuration(long durationMs) {
        long seconds = Math.max(1L, durationMs / 1000L);
        if (seconds < 60L) {
            return seconds + " 秒前";
        }
        long minutes = seconds / 60L;
        if (minutes < 60L) {
            return minutes + " 分钟前";
        }
        long hours = minutes / 60L;
        if (hours < 24L) {
            return hours + " 小时前";
        }
        return (hours / 24L) + " 天前";
    }

    private static String shortMessage(Exception e) {
        String message = e.getMessage();
        if (message == null || message.trim().isEmpty()) {
            message = e.toString();
        }
        message = message.replace('\n', ' ').replace('\r', ' ').trim();
        if (message.length() > 90) {
            return message.substring(0, 90) + "...";
        }
        return message;
    }

    private static final class DesktopCloudPresence {
        final boolean online;
        final String title;
        final String description;

        private DesktopCloudPresence(boolean online, String title, String description) {
            this.online = online;
            this.title = title;
            this.description = description;
        }

        static DesktopCloudPresence online(String displayName) {
            return new DesktopCloudPresence(true, "电脑端在线", displayName == null || displayName.trim().isEmpty() ? "云端心跳正常。" : displayName + " 云端心跳正常。");
        }

        static DesktopCloudPresence offline(String title, String description) {
            return new DesktopCloudPresence(false, title, description);
        }
    }

    private void filterFsList(String query) {
        if (this.fileListLayout == null) return;
        String q = query.trim().toLowerCase(java.util.Locale.ROOT);
        for (int i = 0; i < this.fileListLayout.getChildCount(); i++) {
            View child = this.fileListLayout.getChildAt(i);
            Object tag = child.getTag();
            if (tag instanceof String) {
                String fileName = (String)tag;
                if (q.isEmpty() || fileName.toLowerCase(java.util.Locale.ROOT).contains(q)) {
                    child.setVisibility(View.VISIBLE);
                } else {
                    child.setVisibility(View.GONE);
                }
            }
        }
    }

    private void renderBreadcrumbs(String path) {
        if (this.breadcrumbsLayout == null) return;
        this.breadcrumbsLayout.removeAllViews();
        if (path == null || path.isEmpty()) {
            TextView tv = new TextView((Context)this);
            tv.setText("盘符根");
            tv.setTextColor(Color.rgb(148, 163, 184));
            tv.setTextSize(13f);
            this.breadcrumbsLayout.addView(tv);
            return;
        }
        String normalized = path.replace("\\", "/");
        String[] parts = normalized.split("/");
        final StringBuilder currentBuilder = new StringBuilder();
        boolean isWindows = path.contains(":");
        for (int i = 0; i < parts.length; i++) {
            final String part = parts[i];
            if (part.isEmpty()) continue;
            if (i > 0) {
                currentBuilder.append(isWindows ? "\\" : "/");
                TextView tvArrow = new TextView((Context)this);
                tvArrow.setText(" > ");
                tvArrow.setTextColor(Color.rgb(100, 116, 139));
                tvArrow.setTextSize(11f);
                this.breadcrumbsLayout.addView(tvArrow);
            }
            currentBuilder.append(part);
            final String clickPath = currentBuilder.toString();
            TextView tvPart = new TextView((Context)this);
            tvPart.setText(part);
            tvPart.setTextSize(13f);
            if (i == parts.length - 1) {
                tvPart.setTextColor(Color.rgb(34, 211, 238));
                tvPart.setTypeface(Typeface.DEFAULT_BOLD);
            } else {
                tvPart.setTextColor(Color.rgb(182, 194, 214));
                tvPart.setPaintFlags(tvPart.getPaintFlags() | android.graphics.Paint.UNDERLINE_TEXT_FLAG);
                tvPart.setClickable(true);
                tvPart.setOnClickListener(v -> {
                    this.loadFileList(clickPath);
                });
            }
            this.breadcrumbsLayout.addView(tvPart);
        }
        if (this.breadcrumbsScrollView != null) {
            this.breadcrumbsScrollView.post(() -> this.breadcrumbsScrollView.fullScroll(View.FOCUS_RIGHT));
        }
    }

    private void startFsUploadFile() {
        this.isFsUploading = true;
        Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
        intent.setType("*/*");
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        this.startActivityForResult(Intent.createChooser(intent, "选择文件上传"), 2001);
    }

    private void startFsUploadPhoto() {
        this.isFsUploading = true;
        Intent intent = new Intent(Intent.ACTION_PICK, android.provider.MediaStore.Images.Media.EXTERNAL_CONTENT_URI);
        this.startActivityForResult(Intent.createChooser(intent, "选择照片上传"), 2002);
    }

    private void startFsTakePhoto() {
        this.isFsUploading = true;
        this.takeCameraPhoto();
    }

    private void uploadFileToPc(Uri uri) {
        if (this.currentPath == null || this.currentPath.isEmpty()) {
            Toast.makeText(this, "当前目录无效，请先进入一个具体路径", Toast.LENGTH_SHORT).show();
            return;
        }
        Toast.makeText(this, "开始上传文件...", Toast.LENGTH_SHORT).show();
        this.executor.execute(() -> {
            try {
                String fileName = "upload_" + System.currentTimeMillis();
                android.database.Cursor cursor = this.getContentResolver().query(uri, null, null, null, null);
                if (cursor != null) {
                    try {
                        if (cursor.moveToFirst()) {
                            int nameIndex = cursor.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME);
                            if (nameIndex != -1) {
                                String name = cursor.getString(nameIndex);
                                if (name != null && !name.isEmpty()) {
                                    fileName = name;
                                }
                            }
                        }
                    } finally {
                        cursor.close();
                    }
                }
                final String finalFileName = fileName;
                java.io.InputStream inputStream = this.getContentResolver().openInputStream(uri);
                if (inputStream == null) {
                    throw new java.io.IOException("无法打开输入流");
                }
                java.io.ByteArrayOutputStream byteBuffer = new java.io.ByteArrayOutputStream();
                byte[] buffer = new byte[8192];
                int len;
                while ((len = inputStream.read(buffer)) != -1) {
                    byteBuffer.write(buffer, 0, len);
                }
                inputStream.close();
                byte[] bytes = byteBuffer.toByteArray();
                String base64Content = android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP);
                String separator = this.currentPath.endsWith("\\") || this.currentPath.endsWith("/") ? "" : "\\";
                String targetFilePath = this.currentPath + separator + finalFileName;
                String token = this.prefs.getString("token", "").trim();
                String baseUrl = this.normalizedBaseUrl();
                JSONObject payload = new JSONObject();
                payload.put("path", (Object)targetFilePath);
                payload.put("content", (Object)base64Content);
                payload.put("base64", true);
                JSONObject res = YanziApiClient.postJson(baseUrl, "/v1/fs/write", payload, token, "上传文件");
                if (res.optBoolean("ok", false)) {
                    this.runOnUiThread(() -> {
                        Toast.makeText(this, "上传成功: " + finalFileName, Toast.LENGTH_SHORT).show();
                        this.loadFileList(this.currentPath);
                    });
                } else {
                    String error = res.optString("error", "未知错误");
                    this.runOnUiThread(() -> {
                        Toast.makeText(this, "上传失败: " + error, Toast.LENGTH_LONG).show();
                    });
                }
            } catch (Exception e) {
                this.runOnUiThread(() -> {
                    Toast.makeText(this, "上传失败: " + e.getMessage(), Toast.LENGTH_LONG).show();
                });
            }
        });
    }

    private boolean isTextFile(String fileName) {
        if (fileName == null) return false;
        String nameLower = fileName.toLowerCase(java.util.Locale.ROOT);
        return nameLower.endsWith(".txt") || nameLower.endsWith(".json") || nameLower.endsWith(".js") 
            || nameLower.endsWith(".py") || nameLower.endsWith(".md") || nameLower.endsWith(".html") 
            || nameLower.endsWith(".css") || nameLower.endsWith(".xml") || nameLower.endsWith(".ini") 
            || nameLower.endsWith(".conf") || nameLower.endsWith(".yaml") || nameLower.endsWith(".yml") 
            || nameLower.endsWith(".sh") || nameLower.endsWith(".bat") || nameLower.endsWith(".ps1") 
            || nameLower.endsWith(".cs") || nameLower.endsWith(".java") || nameLower.endsWith(".cpp") 
            || nameLower.endsWith(".h") || nameLower.endsWith(".c") || nameLower.endsWith(".log")
            || nameLower.endsWith(".patch") || nameLower.endsWith(".diff") || nameLower.endsWith(".properties");
    }

    private void showTextEditorDialog(String fullFilePath, String fileName, String content) {
        android.app.AlertDialog.Builder builder = new android.app.AlertDialog.Builder((Context)this);
        builder.setTitle((CharSequence)("编辑文件: " + fileName));

        LinearLayout container = new LinearLayout((Context)this);
        container.setOrientation(1);
        container.setPadding(this.dp(16), this.dp(10), this.dp(16), this.dp(10));

        ScrollView sv = new ScrollView((Context)this);
        EditText etContent = new EditText((Context)this);
        etContent.setText((CharSequence)content);
        etContent.setTextColor(-1);
        etContent.setTextSize(14f);
        etContent.setBackgroundColor(Color.rgb(15, 23, 42));
        etContent.setPadding(this.dp(12), this.dp(12), this.dp(12), this.dp(12));
        etContent.setGravity(android.view.Gravity.TOP);
        etContent.setInputType(131073); // TYPE_TEXT_FLAG_MULTI_LINE | TYPE_CLASS_TEXT
        etContent.setHorizontallyScrolling(false);
        etContent.setMinimumHeight(this.dp(350));

        sv.addView((View)etContent, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
        container.addView((View)sv, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(400)));

        builder.setView((View)container);

        builder.setPositiveButton((CharSequence)"保存回传", (dialog, which) -> {
            String newContent = etContent.getText().toString();
            Toast.makeText(this.getApplicationContext(), "正在回传并保存文件...", Toast.LENGTH_SHORT).show();
            this.executor.execute(() -> {
                try {
                    String token = this.prefs.getString("token", "").trim();
                    String baseUrl = this.normalizedBaseUrl();
                    JSONObject writePayload = new JSONObject().put("path", (Object)fullFilePath).put("content", (Object)newContent);
                    JSONObject res = YanziApiClient.postJson(baseUrl, "/v1/fs/write", writePayload, token, "回传文件");
                    if (res.optBoolean("ok", false)) {
                        this.runOnUiThread(() -> {
                            Toast.makeText(this.getApplicationContext(), "保存回传成功！", Toast.LENGTH_SHORT).show();
                        });
                    } else {
                        String error = res.optString("error", "未知错误");
                        this.runOnUiThread(() -> {
                            Toast.makeText(this.getApplicationContext(), "保存失败: " + error, Toast.LENGTH_LONG).show();
                        });
                    }
                } catch (Exception ex) {
                    this.runOnUiThread(() -> {
                        Toast.makeText(this.getApplicationContext(), "保存失败: " + ex.getMessage(), Toast.LENGTH_LONG).show();
                    });
                }
            });
        });

        builder.setNegativeButton((CharSequence)"取消", null);
        builder.show();
    }

    private void saveCurrentYanmOrder() {
        ArrayList<String> newList = new ArrayList<String>();
        for (int i = 0; i < this.yanmList.getChildCount(); i++) {
            View child = this.yanmList.getChildAt(i);
            Object tag = child.getTag();
            if (tag instanceof String) {
                String tagStr = (String) tag;
                if (tagStr.startsWith("yanm_comp_")) {
                    String compId = tagStr.substring("yanm_comp_".length());
                    newList.add(compId);
                }
            }
        }
        this.sortedComponentIds.clear();
        this.sortedComponentIds.addAll(newList);
        this.saveSortedState();
        this.yanmList.post(() -> {
            if (this.currentYanmSnapshot != null) {
                this.renderYanm(this.currentYanmSnapshot);
            }
        });
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
                
                try {
                    String pConfig = YanziApiClient.fetchPersonalConfig(baseUrl, token);
                    this.prefs.edit().putString("personalSyncConfig", pConfig).apply();
                } catch (Exception ignored) {}

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
                this.aiSendButton.setBackground(this.createStopIconDrawable());
                this.aiSendButton.setCompoundDrawablesWithIntrinsicBounds(null, null, null, null);
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
        this.runOnUiThread(() -> {
            this.addAiChatMessage("系统反馈:" + toolName, text, -256, true);
            this.isAiCancelled = false;
            this.setAiLoadingState(true);
            this.fetchAiReply();
        });
    }

    private boolean isKnownAiTool(String toolName) {
        return "query_extensions".equals(toolName) || "execute_extension".equals(toolName) || "view_yanm".equals(toolName) || "view_yanm_state".equals(toolName) || "update_yanm_state".equals(toolName) || "update_yanm_component".equals(toolName) || "manage_mobile_extension".equals(toolName) || "execute_command".equals(toolName) || "execute_mobile_command".equals(toolName);
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
        String mode = toolCall.optString("mode", "");
        String stateKey = MainActivity.firstNonEmpty(toolCall.optString("stateKey", ""), toolCall.optString("key", ""));
        String value = toolCall.has("value") ? toolCall.optString("value", "") : "";
        String code = toolCall.optString("code", "");
        int htmlHash = html.isEmpty() ? 0 : html.hashCode();
        int valueHash = value.isEmpty() ? 0 : value.hashCode();
        int codeHash = code.isEmpty() ? 0 : code.hashCode();
        return toolName + "|id=" + id + "|action=" + action + "|title=" + title + "|mode=" + mode + "|stateKey=" + stateKey + "|html=" + htmlHash + "|value=" + valueHash + "|code=" + codeHash;
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
                        JSONArray components = MainActivity.firstArray(yanmObj, "components", "Components");
                        JSONObject state = MainActivity.firstObject(yanmObj, "componentState", "ComponentState");
                        if (state == null) {
                            state = new JSONObject();
                        }
                        int componentCount = components == null ? 0 : components.length();
                        for (int i = 0; i < componentCount; ++i) {
                            JSONObject c = components.optJSONObject(i);
                            if (c == null) continue;
                            String id = MainActivity.getYanmComponentId(c, i);
                            String stateKey = YanmWidgetData.resolveStateKey(c, id);
                            String value = state.optString(stateKey, "");
                            JSONObject simple = new JSONObject();
                            simple.put("id", (Object)id);
                            simple.put("title", (Object)MainActivity.getYanmComponentTitle(c, i));
                            simple.put("stateKey", (Object)stateKey);
                            simple.put("stateLength", value.length());
                            yanmList.put((Object)simple);
                        }
                        yanmNamesStr = yanmList.toString();
                    }
                    catch (Exception yanmStr) {
                        // empty catch block
                    }
                    String finalPrompt = "\u3010\u7cfb\u7edf\u6307\u4ee4\uff08\u4e25\u683c\u9075\u5b88\uff09\u3011\n" + basePrompt + "\n\u5f53\u524d\u53ef\u7528\u6269\u5c55\u6709:\n" + extListPrompt.toString() + "\n\u5f53\u524d\u71d5\u5e55\u7ec4\u4ef6\u6709:\n" + yanmNamesStr + "\n\u3010\u71d5\u5e55\u5de5\u5177\u6700\u77ed\u8def\u5f84\u3011\n\u5982\u679c\u7528\u6237\u70b9\u540d\u4e86\u7ec4\u4ef6\u6807\u9898\uff08\u4f8b\u5982\u201c\u4fbf\u7b7e\u201d\uff09\uff0c\u4e14\u4e0a\u9762\u6e05\u5355\u5df2\u7ecf\u5305\u542b\u8be5\u7ec4\u4ef6\u7684 id/stateKey\uff0c\u4e0d\u8981\u5148\u8c03 view_yanm\uff1b\u76f4\u63a5\u7528\u8be5 id \u6216\u6807\u9898\u8c03 view_yanm_state \u8bfb\u5f53\u524d\u503c\uff0c\u7136\u540e\u8c03 update_yanm_state \u66f4\u65b0\u3002\u53ea\u6709\u6e05\u5355\u91cc\u627e\u4e0d\u5230\u76ee\u6807\u7ec4\u4ef6\u65f6\uff0c\u624d\u8c03 view_yanm \u5237\u65b0\u7ec4\u4ef6\u6e05\u5355\u3002";
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
                                if ("execute_command".equals(toolName)) {
                                    String cmd = toolCall.optString("command");
                                    this.runOnUiThread(() -> {
                                        this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:execute_command", content, Color.rgb(167, 243, 208), true);
                                        this.executor.execute(() -> {
                                            try {
                                                String baseUrl = this.normalizedBaseUrl();
                                                String token = this.requireToken();
                                                JSONObject cmdPayload = new JSONObject().put("command", (Object)cmd);
                                                JSONObject cmdRes = YanziApiClient.postJson(baseUrl, "/v1/shell/run", cmdPayload, token, "AI\u6267\u884c\u547d\u4ee4");
                                                String output = cmdRes.optString("output", "");
                                                int exitCode = cmdRes.optInt("exitCode", 0);
                                                this.sendAiSystemFeedback("execute_command", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u547d\u4ee4\u884c\u6267\u884c\u7ed3\u679c(\u9000\u51fa\u7801:" + exitCode + ")\uff1a\n" + output + "\n\u8bf7\u6839\u636e\u7ed3\u679c\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\uff0c\u7edd\u5bf9\u4e0d\u8981\u518d\u6b21\u8c03\u7528\u672c\u5de5\u5177\uff01");
                                            }
                                            catch (Exception e) {
                                                this.sendAiSystemFeedback("execute_command", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u6267\u884c\u547d\u4ee4\u5931\u8d25\uff1a" + e.getMessage());
                                            }
                                            finally {
                                                this.finishAiToolCall(activeToolCallKey);
                                            }
                                        });
                                    });
                                    return;
                                }
                                if ("execute_mobile_command".equals(toolName)) {
                                    String cmd = toolCall.optString("command");
                                    this.runOnUiThread(() -> {
                                        this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:execute_mobile_command", content, Color.rgb(167, 243, 208), true);
                                        this.executor.execute(() -> {
                                            try {
                                                int[] exitCodeOut = new int[1];
                                                String output = FloatingWheelService.executeShellCommand(cmd, exitCodeOut);
                                                this.sendAiSystemFeedback("execute_mobile_command", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u626b\u673a\u672c\u5730\u547d\u4ee4\u884c\u6267\u884c\u7ed3\u679c(\u9000\u51fa\u7801:" + exitCodeOut[0] + ")\uff1a\n" + output + "\n\u8bf7\u6839\u636e\u7ed3\u679c\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\uff0c\u7edd\u5bf9\u4e0d\u8981\u518d\u6b21\u8c03\u7528\u672c\u5de5\u5177\uff01");
                                            }
                                            catch (Exception e) {
                                                this.sendAiSystemFeedback("execute_mobile_command", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u626b\u673a\u672c\u5730\u6267\u884c\u547d\u4ee4\u5931\u8d25\uff1a" + e.getMessage());
                                            }
                                            finally {
                                                this.finishAiToolCall(activeToolCallKey);
                                            }
                                        });
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
                                    boolean includeHtml = toolCall.optBoolean("includeHtml", false) || "frontend".equalsIgnoreCase(toolCall.optString("mode", ""));
                                    this.runOnUiThread(() -> {
                                        try {
                                            this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:view_yanm", content, Color.rgb((int)167, (int)243, (int)208), true);
                                            JSONObject yanm = this.readCachedYanmSnapshotForAi();
                                            String resultStr = this.buildYanmViewResult(yanm, id, includeHtml).toString();
                                            this.sendAiSystemFeedback("view_yanm", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u67e5\u8be2\u7ed3\u679c\uff1a" + resultStr + "\n\u8bf7\u6839\u636e\u7ed3\u679c\u5224\u65ad\u662f\u5426\u9700\u8981\u7ee7\u7eed\u8c03\u7528\u5de5\u5177\uff0c\u6216\u8005\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\u3002");
                                        }
                                        catch (Exception e) {
                                            this.sendAiSystemFeedback("view_yanm", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u67e5\u8be2\u5931\u8d25\uff1a" + e.getMessage() + "\n\u8bf7\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\u3002");
                                        }
                                        finally {
                                            this.finishAiToolCall(activeToolCallKey);
                                        }
                                    });
                                    return;
                                }
                                if ("view_yanm_state".equals(toolName)) {
                                    String id = toolCall.optString("id");
                                    String stateKey = MainActivity.firstNonEmpty(toolCall.optString("stateKey", ""), toolCall.optString("key", ""));
                                    this.runOnUiThread(() -> {
                                        try {
                                            this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:view_yanm_state", content, Color.rgb((int)167, (int)243, (int)208), true);
                                            JSONObject yanm = this.readCachedYanmSnapshotForAi();
                                            String resultStr = this.buildYanmStateViewResult(yanm, id, stateKey).toString();
                                            this.sendAiSystemFeedback("view_yanm_state", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u71d5\u5e55\u540e\u7aef\u6570\u636e\u67e5\u8be2\u7ed3\u679c\uff1a" + resultStr + "\n\u8bf7\u6839\u636e\u7ed3\u679c\u5224\u65ad\u662f\u5426\u9700\u8981\u7ee7\u7eed\u8c03\u7528\u5de5\u5177\uff0c\u6216\u8005\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u56de\u590d\u7528\u6237\u3002");
                                        }
                                        catch (Exception e) {
                                            this.sendAiSystemFeedback("view_yanm_state", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u71d5\u5e55\u540e\u7aef\u6570\u636e\u67e5\u8be2\u5931\u8d25\uff1a" + e.getMessage());
                                        }
                                        finally {
                                            this.finishAiToolCall(activeToolCallKey);
                                        }
                                    });
                                    return;
                                }
                                if ("update_yanm_state".equals(toolName)) {
                                    String id = toolCall.optString("id");
                                    String stateKey = MainActivity.firstNonEmpty(toolCall.optString("stateKey", ""), toolCall.optString("key", ""));
                                    String value = this.readAiToolString(toolCall, "value", "text", "content");
                                    this.runOnUiThread(() -> {
                                        this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:update_yanm_state", content, Color.rgb((int)167, (int)243, (int)208), true);
                                        try {
                                            String resultStr = this.updateYanmStateFromAi(id, stateKey, value).toString();
                                            this.sendAiSystemFeedback("update_yanm_state", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u5df2\u66f4\u65b0\u71d5\u5e55\u540e\u7aef\u6570\u636e\uff1a" + resultStr + "\n\u8bf7\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u603b\u7ed3\u56de\u590d\u7528\u6237\uff0c\u4e0d\u8981\u518d\u8c03\u7528\u5de5\u5177\u3002");
                                        }
                                        catch (Exception e) {
                                            this.sendAiSystemFeedback("update_yanm_state", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u66f4\u65b0\u71d5\u5e55\u540e\u7aef\u6570\u636e\u5931\u8d25\uff1a" + e.getMessage());
                                        }
                                        finally {
                                            this.finishAiToolCall(activeToolCallKey);
                                        }
                                    });
                                    return;
                                }
                                if ("update_yanm_component".equals(toolName)) {
                                    String id = toolCall.optString("id");
                                    this.runOnUiThread(() -> {
                                        this.addAiChatMessage("\u5de5\u5177\u8c03\u7528:update_yanm_component", content, Color.rgb((int)167, (int)243, (int)208), true);
                                        try {
                                            JSONObject result;
                                            if (toolCall.has("value") || toolCall.has("stateKey") || toolCall.has("key")) {
                                                String stateKey = MainActivity.firstNonEmpty(toolCall.optString("stateKey", ""), toolCall.optString("key", ""));
                                                String value = this.readAiToolString(toolCall, "value", "text", "content");
                                                result = this.updateYanmStateFromAi(id, stateKey, value);
                                            } else {
                                                result = this.updateYanmComponentFrontendFromAi(toolCall);
                                            }
                                            this.sendAiSystemFeedback("update_yanm_component", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u71d5\u5e55\u7ec4\u4ef6\u66f4\u65b0\u7ed3\u679c\uff1a" + result.toString() + "\n\u8bf7\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u603b\u7ed3\u56de\u590d\u7528\u6237\uff0c\u4e0d\u8981\u518d\u8c03\u7528\u5de5\u5177\u3002");
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
                                                this.sendAiSystemFeedback("manage_mobile_extension", "\u3010\u7cfb\u7edf\u53cd\u9988\u3011\u5df2\u6210\u529f" + ("create".equals(action) ? "\u521b\u5efa" : "\u66f4\u65b0") + "\u624b\u673a\u6269\u5c55: " + id + "\u3002\u4f60\u53ef\u4ee5\u7ee7\u7eed\u8c03\u7528\u0020\u0065\u0078\u0065\u0063\u0075\u0074\u0065\u005f\u0065\u0078\u0074\u0065\u006e\u0073\u0069\u006f\u006e\u0020\u5de5\u5177\uff08\u53c2\u6570\uff1a\u0069\u0064\u0020\u4e3a\u8be5\u6269\u5c55\u0049\u0044\uff09\u6765\u8fd0\u884c\u8be5\u624b\u673a\u6269\u5c55\uff0c\u6216\u8005\u76f4\u63a5\u4f7f\u7528\u81ea\u7136\u8bed\u8a00\u5411\u7528\u6237\u53cd\u9988\u7ed3\u679c\u3002");
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
        dialogLayout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);
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
        dialogLayout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);
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
        if (android.os.Looper.myLooper() != android.os.Looper.getMainLooper()) {
            this.runOnUiThread(() -> this.addAiChatMessage(sender, text, color, saveToHistory));
            return;
        }
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
                    } else if ("view_yanm_state".equals(toolName)) {
                        String toolId = toolCall.optString("id");
                        String stateKey = MainActivity.firstNonEmpty(toolCall.optString("stateKey", ""), toolCall.optString("key", ""));
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: view_yanm_state" + (toolId.isEmpty() ? "" : " (id: " + toolId + ")") + (stateKey.isEmpty() ? "" : " (key: " + stateKey + ")");
                    } else if ("update_yanm_state".equals(toolName)) {
                        String toolId = toolCall.optString("id");
                        String stateKey = MainActivity.firstNonEmpty(toolCall.optString("stateKey", ""), toolCall.optString("key", ""));
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: update_yanm_state" + (toolId.isEmpty() ? "" : " (id: " + toolId + ")") + (stateKey.isEmpty() ? "" : " (key: " + stateKey + ")");
                    } else if ("update_yanm_component".equals(toolName)) {
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: update_yanm_component (id: " + toolCall.optString("id") + ")";
                    } else if ("manage_mobile_extension".equals(toolName)) {
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: manage_mobile_extension (action: " + toolCall.optString("action") + ", id: " + toolCall.optString("id") + ")";
                    } else if ("view_yanm".equals(toolName)) {
                        String toolId = toolCall.optString("id");
                        displayText = "\ud83d\udd27 \u4f7f\u7528\u5de5\u5177: view_yanm" + (toolId.isEmpty() ? "" : " (id: " + toolId + ")");
                    } else if ("execute_command".equals(toolName)) {
                        displayText = "\ud83d\udee0 \u4f7f\u7528\u5de5\u5177: execute_command (command: " + toolCall.optString("command") + ")";
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
        } else if (isFeedback) {
            msgContainer.setGravity(0x800003);
            LinearLayout header = new LinearLayout((Context)this);
            header.setOrientation(0);
            header.setGravity(16);
            TextView headerText = new TextView((Context)this);
            String feedbackTool = "";
            if (sender != null && sender.contains(":")) {
                feedbackTool = sender.substring(sender.indexOf(":") + 1);
            }
            String toolLabel = feedbackTool.isEmpty() ? "\u7cfb\u7edf\u53cd\u9988" : feedbackTool;
            headerText.setText((CharSequence)("\u25b6 \ud83d\udd27 \u4f7f\u7528\u5de5\u5177: " + toolLabel + " (\u70b9\u51fb\u5c55\u5f00)"));
            int headerColor = Color.rgb((int)234, (int)179, (int)8);
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
        } else if (isSystem) {
            msgContainer.setGravity(0x800003);
            TextView errorTv = new TextView((Context)this);
            errorTv.setText((CharSequence)displayText, TextView.BufferType.SPANNABLE);
            int msgColor = (color != -1) ? color : Color.rgb((int)248, (int)113, (int)113);
            errorTv.setTextColor(msgColor);
            errorTv.setTextSize(2, 12.0f);
            errorTv.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
            errorTv.setTextIsSelectable(true);
            GradientDrawable bg = new GradientDrawable();
            if (msgColor == Color.rgb(248, 113, 113) || msgColor == Color.RED) {
                bg.setColor(Color.argb((int)25, (int)248, (int)113, (int)113));
                bg.setStroke(this.dp(1), Color.argb((int)50, (int)248, (int)113, (int)113));
            } else {
                bg.setColor(Color.argb((int)15, (int)156, (int)163, (int)175));
                bg.setStroke(this.dp(1), Color.argb((int)30, (int)156, (int)163, (int)175));
            }
            bg.setCornerRadius((float)this.dp(8));
            errorTv.setBackground((Drawable)bg);
            msgContainer.setOnLongClickListener(v -> {
                this.showAiMessageMenu(info);
                return true;
            });
            msgContainer.addView((View)errorTv, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
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
            TextView dragHandle = this.textView("☰", 16, Color.rgb((int)148, (int)163, (int)184), false);
            dragHandle.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
            headerLayout.addView((View)dragHandle, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
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

            dragHandle.setOnTouchListener((v, event) -> {
                if (event.getAction() == android.view.MotionEvent.ACTION_DOWN) {
                    android.content.ClipData data = android.content.ClipData.newPlainText("component_id", componentId);
                    android.view.View.DragShadowBuilder shadowBuilder = new android.view.View.DragShadowBuilder(card);
                    card.startDragAndDrop(data, shadowBuilder, card, 0);
                    return true;
                }
                return false;
            });

            card.setOnLongClickListener(v -> {
                android.content.ClipData data = android.content.ClipData.newPlainText("component_id", componentId);
                android.view.View.DragShadowBuilder shadowBuilder = new android.view.View.DragShadowBuilder(card);
                card.startDragAndDrop(data, shadowBuilder, card, 0);
                return true;
            });

            card.setOnDragListener(new android.view.View.OnDragListener() {
                @Override
                public boolean onDrag(android.view.View v, android.view.DragEvent event) {
                    switch (event.getAction()) {
                        case android.view.DragEvent.ACTION_DRAG_STARTED:
                            if (event.getLocalState() == card) {
                                card.setAlpha(0.4f);
                            }
                            return true;
                        case android.view.DragEvent.ACTION_DRAG_ENTERED:
                            android.view.View draggedView = (android.view.View) event.getLocalState();
                            if (draggedView != null && draggedView != card) {
                                int targetIndex = yanmList.indexOfChild(card);
                                int sourceIndex = yanmList.indexOfChild(draggedView);
                                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex) {
                                    yanmList.removeView(draggedView);
                                    yanmList.addView(draggedView, targetIndex);
                                }
                            }
                            return true;
                        case android.view.DragEvent.ACTION_DRAG_EXITED:
                            return true;
                        case android.view.DragEvent.ACTION_DROP:
                            return true;
                        case android.view.DragEvent.ACTION_DRAG_ENDED:
                            if (event.getLocalState() == card) {
                                card.setAlpha(1.0f);
                                saveCurrentYanmOrder();
                            }
                            return true;
                    }
                    return false;
                }
            });

            i++;
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
        this.runOnUiThread(() -> {
            if (this.flatLogTv != null) {
                this.flatLogTv.setText((CharSequence)this.getYanmSyncLogs());
                if (this.flatLogScrollView != null) {
                    this.flatLogScrollView.post(() -> this.flatLogScrollView.fullScroll(View.FOCUS_DOWN));
                }
            }
        });
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
        this.yanmSyncHandler.postDelayed(this.pendingYanmSync, 3000L);
        this.setStatus("\u71d5\u5e55\u72b6\u6001\u5f85\u540c\u6b65\u5230\u4e91\u7aef\uff1a" + reason);
    }

    private void syncYanmStateToCloud(String reason) {
        JSONObject snapshot = this.currentYanmSnapshot;
        if (snapshot == null) {
            this.setStatus("燕幕同步跳过：没有完整快照。");
            return;
        }
        this.executor.execute(() -> {
            try {
                String configStr = this.prefs.getString("personalSyncConfig", "{}");
                JSONObject cloudConfig = new JSONObject(configStr);
                boolean enabled = cloudConfig.optBoolean("enabled", false);
                String provider = cloudConfig.optString("provider", "none");
                
                if (enabled && !"none".equals(provider)) {
                    JSONObject secrets = cloudConfig.optJSONObject("secrets");
                    JSONObject settings = cloudConfig.optJSONObject("settings");
                    if (secrets == null) secrets = new JSONObject();
                    if (settings == null) settings = new JSONObject();
                    
                    SimpleDateFormat sdf = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT);
                    sdf.setTimeZone(java.util.TimeZone.getTimeZone("UTC"));
                    String timeStr = sdf.format(new Date());
                    JSONObject wrapper = new JSONObject()
                            .put("updatedAtUtc", (Object)timeStr)
                            .put("yanm", (Object)snapshot);
                    String wrapperStr = wrapper.toString();
                    
                    if ("webdav".equals(provider)) {
                        JSONObject webDav = settings.optJSONObject("webDav");
                        String password = secrets.optString("webDavPassword", "");
                        if (webDav != null) {
                            YanziApiClient.WebDavConfig config = new YanziApiClient.WebDavConfig();
                            config.serverUrl = webDav.optString("url", "");
                            config.rootPath = webDav.optString("pathPrefix", "");
                            config.username = webDav.optString("username", "");
                            config.password = password;
                            
                            YanziApiClient.putWebDavBytes(config, "state/yanm-state.json", wrapperStr.getBytes(StandardCharsets.UTF_8), "application/json");
                            this.runOnUiThread(() -> {
                                this.setStatus("燕幕状态已同步到云端(WebDAV)：" + reason);
                                MobileDiagnostics.append((Context)this, "燕幕状态已同步到 WebDAV：" + reason);
                            });
                            return;
                        }
                    } else if ("github".equals(provider)) {
                        JSONObject gitHub = settings.optJSONObject("github");
                        String token = secrets.optString("githubToken", "");
                        if (gitHub != null && !token.isEmpty()) {
                            String owner = gitHub.optString("username", "");
                            String repo = gitHub.optString("repo", "yanzi-sync");
                            String branch = gitHub.optString("branch", "main");
                            String pathPrefix = gitHub.optString("pathPrefix", "");
                            String relPath = pathPrefix.isEmpty() ? "state/yanm-state.json" : (pathPrefix.endsWith("/") ? pathPrefix : pathPrefix + "/") + "state/yanm-state.json";
                            
                            YanziApiClient.uploadFileToGitHub(token, owner, repo, branch, relPath, wrapperStr);
                            this.runOnUiThread(() -> {
                                this.setStatus("燕幕状态已同步到云端(GitHub)：" + reason);
                                MobileDiagnostics.append((Context)this, "燕幕状态已同步到 GitHub：" + reason);
                            });
                            return;
                        }
                    } else if ("gitee".equals(provider)) {
                        JSONObject gitee = settings.optJSONObject("gitee");
                        String token = secrets.optString("giteeToken", "");
                        if (gitee != null && !token.isEmpty()) {
                            String owner = gitee.optString("username", "");
                            String repo = gitee.optString("repo", "yanzi-sync");
                            String branch = gitee.optString("branch", "master");
                            String pathPrefix = gitee.optString("pathPrefix", "");
                            String relPath = pathPrefix.isEmpty() ? "state/yanm-state.json" : (pathPrefix.endsWith("/") ? pathPrefix : pathPrefix + "/") + "state/yanm-state.json";
                            
                            YanziApiClient.uploadFileToGitee(token, owner, repo, branch, relPath, wrapperStr);
                            this.runOnUiThread(() -> {
                                this.setStatus("燕幕状态已同步到云端(Gitee)：" + reason);
                                MobileDiagnostics.append((Context)this, "燕幕状态已同步到 Gitee：" + reason);
                            });
                            return;
                        }
                    }
                }
                
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
                this.runOnUiThread(() -> {
                    this.setStatus("燕幕状态已同步到云端：" + reason);
                    MobileDiagnostics.append((Context)this, "燕幕状态已同步到云端：" + reason);
                });
            }
            catch (Exception ex) {
                this.runOnUiThread(() -> {
                    this.setStatus("燕幕状态同步失败：" + ex.getMessage());
                    MobileDiagnostics.append((Context)this, "燕幕状态同步失败：" + ex.getMessage());
                });
            }
        });
    }

    private void scheduleYanmComponentStateCloudSync(String stateKey, String value, String reason) {
        String key = stateKey == null ? "" : stateKey.trim();
        if (key.isEmpty()) {
            return;
        }
        synchronized (this.pendingYanmComponentStateUpdates) {
            this.pendingYanmComponentStateUpdates.put(key, value == null ? "" : value);
        }
        if (this.pendingYanmComponentStateSync != null) {
            this.yanmSyncHandler.removeCallbacks(this.pendingYanmComponentStateSync);
        }
        this.pendingYanmComponentStateSync = () -> this.syncYanmComponentStateToCloud(reason);
        this.yanmSyncHandler.postDelayed(this.pendingYanmComponentStateSync, 3000L);
        this.setStatus("\u71d5\u5e55\u540e\u7aef\u6570\u636e\u5f85\u540c\u6b65\u5230\u4e91\u7aef\uff1a" + reason);
    }

    private void syncYanmComponentStateToCloud(String reason) {
        JSONObject patch = new JSONObject();
        synchronized (this.pendingYanmComponentStateUpdates) {
            try {
                for (Map.Entry<String, String> entry : this.pendingYanmComponentStateUpdates.entrySet()) {
                    patch.put(entry.getKey(), (Object)(entry.getValue() == null ? "" : entry.getValue()));
                }
                this.pendingYanmComponentStateUpdates.clear();
            }
            catch (Exception ex) {
                this.setStatus("\u71d5\u5e55\u540e\u7aef\u6570\u636e\u540c\u6b65\u8df3\u8fc7\uff1a" + ex.getMessage());
                return;
            }
        }
        if (patch.length() == 0) {
            return;
        }
        this.executor.execute(() -> {
            try {
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                try {
                    YanziApiClient.putYanmComponentState(baseUrl, token, patch);
                }
                catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    YanziApiClient.putYanmComponentState(baseUrl, token, patch);
                }
                this.runOnUiThread(() -> {
                    this.setStatus("\u71d5\u5e55\u540e\u7aef\u6570\u636e\u5df2\u540c\u6b65\u5230\u4e91\u7aef\uff1a" + reason);
                    MobileDiagnostics.append((Context)this, "燕幕后端数据已增量同步到云端：" + reason + ", keys=" + patch.length());
                });
            }
            catch (Exception ex) {
                synchronized (this.pendingYanmComponentStateUpdates) {
                    Iterator<String> keys = patch.keys();
                    while (keys.hasNext()) {
                        String key = keys.next();
                        this.pendingYanmComponentStateUpdates.put(key, patch.optString(key, ""));
                    }
                }
                this.runOnUiThread(() -> {
                    this.setStatus("\u71d5\u5e55\u540e\u7aef\u6570\u636e\u540c\u6b65\u5931\u8d25\uff1a" + ex.getMessage());
                    MobileDiagnostics.append((Context)this, "燕幕后端数据增量同步失败：" + ex.getMessage());
                });
            }
        });
    }

    private JSONObject readCachedYanmSnapshotForAi() throws Exception {
        String cached = this.prefs.getString(CACHE_YANM, "");
        if (cached != null && !cached.trim().isEmpty() && !"{}".equals(cached.trim())) {
            return new JSONObject(cached);
        }
        if (this.currentYanmSnapshot != null) {
            return new JSONObject(this.currentYanmSnapshot.toString());
        }
        throw new IllegalStateException("燕幕缓存为空，请先刷新燕幕。");
    }

    private JSONObject buildYanmViewResult(JSONObject yanm, String id, boolean includeHtml) throws Exception {
        if (id == null || id.trim().isEmpty()) {
            JSONObject result = new JSONObject();
            JSONArray items = new JSONArray();
            JSONArray components = MainActivity.firstArray(yanm, "components", "Components");
            JSONObject state = MainActivity.firstObject(yanm, "componentState", "ComponentState");
            if (state == null) {
                state = new JSONObject();
            }
            int count = components == null ? 0 : components.length();
            for (int i = 0; i < count; ++i) {
                JSONObject component = components.optJSONObject(i);
                if (component == null) {
                    continue;
                }
                items.put((Object)this.buildYanmComponentSummary(yanm, component, i, false, state));
            }
            result.put("mode", "component_list");
            result.put("componentCount", items.length());
            result.put("components", (Object)items);
            result.put("htmlIncluded", false);
            result.put("note", (Object)"组件清单不包含完整 HTML。需要前端代码时，对指定 id 调用 view_yanm 并传 includeHtml:true。修改便签/待办正文请用 view_yanm_state/update_yanm_state。");
            return result;
        }

        int index = MainActivity.findYanmComponentIndex(yanm, id);
        if (index < 0) {
            throw new IllegalStateException("未找到 ID 或标题为 " + id + " 的燕幕组件。");
        }
        JSONArray components = MainActivity.firstArray(yanm, "components", "Components");
        JSONObject component = components.optJSONObject(index);
        JSONObject state = MainActivity.firstObject(yanm, "componentState", "ComponentState");
        if (state == null) {
            state = new JSONObject();
        }
        JSONObject result = this.buildYanmComponentSummary(yanm, component, index, includeHtml, state);
        result.put("mode", "component_detail");
        result.put("htmlIncluded", includeHtml);
        if (!includeHtml) {
            result.put("note", (Object)"详情默认不包含完整 HTML，避免把前端代码误当正文。修改正文请使用 componentState 工具。");
        }
        return result;
    }

    private JSONObject buildYanmStateViewResult(JSONObject yanm, String id, String stateKey) throws Exception {
        JSONObject state = MainActivity.firstObject(yanm, "componentState", "ComponentState");
        if (state == null) {
            state = new JSONObject();
        }
        String trimmedId = id == null ? "" : id.trim();
        String resolvedKey = stateKey == null ? "" : stateKey.trim();
        JSONObject result = new JSONObject();

        if (trimmedId.isEmpty() && resolvedKey.isEmpty()) {
            JSONArray keys = new JSONArray();
            Iterator<String> iterator = state.keys();
            while (iterator.hasNext()) {
                String key = iterator.next();
                String value = state.optString(key, "");
                keys.put((Object)new JSONObject()
                        .put("stateKey", (Object)key)
                        .put("valueLength", value.length())
                        .put("valuePreview", (Object)MainActivity.trimForAi(value, 240)));
            }
            result.put("mode", "state_key_list");
            result.put("keyCount", keys.length());
            result.put("keys", (Object)keys);
            result.put("note", (Object)"未指定组件或 key，因此只返回后端数据 key 列表和摘要。");
            return result;
        }

        JSONObject component = null;
        int index = -1;
        if (!trimmedId.isEmpty()) {
            index = MainActivity.findYanmComponentIndex(yanm, trimmedId);
            if (index < 0) {
                throw new IllegalStateException("未找到 ID 或标题为 " + trimmedId + " 的燕幕组件。");
            }
            JSONArray components = MainActivity.firstArray(yanm, "components", "Components");
            component = components.optJSONObject(index);
            String componentId = MainActivity.getYanmComponentId(component, index);
            if (resolvedKey.isEmpty()) {
                resolvedKey = YanmWidgetData.resolveStateKey(component, componentId);
            }
            result.put("id", (Object)componentId);
            result.put("title", (Object)MainActivity.getYanmComponentTitle(component, index));
        }

        if (resolvedKey.isEmpty()) {
            throw new IllegalStateException("缺少 stateKey；请传 stateKey，或传可解析出 stateKey 的组件 id。");
        }

        String value = state.optString(resolvedKey, "");
        result.put("mode", "state_detail");
        result.put("stateKey", (Object)resolvedKey);
        result.put("value", (Object)value);
        result.put("valueLength", value.length());
        result.put("valuePreview", (Object)MainActivity.trimForAi(value, 240));
        if (component != null) {
            result.put("component", (Object)this.buildYanmComponentSummary(yanm, component, index, false, state));
        }
        if (resolvedKey.contains("todo")) {
            result.put("formatHint", "待办组件的值必须为 JSON 数组字符串，格式如：[{\"text\":\"完成任务\",\"done\":false}]。切记内容字段键名必须是 \"text\"，完成状态键名必须是 \"done\"，布尔值。");
        } else if (resolvedKey.contains("note")) {
            result.put("formatHint", "便签组件的值通常是纯文本字符串。");
        } else if (resolvedKey.contains("bookmark")) {
            result.put("formatHint", "书签组件的值必须为 JSON 数组字符串，如：[\"https://github.com\"]。");
        }
        return result;
    }

    private JSONObject updateYanmStateFromAi(String id, String stateKey, String value) throws Exception {
        JSONObject yanm = this.readCachedYanmSnapshotForAi();
        String trimmedId = id == null ? "" : id.trim();
        String resolvedKey = stateKey == null ? "" : stateKey.trim();
        JSONObject component = null;
        int index = -1;
        if (!trimmedId.isEmpty()) {
            index = MainActivity.findYanmComponentIndex(yanm, trimmedId);
            if (index < 0) {
                throw new IllegalStateException("未找到 ID 或标题为 " + trimmedId + " 的燕幕组件。");
            }
            JSONArray components = MainActivity.firstArray(yanm, "components", "Components");
            component = components.optJSONObject(index);
            String componentId = MainActivity.getYanmComponentId(component, index);
            if (resolvedKey.isEmpty()) {
                resolvedKey = YanmWidgetData.resolveStateKey(component, componentId);
            }
        }
        if (resolvedKey.isEmpty()) {
            throw new IllegalStateException("缺少 stateKey；修改燕幕正文必须指定 stateKey 或组件 id。");
        }

        JSONObject state = MainActivity.firstObject(yanm, "componentState", "ComponentState");
        if (state == null) {
            state = new JSONObject();
        }
        String safeValue = value == null ? "" : value;
        state.put(resolvedKey, (Object)safeValue);
        yanm.put("componentState", (Object)state);

        String title = component == null ? "" : MainActivity.getYanmComponentTitle(component, index);
        this.commitYanmComponentStateFromAi(yanm, resolvedKey, safeValue, "AI-state:" + (title.isEmpty() ? resolvedKey : title + "/" + resolvedKey));

        JSONObject result = new JSONObject()
                .put("ok", true)
                .put("changed", (Object)"componentState")
                .put("stateKey", (Object)resolvedKey)
                .put("valueLength", safeValue.length())
                .put("valuePreview", (Object)MainActivity.trimForAi(safeValue, 240));
        if (component != null) {
            result.put("id", (Object)MainActivity.getYanmComponentId(component, index));
            result.put("title", (Object)title);
        }
        return result;
    }

    private JSONObject updateYanmComponentFrontendFromAi(JSONObject toolCall) throws Exception {
        boolean allowed = "frontend".equalsIgnoreCase(toolCall.optString("mode", "")) || toolCall.optBoolean("allowFrontendEdit", false);
        if (!allowed) {
            throw new IllegalStateException("已拒绝修改组件前端结构。修改便签/待办正文请用 update_yanm_state；如果确实要改 HTML，请传 mode:\"frontend\"。");
        }
        String id = toolCall.optString("id", "").trim();
        if (id.isEmpty()) {
            throw new IllegalStateException("缺少组件 id。");
        }
        JSONObject yanm = this.readCachedYanmSnapshotForAi();
        int index = MainActivity.findYanmComponentIndex(yanm, id);
        if (index < 0) {
            throw new IllegalStateException("未找到 ID 或标题为 " + id + " 的燕幕组件。");
        }
        JSONArray components = MainActivity.firstArray(yanm, "components", "Components");
        JSONObject component = components.optJSONObject(index);
        boolean titleChanged = false;
        boolean htmlChanged = false;
        if (toolCall.has("title")) {
            MainActivity.putYanmStringProperty(component, "title", "Title", toolCall.optString("title", ""));
            titleChanged = true;
        }
        if (toolCall.has("html")) {
            MainActivity.putYanmStringProperty(component, "html", "Html", toolCall.optString("html", ""));
            htmlChanged = true;
        }
        if (!titleChanged && !htmlChanged) {
            throw new IllegalStateException("没有可更新的前端字段；请传 title 或 html。");
        }
        this.commitYanmSnapshotFromAi(yanm, "AI-frontend:" + MainActivity.getYanmComponentTitle(component, index));
        String html = MainActivity.getYanmComponentHtml(component);
        return new JSONObject()
                .put("ok", true)
                .put("changed", (Object)"frontend")
                .put("id", (Object)MainActivity.getYanmComponentId(component, index))
                .put("title", (Object)MainActivity.getYanmComponentTitle(component, index))
                .put("titleChanged", titleChanged)
                .put("htmlChanged", htmlChanged)
                .put("htmlLength", html.length())
                .put("note", (Object)"本次只改 components[] 中的前端字段，未修改 componentState 和燕幕启用状态。");
    }

    private void commitYanmSnapshotFromAi(JSONObject yanm, String reason) throws Exception {
        JSONObject state = MainActivity.firstObject(yanm, "componentState", "ComponentState");
        if (state == null) {
            state = new JSONObject();
            yanm.put("componentState", (Object)state);
        }
        this.currentYanmSnapshot = yanm;
        this.currentYanmState = state;
        this.prefs.edit().putString(CACHE_YANM, yanm.toString()).apply();
        this.updateAllAppWidgets();
        YanmWidgetData.refreshComponentWidgets((Context)this);
        this.renderYanm(yanm);
        this.scheduleYanmCloudSync(reason);
    }

    private void commitYanmComponentStateFromAi(JSONObject yanm, String stateKey, String value, String reason) throws Exception {
        JSONObject state = MainActivity.firstObject(yanm, "componentState", "ComponentState");
        if (state == null) {
            state = new JSONObject();
            yanm.put("componentState", (Object)state);
        }
        this.currentYanmSnapshot = yanm;
        this.currentYanmState = state;
        this.prefs.edit().putString(CACHE_YANM, yanm.toString()).apply();
        this.updateAllAppWidgets();
        YanmWidgetData.refreshComponentWidgets((Context)this);
        this.renderYanm(yanm);
        this.scheduleYanmComponentStateCloudSync(stateKey, value, reason);
        this.scheduleYanmCloudSync(reason);
    }

    private JSONObject buildYanmComponentSummary(JSONObject yanm, JSONObject component, int index, boolean includeHtml, JSONObject state) throws Exception {
        String componentId = MainActivity.getYanmComponentId(component, index);
        String title = MainActivity.getYanmComponentTitle(component, index);
        String stateKey = YanmWidgetData.resolveStateKey(component, componentId);
        String stateValue = state == null ? "" : state.optString(stateKey, "");
        String html = MainActivity.getYanmComponentHtml(component);
        JSONObject result = new JSONObject()
                .put("id", (Object)componentId)
                .put("title", (Object)title)
                .put("type", (Object)MainActivity.firstNonEmpty(component.optString("type"), component.optString("Type"), component.optString("kind"), component.optString("Kind"), "component"))
                .put("stateKey", (Object)stateKey)
                .put("stateLength", stateValue.length())
                .put("hasHtml", !html.isEmpty())
                .put("htmlLength", html.length())
                .put("locked", component.optBoolean("locked", component.optBoolean("Locked", false)));
        if (component.has("x") || component.has("X")) {
            result.put("x", component.optDouble("x", component.optDouble("X", 0)));
        }
        if (component.has("y") || component.has("Y")) {
            result.put("y", component.optDouble("y", component.optDouble("Y", 0)));
        }
        if (component.has("width") || component.has("Width")) {
            result.put("width", component.optDouble("width", component.optDouble("Width", 0)));
        }
        if (component.has("height") || component.has("Height")) {
            result.put("height", component.optDouble("height", component.optDouble("Height", 0)));
        }
        if (includeHtml) {
            result.put("html", (Object)html);
        }
        return result;
    }

    private String readAiToolString(JSONObject toolCall, String ... keys) {
        for (String key : keys) {
            if (toolCall.has(key)) {
                return toolCall.optString(key, "");
            }
        }
        return "";
    }

    private static int findYanmComponentIndex(JSONObject yanm, String componentIdOrTitle) {
        if (yanm == null || componentIdOrTitle == null || componentIdOrTitle.trim().isEmpty()) {
            return -1;
        }
        JSONArray components = MainActivity.firstArray(yanm, "components", "Components");
        if (components == null) {
            return -1;
        }
        String target = componentIdOrTitle.trim();
        for (int i = 0; i < components.length(); ++i) {
            JSONObject component = components.optJSONObject(i);
            if (component == null) {
                continue;
            }
            String id = MainActivity.getYanmComponentId(component, i);
            String title = MainActivity.getYanmComponentTitle(component, i);
            if (target.equalsIgnoreCase(id) || target.equalsIgnoreCase(title)) {
                return i;
            }
        }
        return -1;
    }

    private static String getYanmComponentId(JSONObject component, int index) {
        return MainActivity.firstNonEmpty(
                component == null ? "" : component.optString("id"),
                component == null ? "" : component.optString("Id"),
                component == null ? "" : component.optString("title"),
                component == null ? "" : component.optString("Title"),
                component == null ? "" : component.optString("name"),
                component == null ? "" : component.optString("Name"),
                "comp_" + index);
    }

    private static String getYanmComponentTitle(JSONObject component, int index) {
        return MainActivity.firstNonEmpty(
                component == null ? "" : component.optString("title"),
                component == null ? "" : component.optString("Title"),
                component == null ? "" : component.optString("name"),
                component == null ? "" : component.optString("Name"),
                "组件 " + (index + 1));
    }

    private static String getYanmComponentHtml(JSONObject component) {
        return MainActivity.firstNonEmpty(
                component == null ? "" : component.optString("html"),
                component == null ? "" : component.optString("Html"),
                component == null ? "" : component.optString("markup"),
                component == null ? "" : component.optString("Markup"),
                component == null ? "" : component.optString("contentHtml"),
                component == null ? "" : component.optString("ContentHtml"));
    }

    private static void putYanmStringProperty(JSONObject object, String lowerKey, String upperKey, String value) throws Exception {
        String targetKey = object.has(upperKey) && !object.has(lowerKey) ? upperKey : lowerKey;
        object.put(targetKey, (Object)(value == null ? "" : value));
    }

    private static String trimForAi(String value, int maxLength) {
        String text = value == null ? "" : value.replace("\r\n", "\n").replace('\r', '\n');
        if (maxLength <= 0 || text.length() <= maxLength) {
            return text;
        }
        return text.substring(0, maxLength) + "...";
    }

    private void pushMobileExtensionsToCloud() {
        this.executor.execute(() -> {
            try {
                String localJson = this.readLocalMobileExtensions().toString();
                
                String configStr = this.prefs.getString("personalSyncConfig", "{}");
                JSONObject cloudConfig = new JSONObject(configStr);
                boolean enabled = cloudConfig.optBoolean("enabled", false);
                String provider = cloudConfig.optString("provider", "none");
                
                if (enabled && !"none".equals(provider)) {
                    JSONObject secrets = cloudConfig.optJSONObject("secrets");
                    JSONObject settings = cloudConfig.optJSONObject("settings");
                    if (secrets == null) secrets = new JSONObject();
                    if (settings == null) settings = new JSONObject();
                    
                    if ("webdav".equals(provider)) {
                        JSONObject webDav = settings.optJSONObject("webDav");
                        String password = secrets.optString("webDavPassword", "");
                        if (webDav != null) {
                            YanziApiClient.WebDavConfig config = new YanziApiClient.WebDavConfig();
                            config.serverUrl = webDav.optString("url", "");
                            config.rootPath = webDav.optString("pathPrefix", "");
                            config.username = webDav.optString("username", "");
                            config.password = password;
                            
                            YanziApiClient.putWebDavBytes(config, "mobile-extensions.json", localJson.getBytes(StandardCharsets.UTF_8), "application/json");
                            Log.i("MainActivity", "Successfully pushed mobile extensions to WebDAV");
                            return;
                        }
                    } else if ("github".equals(provider)) {
                        JSONObject gitHub = settings.optJSONObject("github");
                        String token = secrets.optString("githubToken", "");
                        if (gitHub != null && !token.isEmpty()) {
                            String owner = gitHub.optString("username", "");
                            String repo = gitHub.optString("repo", "yanzi-sync");
                            String branch = gitHub.optString("branch", "main");
                            String pathPrefix = gitHub.optString("pathPrefix", "");
                            String relPath = pathPrefix.isEmpty() ? "mobile-extensions.json" : (pathPrefix.endsWith("/") ? pathPrefix : pathPrefix + "/") + "mobile-extensions.json";
                            
                            YanziApiClient.uploadFileToGitHub(token, owner, repo, branch, relPath, localJson);
                            Log.i("MainActivity", "Successfully pushed mobile extensions to GitHub");
                            return;
                        }
                    } else if ("gitee".equals(provider)) {
                        JSONObject gitee = settings.optJSONObject("gitee");
                        String token = secrets.optString("giteeToken", "");
                        if (gitee != null && !token.isEmpty()) {
                            String owner = gitee.optString("username", "");
                            String repo = gitee.optString("repo", "yanzi-sync");
                            String branch = gitee.optString("branch", "master");
                            String pathPrefix = gitee.optString("pathPrefix", "");
                            String relPath = pathPrefix.isEmpty() ? "mobile-extensions.json" : (pathPrefix.endsWith("/") ? pathPrefix : pathPrefix + "/") + "mobile-extensions.json";
                            
                            YanziApiClient.uploadFileToGitee(token, owner, repo, branch, relPath, localJson);
                            Log.i("MainActivity", "Successfully pushed mobile extensions to Gitee");
                            return;
                        }
                    }
                }
                
                try {
                    String baseUrl = this.normalizedBaseUrl();
                    String token = this.requireToken();
                    YanziApiClient.putMobileExtensions(baseUrl, token, localJson);
                } catch (Exception ex) {
                    if (MainActivity.isUnauthorized(ex)) {
                        String token = this.refreshToken();
                        String baseUrl = this.normalizedBaseUrl();
                        YanziApiClient.putMobileExtensions(baseUrl, token, localJson);
                    } else {
                        throw ex;
                    }
                }
            } catch (Exception e) {
                Log.e("MainActivity", "Failed to push mobile extensions to cloud", e);
            }
        });
    }

    private void syncMobileExtensionsFromCloud() {
        this.executor.execute(() -> {
            try {
                String cloudJsonStr = null;
                
                String configStr = this.prefs.getString("personalSyncConfig", "{}");
                JSONObject cloudConfig = new JSONObject(configStr);
                boolean enabled = cloudConfig.optBoolean("enabled", false);
                String provider = cloudConfig.optString("provider", "none");
                
                if (enabled && !"none".equals(provider)) {
                    JSONObject secrets = cloudConfig.optJSONObject("secrets");
                    JSONObject settings = cloudConfig.optJSONObject("settings");
                    if (secrets == null) secrets = new JSONObject();
                    if (settings == null) settings = new JSONObject();
                    
                    if ("webdav".equals(provider)) {
                        JSONObject webDav = settings.optJSONObject("webDav");
                        String password = secrets.optString("webDavPassword", "");
                        if (webDav != null) {
                            YanziApiClient.WebDavConfig config = new YanziApiClient.WebDavConfig();
                            config.serverUrl = webDav.optString("url", "");
                            config.rootPath = webDav.optString("pathPrefix", "");
                            config.username = webDav.optString("username", "");
                            config.password = password;
                            
                            byte[] data = YanziApiClient.getWebDavBytes(config, "mobile-extensions.json");
                            cloudJsonStr = (data == null) ? "[]" : new String(data, StandardCharsets.UTF_8);
                        }
                    } else if ("github".equals(provider)) {
                        JSONObject gitHub = settings.optJSONObject("github");
                        String token = secrets.optString("githubToken", "");
                        if (gitHub != null && !token.isEmpty()) {
                            String owner = gitHub.optString("username", "");
                            String repo = gitHub.optString("repo", "yanzi-sync");
                            String branch = gitHub.optString("branch", "main");
                            String pathPrefix = gitHub.optString("pathPrefix", "");
                            String relPath = pathPrefix.isEmpty() ? "mobile-extensions.json" : (pathPrefix.endsWith("/") ? pathPrefix : pathPrefix + "/") + "mobile-extensions.json";
                            
                            cloudJsonStr = YanziApiClient.fetchFileFromGitHub(token, owner, repo, branch, relPath);
                        }
                    } else if ("gitee".equals(provider)) {
                        JSONObject gitee = settings.optJSONObject("gitee");
                        String token = secrets.optString("giteeToken", "");
                        if (gitee != null && !token.isEmpty()) {
                            String owner = gitee.optString("username", "");
                            String repo = gitee.optString("repo", "yanzi-sync");
                            String branch = gitee.optString("branch", "master");
                            String pathPrefix = gitee.optString("pathPrefix", "");
                            String relPath = pathPrefix.isEmpty() ? "mobile-extensions.json" : (pathPrefix.endsWith("/") ? pathPrefix : pathPrefix + "/") + "mobile-extensions.json";
                            
                            cloudJsonStr = YanziApiClient.fetchFileFromGitee(token, owner, repo, branch, relPath);
                        }
                    }
                }
                
                if (cloudJsonStr == null) {
                    String baseUrl = this.normalizedBaseUrl();
                    String token = this.requireToken();
                    try {
                        cloudJsonStr = YanziApiClient.fetchMobileExtensions(baseUrl, token);
                    } catch (Exception ex) {
                        if (!MainActivity.isUnauthorized(ex)) {
                            throw ex;
                        }
                        token = this.refreshToken();
                        cloudJsonStr = YanziApiClient.fetchMobileExtensions(baseUrl, token);
                    }
                }
                
                final String finalCloudJson = cloudJsonStr;
                this.runOnUiThread(() -> {
                    try {
                        JSONArray cloudArray = new JSONArray(finalCloudJson);
                        JSONArray localArray = this.readLocalMobileExtensions();
                        
                        java.util.Map<String, JSONObject> mergedMap = new java.util.LinkedHashMap<>();
                        
                        for (int i = 0; i < localArray.length(); ++i) {
                            JSONObject item = localArray.optJSONObject(i);
                            if (item != null) {
                                String id = item.optString("id");
                                if (!id.isEmpty()) {
                                    mergedMap.put(id, item);
                                }
                            }
                        }
                        
                        for (int i = 0; i < cloudArray.length(); ++i) {
                            JSONObject item = cloudArray.optJSONObject(i);
                            if (item != null) {
                                String id = item.optString("id");
                                if (!id.isEmpty()) {
                                    mergedMap.put(id, item);
                                }
                            }
                        }
                        
                        JSONArray mergedArray = new JSONArray();
                        for (JSONObject obj : mergedMap.values()) {
                            mergedArray.put(obj);
                        }
                        
                        this.prefs.edit().putString("mobileExtensions", mergedArray.toString()).apply();
                        this.renderLocalMobileExtensions();
                        this.setStatus("\u624b\u673a\u6269\u5c55\u5df2\u540c\u6b65\u3002");
                        
                        this.pushMobileExtensionsToCloud();
                        
                    } catch (Exception e) {
                        this.setStatus("\u540c\u6b65\u624b\u673a\u6269\u5c55\u89e3\u6790\u5931\u8d25\uff1a" + e.getMessage());
                    }
                });
            } catch (Exception e) {
                this.runOnUiThread(() -> {
                    this.setStatus("\u62c9\u53d6\u4e91\u7aef\u624b\u673a\u6269\u5c55\u5931\u8d25\uff1a" + e.getMessage());
                });
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
        card.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);
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
        input.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
        input.setHintTextColor(ThemeConfig.COLOR_HINT);
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
        button.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
        button.setAllCaps(false);
        GradientDrawable gd = new GradientDrawable();
        gd.setColor(ThemeConfig.COLOR_BUTTON_BG);
        gd.setCornerRadius((float)this.dp(8));
        button.setBackground((Drawable)gd);
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
            return headEnd >= 0 ? trimmed.substring(0, headEnd) + mobileHead + bridge + trimmed.substring(headEnd) : trimmed.replaceFirst("(?i)<html[^>]*>", "$0<head>" + mobileHead + bridge + "</head>");
        }
        return "<!doctype html><html><head>" + mobileHead + bridge + "</head><body>" + trimmed + "</body></html>";
    }

    private String buildMobileScriptHtml(String source) {
        return "<!doctype html><html><body><script>window.context={mobile:{toast:function(text){yanziMobileJsHost.toast(String(text||''));},sendToDesktop:function(text){yanziMobileJsHost.sendToDesktop(String(text||''));},done:function(text){yanziMobileJsHost.done(String(text||''));},fail:function(text){yanziMobileJsHost.fail(String(text||''));},getSharedText:function(){return yanziMobileJsHost.getSharedText();},getClipboardText:function(){return Promise.resolve(yanziMobileJsHost.getClipboardText());},setClipboardText:function(text){return Promise.resolve(yanziMobileJsHost.setClipboardText(String(text||'')));},openUrl:function(url){return Promise.resolve(yanziMobileJsHost.openUrl(String(url||'')));},pickPhoto:function(){return Promise.resolve(yanziMobileJsHost.pickPhoto());},readTextFile:function(name){return Promise.resolve(JSON.parse(yanziMobileJsHost.readTextFile(String(name||''))));},saveTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.saveTextFile(String(name||''),String(text||''))));},appendTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.appendTextFile(String(name||''),String(text||''))));},httpGet:function(url){return Promise.resolve(JSON.parse(yanziMobileJsHost.httpGet(String(url||''))));},httpPostJson:function(url,jsonText){return Promise.resolve(JSON.parse(yanziMobileJsHost.httpPostJson(String(url||''),String(jsonText||''))));},getBatteryLevel:function(){return yanziMobileJsHost.getBatteryLevel();},getScreenBrightness:function(){return yanziMobileJsHost.getScreenBrightness();},setScreenBrightness:function(val){yanziMobileJsHost.setScreenBrightness(Number(val||0));},getLocation:function(){return Promise.resolve(JSON.parse(yanziMobileJsHost.getLocation()));},listScriptFiles:function(){return Promise.resolve(JSON.parse(yanziMobileJsHost.listScriptFiles()));},deleteScriptFile:function(name){return Promise.resolve(JSON.parse(yanziMobileJsHost.deleteScriptFile(String(name||''))));}}};async function __run(){try{" + source + "\n;if(typeof run==='function'){await run(window.context);}yanziMobileJsHost.done('\u811a\u672c\u6267\u884c\u5b8c\u6210');}catch(e){yanziMobileJsHost.fail(String(e&&e.message?e.message:e));}}__run();</script></body></html>";
    }

    private void executeMobileScriptHeadless(String source, String taskName, ScriptCallback callback) {
        this.runOnUiThread(() -> {
            block2: {
                try {
                    WebView runner = new WebView((Context)this);
                    runner.getSettings().setJavaScriptEnabled(true);
                    runner.addJavascriptInterface((Object)new MobileJsBridge(callback), "yanziMobileJsHost");
                    runner.loadDataWithBaseURL("http://localhost/", this.buildMobileScriptHtml(source), "text/html", "UTF-8", null);
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

    private void runLocalMobileExtensionByIdSilently(String id, String name) {
        try {
            JSONArray array = this.readLocalMobileExtensions();
            JSONObject targetItem = null;
            for (int i = 0; i < array.length(); ++i) {
                JSONObject item = array.optJSONObject(i);
                if (item != null && id.equals(item.optString("id"))) {
                    targetItem = item;
                    break;
                }
            }
            if (targetItem == null) {
                Toast.makeText((Context)this, "\u672a\u627e\u5230\u624b\u673a\u6269\u5c55\uff1a" + name, Toast.LENGTH_SHORT).show();
                this.finish();
                return;
            }
            String code = targetItem.optString("code");
            if (code == null || code.isEmpty()) {
                JSONObject script = targetItem.optJSONObject("script");
                if (script != null) {
                    code = script.optString("source");
                }
            }
            if (code != null && !code.isEmpty()) {
                Toast.makeText((Context)this, "\u6b63\u5728\u8fd0\u884c\u6269\u5c55\uff1a" + name, Toast.LENGTH_SHORT).show();
                this.executeMobileScriptHeadless(code, name, result -> {
                    this.runOnUiThread(() -> this.finish());
                });
            } else {
                Toast.makeText((Context)this, "\u6269\u5c55\u65e0\u53ef\u6267\u884c\u4ee3\u7801\uff1a" + name, Toast.LENGTH_SHORT).show();
                this.finish();
            }
        } catch (Exception ex) {
            Toast.makeText((Context)this, "\u542f\u52a8\u5931\u8d25\uff1a" + ex.getMessage(), Toast.LENGTH_SHORT).show();
            this.finish();
        }
    }

    private void createLocalMobileExtensionShortcut(String id, String name, String iconName) {
        try {
            if (Build.VERSION.SDK_INT >= 26) {
                ShortcutManager shortcutManager = (ShortcutManager)this.getSystemService(ShortcutManager.class);
                if (shortcutManager != null && shortcutManager.isRequestPinShortcutSupported()) {
                    Intent shortcutIntent = new Intent((Context)this, MainActivity.class);
                    shortcutIntent.setAction("android.intent.action.VIEW");
                    shortcutIntent.putExtra("run_mobile_extension_id", id);
                    shortcutIntent.putExtra("run_mobile_extension_name", name);
                    shortcutIntent.addFlags(0x14000000);
                    
                    int size = 192;
                    Bitmap bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
                    Canvas canvas = new Canvas(bitmap);
                    Paint bgPaint = new Paint(1);
                    bgPaint.setStyle(Paint.Style.FILL);
                    
                    int colorIndex = Math.abs(id.hashCode()) % 5;
                    int iconBgColor = Color.rgb(59, 130, 246);
                    if (colorIndex == 1) iconBgColor = Color.rgb(16, 185, 129);
                    else if (colorIndex == 2) iconBgColor = Color.rgb(239, 68, 68);
                    else if (colorIndex == 3) iconBgColor = Color.rgb(245, 158, 11);
                    else if (colorIndex == 4) iconBgColor = Color.rgb(139, 92, 246);
                    bgPaint.setColor(iconBgColor);
                    
                    float radius = (float)size * 0.22f;
                    canvas.drawRoundRect(new RectF(0.0f, 0.0f, (float)size, (float)size), radius, radius, bgPaint);
                    
                    String cleanIcon = iconName;
                    if (cleanIcon.startsWith("mdi:")) {
                        cleanIcon = cleanIcon.substring(4);
                    }
                    Path iconPath = MobileIconLibrary.resolveOrDefault(cleanIcon);
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
                    
                    Icon icon = Icon.createWithBitmap(bitmap);
                    ShortcutInfo shortcutInfo = new ShortcutInfo.Builder((Context)this, "mobext_" + id)
                        .setShortLabel((CharSequence)name)
                        .setLongLabel((CharSequence)name)
                        .setIcon(icon)
                        .setIntent(shortcutIntent)
                        .build();
                    boolean success = shortcutManager.requestPinShortcut(shortcutInfo, null);
                    if (success) {
                        this.setStatus("\u5df2\u5411\u7cfb\u7edf\u53d1\u9001\u521b\u5efa\u684c\u9762\u56fe\u6807\u8bf7\u6c42\uff1a" + name);
                        Toast.makeText((Context)this, (CharSequence)("\u5df2\u5411\u7cfb\u7edf\u53d1\u9001\u521b\u5efa\u684c\u9762\u56fe\u6807\u8bf7\u6c42\uff1a" + name), Toast.LENGTH_SHORT).show();
                    } else {
                        this.setStatus("\u7cfb\u7edf\u62d2\u7edd\u4e86\u5feb\u6377\u65b9\u5f0f\u521b\u5efa\u8bf7\u6c42");
                        Toast.makeText((Context)this, (CharSequence)"\u7cfb\u7edf\u62d2\u7edd\u4e86\u5feb\u6377\u65b9\u5f0f\u521b\u5efa\u8bf7\u6c42(\u8bf7\u68c0\u67e5\u5feb\u6377\u65b9\u5f0f\u6743\u9650)", Toast.LENGTH_LONG).show();
                    }
                    return;
                }
                this.setStatus("\u5f53\u524d\u7cfb\u7edf\u6216\u684c\u9762\u0020\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f");
                Toast.makeText((Context)this, (CharSequence)"\u5f53\u524d\u7cfb\u7edf\u6216\u684c\u9762\u0020\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f", Toast.LENGTH_SHORT).show();
            } else {
                this.setStatus("\u5f53\u524d\u7cfb\u7edf\u7248\u672c\u8f83\u4f4e\uff0c\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f");
                Toast.makeText((Context)this, (CharSequence)"\u5f53\u524d\u7cfb\u7edf\u7248\u672c\u8f83\u4f4e\uff0c\u4e0d\u652f\u6301\u521b\u5efa\u5feb\u6377\u65b9\u5f0f", Toast.LENGTH_SHORT).show();
            }
        }
        catch (Exception ex) {
            this.setStatus("\u521b\u5efa\u684c\u9762\u56fe\u6807\u5931\u8d25\uff1a" + ex.getMessage());
            Toast.makeText((Context)this, (CharSequence)("\u521b\u5efa\u684c\u9762\u56fe\u6807\u5931\u8d25\uff1a" + ex.getMessage()), Toast.LENGTH_SHORT).show();
        }
    }

    private void addLocalMobileExtensionToWheel(String id, String name) {
        try {
            JSONArray array = this.readLocalMobileExtensions();
            boolean[] occupied = new boolean[6];
            for (int i = 0; i < array.length(); ++i) {
                JSONObject item = array.optJSONObject(i);
                if (item != null) {
                    int slot = item.optInt("_wheelSlot", -1);
                    if (slot >= 0 && slot < 6) {
                        occupied[slot] = true;
                    }
                }
            }
            
            int freeSlot = -1;
            for (int slot = 0; slot < 6; ++slot) {
                if (!occupied[slot]) {
                    freeSlot = slot;
                    break;
                }
            }
            
            if (freeSlot == -1) {
                Toast.makeText((Context)this, "\u71d5\u73af\u8f6e\u76d8\u5df2\u6ee1\u0020(\u5171\u00206\u0020\u4e2a\u69fd\u4f4d)\uff0c\u8bf7\u5148\u5728\u60ac\u6d6e\u8f6e\u76d8\u4e0a\u957f\u6309\u5df2\u6709\u63d2\u69fd\u8fdb\u884c\u6e05\u9664\u3002", Toast.LENGTH_LONG).show();
                return;
            }
            
            JSONObject targetItem = null;
            for (int i = 0; i < array.length(); ++i) {
                JSONObject item = array.optJSONObject(i);
                if (item != null && id.equals(item.optString("id"))) {
                    targetItem = item;
                    break;
                }
            }
            
            if (targetItem != null) {
                targetItem.put("_wheelSlot", freeSlot);
                this.prefs.edit().putString("mobileExtensions", array.toString()).apply();
                this.renderLocalMobileExtensions();
                Toast.makeText((Context)this, "\u6210\u529f\u5c06\u201c" + name + "\u201d\u6dfb\u52a0\u5230\u71d5\u73af\u8f6e\u76d8\u63d2\u69fd\u0020" + (freeSlot + 1), Toast.LENGTH_SHORT).show();
            }
        }
        catch (Exception ex) {
            Toast.makeText((Context)this, "\u6dfb\u52a0\u5230\u71d5\u73af\u5931\u8d25\uff1a" + ex.getMessage(), Toast.LENGTH_SHORT).show();
        }
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
            MainActivity.this.appendMobileShellLog("[API] toast: " + text);
            MainActivity.this.runOnUiThread(() -> Toast.makeText((Context)MainActivity.this, (CharSequence)text, (int)0).show());
        }

        @JavascriptInterface
        public void sendToDesktop(String text) {
            MainActivity.this.appendMobileShellLog("[API] sendToDesktop: " + text);
            MainActivity.this.runOnUiThread(() -> MainActivity.this.sendTextValueToDesktop(text, "\u624b\u673a\u811a\u672c\u6b63\u5728\u53d1\u9001\u5230\u7535\u8111..."));
        }

        @JavascriptInterface
        public String getSharedText() {
            String val = MainActivity.this.textInput == null ? "" : MainActivity.this.textInput.getText().toString();
            MainActivity.this.appendMobileShellLog("[API] getSharedText -> " + val);
            return val;
        }

        @JavascriptInterface
        public String getClipboardText() {
            ClipboardManager manager = (ClipboardManager)MainActivity.this.getSystemService("clipboard");
            if (manager == null || manager.getPrimaryClip() == null || manager.getPrimaryClip().getItemCount() == 0) {
                MainActivity.this.appendMobileShellLog("[API] getClipboardText -> (empty)");
                return "";
            }
            CharSequence value = manager.getPrimaryClip().getItemAt(0).coerceToText((Context)MainActivity.this);
            String val = value == null ? "" : value.toString();
            MainActivity.this.appendMobileShellLog("[API] getClipboardText -> " + val);
            return val;
        }

        @JavascriptInterface
        public String setClipboardText(String text) {
            MainActivity.this.appendMobileShellLog("[API] setClipboardText: " + text);
            ClipboardManager manager = (ClipboardManager)MainActivity.this.getSystemService("clipboard");
            if (manager != null) {
                manager.setPrimaryClip(ClipData.newPlainText((CharSequence)"Yanzi mobile script", (CharSequence)(text == null ? "" : text)));
            }
            return text == null ? "" : text;
        }

        @JavascriptInterface
        public String openUrl(String url) {
            MainActivity.this.appendMobileShellLog("[API] openUrl: " + url);
            MainActivity.this.runOnUiThread(() -> {
                Intent intent = new Intent("android.intent.action.VIEW", Uri.parse((String)url));
                MainActivity.this.startActivity(intent);
            });
            return url;
        }

        @JavascriptInterface
        public String pickPhoto() {
            MainActivity.this.appendMobileShellLog("[API] pickPhoto");
            MainActivity.this.runOnUiThread(() -> MainActivity.this.pickPhotoFromGallery());
            return "ok";
        }

        /*
         * Enabled aggressive exception aggregation
         */
        @JavascriptInterface
        public String readTextFile(String name) {
            MainActivity.this.appendMobileShellLog("[API] readTextFile: " + name);
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
            MainActivity.this.appendMobileShellLog("[API] saveTextFile: " + name + " (len=" + (text != null ? text.length() : 0) + ")");
            return this.writeTextFile(name, text, false);
        }

        @JavascriptInterface
        public String appendTextFile(String name, String text) {
            MainActivity.this.appendMobileShellLog("[API] appendTextFile: " + name + " (len=" + (text != null ? text.length() : 0) + ")");
            return this.writeTextFile(name, text, true);
        }

        @JavascriptInterface
        public String httpGet(String url) {
            MainActivity.this.appendMobileShellLog("[API] httpGet: " + url);
            return this.runHttpRequest("GET", url, null, null);
        }

        @JavascriptInterface
        public String httpPostJson(String url, String jsonText) {
            MainActivity.this.appendMobileShellLog("[API] httpPostJson: " + url);
            return this.runHttpRequest("POST", url, jsonText, "application/json; charset=utf-8");
        }

        @JavascriptInterface
        public void done(String text) {
            MainActivity.this.appendMobileShellLog("[API] done: " + text);
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
            MainActivity.this.appendMobileShellLog("[API] fail: " + text);
            MainActivity.this.runOnUiThread(() -> {
                MainActivity.this.updateMobileScriptResult("\u6d4b\u8bd5\u5931\u8d25\uff1a " + text, true);
                MainActivity.this.setStatus("\u624b\u673a\u811a\u672c\u6267\u884c\u5931\u8d25\uff1a" + text);
                if (this.callback != null) {
                    this.callback.onResult("\u5931\u8d25: " + text);
                }
            });
        }

        @JavascriptInterface
        public int getBatteryLevel() {
            MainActivity.this.appendMobileShellLog("[API] getBatteryLevel");
            try {
                if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.LOLLIPOP) {
                    android.os.BatteryManager bm = (android.os.BatteryManager) MainActivity.this.getSystemService(Context.BATTERY_SERVICE);
                    if (bm != null) {
                        return bm.getIntProperty(android.os.BatteryManager.BATTERY_PROPERTY_CAPACITY);
                    }
                }
                Intent intent = MainActivity.this.registerReceiver(null, new android.content.IntentFilter(Intent.ACTION_BATTERY_CHANGED));
                if (intent != null) {
                    int level = intent.getIntExtra(android.os.BatteryManager.EXTRA_LEVEL, -1);
                    int scale = intent.getIntExtra(android.os.BatteryManager.EXTRA_SCALE, -1);
                    if (level != -1 && scale != -1 && scale != 0) {
                        return (level * 100) / scale;
                    }
                }
            } catch (Exception e) {
                MainActivity.this.appendMobileShellLog("[SYSTEM] \u83b7\u53d6\u7535\u91cf\u5931\u8d25: " + e.getMessage());
            }
            return -1;
        }

        @JavascriptInterface
        public float getScreenBrightness() {
            MainActivity.this.appendMobileShellLog("[API] getScreenBrightness");
            try {
                android.view.WindowManager.LayoutParams lp = MainActivity.this.getWindow().getAttributes();
                if (lp.screenBrightness < 0) {
                    int val = android.provider.Settings.System.getInt(MainActivity.this.getContentResolver(), android.provider.Settings.System.SCREEN_BRIGHTNESS);
                    return val / 255.0f;
                }
                return lp.screenBrightness;
            } catch (Exception e) {
                MainActivity.this.appendMobileShellLog("[SYSTEM] \u83b7\u53d6\u4eae\u5ea6\u5931\u8d25: " + e.getMessage());
                return 0.5f;
            }
        }

        @JavascriptInterface
        public void setScreenBrightness(float brightness) {
            MainActivity.this.appendMobileShellLog("[API] setScreenBrightness: " + brightness);
            MainActivity.this.runOnUiThread(() -> {
                try {
                    android.view.WindowManager.LayoutParams lp = MainActivity.this.getWindow().getAttributes();
                    lp.screenBrightness = Math.max(0.0f, Math.min(1.0f, brightness));
                    MainActivity.this.getWindow().setAttributes(lp);
                } catch (Exception e) {
                    MainActivity.this.appendMobileShellLog("[SYSTEM] \u8bbe\u7f6e\u4eae\u5ea6\u5931\u8d25: " + e.getMessage());
                }
            });
        }

        @JavascriptInterface
        public String getLocation() {
            MainActivity.this.appendMobileShellLog("[API] getLocation: \u6b63\u5728\u901a\u8fc7\u7f51\u7edc\u83b7\u53d6\u7c97\u7565\u5b9a\u4f4d...");
            return this.runHttpRequest("GET", "http://ip-api.com/json?lang=zh-CN", null, null);
        }

        @JavascriptInterface
        public String listScriptFiles() {
            MainActivity.this.appendMobileShellLog("[API] listScriptFiles");
            try {
                File dir = MainActivity.this.resolveMobileScriptFile("");
                File[] files = dir.listFiles();
                JSONArray arr = new JSONArray();
                if (files != null) {
                    for (File f : files) {
                        if (f.isFile()) {
                            JSONObject jobj = new JSONObject();
                            jobj.put("name", f.getName());
                            jobj.put("size", f.length());
                            jobj.put("lastModified", f.lastModified());
                            arr.put(jobj);
                        }
                    }
                }
                return new JSONObject().put("ok", true).put("files", (Object)arr).toString();
            } catch (Exception e) {
                return MainActivity.buildJsonErrorResult(e.getMessage());
            }
        }

        @JavascriptInterface
        public String deleteScriptFile(String name) {
            MainActivity.this.appendMobileShellLog("[API] deleteScriptFile: " + name);
            try {
                File file = MainActivity.this.resolveMobileScriptFile(name);
                if (!file.exists()) {
                    return new JSONObject().put("ok", false).put("error", (Object)"\u6587\u4ef6\u4e0d\u5b58\u5728").toString();
                }
                boolean deleted = file.delete();
                return new JSONObject().put("ok", deleted).toString();
            } catch (Exception e) {
                return MainActivity.buildJsonErrorResult(e.getMessage());
            }
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
            if (this.componentId != null && !this.componentId.trim().isEmpty()) {
                String scopedKey = "component:" + this.componentId.trim() + ":" + key;
                if (state.has(scopedKey)) {
                    return state.optString(scopedKey, "");
                }
            }
            return state.optString(key, "");
        }

        @JavascriptInterface
        public void setState(String key, String value) {
            try {
                if (MainActivity.this.currentYanmState == null) {
                    MainActivity.this.currentYanmState = new JSONObject();
                }
                String actualKey = key;
                if (this.componentId != null && !this.componentId.trim().isEmpty()) {
                    actualKey = "component:" + this.componentId.trim() + ":" + key;
                }
                MainActivity.this.currentYanmState.put(actualKey, (Object)value);
                if (MainActivity.this.currentYanmSnapshot == null) {
                    MainActivity.this.currentYanmSnapshot = new JSONObject();
                }
                MainActivity.this.currentYanmSnapshot.put("componentState", (Object)MainActivity.this.currentYanmState);
                
                final String finalKey = actualKey;
                MainActivity.this.runOnUiThread(() -> {
                    MainActivity.this.prefs.edit().putString(CACHE_YANM, MainActivity.this.currentYanmSnapshot.toString()).apply();
                    MainActivity.this.updateAllAppWidgets();
                    YanmWidgetData.refreshComponentWidgets((Context)MainActivity.this);
                    MainActivity.this.setStatus("\u71d5\u5e55\u72b6\u6001\u5df2\u5728\u624b\u673a\u7aef\u66f4\u65b0\uff1a" + this.componentTitle + " / " + finalKey);
                    MainActivity.this.scheduleYanmComponentStateCloudSync(finalKey, value, this.componentTitle + " / " + finalKey);
                    MainActivity.this.scheduleYanmCloudSync(this.componentTitle + " / " + finalKey);
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
        public static boolean sLanFailedThisSession = false;

        static String login(String baseUrl, String email, String password) throws Exception {
            return loginResponse(baseUrl, email, password).getString("accessToken");
        }

        static JSONObject loginResponse(String baseUrl, String email, String password) throws Exception {
            JSONObject payload = new JSONObject().put("email", (Object)email).put("password", (Object)password);
            return YanziApiClient.postJson(baseUrl, "/v1/auth/login", payload, null, "\u767b\u5f55");
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
            JSONObject payload = YanziApiClient.getJson(baseUrl, "/v1/me/extensions", token, "读取扩展列表");
            JSONArray items = payload.optJSONArray("items");
            ArrayList<RemoteExtension> result = new ArrayList<RemoteExtension>();
            if (items == null) {
                return result;
            }

            java.util.concurrent.ExecutorService pool = java.util.concurrent.Executors.newFixedThreadPool(8);
            List<java.util.concurrent.Future<RemoteExtension>> futures = new ArrayList<java.util.concurrent.Future<RemoteExtension>>();

            for (int i = 0; i < items.length(); ++i) {
                JSONObject item = items.optJSONObject(i);
                if (item == null || item.optInt("enabled", 1) == 0) continue;
                final String extensionId = MainActivity.firstNonEmpty(new String[]{item.optString("extension_id"), item.optString("extensionId"), item.optString("ExtensionId"), item.optString("Extension_id")});
                if (extensionId.isEmpty() || "yanzi-webdav-settings".equals(extensionId) || "yanzi-webdav-setting".equals(extensionId) || "yanzi-quickpanel-settings".equals(extensionId) || "yanzi-quickpanel-setting".equals(extensionId) || "yanzi-personal-sync-settings".equals(extensionId) || "yanzi-personal-sync-setting".equals(extensionId) || "yanzi-ai-settings".equals(extensionId) || "yanzi-ai-setting".equals(extensionId) || "yanzi-general-settings".equals(extensionId) || "yanzi-general-setting".equals(extensionId)) continue;
                
                futures.add(pool.submit(new java.util.concurrent.Callable<RemoteExtension>() {
                    @Override
                    public RemoteExtension call() {
                        try {
                            JSONObject detail = YanziApiClient.getJson(baseUrl, "/v1/extensions/" + YanziApiClient.encodePath(extensionId), token, "读取扩展详情");
                            JSONObject manifest = detail.optJSONObject("manifest");
                            String name = MainActivity.firstNonEmpty(new String[]{detail.optString("display_name"), detail.optString("displayName"), detail.optString("DisplayName"), detail.optString("name"), detail.optString("Name"), manifest == null ? "" : manifest.optString("name"), manifest == null ? "" : manifest.optString("Name"), manifest == null ? "" : manifest.optString("display_name"), manifest == null ? "" : manifest.optString("displayName"), manifest == null ? "" : manifest.optString("DisplayName"), extensionId});
                            String description = MainActivity.firstNonEmpty(new String[]{detail.optString("description"), detail.optString("Description"), manifest == null ? "" : manifest.optString("description"), manifest == null ? "" : manifest.optString("Description")});
                            String icon = MainActivity.firstNonEmpty(new String[]{detail.optString("icon"), detail.optString("Icon"), manifest == null ? "" : manifest.optString("icon"), manifest == null ? "" : manifest.optString("Icon")});
                            String accentHex = MainActivity.firstNonEmpty(new String[]{detail.optString("accent_hex"), detail.optString("accentHex"), detail.optString("AccentHex"), manifest == null ? "" : manifest.optString("accent_hex"), manifest == null ? "" : manifest.optString("accentHex"), manifest == null ? "" : manifest.optString("AccentHex")});
                            return new RemoteExtension(extensionId, name, description, icon, accentHex);
                        }
                        catch (Exception ignored) {
                            return new RemoteExtension(extensionId, extensionId, "扩展详情暂不可用，仍可尝试远程执行。", "", "");
                        }
                    }
                }));
            }

            for (java.util.concurrent.Future<RemoteExtension> future : futures) {
                try {
                    result.add(future.get());
                }
                catch (Exception ignored) {}
            }
            pool.shutdown();
            return result;
        }

        static JSONObject fetchYanmState(String baseUrl, String token) throws Exception {
            JSONObject payload = YanziApiClient.getJson(baseUrl, "/v1/me/yanm-state", token, "\u8bfb\u53d6\u71d5\u5e55");
            JSONObject yanm = payload.optJSONObject("yanm");
            if (yanm == null) {
                throw new IllegalStateException("\u8d26\u53f7\u4e91\u7aef\u6ca1\u6709\u71d5\u5e55\u6570\u636e\u3002");
            }
            String viewUrl = payload.optString("viewUrl", "");
            if (!viewUrl.isEmpty() && sContext != null) {
                sContext.getSharedPreferences("yanzi-mobile", 0).edit().putString("yanm_view_url", viewUrl).apply();
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
            JSONObject res = YanziApiClient.putJson(baseUrl, "/v1/me/yanm-state", payload, token, "\u540c\u6b65\u71d5\u5e55");
            String viewUrl = res.optString("viewUrl", "");
            if (!viewUrl.isEmpty() && sContext != null) {
                sContext.getSharedPreferences("yanzi-mobile", 0).edit().putString("yanm_view_url", viewUrl).apply();
            }
            return res;
        }

        static JSONObject putYanmComponentState(String baseUrl, String token, JSONObject componentState) throws Exception {
            JSONObject payload = new JSONObject()
                    .put("updatedAtUtc", (Object)new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT).format(new Date()))
                    .put("componentState", (Object)componentState);
            JSONObject res = YanziApiClient.putJson(baseUrl, "/v1/me/yanm-state/component-state", payload, token, "\u540c\u6b65\u71d5\u5e55\u540e\u7aef\u6570\u636e");
            String viewUrl = res.optString("viewUrl", "");
            if (!viewUrl.isEmpty() && sContext != null) {
                sContext.getSharedPreferences("yanzi-mobile", 0).edit().putString("yanm_view_url", viewUrl).apply();
            }
            return res;
        }

        static String fetchPersonalConfig(String baseUrl, String token) throws Exception {
            JSONObject payload = YanziApiClient.getJson(baseUrl, "/v1/sync/personal-config", token, "\u8bfb\u53d6\u540c\u6b65\u914d\u7f6e");
            return payload.toString();
        }

        static String fetchMobileExtensions(String baseUrl, String token) throws Exception {
            JSONObject payload = YanziApiClient.getJson(baseUrl, "/v1/me/mobile/extensions", token, "\u8bfb\u53d6\u624b\u673a\u6269\u5c55");
            return payload.optString("extensions", "[]");
        }

        static void putMobileExtensions(String baseUrl, String token, String extensionsJson) throws Exception {
            JSONObject payload = new JSONObject().put("extensions", (Object)extensionsJson);
            YanziApiClient.putJson(baseUrl, "/v1/me/mobile/extensions", payload, token, "\u540c\u6b65\u624b\u673a\u6269\u5c55");
        }

        static byte[] getWebDavBytes(WebDavConfig config, String relativePath) throws Exception {
            HttpURLConnection connection = YanziApiClient.openWebDav(config, relativePath);
            connection.setRequestMethod("GET");
            int status = connection.getResponseCode();
            if (status == 404) {
                return null;
            }
            if (status < 200 || status >= 300) {
                throw new IllegalStateException("WebDAV GET failed: " + status);
            }
            InputStream is = connection.getInputStream();
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buffer = new byte[8192];
            int len;
            while ((len = is.read(buffer)) != -1) {
                bos.write(buffer, 0, len);
            }
            is.close();
            return bos.toByteArray();
        }

        static String fetchFileFromGitHub(String token, String owner, String repo, String branch, String relativePath) throws Exception {
            String urlStr = "https://api.github.com/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("Authorization", "Bearer " + token.trim());
            conn.setRequestProperty("Accept", "application/vnd.github.raw");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            
            int code = conn.getResponseCode();
            if (code == 404) {
                return "[]";
            }
            if (code < 200 || code >= 300) {
                throw new java.io.IOException("GitHub read failed: " + code);
            }
            InputStream is = conn.getInputStream();
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int len;
            while ((len = is.read(buf)) != -1) {
                bos.write(buf, 0, len);
            }
            is.close();
            return bos.toString("UTF-8");
        }

        static void uploadFileToGitHub(String token, String owner, String repo, String branch, String relativePath, String content) throws Exception {
            String sha = null;
            String urlStr = "https://api.github.com/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("Authorization", "Bearer " + token.trim());
            conn.setRequestProperty("Accept", "application/json");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            
            int code = conn.getResponseCode();
            if (code == 200) {
                InputStream is = conn.getInputStream();
                ByteArrayOutputStream bos = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int len;
                while ((len = is.read(buf)) != -1) {
                    bos.write(buf, 0, len);
                }
                is.close();
                JSONObject res = new JSONObject(bos.toString("UTF-8"));
                sha = res.optString("sha", null);
            }
            
            HttpURLConnection putConn = (HttpURLConnection) new URL("https://api.github.com/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath)).openConnection();
            putConn.setRequestMethod("PUT");
            putConn.setRequestProperty("Authorization", "Bearer " + token.trim());
            putConn.setRequestProperty("Content-Type", "application/json");
            putConn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            putConn.setDoOutput(true);
            
            JSONObject payload = new JSONObject();
            payload.put("message", "Sync mobile-extensions.json from Mobile");
            String base64Content = Base64.encodeToString(content.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
            payload.put("content", base64Content);
            if (sha != null) {
                payload.put("sha", sha);
            }
            payload.put("branch", branch);
            
            OutputStream os = putConn.getOutputStream();
            os.write(payload.toString().getBytes(StandardCharsets.UTF_8));
            os.flush();
            os.close();
            
            int putCode = putConn.getResponseCode();
            if (putCode < 200 || putCode >= 300) {
                throw new java.io.IOException("GitHub write failed: " + putCode);
            }
        }

        static String fetchFileFromGitee(String token, String owner, String repo, String branch, String relativePath) throws Exception {
            String urlStr = "https://gitee.com/api/v5/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?access_token=" + token.trim() + "&ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            
            int code = conn.getResponseCode();
            if (code == 404) {
                return "[]";
            }
            if (code < 200 || code >= 300) {
                throw new java.io.IOException("Gitee read failed: " + code);
            }
            InputStream is = conn.getInputStream();
            ByteArrayOutputStream bos = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int len;
            while ((len = is.read(buf)) != -1) {
                bos.write(buf, 0, len);
            }
            is.close();
            JSONObject res = new JSONObject(bos.toString("UTF-8"));
            String contentBase64 = res.optString("content", "");
            if (contentBase64.isEmpty()) {
                return "[]";
            }
            byte[] decoded = Base64.decode(contentBase64, Base64.DEFAULT);
            return new String(decoded, StandardCharsets.UTF_8);
        }

        static void uploadFileToGitee(String token, String owner, String repo, String branch, String relativePath, String content) throws Exception {
            String sha = null;
            String urlStr = "https://gitee.com/api/v5/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath) + "?access_token=" + token.trim() + "&ref=" + encodePath(branch);
            URL url = new URL(urlStr);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            
            int code = conn.getResponseCode();
            if (code == 200) {
                InputStream is = conn.getInputStream();
                ByteArrayOutputStream bos = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int len;
                while ((len = is.read(buf)) != -1) {
                    bos.write(buf, 0, len);
                }
                is.close();
                JSONObject res = new JSONObject(bos.toString("UTF-8"));
                sha = res.optString("sha", null);
            }
            
            HttpURLConnection putConn = (HttpURLConnection) new URL("https://gitee.com/api/v5/repos/" + encodePath(owner) + "/" + encodePath(repo) + "/contents/" + encodePath(relativePath)).openConnection();
            putConn.setRequestMethod("PUT");
            putConn.setRequestProperty("Content-Type", "application/json");
            putConn.setRequestProperty("User-Agent", "Yanzi-Mobile/0.1");
            putConn.setDoOutput(true);
            
            JSONObject payload = new JSONObject();
            payload.put("access_token", token.trim());
            payload.put("message", "Sync mobile-extensions.json from Mobile");
            String base64Content = Base64.encodeToString(content.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
            payload.put("content", base64Content);
            if (sha != null) {
                payload.put("sha", sha);
            }
            payload.put("branch", branch);
            
            OutputStream os = putConn.getOutputStream();
            os.write(payload.toString().getBytes(StandardCharsets.UTF_8));
            os.flush();
            os.close();
            
            int putCode = putConn.getResponseCode();
            if (putCode < 200 || putCode >= 300) {
                throw new java.io.IOException("Gitee write failed: " + putCode);
            }
        }

        private static JSONObject putJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            if (!sLanFailedThisSession && YanziApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = sContext != null ? LanDiscoveryManager.getLanBaseUrl(sContext) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = sContext != null ? LanDiscoveryManager.getLanApiToken(sContext) : LanDiscoveryManager.cachedLanApiToken;
                        int timeoutMs = 1500;
                        if (path.contains("/shell/run") || path.contains("/fs/write") || path.contains("/fs/read")) {
                            timeoutMs = 8000;
                        }
                        JSONObject result = YanziApiClient.doRequest(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "PUT", payload, timeoutMs);
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
            if (!sLanFailedThisSession && YanziApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = sContext != null ? LanDiscoveryManager.getLanBaseUrl(sContext) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = sContext != null ? LanDiscoveryManager.getLanApiToken(sContext) : LanDiscoveryManager.cachedLanApiToken;
                        int timeoutMs = 1500;
                        if (path.contains("/shell/run") || path.contains("/fs/write") || path.contains("/fs/read")) {
                            timeoutMs = 8000;
                        }
                        JSONObject result = YanziApiClient.doRequest(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "POST", payload, timeoutMs);
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
            if (!sLanFailedThisSession && YanziApiClient.shouldUseLan(path)) {
                String lanBaseUrl;
                String string = lanBaseUrl = sContext != null ? LanDiscoveryManager.getLanBaseUrl(sContext) : LanDiscoveryManager.cachedLanBaseUrl;
                if (lanBaseUrl != null) {
                    try {
                        String lanToken = sContext != null ? LanDiscoveryManager.getLanApiToken(sContext) : LanDiscoveryManager.cachedLanApiToken;
                        int timeoutMs = 1500;
                        if (path.contains("/shell/run") || path.contains("/fs/write") || path.contains("/fs/read")) {
                            timeoutMs = 8000;
                        }
                        JSONObject result = YanziApiClient.doRequest(lanBaseUrl, path, lanToken != null ? lanToken : token, action, "GET", null, timeoutMs);
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
            sLanFailedThisSession = true;
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
            connection.setRequestProperty("X-Yanzi-Client", "mobile");
            connection.setRequestProperty("X-Yanzi-Client-Version", "0.1.0");
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
                        String textToCopy = info.text;
                        if (info.feedbackTextView != null) {
                            textToCopy = "AI \u539f\u59cb\u56de\u590d\uff1a\n" + info.text + "\n\n" + info.feedbackTextView.getText().toString();
                        }
                        manager.setPrimaryClip(ClipData.newPlainText("AI Message", textToCopy));
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
        
        escaped = escaped.replaceAll("(?m)^######\\s+(.*?)\\s*$", "<h6>$1</h6>");
        escaped = escaped.replaceAll("(?m)^#####\\s+(.*?)\\s*$", "<h5>$1</h5>");
        escaped = escaped.replaceAll("(?m)^####\\s+(.*?)\\s*$", "<h4>$1</h4>");
        escaped = escaped.replaceAll("(?m)^###\\s+(.*?)\\s*$", "<h3>$1</h3>");
        escaped = escaped.replaceAll("(?m)^##\\s+(.*?)\\s*$", "<h2>$1</h2>");
        escaped = escaped.replaceAll("(?m)^#\\s+(.*?)\\s*$", "<h1>$1</h1>");
        
        escaped = escaped.replaceAll("\\*\\*(.*?)\\*\\*", "<b>$1</b>");
        escaped = escaped.replaceAll("\\*(.*?)\\*", "<i>$1</i>");
        escaped = escaped.replaceAll("__(.*?)__", "<u>$1</u>");
        escaped = escaped.replaceAll("`(.*?)`", "<tt><font color=\"#4ADE80\">$1</font></tt>");
        escaped = escaped.replaceAll("(?m)^\\s*-\\s+(.*)$", "&bull; $1");
        escaped = escaped.replaceAll("(?m)^\\s*\\*\\s+(.*)$", "&bull; $1");
        escaped = escaped.replace("\n", "<br/>");
        
        // Remove trailing <br/> added right after heading tags
        escaped = escaped.replaceAll("</h([1-6])><br/>", "</h$1>");

        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.N) {
            return Html.fromHtml(escaped, Html.FROM_HTML_MODE_LEGACY);
        } else {
            return Html.fromHtml(escaped);
        }
    }

    private void toggleTtsStatus(ImageView speakBtn) {
        this.isTtsEnabled = !this.isTtsEnabled;
        this.prefs.edit().putBoolean("isTtsEnabled", this.isTtsEnabled).apply();
        speakBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault(this.isTtsEnabled ? "volume-high" : "volume-mute"), Color.WHITE));
        if (!this.isTtsEnabled) {
            this.stopTtsPlayback(true);
        } else {
            this.initTextToSpeech();
        }
        Toast.makeText((Context)this, this.isTtsEnabled ? "已开启语音朗读" : "已关闭语音朗读", Toast.LENGTH_SHORT).show();
    }

    private void switchToVoiceInput() {
        this.holdToSpeakBtn.setVisibility(0); // VISIBLE
        this.holdToSpeakBtn.setText(this.isWakeTriggeredSpeechSessionActive ? "请开始说话" : "按住 说话");
        this.updateSpeechVolumeWave(0.0f);
        this.aiChatInput.setVisibility(8);    // GONE
        this.voiceToggleBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("keyboard"), Color.rgb(200, 200, 200)));
        this.hideKeyboard((View)this.aiChatInput);
    }

    private void switchToTextInput() {
        this.holdToSpeakBtn.setVisibility(8); // GONE
        this.aiChatInput.setVisibility(0);    // VISIBLE
        this.updateSpeechVolumeWave(0.0f);
        this.voiceToggleBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("microphone"), Color.rgb(200, 200, 200)));
    }

    private void updateSpeechVolumeWave(float rmsdB) {
        Button targetBtn = this.isChatVoiceActive ? this.chatHoldToSpeakBtn : this.holdToSpeakBtn;
        if (targetBtn == null) return;
        float normalized = Math.max(0.0f, Math.min(1.0f, rmsdB / 10.0f));
        int baseRed = this.isWakeTriggeredSpeechSessionActive ? 14 : 59;
        int baseGreen = this.isWakeTriggeredSpeechSessionActive ? 165 : 130;
        int baseBlue = this.isWakeTriggeredSpeechSessionActive ? 233 : 246;
        int red = Math.min(255, baseRed + (int)(20.0f * normalized));
        int green = Math.min(255, baseGreen + (int)(40.0f * normalized));
        int blue = Math.min(255, baseBlue + (int)(9.0f * normalized));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.rgb(red, green, blue));
        bg.setCornerRadius((float)this.dp(8 + (int)(8.0f * normalized)));
        bg.setStroke(this.dp(1 + (int)(2.0f * normalized)), Color.argb(120 + (int)(90.0f * normalized), 125, 211, 252));
        targetBtn.setBackground((Drawable)bg);
        float scale = 1.0f + 0.035f * normalized;
        targetBtn.setScaleX(scale);
        targetBtn.setScaleY(scale);
    }

    private void toggleWakeListening() {
        if (this.isWakeListeningEnabled) {
            this.isWakeListeningEnabled = false;
            this.stopWakeListening(false);
            this.updateWakeToggleButton();
            return;
        }
        if (!this.checkWakeAudioPermission()) {
            return;
        }
        this.isWakeListeningEnabled = true;
        this.updateWakeToggleButton();
        this.startWakeListening();
    }

    private boolean checkWakeAudioPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M &&
            this.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            this.requestPermissions(new String[]{android.Manifest.permission.RECORD_AUDIO}, 104);
            return false;
        }
        return true;
    }

    private void updateWakeToggleButton() {
        if (this.wakeToggleBtn == null) return;
        if (this.isWakeListeningEnabled) {
            this.wakeToggleBtn.setColorFilter(Color.rgb(34, 211, 238));
        } else {
            this.wakeToggleBtn.setColorFilter(Color.rgb(148, 163, 184));
        }
    }

    private void startWakeListening() {
        if (!this.isWakeListeningEnabled || this.isWakeListeningActive || this.isWakeModelLoading || this.isWakeTriggeredSpeech || this.isWakeTriggeredSpeechSessionActive) {
            return;
        }
        if (!this.checkWakeAudioPermission()) {
            return;
        }
        if (this.wakeModel == null) {
            this.isWakeModelLoading = true;
            this.updateWakeToggleButton();
            StorageService.unpack(this, WAKE_MODEL_ASSET_NAME, WAKE_MODEL_TARGET_NAME, model -> {
                MainActivity.this.wakeModel = model;
                MainActivity.this.isWakeModelLoading = false;
                MainActivity.this.startWakeListening();
            }, exception -> {
                MainActivity.this.isWakeModelLoading = false;
                MainActivity.this.isWakeListeningEnabled = false;
                MainActivity.this.updateWakeToggleButton();
                Log.e("YanziWake", "Failed to unpack wake model", exception);
                Toast.makeText((Context)MainActivity.this, "本地唤醒模型加载失败", Toast.LENGTH_SHORT).show();
            });
            return;
        }
        try {
            Recognizer recognizer = new Recognizer(this.wakeModel, 16000.0f, WAKE_GRAMMAR);
            this.wakeSpeechService = new SpeechService(recognizer, 16000.0f);
            this.isWakeListeningActive = true;
            this.wakeSpeechService.startListening(new org.vosk.android.RecognitionListener() {
                @Override
                public void onPartialResult(String hypothesis) {
                    MainActivity.this.handleWakeRecognitionText(hypothesis, false);
                }

                @Override
                public void onResult(String hypothesis) {
                    MainActivity.this.handleWakeRecognitionText(hypothesis, true);
                }

                @Override
                public void onFinalResult(String hypothesis) {
                    MainActivity.this.handleWakeRecognitionText(hypothesis, true);
                }

                @Override
                public void onError(Exception exception) {
                    Log.e("YanziWake", "Wake listening error", exception);
                    MainActivity.this.isWakeListeningActive = false;
                    MainActivity.this.wakeSpeechService = null;
                    MainActivity.this.restartWakeListeningLater();
                }

                @Override
                public void onTimeout() {
                    Log.d("YanziWake", "Wake listening timeout");
                    MainActivity.this.isWakeListeningActive = false;
                    MainActivity.this.wakeSpeechService = null;
                    MainActivity.this.restartWakeListeningLater();
                }
            });
            this.updateWakeToggleButton();
            Log.d("YanziWake", "Wake listening started");
        } catch (Exception e) {
            this.isWakeListeningActive = false;
            this.wakeSpeechService = null;
            Log.e("YanziWake", "Failed to start wake listening", e);
            Toast.makeText((Context)this, "启动本地唤醒失败", Toast.LENGTH_SHORT).show();
        }
    }

    private void stopWakeListening(boolean disable) {
        if (disable) {
            this.isWakeListeningEnabled = false;
            this.isWakeTriggeredSpeech = false;
            this.isWakeTriggeredSpeechSessionActive = false;
        }
        SpeechService service = this.wakeSpeechService;
        this.wakeSpeechService = null;
        this.isWakeListeningActive = false;
        if (service != null) {
            try {
                service.stop();
                service.shutdown();
            } catch (Exception e) {
                Log.e("YanziWake", "Failed to stop wake listening", e);
            }
        }
        this.updateWakeToggleButton();
    }

    private void releaseWakeListening() {
        this.stopWakeListening(true);
        if (this.wakeModel != null) {
            try {
                this.wakeModel.close();
            } catch (Exception ignored) {}
            this.wakeModel = null;
        }
    }

    private void restartWakeListeningLater() {
        if (!this.isWakeListeningEnabled || this.isWakeTriggeredSpeech || this.isWakeTriggeredSpeechSessionActive) {
            return;
        }
        new Handler(Looper.getMainLooper()).postDelayed(() -> {
            if (MainActivity.this.isWakeListeningEnabled && !MainActivity.this.isWakeTriggeredSpeech && !MainActivity.this.isWakeTriggeredSpeechSessionActive) {
                MainActivity.this.startWakeListening();
            }
        }, 800L);
    }

    private void handleWakeRecognitionText(String hypothesis, boolean finalResult) {
        String text = this.extractVoskRecognitionText(hypothesis);
        if (text.isEmpty()) {
            return;
        }
        Log.d("YanziWake", (finalResult ? "result=" : "partial=") + text);
        String normalized = text.replace(" ", "").replace("，", "").replace(",", "").replace("。", "").replace(".", "");
        if (isWakePhraseMatched(normalized, finalResult)) {
            this.handleWakeWordDetected();
        }
    }

    private boolean isWakePhraseMatched(String normalizedText, boolean finalResult) {
        if (normalizedText == null || normalizedText.isEmpty()) {
            return false;
        }
        if ("燕子燕子".equals(normalizedText)) {
            return true;
        }
        return finalResult && normalizedText.startsWith("燕子燕子") && normalizedText.length() <= 6;
    }

    private String extractVoskRecognitionText(String json) {
        if (json == null || json.trim().isEmpty()) {
            return "";
        }
        try {
            JSONObject obj = new JSONObject(json);
            String text = obj.optString("partial", "");
            if (text == null || text.trim().isEmpty()) {
                text = obj.optString("text", "");
            }
            return text == null ? "" : text.trim();
        } catch (Exception e) {
            return "";
        }
    }

    private void handleWakeWordDetected() {
        if (this.isWakeTriggeredSpeech || this.isWakeTriggeredSpeechSessionActive) {
            return;
        }
        Log.d("YanziWake", "Wake word detected");
        this.stopTtsPlayback(true);
        // this.playWakeReadyTone(); // 去掉应用层唤醒嘟声，防与系统语音弹窗的嘟声冲突重叠
        this.isWakeTriggeredSpeech = true;
        this.isWakeTriggeredSpeechSessionActive = true;
        this.stopWakeListening(false);
        this.selectTab("ai");
        this.switchToVoiceInput();
        this.startSpeechRecognition(true);
    }

    private void playWakeReadyTone() {
        try {
            ToneGenerator toneGenerator = new ToneGenerator(AudioManager.STREAM_MUSIC, 70);
            toneGenerator.startTone(ToneGenerator.TONE_PROP_ACK, 120);
            new Handler(Looper.getMainLooper()).postDelayed(() -> {
                try {
                    toneGenerator.release();
                } catch (Exception ignored) {}
            }, 300L);
        } catch (Exception e) {
            Log.e("YanziWake", "Failed to play wake tone", e);
        }
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

    private void cancelSpeechRecognition() {
        this.cancelPendingSpeechRestart();
        if (this.speechRecognizer != null) {
            try {
                this.speechRecognizer.cancel();
            } catch (Exception e) {
                Log.e("YanziVoice", "Failed to cancel SpeechRecognizer", e);
            }
        }
        this.isSpeechListening = false;
        this.pendingStopSpeech = false;
        this.updateSpeechVolumeWave(0.0f);
    }

    private void destroySpeechRecognizer() {
        this.cancelPendingSpeechRestart();
        final android.speech.SpeechRecognizer recognizerToDestroy = this.speechRecognizer;
        this.speechRecognizer = null;
        this.currentSpeechPackage = null;
        this.isSpeechListening = false;
        this.pendingStopSpeech = false;
        this.updateSpeechVolumeWave(0.0f);
        
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
        this.startSpeechRecognition(true);
    }

    private void startSpeechRecognition(boolean resetAccumulatedText) {
        try {
            this.destroySpeechRecognizer();
            if (resetAccumulatedText) {
                this.speechAccumulatedText.setLength(0);
                this.speechContinuationCount = 0;
                this.speechRetryCount = 0;
                this.failedSpeechPackages.clear();
            } else {
                this.speechContinuationCount++;
            }
            this.lastSpeechStartTime = System.currentTimeMillis();
            this.pendingStopSpeech = false;
            this.isSpeechActionUp = this.isWakeTriggeredSpeechSessionActive;
            this.isSpeechFinished = false;
            android.content.ComponentName comp = this.findAvailableSpeechService();
            if (comp != null) {
                this.initSpeechRecognizer(comp);
            } else {
                if (!this.failedSpeechPackages.contains("default") && this.isDefaultRecognizerAllowed()) {
                    this.initSpeechRecognizer(null);
                }
            }
            if (this.speechRecognizer != null) {
                this.isSpeechListening = false;
                this.speechRecognizer.startListening(this.speechRecognizerIntent);
            } else {
                this.startSpeechIntent();
                this.switchToTextInput();
                if (this.isWakeTriggeredSpeech || this.isWakeTriggeredSpeechSessionActive) {
                    this.isWakeTriggeredSpeech = false;
                    this.isWakeTriggeredSpeechSessionActive = false;
                }
            }
        } catch (Exception e) {
            Log.e("YanziVoice", "Failed to start speech recognition", e);
            if (this.currentSpeechPackage != null) {
                this.failedSpeechPackages.add(this.currentSpeechPackage);
            }
            this.destroySpeechRecognizer();
            if (this.speechRetryCount < 3) {
                this.speechRetryCount++;
                Log.d("YanziVoice", "Exception caught during speech setup, retrying next engine. Attempt: " + this.speechRetryCount);
                this.startSpeechRecognition(false);
            } else {
                this.startSpeechIntent();
                this.switchToTextInput();
                if (this.isWakeTriggeredSpeech || this.isWakeTriggeredSpeechSessionActive) {
                    this.isWakeTriggeredSpeech = false;
                    this.isWakeTriggeredSpeechSessionActive = false;
                }
            }
        }
    }

    private void stopSpeechRecognition() {
        try {
            this.cancelPendingSpeechRestart();
            this.isSpeechActionUp = true;
            long duration = System.currentTimeMillis() - this.lastSpeechStartTime;
            if (duration < 500) {
                Log.d("YanziVoice", "Speech duration too short: " + duration + "ms, accumulatedLength=" + this.speechAccumulatedText.length());
                this.destroySpeechRecognizer();
                if (this.isChatVoiceActive) {
                    this.switchToChatTextInput();
                } else {
                    this.switchToTextInput();
                }
                if (this.speechAccumulatedText.length() == 0) {
                    Toast.makeText((Context)this, "说话时间太短", Toast.LENGTH_SHORT).show();
                }
                return;
            }
            if (this.isSpeechFinished) {
                Log.d("YanziVoice", "Speech already finished when ActionUp, switching UI");
                if (this.isChatVoiceActive) {
                    this.switchToChatTextInput();
                } else {
                    this.switchToTextInput();
                }
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
            if (this.isChatVoiceActive) {
                this.switchToChatTextInput();
            } else {
                this.switchToTextInput();
            }
            this.destroySpeechRecognizer();
        }
    }

    private void cancelPendingSpeechRestart() {
        if (this.pendingSpeechRestartRunnable != null) {
            this.speechRestartHandler.removeCallbacks(this.pendingSpeechRestartRunnable);
            this.pendingSpeechRestartRunnable = null;
        }
    }

    private void startSpeechIntent() {
        android.content.Intent intent = new android.content.Intent(android.speech.RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        this.configureSpeechRecognizerIntent(intent);
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

            // 检测是否是一加、OPPO、Vivo 等对系统语音服务做了严格第三方签名校验限制的品牌
            String manufacturer = android.os.Build.MANUFACTURER.toLowerCase(Locale.ROOT);
            boolean isRestrictedBrand = manufacturer.contains("oppo") || manufacturer.contains("oneplus") || manufacturer.contains("vivo") || manufacturer.contains("realme");

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

            // 已知不是真实语音识别服务（或国内无法联网/不可被第三方绑定）的黑名单
            String[] blacklistPkgs = {
                "com.arlosoft.macrodroid",
                "net.dinglisch.android.taskerm",
                "com.google.android.googlequicksearchbox",
                "com.tencent.android.qqdownloader",
                "com.qihoo.appstore",
                "com.baidu.appsearch"
            };

            // 1. 优先寻找大厂及主流引擎
            for (String pref : preferredPkgs) {
                // 如果属于受限机型，跳过其私有闭源语音包名，防签名校验报错
                if (isRestrictedBrand && (pref.contains("coloros") || pref.contains("heytap") || pref.contains("vivo"))) {
                    continue;
                }
                if (this.failedSpeechPackages.contains(pref)) {
                    continue;
                }
                for (android.content.pm.ResolveInfo ri : services) {
                    android.content.pm.ServiceInfo si = ri.serviceInfo;
                    if (si != null && si.packageName.equalsIgnoreCase(pref)) {
                        if (this.failedSpeechPackages.contains(si.packageName)) {
                            continue;
                        }
                        Log.d("YanziVoice", "Found preferred speech service: " + si.packageName + "/" + si.name);
                        return new android.content.ComponentName(si.packageName, si.name);
                    }
                }
            }

            // 2. 查找包含 speech/voice 等关键词且不在黑名单中的引擎
            for (android.content.pm.ResolveInfo ri : services) {
                android.content.pm.ServiceInfo si = ri.serviceInfo;
                if (si != null) {
                    if (this.failedSpeechPackages.contains(si.packageName)) {
                        continue;
                    }
                    boolean isBlacklisted = false;
                    for (String black : blacklistPkgs) {
                        if (si.packageName.toLowerCase(Locale.ROOT).contains(black.toLowerCase(Locale.ROOT))) {
                            isBlacklisted = true;
                            break;
                        }
                    }
                    if (isBlacklisted) continue;

                    String pkg = si.packageName.toLowerCase(Locale.ROOT);
                    String name = si.name.toLowerCase(Locale.ROOT);
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
                    if (this.failedSpeechPackages.contains(si.packageName)) {
                        continue;
                    }
                    boolean isBlacklisted = false;
                    for (String black : blacklistPkgs) {
                        if (si.packageName.toLowerCase(Locale.ROOT).contains(black.toLowerCase(Locale.ROOT))) {
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

    private boolean isDefaultRecognizerAllowed() {
        try {
            String serviceSetting = android.provider.Settings.Secure.getString(
                this.getContentResolver(),
                "voice_recognition_service"
            );
            if (serviceSetting != null && serviceSetting.contains("com.google.android.googlequicksearchbox")) {
                return false;
            }
        } catch (Exception e) {
            Log.e("YanziVoice", "Error checking voice_recognition_service", e);
        }
        return true;
    }

    private boolean isBackendSpeechRecognizerWorkable() {
        if (!android.speech.SpeechRecognizer.isRecognitionAvailable((Context)this)) {
            return false;
        }
        android.content.ComponentName comp = this.findAvailableSpeechService();
        if (comp != null) {
            return true;
        }
        return !this.failedSpeechPackages.contains("default") && this.isDefaultRecognizerAllowed();
    }

    private void initSpeechRecognizer(android.content.ComponentName comp) {
        if (this.speechRecognizer != null) return; // 重点：常驻单一实例，避免频繁bind/unbind
        try {
            if (comp == null) {
                this.speechRecognizer = android.speech.SpeechRecognizer.createSpeechRecognizer((Context)this);
                this.currentSpeechPackage = "default";
                Log.d("YanziVoice", "Initialized default SpeechRecognizer");
            } else {
                this.speechRecognizer = android.speech.SpeechRecognizer.createSpeechRecognizer((Context)this, comp);
                this.currentSpeechPackage = comp.getPackageName();
                Log.d("YanziVoice", "Initialized SpeechRecognizer with component: " + comp.flattenToShortString());
            }
        } catch (Exception e) {
            Log.e("YanziVoice", "Failed to create SpeechRecognizer", e);
            this.speechRecognizer = null;
            return;
        }
        this.speechRecognizerIntent = new Intent(android.speech.RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        this.configureSpeechRecognizerIntent(this.speechRecognizerIntent);
        this.speechRecognizer.setRecognitionListener(new android.speech.RecognitionListener() {
            @Override
            public void onReadyForSpeech(Bundle params) {
                Log.d("YanziVoice", "onReadyForSpeech");
                MainActivity.this.isSpeechListening = true;
                MainActivity.this.updateSpeechVolumeWave(0.0f);
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
            public void onRmsChanged(float rmsdB) {
                MainActivity.this.updateSpeechVolumeWave(rmsdB);
            }

            @Override
            public void onBufferReceived(byte[] buffer) {}

            @Override
            public void onEndOfSpeech() {
                Log.d("YanziVoice", "onEndOfSpeech");
                MainActivity.this.isSpeechListening = false;
                MainActivity.this.updateSpeechVolumeWave(0.0f);
            }

            @Override
            public void onError(int error) {
                Log.e("YanziVoice", "Speech recognition error: " + error);
                if ((error == android.speech.SpeechRecognizer.ERROR_NETWORK || 
                     error == android.speech.SpeechRecognizer.ERROR_SERVER || 
                     error == android.speech.SpeechRecognizer.ERROR_CLIENT) && 
                    MainActivity.this.speechRetryCount < 3) {
                    
                    MainActivity.this.speechRetryCount++;
                    if (MainActivity.this.currentSpeechPackage != null) {
                        MainActivity.this.failedSpeechPackages.add(MainActivity.this.currentSpeechPackage);
                    }
                    Log.d("YanziVoice", "Encountered critical error " + error + ", trying to switch speech engine. Attempt: " + MainActivity.this.speechRetryCount);
                    MainActivity.this.destroySpeechRecognizer();
                    MainActivity.this.startSpeechRecognition(false);
                    return;
                }
                MainActivity.this.isSpeechFinished = true;
                MainActivity.this.updateSpeechVolumeWave(0.0f);
                String msg = getSpeechErrorMsg(error);
                if (MainActivity.this.isWakeTriggeredSpeech || MainActivity.this.isWakeTriggeredSpeechSessionActive) {
                    MainActivity.this.isWakeTriggeredSpeech = false;
                    MainActivity.this.isWakeTriggeredSpeechSessionActive = false;
                    Toast.makeText((Context)MainActivity.this, "唤醒后识别失败: " + msg, Toast.LENGTH_SHORT).show();
                    MainActivity.this.destroySpeechRecognizer();
                    MainActivity.this.switchToTextInput();
                    MainActivity.this.restartWakeListeningLater();
                    return;
                }
                if (!MainActivity.this.isSpeechActionUp && MainActivity.this.speechContinuationCount < SPEECH_MAX_CONTINUATION_COUNT) {
                    Log.d("YanziVoice", "Speech error before ActionUp, restarting recognition: " + msg);
                    MainActivity.this.destroySpeechRecognizer();
                    MainActivity.this.scheduleSpeechRecognitionRestart();
                    return;
                }
                if (error == android.speech.SpeechRecognizer.ERROR_NO_MATCH) {
                    Toast.makeText((Context)MainActivity.this, "未检测到有效语音", Toast.LENGTH_SHORT).show();
                } else {
                    Toast.makeText((Context)MainActivity.this, "识别失败: " + msg, Toast.LENGTH_SHORT).show();
                }
                MainActivity.this.destroySpeechRecognizer();
                if (MainActivity.this.isSpeechActionUp) {
                    MainActivity.this.switchToTextInput();
                }
            }

            @Override
            public void onResults(Bundle results) {
                MainActivity.this.isSpeechFinished = true;
                MainActivity.this.updateSpeechVolumeWave(0.0f);
                ArrayList<String> matches = results.getStringArrayList(android.speech.SpeechRecognizer.RESULTS_RECOGNITION);
                if (matches != null && !matches.isEmpty()) {
                    String text = matches.get(0);
                    Log.d("YanziVoice", "onResults: " + text);
                    MainActivity.this.appendSpeechRecognitionText(text);
                }
                if (MainActivity.this.isWakeTriggeredSpeech || MainActivity.this.isWakeTriggeredSpeechSessionActive) {
                    MainActivity.this.isWakeTriggeredSpeech = false;
                    MainActivity.this.isWakeTriggeredSpeechSessionActive = false;
                    MainActivity.this.destroySpeechRecognizer();
                    MainActivity.this.switchToTextInput();
                    if (MainActivity.this.aiChatInput != null && MainActivity.this.aiChatInput.getText() != null && MainActivity.this.aiChatInput.getText().toString().trim().length() > 0) {
                        MainActivity.this.handleAiSendButtonClick();
                    }
                    MainActivity.this.restartWakeListeningLater();
                    return;
                }
                MainActivity.this.destroySpeechRecognizer();
                if (MainActivity.this.isSpeechActionUp) {
                    MainActivity.this.switchToTextInput();
                } else if (MainActivity.this.speechContinuationCount < SPEECH_MAX_CONTINUATION_COUNT) {
                    MainActivity.this.scheduleSpeechRecognitionRestart();
                }
            }

            @Override
            public void onPartialResults(Bundle partialResults) {}

            @Override
            public void onEvent(int eventType, Bundle params) {}
        });
    }

    private void configureSpeechRecognizerIntent(android.content.Intent intent) {
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE_MODEL, android.speech.RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault());
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_PROMPT, "请说话...");
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_MAX_RESULTS, SPEECH_MAX_RESULTS);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_PARTIAL_RESULTS, false);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, SPEECH_COMPLETE_SILENCE_MS);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, SPEECH_POSSIBLY_COMPLETE_SILENCE_MS);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, SPEECH_MINIMUM_LENGTH_MS);
        intent.putExtra(android.speech.RecognizerIntent.EXTRA_CALLING_PACKAGE, this.getPackageName());
    }

    private void appendSpeechRecognitionText(String text) {
        if (text == null) return;
        String normalized = text.trim();
        if (normalized.isEmpty()) return;
        if (this.speechAccumulatedText.length() > 0) {
            this.speechAccumulatedText.append(' ');
        }
        this.speechAccumulatedText.append(normalized);
        String fullText = this.speechAccumulatedText.toString();
        if (this.isChatVoiceActive) {
            if (this.chatInputEditText != null) {
                this.chatInputEditText.setText((CharSequence)fullText);
                this.chatInputEditText.setSelection(fullText.length());
            }
        } else {
            if (this.aiChatInput != null) {
                this.aiChatInput.setText((CharSequence)fullText);
                this.aiChatInput.setSelection(fullText.length());
            }
        }
    }

    private void scheduleSpeechRecognitionRestart() {
        if (this.isSpeechActionUp || this.speechContinuationCount >= SPEECH_MAX_CONTINUATION_COUNT) {
            return;
        }
        this.cancelPendingSpeechRestart();
        this.pendingSpeechRestartRunnable = () -> {
            this.pendingSpeechRestartRunnable = null;
            if (!this.isSpeechActionUp) {
                Log.d("YanziVoice", "Restarting speech recognition for continuous hold, count=" + this.speechContinuationCount);
                this.startSpeechRecognition(false);
            }
        };
        this.speechRestartHandler.postDelayed(this.pendingSpeechRestartRunnable, 150L);
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

    private void setTtsSpeaking(boolean speaking) {
        this.isTtsSpeaking = speaking;
        this.runOnUiThread(() -> this.updateTtsStopButton());
    }

    private void updateTtsStopButton() {
        if (this.ttsStopButton == null) return;
        this.ttsStopButton.setVisibility(this.isTtsSpeaking ? View.VISIBLE : View.GONE);
    }

    private void stopTtsPlayback(boolean clearPending) {
        if (clearPending) {
            this.pendingSpeakText = null;
        }
        if (this.textToSpeech != null) {
            try {
                this.textToSpeech.stop();
            } catch (Exception e) {
                Log.e("YanziTTS", "Stop failed", e);
            }
        }
        this.setTtsSpeaking(false);
        this.restartWakeListeningLater();
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
                this.textToSpeech.setOnUtteranceProgressListener(new android.speech.tts.UtteranceProgressListener() {
                    @Override
                    public void onStart(String utteranceId) {
                        MainActivity.this.setTtsSpeaking(true);
                    }

                    @Override
                    public void onDone(String utteranceId) {
                        MainActivity.this.setTtsSpeaking(false);
                        MainActivity.this.restartWakeListeningLater();
                    }

                    @Override
                    public void onError(String utteranceId) {
                        MainActivity.this.setTtsSpeaking(false);
                        MainActivity.this.restartWakeListeningLater();
                    }
                });
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
                this.setTtsSpeaking(false);
                Toast.makeText((Context)this, "朗读失败：TTS 播放接口返回错误 (ERROR)", Toast.LENGTH_SHORT).show();
            } else {
                this.setTtsSpeaking(true);
            }
        } catch (Exception e) {
            this.setTtsSpeaking(false);
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

    private void selectMobileSubTab(int index) {
        if (index < 0 || index > 2) return;
        this.currentMobileSubTab = index;
        
        this.runOnUiThread(() -> {
            android.graphics.drawable.GradientDrawable activeBg = new android.graphics.drawable.GradientDrawable();
            activeBg.setCornerRadius((float)this.dp(8));
            activeBg.setColor(Color.argb(20, 34, 211, 238));
            
            if (this.btnShowMobileExtensions != null) {
                this.btnShowMobileExtensions.setTextColor(Color.rgb(148, 163, 184));
                this.btnShowMobileExtensions.setBackgroundColor(Color.TRANSPARENT);
            }
            if (this.btnShowMobileDocs != null) {
                this.btnShowMobileDocs.setTextColor(Color.rgb(148, 163, 184));
                this.btnShowMobileDocs.setBackgroundColor(Color.TRANSPARENT);
            }
            if (this.btnShowMobileShell != null) {
                this.btnShowMobileShell.setTextColor(Color.rgb(148, 163, 184));
                this.btnShowMobileShell.setBackgroundColor(Color.TRANSPARENT);
            }
            
            if (index == 0) {
                if (this.btnShowMobileExtensions != null) {
                    this.btnShowMobileExtensions.setTextColor(Color.rgb(34, 211, 238));
                    this.btnShowMobileExtensions.setBackground((android.graphics.drawable.Drawable)activeBg);
                }
            } else if (index == 1) {
                if (this.btnShowMobileDocs != null) {
                    this.btnShowMobileDocs.setTextColor(Color.rgb(34, 211, 238));
                    this.btnShowMobileDocs.setBackground((android.graphics.drawable.Drawable)activeBg);
                }
            } else if (index == 2) {
                if (this.btnShowMobileShell != null) {
                    this.btnShowMobileShell.setTextColor(Color.rgb(34, 211, 238));
                    this.btnShowMobileShell.setBackground((android.graphics.drawable.Drawable)activeBg);
                }
            }
            
            if (this.mobileViewPager != null && this.mobileViewPager.getCurrentItem() != index) {
                this.mobileViewPager.setCurrentItem(index, true);
            }
            
            if (index == 0) {
                if (this.isEditingMobileExtension) {
                    if (this.mobileViewPager != null) this.mobileViewPager.setVisibility(View.GONE);
                    this.mobileExtensionEditorView.setVisibility(View.VISIBLE);
                } else {
                    if (this.mobileViewPager != null) this.mobileViewPager.setVisibility(View.VISIBLE);
                    this.mobileExtensionEditorView.setVisibility(View.GONE);
                }
            } else {
                if (this.mobileViewPager != null) this.mobileViewPager.setVisibility(View.VISIBLE);
                this.mobileExtensionEditorView.setVisibility(View.GONE);
            }
        });
    }

    private void buildMobileDocsView(LinearLayout container) {
        TextView descText = this.textView("\u624b\u673a\u6269\u5c55\u57fa\u4e8e\u8f6b\u91cf\u7ea7 JavaScript \u73af\u5883\u6267\u884c\u3002\u60a8\u53ef\u4ee5\u5728\u811a\u672c\u7684 async function run(context) \u4e2d\u8c03\u7528\u4ee5\u4e0b context.mobile API\u3002", 13, Color.rgb(182, 194, 214), false);
        descText.setPadding(0, this.dp(4), 0, this.dp(12));
        container.addView((View)descText);
        
        java.util.List<DocItem> docs = new java.util.ArrayList<>();
        docs.add(new DocItem("context.mobile.toast(text)", "\u5f39\u51fa\u7cfb\u7edf Toast \u63d0\u793a\u6d88\u606f\uff0c\u65e0\u8fd4\u56de\u503c\u3002", "text (string): \u63d0\u793a\u6587\u672c", "context.mobile.toast('Hello');"));
        docs.add(new DocItem("context.mobile.sendToDesktop(text)", "\u53d1\u9001\u6587\u672c\u6d88\u606f\u5230\u5f53\u524d\u5df2\u8fde\u63a5\u7684\u7535\u8111\u7aef\uff08\u9700\u5728\u7ebf\uff0c\u65e0\u8fd4\u56de\u503c\uff09\u3002", "text (string): \u53d1\u9001\u5185\u5bb9", "context.mobile.sendToDesktop('hi');"));
        docs.add(new DocItem("context.mobile.getSharedText()", "\u83b7\u53d6\u624b\u673a\u4e3b\u754c\u9762\u8f93\u5165\u6846\u5f53\u524d\u7684\u5171\u4eab\u6587\u672c\u5185\u5bb9\u3002", "\u65e0", "const text = context.mobile.getSharedText();"));
        docs.add(new DocItem("context.mobile.getClipboardText()", "\u5f02\u6b65\u83b7\u53d6\u624b\u673a\u7cfb\u7edf\u5f53\u524d\u7684\u526a\u8d34\u677f\u6587\u672c\u5185\u5bb9\u3002", "\u65e0", "const clip = await context.mobile.getClipboardText();"));
        docs.add(new DocItem("context.mobile.setClipboardText(text)", "\u5f02\u6b65\u8bbe\u7f6e\u624b\u673a\u7cfb\u7edf\u526a\u8d34\u677f\u6587\u672c\u3002", "text (string): \u5199\u5165\u7684\u6587\u672c", "await context.mobile.setClipboardText('new text');"));
        docs.add(new DocItem("context.mobile.openUrl(url)", "\u5f02\u6b65\u8c03\u7528\u7cfb\u7edf\u6d4f\u89c8\u5668\u6253\u5f00\u630f\u5b9a\u7684\u7f51\u5740\u3002", "url (string): \u8981\u6253\u5f00\u7684\u7f51\u5740", "await context.mobile.openUrl('https://www.baidu.com');"));
        docs.add(new DocItem("context.mobile.pickPhoto()", "\u5f02\u6b65\u89e6\u53d1\u624b\u673a\u76f8\u518c\u9009\u62e9\u7167\u7247\u5e76\u53d1\u9001\u5230\u5df2\u8fde\u63a5\u7684\u7535\u8111\u7aef\u3002", "\u65e0", "await context.mobile.pickPhoto();"));
        docs.add(new DocItem("context.mobile.readTextFile(name)", "\u5f02\u6b65\u5728\u624b\u673a\u79c1\u6709\u811a\u672c\u5b58\u50a8\u533a\u4e2d\u8bfb\u53d6\u630f\u5b9a\u540d\u79f0\u7684\u6587\u672c\u6587\u4ef6\u3002", "name (string): \u6587\u4ef6\u540d", "const res = await context.mobile.readTextFile('data.txt');\nif (res.ok) { context.mobile.toast(res.text); }"));
        docs.add(new DocItem("context.mobile.saveTextFile(name, text)", "\u5f02\u6b65\u5728\u624b\u673a\u79c1\u6709\u811a\u672c\u5b58\u50a8\u533a\u4e2d\u4fdd\u5b58/\u91cd\u5199\u6587\u672c\u6587\u4ef6\u3002", "name (string): \u6587\u4ef6\u540d\ntext (string): \u5199\u5165\u5185\u5bb9", "await context.mobile.saveTextFile('data.txt', 'hello');"));
        docs.add(new DocItem("context.mobile.appendTextFile(name, text)", "\u5f02\u6b65\u5728\u624b\u673a\u79c1\u6709\u811a\u672c\u5b58\u50a8\u533a\u4e2d\u8ffd\u52a0\u6587\u672c\u5185\u5bb9\u3002", "name (string): \u6587\u4ef6\u540d\ntext (string): \u8ffd\u52a0\u5185\u5bb9", "await context.mobile.appendTextFile('log.txt', '\\nnew log');"));
        docs.add(new DocItem("context.mobile.httpGet(url)", "\u5f02\u6b65\u53d1\u9001 HTTP GET \u8bf7\u6c42\uff0c\u8fd4\u56de JSON \u54cd\u5e94\u3002", "url (string): \u8bf7\u6c42\u7f51\u5740", "const res = await context.mobile.httpGet('https://api.github.com');"));
        docs.add(new DocItem("context.mobile.httpPostJson(url, jsonText)", "\u5f02\u6b65\u53d1\u9001 HTTP POST JSON \u8bf7\u6c42\u3002", "url (string): \u8bf7\u6c42\u7f51\u5740\njsonText (string): JSON\u5b57\u7b26\u4e32", "const res = await context.mobile.httpPostJson('https://example.com/api', JSON.stringify({a:1}));"));
        docs.add(new DocItem("context.mobile.done(text)", "\u6807\u8bb0\u5f53\u524d\u811a\u672c\u6210\u529f\u8fd0\u884c\u5b8c\u6210\uff0c\u5e76\u901a\u77e5\u9000\u51fa\u3002", "text (string): \u5b8c\u6210\u6d88\u606f", "context.mobile.done('\u6267\u884c\u6210\u529f');"));
        docs.add(new DocItem("context.mobile.fail(text)", "\u6807\u8bb0\u5f53\u524d\u811a\u672c\u8fd0\u884c\u5931\u8d25\uff0c\u5e76\u4e0a\u62a5\u9519\u8bef\u4fe8\u606f\u3002", "text (string): \u9519\u8bef\u4fe8\u606f", "context.mobile.fail('\u8fd5\u884c\u51fa\u9519');"));
        docs.add(new DocItem("context.mobile.getBatteryLevel()", "\u83b7\u53d6\u624b\u673a\u5f53\u524d\u5269\u4f59\u7535\u91cf\u767e\u5206\u6bd5\u3002", "\u65e0", "const battery = context.mobile.getBatteryLevel();"));
        docs.add(new DocItem("context.mobile.getScreenBrightness()", "\u83b7\u53d6\u5f53\u524d\u5e5c\u5e55\u4eae\u5ea6\uff080.0\u81f31.0\uff09\u3002", "\u65e0", "const brightness = context.mobile.getScreenBrightness();"));
        docs.add(new DocItem("context.mobile.setScreenBrightness(val)", "\u8bbe\u7f6e\u5f53\u524d\u5e5c\u5e55\u4eae\u5ea6\u3002", "val (number): \u4eae\u5ea6\u503c\uff080.0\u81f31.0\uff09", "context.mobile.setScreenBrightness(0.5);"));
        docs.add(new DocItem("context.mobile.getLocation()", "\u5f02\u6b65\u83b7\u53d6\u57fa\u4e8e IP \u7f51\u7edc\u7684\u7c97\u7565\u5730\u7406\u5b9a\u4f4d\u4fe8\u606f\u3002", "\u65e0", "const loc = await context.mobile.getLocation();"));
        docs.add(new DocItem("context.mobile.listScriptFiles()", "\u5f02\u6b65\u83b7\u53d6\u624b\u673a\u79c1\u6709\u811a\u672c\u76ee\u5f55\u4e0b\u7684\u5168\u90e8\u6587\u4ef6\u5217\u8868\u3002", "\u65e0", "const res = await context.mobile.listScriptFiles();"));
        docs.add(new DocItem("context.mobile.deleteScriptFile(name)", "\u5f02\u6b65\u5220\u9664\u79c1\u6709\u811a\u672c\u76ee\u5f55\u4e0b\u7684\u630f\u5b9a\u6587\u4ef6\u3002", "name (string): \u6587\u4ef6\u540d", "const res = await context.mobile.deleteScriptFile('temp.txt');"));
        
        for (DocItem item : docs) {
            LinearLayout card = new LinearLayout((Context)this);
            card.setOrientation(1);
            card.setPadding(this.dp(12), this.dp(12), this.dp(12), this.dp(12));
            
            LinearLayout.LayoutParams cardParams = new LinearLayout.LayoutParams(-1, -2);
            cardParams.bottomMargin = this.dp(12);
            card.setLayoutParams((ViewGroup.LayoutParams)cardParams);
            
            android.graphics.drawable.GradientDrawable bg = new android.graphics.drawable.GradientDrawable();
            bg.setColor(Color.rgb(30, 41, 59));
            bg.setCornerRadius((float)this.dp(8));
            card.setBackground((android.graphics.drawable.Drawable)bg);
            
            TextView nameTv = new TextView((Context)this);
            nameTv.setText((CharSequence)item.name);
            nameTv.setTextColor(Color.rgb(34, 211, 238));
            nameTv.setTextSize(15f);
            nameTv.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
            card.addView((View)nameTv);
            
            TextView descTv = new TextView((Context)this);
            descTv.setText((CharSequence)("\u529f\u80fd\uff1a" + item.desc));
            descTv.setTextColor(Color.rgb(226, 232, 240));
            descTv.setTextSize(13f);
            descTv.setPadding(0, this.dp(6), 0, 0);
            card.addView((View)descTv);
            
            TextView paramsTv = new TextView((Context)this);
            paramsTv.setText((CharSequence)("\u53c2\u6570\uff1a" + item.params));
            paramsTv.setTextColor(Color.rgb(148, 163, 184));
            paramsTv.setTextSize(12f);
            paramsTv.setPadding(0, this.dp(4), 0, 0);
            card.addView((View)paramsTv);
            
            TextView exampleTv = new TextView((Context)this);
            exampleTv.setText((CharSequence)item.example);
            exampleTv.setTextColor(Color.rgb(167, 243, 208));
            exampleTv.setTextSize(11f);
            exampleTv.setTypeface(Typeface.MONOSPACE);
            exampleTv.setPadding(this.dp(10), this.dp(8), this.dp(10), this.dp(8));
            
            LinearLayout.LayoutParams exParams = new LinearLayout.LayoutParams(-1, -2);
            exParams.topMargin = this.dp(8);
            exampleTv.setLayoutParams((ViewGroup.LayoutParams)exParams);
            
            android.graphics.drawable.GradientDrawable exBg = new android.graphics.drawable.GradientDrawable();
            exBg.setColor(Color.rgb(15, 23, 42));
            exBg.setCornerRadius((float)this.dp(4));
            exampleTv.setBackground((android.graphics.drawable.Drawable)exBg);
            
            card.addView((View)exampleTv);
            container.addView((View)card);
        }
    }

    private void buildMobileShellView(LinearLayout container) {
        TextView descText = this.textView("\u5728\u6b64\u53ef\u76f2\u63a5\u7f16\u5199\u5e76\u8fd0\u884c JS \u811a\u672c\uff0c\u6216\u67e5\u770b\u6269\u5c55\u7684 API \u8c03\u7528\u65e5\u5fd7\u3002", 13, Color.rgb(182, 194, 214), false);
        descText.setPadding(0, this.dp(4), 0, this.dp(12));
        container.addView((View)descText);
        
        LinearLayout logLayout = new LinearLayout((Context)this);
        logLayout.setOrientation(1);
        android.graphics.drawable.GradientDrawable logBg = new android.graphics.drawable.GradientDrawable();
        logBg.setColor(Color.rgb(9, 13, 22));
        logBg.setCornerRadius((float)this.dp(8));
        logLayout.setBackground((android.graphics.drawable.Drawable)logBg);
        logLayout.setPadding(this.dp(12), this.dp(12), this.dp(12), this.dp(12));
        
        LinearLayout.LayoutParams logLayoutParams = new LinearLayout.LayoutParams(-1, this.dp(200));
        logLayoutParams.bottomMargin = this.dp(12);
        logLayout.setLayoutParams((ViewGroup.LayoutParams)logLayoutParams);
        
        this.svMobileShellLog = new androidx.core.widget.NestedScrollView((Context)this);
        this.svMobileShellLog.setNestedScrollingEnabled(true);
        this.tvMobileShellLog = new TextView((Context)this);
        this.tvMobileShellLog.setText((CharSequence)"-- Yanzi Mobile JS Shell Ready --\n");
        this.tvMobileShellLog.setTextColor(Color.rgb(34, 211, 238));
        this.tvMobileShellLog.setTextSize(12f);
        this.tvMobileShellLog.setTypeface(Typeface.MONOSPACE);
        
        this.svMobileShellLog.addView((View)this.tvMobileShellLog, (ViewGroup.LayoutParams)new FrameLayout.LayoutParams(-1, -2));
        logLayout.addView((View)this.svMobileShellLog, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -1));
        container.addView((View)logLayout);
        
        LinearLayout actionRow = new LinearLayout((Context)this);
        actionRow.setOrientation(0);
        actionRow.setGravity(16);
        actionRow.setPadding(0, 0, 0, this.dp(8));
        
        TextView inputTitle = this.textView("\u8f93\u5165 JS \u4ee3\u7801\uff1a", 14, -1, true);
        actionRow.addView((View)inputTitle, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        
        android.widget.Button btnClearLog = new android.widget.Button((Context)this);
        btnClearLog.setText((CharSequence)"\u6e05\u9664\u65e5\u5fd7");
        btnClearLog.setTextColor(Color.rgb(148, 163, 184));
        btnClearLog.setBackgroundColor(Color.TRANSPARENT);
        btnClearLog.setAllCaps(false);
        btnClearLog.setTextSize(12f);
        btnClearLog.setOnClickListener(v -> this.tvMobileShellLog.setText((CharSequence)""));
        actionRow.addView((View)btnClearLog, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, this.dp(30)));
        container.addView((View)actionRow);
        
        this.etMobileShellInput = new EditText((Context)this);
        this.etMobileShellInput.setHint((CharSequence)"context.mobile.toast('Hello Yanzi!');");
        this.etMobileShellInput.setHintTextColor(Color.rgb(100, 116, 139));
        this.etMobileShellInput.setTextColor(-1);
        this.etMobileShellInput.setTextSize(14f);
        this.etMobileShellInput.setTypeface(Typeface.MONOSPACE);
        this.etMobileShellInput.setGravity(83);
        this.etMobileShellInput.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        
        LinearLayout.LayoutParams inputParams = new LinearLayout.LayoutParams(-1, this.dp(100));
        inputParams.bottomMargin = this.dp(12);
        this.etMobileShellInput.setLayoutParams((ViewGroup.LayoutParams)inputParams);
        
        android.graphics.drawable.GradientDrawable inputBg = new android.graphics.drawable.GradientDrawable();
        inputBg.setColor(Color.rgb(15, 23, 42));
        inputBg.setCornerRadius((float)this.dp(6));
        inputBg.setStroke(this.dp(1), Color.rgb(51, 65, 85));
        this.etMobileShellInput.setBackground((android.graphics.drawable.Drawable)inputBg);
        container.addView((View)this.etMobileShellInput);
        
        android.widget.Button btnRun = this.button("\u8fd0\u884c\u4ee3\u7801");
        btnRun.setOnClickListener(v -> {
            String code = this.etMobileShellInput.getText().toString();
            this.runDirectJsCode(code);
        });
        container.addView((View)btnRun, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(44)));
    }

    private void appendMobileShellLog(String text) {
        this.runOnUiThread(() -> {
            if (this.tvMobileShellLog != null) {
                this.tvMobileShellLog.append(text + "\n");
                if (this.svMobileShellLog != null) {
                    this.svMobileShellLog.post(() -> this.svMobileShellLog.fullScroll(View.FOCUS_DOWN));
                }
            }
        });
    }

    private void runDirectJsCode(String jsCode) {
        if (jsCode.trim().isEmpty()) {
            this.appendMobileShellLog("[SYSTEM] \u8bf7\u8f93\u5165\u9700\u8981\u6267\u884c\u7684\u4ee3\u7801\u3002");
            return;
        }
        this.appendMobileShellLog("\n[SYSTEM] \u5f00\u59cb\u8fd0\u884c\u811a\u672c...");
        try {
            String finalSource;
            if (jsCode.contains("async function run") || jsCode.contains("function run")) {
                finalSource = jsCode;
            } else {
                finalSource = "async function run(context) {\n" + jsCode + "\n}";
            }
            this.executeMobileScriptHeadless(finalSource, "DirectCode", new ScriptCallback() {
                @Override
                public void onResult(String result) {
                    MainActivity.this.appendMobileShellLog("[SYSTEM] \u6267\u884c\u7ed3\u679c: " + result);
                }
            });
        }
        catch (Exception ex) {
            this.appendMobileShellLog("[SYSTEM] \u542f\u52a8\u9519\u8bef: " + ex.getMessage());
        }
    }

    private static class DocItem {
        String name;
        String desc;
        String params;
        String example;
        DocItem(String name, String desc, String params, String example) {
            this.name = name;
            this.desc = desc;
            this.params = params;
            this.example = example;
        }
    }

    private String getYanmSyncLogs() {
        String allLogs = MobileDiagnostics.get((Context)this);
        if (allLogs == null || allLogs.trim().isEmpty()) {
            return "\u6682\u65e0\u540c\u6b65\u8bb0\u5f55\u3002";
        }
        String[] lines = allLogs.split("\n");
        StringBuilder sb = new StringBuilder();
        for (String line : lines) {
            if (line.contains("\u71d5\u5e55") || line.contains("yanm") || line.contains("\u540c\u6b65") || line.contains("\u76f4\u8fde") || line.contains("LAN")) {
                sb.append(line).append("\n");
            }
        }
        return sb.length() == 0 ? "\u6682\u65e0\u76f8\u5173\u7684\u540c\u6b65\u8bb0\u5f55\u3002" : sb.toString().trim();
    }

    private void showYanmSyncLogsDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder((Context)this);
        
        LinearLayout layout = new LinearLayout((Context)this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
        layout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        
        LinearLayout titleBar = new LinearLayout((Context)this);
        titleBar.setOrientation(LinearLayout.HORIZONTAL);
        titleBar.setGravity(Gravity.CENTER_VERTICAL);
        
        TextView titleTv = new TextView((Context)this);
        titleTv.setText((CharSequence)"\u71d5\u5e55\u540c\u6b65\u4e0e\u8fde\u63a5\u65e5\u5fd7");
        titleTv.setTextSize(18f);
        titleTv.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
        titleTv.setTypeface(null, Typeface.BOLD);
        titleBar.addView((View)titleTv, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        
        Button btnRefresh = new Button((Context)this);
        btnRefresh.setText((CharSequence)"\u5237\u65b0");
        btnRefresh.setTextSize(12f);
        btnRefresh.setTextColor(ThemeConfig.COLOR_TEXT_SECONDARY);
        btnRefresh.setBackgroundColor(Color.TRANSPARENT);
        btnRefresh.setPadding(this.dp(8), this.dp(4), this.dp(8), this.dp(4));
        titleBar.addView((View)btnRefresh, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
        
        layout.addView((View)titleBar);
        
        View divider = new View((Context)this);
        divider.setBackgroundColor(ThemeConfig.COLOR_DIVIDER);
        LinearLayout.LayoutParams divParams = new LinearLayout.LayoutParams(-1, this.dp(1));
        divParams.topMargin = this.dp(10);
        divParams.bottomMargin = this.dp(10);
        layout.addView(divider, (ViewGroup.LayoutParams)divParams);
        
        androidx.core.widget.NestedScrollView scrollView = new androidx.core.widget.NestedScrollView((Context)this);
        scrollView.setPadding(this.dp(10), this.dp(10), this.dp(10), this.dp(10));
        scrollView.setBackgroundColor(ThemeConfig.COLOR_BACKGROUND);
        
        TextView logTv = new TextView((Context)this);
        logTv.setTextSize(12f);
        logTv.setTextColor(Color.rgb(212, 212, 216));
        logTv.setTypeface(Typeface.MONOSPACE);
        
        logTv.setText((CharSequence)this.getYanmSyncLogs());
        scrollView.addView((View)logTv);
        layout.addView((View)scrollView, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(300)));
        
        LinearLayout actionsLayout = new LinearLayout((Context)this);
        actionsLayout.setOrientation(LinearLayout.HORIZONTAL);
        actionsLayout.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
        actionsLayout.setPadding(0, this.dp(12), 0, 0);
        
        String viewUrl = this.getSharedPreferences("yanzi-mobile", 0).getString("yanm_view_url", "");
        if (viewUrl != null && !viewUrl.trim().isEmpty()) {
            Button btnGoCloud = new Button((Context)this);
            btnGoCloud.setText((CharSequence)"\u524d\u5f80\u4e91\u7aef");
            btnGoCloud.setTextSize(13f);
            btnGoCloud.setTextColor(Color.rgb(16, 185, 129)); // emerald-500
            btnGoCloud.setBackgroundColor(Color.TRANSPARENT);
            btnGoCloud.setOnClickListener(v -> {
                try {
                    Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(viewUrl.trim()));
                    this.startActivity(intent);
                } catch (Exception e) {
                    Toast.makeText((Context)this, (CharSequence)"\u65e0\u6cd5\u6263\u5f00\u94fe\u63a5", (int)0).show();
                }
            });
            actionsLayout.addView((View)btnGoCloud);
        }
        
        Button btnClear = new Button((Context)this);
        btnClear.setText((CharSequence)"\u6e05\u9664");
        btnClear.setTextSize(13f);
        btnClear.setTextColor(Color.rgb(239, 68, 68));
        btnClear.setBackgroundColor(Color.TRANSPARENT);
        actionsLayout.addView((View)btnClear);
        
        Button btnCopy = new Button((Context)this);
        btnCopy.setText((CharSequence)"\u590d\u5236");
        btnCopy.setTextSize(13f);
        btnCopy.setTextColor(Color.rgb(34, 211, 238));
        btnCopy.setBackgroundColor(Color.TRANSPARENT);
        actionsLayout.addView((View)btnCopy);
        
        Button btnClose = new Button((Context)this);
        btnClose.setText((CharSequence)"\u5173\u95ed");
        btnClose.setTextSize(13f);
        btnClose.setTextColor(Color.WHITE);
        btnClose.setBackgroundColor(Color.TRANSPARENT);
        actionsLayout.addView((View)btnClose);
        
        layout.addView((View)actionsLayout);
        
        builder.setView((View)layout);
        AlertDialog dialog = builder.create();
        
        btnRefresh.setOnClickListener(v -> {
            logTv.setText((CharSequence)this.getYanmSyncLogs());
        });
        btnClear.setOnClickListener(v -> {
            MobileDiagnostics.clear((Context)this);
            logTv.setText((CharSequence)"\u6e05\u9664\u6210\u529f");
        });
        btnCopy.setOnClickListener(v -> {
            ClipboardManager manager = (ClipboardManager)this.getSystemService("clipboard");
            if (manager != null) {
                manager.setPrimaryClip(ClipData.newPlainText((CharSequence)"Yanm Sync Logs", logTv.getText()));
                Toast.makeText((Context)this, (CharSequence)"\u540c\u6b65\u65e5\u5fd7\u5df2\u590d\u5236\u5230\u526a\u8d34\u677f", (int)0).show();
            }
        });
        btnClose.setOnClickListener(v -> dialog.dismiss());
        
        dialog.show();
        if (dialog.getWindow() != null) {
            GradientDrawable drawable = new GradientDrawable();
            drawable.setColor(Color.rgb(24, 24, 27));
            drawable.setCornerRadius((float)this.dp(12));
            dialog.getWindow().setBackgroundDrawable((android.graphics.drawable.Drawable)drawable);
        }
    }

    private LinearLayout createListItem(String title, final String subText, final Runnable onClick) {
        LinearLayout row = new LinearLayout((Context)this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(this.dp(16), this.dp(14), this.dp(16), this.dp(14));
        row.setClickable(true);
        row.setFocusable(true);
        
        TextView titleTv = new TextView((Context)this);
        titleTv.setText((CharSequence)title);
        titleTv.setTextSize(15f);
        titleTv.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
        row.addView((View)titleTv, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        
        LinearLayout rightLayout = new LinearLayout((Context)this);
        rightLayout.setOrientation(LinearLayout.HORIZONTAL);
        rightLayout.setGravity(Gravity.END | Gravity.CENTER_VERTICAL);
        
        final TextView subTv = new TextView((Context)this);
        subTv.setText((CharSequence)(subText == null ? "" : subText));
        subTv.setTextSize(13f);
        subTv.setTextColor(ThemeConfig.COLOR_TEXT_SECONDARY);
        subTv.setPadding(0, 0, this.dp(8), 0);
        rightLayout.addView((View)subTv);
        
        TextView chevron = new TextView((Context)this);
        chevron.setText((CharSequence)">");
        chevron.setTextSize(14f);
        chevron.setTextColor(ThemeConfig.COLOR_TEXT_MUTED);
        rightLayout.addView((View)chevron);
        
        row.addView((View)rightLayout, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
        
        row.setOnTouchListener(new View.OnTouchListener() {
            @Override
            public boolean onTouch(View v, android.view.MotionEvent event) {
                if (event.getAction() == android.view.MotionEvent.ACTION_DOWN) {
                    v.setBackgroundColor(ThemeConfig.COLOR_ITEM_PRESSED);
                } else if (event.getAction() == android.view.MotionEvent.ACTION_UP || event.getAction() == android.view.MotionEvent.ACTION_CANCEL) {
                    v.setBackgroundColor(Color.TRANSPARENT);
                }
                return false;
            }
        });
        
        row.setOnClickListener(v -> {
            if (onClick != null) {
                onClick.run();
            }
        });
        
        row.setTag(subTv);
        return row;
    }

    private LinearLayout createListGroup(LinearLayout... items) {
        LinearLayout group = new LinearLayout((Context)this);
        group.setOrientation(LinearLayout.VERTICAL);
        GradientDrawable gd = new GradientDrawable();
        gd.setColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        gd.setCornerRadius((float)this.dp(12));
        group.setBackground((Drawable)gd);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.bottomMargin = this.dp(12);
        group.setLayoutParams((ViewGroup.LayoutParams)lp);
        
        for (int i = 0; i < items.length; i++) {
            group.addView((View)items[i]);
            if (i < items.length - 1) {
                View divider = new View((Context)this);
                divider.setBackgroundColor(ThemeConfig.COLOR_DIVIDER);
                LinearLayout.LayoutParams divLp = new LinearLayout.LayoutParams(-1, this.dp(1));
                divLp.leftMargin = this.dp(16);
                divLp.rightMargin = this.dp(16);
                group.addView(divider, (ViewGroup.LayoutParams)divLp);
            }
        }
        return group;
    }

    private void setupProfileHeader() {
        LinearLayout header = new LinearLayout((Context)this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(this.dp(8), this.dp(16), this.dp(8), this.dp(24));
        header.setClickable(true);
        header.setFocusable(true);
        
        this.profileAvatarView = new android.widget.ImageView((Context)this);
        int avatarSize = this.dp(60);
        LinearLayout.LayoutParams avatarParams = new LinearLayout.LayoutParams(avatarSize, avatarSize);
        avatarParams.rightMargin = this.dp(16);
        this.profileAvatarView.setLayoutParams((ViewGroup.LayoutParams)avatarParams);
        
        int resId = this.getResources().getIdentifier("yanzi_launcher_bitmap", "drawable", this.getPackageName());
        if (resId == 0) {
            resId = this.getResources().getIdentifier("yanzi_launcher", "drawable", this.getPackageName());
        }
        if (resId == 0) {
            resId = this.getResources().getIdentifier("ic_launcher", "drawable", this.getPackageName());
        }
        if (resId != 0) {
            this.profileAvatarView.setImageResource(resId);
        }
        
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.LOLLIPOP) {
            this.profileAvatarView.setClipToOutline(true);
            this.profileAvatarView.setOutlineProvider(new android.view.ViewOutlineProvider() {
                @Override
                public void getOutline(View view, android.graphics.Outline outline) {
                    outline.setRoundRect(0, 0, view.getWidth(), view.getHeight(), (float)MainActivity.this.dp(12));
                }
            });
        }
        
        LinearLayout textLayout = new LinearLayout((Context)this);
        textLayout.setOrientation(LinearLayout.VERTICAL);
        
        this.profileNameView = new android.widget.TextView((Context)this);
        this.profileNameView.setTextSize(18f);
        this.profileNameView.setTextColor(-1);
        this.profileNameView.setTypeface(null, Typeface.BOLD);
        
        this.profileSubtextView = new android.widget.TextView((Context)this);
        this.profileSubtextView.setTextSize(13f);
        this.profileSubtextView.setTextColor(Color.rgb(161, 161, 170));
        this.profileSubtextView.setPadding(0, this.dp(4), 0, 0);
        
        textLayout.addView((View)this.profileNameView);
        textLayout.addView((View)this.profileSubtextView);
        
        header.addView((View)this.profileAvatarView);
        header.addView((View)textLayout, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        
        header.setOnClickListener(v -> this.showAccountSettingsDialog());
        
        this.profileTabPage.addView((View)header);
    }

    private void updateProfileHeader() {
        if (this.profileAvatarView == null || this.profileNameView == null || this.profileSubtextView == null) {
            return;
        }
        String email = this.prefs.getString("email", "");
        String username = this.prefs.getString("username", "");
        if (username == null || username.trim().isEmpty()) {
            if (email != null && !email.trim().isEmpty()) {
                if (email.contains("@")) {
                    username = email.substring(0, email.indexOf("@"));
                } else {
                    username = email;
                }
            }
        }
        if (username != null && !username.trim().isEmpty()) {
            this.profileNameView.setText((CharSequence)username);
            this.profileSubtextView.setVisibility(View.GONE);
            this.profileAvatarView.setAlpha(1.0f);
        } else {
            this.profileNameView.setText((CharSequence)"\u672a\u767b\u5f55");
            this.profileSubtextView.setText((CharSequence)"\u70b9\u51fb\u767b\u5f55\u540c\u6b65\u670d\u52a1");
            this.profileSubtextView.setVisibility(View.VISIBLE);
            this.profileAvatarView.setAlpha(0.6f);
        }
    }

    private void showAccountSettingsDialog() {
        LinearLayout dialogLayout = new LinearLayout((Context)this);
        dialogLayout.setOrientation(1);
        dialogLayout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
        dialogLayout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        if (this.emailInput.getParent() != null) {
            ((ViewGroup)this.emailInput.getParent()).removeView((View)this.emailInput);
        }
        if (this.passwordInput.getParent() != null) {
            ((ViewGroup)this.passwordInput.getParent()).removeView((View)this.passwordInput);
        }
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
        
        AlertDialog dialog = new AlertDialog.Builder((Context)this, 16974545)
            .setTitle((CharSequence)"\u8d26\u53f7")
            .setView((View)dialogLayout)
            .setCancelable(true)
            .show();
        this.accountDialog = dialog;
        dialog.setOnDismissListener(d -> {
            this.accountDialog = null;
        });
            
        logoutBtn.setOnClickListener(v1 -> {
            this.prefs.edit().putString("token", "").putString("email", "").putString("username", "").apply();
            this.setStatus("\u5df2\u6e05\u9664\u67ac\u5730\u767b\u5f55\u6001\u3002");
            if (this.loginButton != null) {
                this.loginButton.setEnabled(true);
            }
            this.updateProfileHeader();
            this.accountDialog = null;
            dialog.dismiss();
        });
    }

    private void showSendTextToDesktopDialog() {
        LinearLayout dialogLayout = new LinearLayout((Context)this);
        dialogLayout.setOrientation(1);
        dialogLayout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
        dialogLayout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        if (this.textInput.getParent() != null) {
            ((ViewGroup)this.textInput.getParent()).removeView((View)this.textInput);
        }
        dialogLayout.addView((View)this.textInput);
        Button sendBtn = this.button("\u53d1\u9001");
        dialogLayout.addView((View)sendBtn, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, this.dp(48)));
        AlertDialog dialog = new AlertDialog.Builder((Context)this, 16974545)
            .setTitle((CharSequence)"\u53d1\u9001\u6d88\u606f\u5230\u7535\u8111")
            .setView((View)dialogLayout)
            .setPositiveButton((CharSequence)"\u5173\u95ed", null)
            .show();
        sendBtn.setOnClickListener(v1 -> {
            this.sendToDesktop();
            dialog.dismiss();
        });
    }

    private void showCloudSyncUpdateSettingsDialog(final TextView statusTv) {
        LinearLayout dialogLayout = new LinearLayout((Context)this);
        dialogLayout.setOrientation(LinearLayout.VERTICAL);
        dialogLayout.setPadding(this.dp(20), this.dp(20), this.dp(20), this.dp(20));
        dialogLayout.setBackgroundColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        
        LinearLayout rowSwitch = new LinearLayout((Context)this);
        rowSwitch.setOrientation(LinearLayout.HORIZONTAL);
        rowSwitch.setGravity(Gravity.CENTER_VERTICAL);
        rowSwitch.setPadding(0, 0, 0, this.dp(16));
        
        TextView labelSwitch = this.textView("\u5f00\u542f\u4e91\u7aef\u81ea\u52a8\u66f4\u65b0", 15, -1, false);
        android.widget.Switch sw = new android.widget.Switch((Context)this);
        sw.setChecked(this.prefs.getBoolean("auto_cloud_update", false));
        
        rowSwitch.addView((View)labelSwitch, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        rowSwitch.addView((View)sw, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
        dialogLayout.addView((View)rowSwitch);
        
        LinearLayout rowFreq = new LinearLayout((Context)this);
        rowFreq.setOrientation(LinearLayout.VERTICAL);
        
        TextView labelFreq = this.textView("\u66f4\u65b0\u9891\u7387\u0020\u0028\u79d2\u0029", 14, Color.rgb(148, 163, 184), false);
        rowFreq.addView((View)labelFreq);
        
        final EditText etInterval = new EditText((Context)this);
        etInterval.setInputType(2);
        etInterval.setTextColor(-1);
        int currentInterval = this.prefs.getInt("auto_cloud_update_interval", 60);
        etInterval.setText((CharSequence)String.valueOf(currentInterval));
        etInterval.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        etInterval.setBackgroundColor(Color.rgb(30, 30, 30));
        
        rowFreq.addView((View)etInterval, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-1, -2));
        dialogLayout.addView((View)rowFreq);
        
        AlertDialog dialog = new AlertDialog.Builder((Context)this, 16974545)
            .setTitle((CharSequence)"\u4e91\u7aef\u81ea\u52a8\u66f4\u65b0\u914d\u7f6e")
            .setView((View)dialogLayout)
            .setPositiveButton((CharSequence)"\u4fdd\u5b58", (dialogInterface, which) -> {
                boolean autoUpdate = sw.isChecked();
                String val = etInterval.getText().toString().trim();
                int interval = 60;
                try {
                    interval = Integer.parseInt(val);
                } catch (Exception e) {}
                if (interval < 10) {
                    interval = 10;
                }
                
                this.prefs.edit()
                    .putBoolean("auto_cloud_update", autoUpdate)
                    .putInt("auto_cloud_update_interval", interval)
                    .apply();
                
                this.autoCloudUpdateHandler.removeCallbacks(this.autoCloudUpdateRunnable);
                if (autoUpdate) {
                    this.autoCloudUpdateHandler.postDelayed(this.autoCloudUpdateRunnable, 1000L);
                    Toast.makeText(this.getApplicationContext(), (CharSequence)"\u5df2\u542f\u7528\u81ea\u52a8\u66f4\u65b0", Toast.LENGTH_SHORT).show();
                    if (statusTv != null) {
                        statusTv.setText((CharSequence)("\u5df2\u542f\u7528(" + interval + "\u79d2)"));
                    }
                } else {
                    Toast.makeText(this.getApplicationContext(), (CharSequence)"\u5df2\u5173\u95ed\u81ea\u52a8\u66f4\u65b0", Toast.LENGTH_SHORT).show();
                    if (statusTv != null) {
                        statusTv.setText((CharSequence)"\u5df2\u5173\u95ed");
                    }
                }
            })
            .setNegativeButton((CharSequence)"\u5356\u5b50", null) //这里在原有代码中实际上是"取消"的Unicode转义
            .setNegativeButton((CharSequence)"\u53d6\u6d88", null)
            .show();
    }

    private void pickChatPhoto() {
        try {
            Intent intent = new Intent("android.intent.action.OPEN_DOCUMENT");
            intent.addCategory("android.intent.category.OPENABLE");
            intent.setType("image/*");
            this.startActivityForResult(intent, 4103);
        }
        catch (Exception ex) {
            this.setStatus("\u6253\u5f00\u76f8\u518c\u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private void pickChatFile() {
        try {
            Intent intent = new Intent("android.intent.action.GET_CONTENT");
            intent.setType("*/*");
            intent.addCategory("android.intent.category.OPENABLE");
            this.startActivityForResult(intent, 4102);
        }
        catch (Exception ex) {
            this.setStatus("\u6253\u5f00\u6587\u4ef6\u7ba1\u7406\u5668\u5931\u8d25\uff1a" + ex.getMessage());
        }
    }

    private void sendPhotoToDesktopChat(Uri uri) {
        this.setStatus("\u6b63\u5728\u5904\u7406\u7167\u7247...");
        this.showPhotoProgress("\u6b63\u5728\u53d1\u9001\u7167\u7247...");
        this.renderChatMessage("self", "photo", uri.toString(), true);
        this.saveChatMessageToLocal("self", "photo", uri.toString());
        
        this.executor.execute(() -> {
            try {
                byte[] jpegBytes = this.readJpegBytesFromUri(uri);
                int[] size = MainActivity.readImageSizeFromJpegBytes(jpegBytes);
                int width = size[0];
                int height = size[1];
                
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                String messageId;
                try {
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendPhotoToDesktop(baseUrl, token, this.deviceId, jpegBytes, width, height);
                } catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendPhotoToDesktop(baseUrl, token, this.deviceId, jpegBytes, width, height);
                }
                final String finalMsgId = messageId;
                this.runOnUiThread(() -> {
                    this.hidePhotoProgress();
                    this.setStatus("\u7167\u7247\u5df2\u53d1\u9001\uff0cid=" + finalMsgId);
                });
            } catch (Exception ex) {
                this.runOnUiThread(() -> {
                    this.hidePhotoProgress();
                    this.setStatus("\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage());
                    this.renderChatMessage("system", "text", "\u7167\u7247\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage(), true);
                });
            }
        });
    }

    private void sendFileToDesktopChat(Uri uri) {
        this.setStatus("\u6b63\u5728\u5904\u7406\u6587\u4ef6...");
        this.showPhotoProgress("\u6b63\u5728\u53d1\u9001\u6587\u4ef6...");
        
        this.executor.execute(() -> {
            try {
                String fileName = "file_" + System.currentTimeMillis();
                long fileSize = 0;
                android.database.Cursor cursor = this.getContentResolver().query(uri, null, null, null, null);
                if (cursor != null) {
                    try {
                        if (cursor.moveToFirst()) {
                            int nameIndex = cursor.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME);
                            if (nameIndex != -1) {
                                String name = cursor.getString(nameIndex);
                                if (name != null && !name.isEmpty()) {
                                    fileName = name;
                                }
                            }
                            int sizeIndex = cursor.getColumnIndex(android.provider.OpenableColumns.SIZE);
                            if (sizeIndex != -1) {
                                fileSize = cursor.getLong(sizeIndex);
                            }
                        }
                    } finally {
                        cursor.close();
                    }
                }
                
                final String finalFileName = fileName;
                if (fileSize > 30 * 1024 * 1024) {
                    throw new IllegalStateException("\u4e0d\u652f\u6301\u53d1\u9001\u8d85\u8fc7 30MB \u7684\u5927\u6587\u4ef6");
                }
                
                java.io.InputStream inputStream = this.getContentResolver().openInputStream(uri);
                if (inputStream == null) {
                    throw new java.io.IOException("\u65e0\u6cd5\u6253\u5f00\u8f93\u5165\u6d41");
                }
                java.io.ByteArrayOutputStream byteBuffer = new java.io.ByteArrayOutputStream();
                byte[] buffer = new byte[8192];
                int len;
                while ((len = inputStream.read(buffer)) != -1) {
                    byteBuffer.write(buffer, 0, len);
                }
                inputStream.close();
                byte[] bytes = byteBuffer.toByteArray();
                
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                YanziApiClient.WebDavConfig config;
                try {
                    config = YanziApiClient.fetchWebDavConfig(baseUrl, token);
                } catch (Exception ex) {
                    if (MainActivity.isUnauthorized(ex)) {
                        token = this.refreshToken();
                        config = YanziApiClient.fetchWebDavConfig(baseUrl, token);
                    } else {
                        throw new IllegalStateException("\u65e0\u6cd5\u83b7\u53d6 WebDAV \u914d\u7f6e\uff0c\u8bf7\u786e\u8ba4\u5df2\u5728\u7535\u8111\u6216\u6211\u7684\u9875\u9762\u914d\u7f6e\u4e86\u575a\u679c\u4e91\u670d\u52a1\uff1a" + ex.getMessage());
                    }
                }
                
                String relativePath = "temp-mobile-upload-" + System.currentTimeMillis() + "-" + finalFileName;
                this.runOnUiThread(() -> this.setStatus("\u6b63\u5728\u4e0a\u4f20\u5230\u4e91\u7aef..."));
                YanziApiClient.putWebDavBytes(config, relativePath, bytes, "application/octet-stream");
                
                this.runOnUiThread(() -> {
                    this.renderChatMessage("self", "file", finalFileName, true);
                    this.saveChatMessageToLocal("self", "file", finalFileName);
                });
                
                JSONObject payload = new JSONObject();
                payload.put("sourceDeviceId", (Object)this.deviceId);
                payload.put("targetPlatform", (Object)"desktop");
                payload.put("kind", (Object)"file");
                payload.put("title", (Object)finalFileName);
                payload.put("text", (Object)("\u624b\u673a\u6587\u4ef6\uff1a" + finalFileName));
                
                JSONObject innerPayload = new JSONObject();
                innerPayload.put("source", (Object)"android");
                innerPayload.put("sourceDeviceName", (Object)MainActivity.buildDeviceDisplayName());
                innerPayload.put("createdAt", System.currentTimeMillis());
                innerPayload.put("webDavPath", (Object)relativePath);
                payload.put("payload", (Object)innerPayload);
                
                String messageId = YanziApiClient.postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "\u53d1\u9001\u6587\u4ef6").optString("messageId", "unknown");
                
                this.runOnUiThread(() -> {
                    this.hidePhotoProgress();
                    this.setStatus("\u6587\u4ef6\u5df2\u6210\u529f\u53d1\u9001\uff0cid=" + messageId);
                });
            } catch (Exception ex) {
                this.runOnUiThread(() -> {
                    this.hidePhotoProgress();
                    this.setStatus("\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage());
                    this.renderChatMessage("system", "text", "\u6587\u4ef6\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage(), true);
                });
            }
        });
    }

    private void handleSendChatMessageClick() {
        if (this.chatInputEditText == null) return;
        String text = this.chatInputEditText.getText().toString().trim();
        if (text.isEmpty()) return;
        
        this.chatInputEditText.setText("");
        this.setStatus("\u6b63\u5728\u53d1\u9001\u5230\u7535\u8111...");
        this.renderChatMessage("self", "text", text, true);
        this.saveChatMessageToLocal("self", "text", text);
        
        this.executor.execute(() -> {
            try {
                String baseUrl = this.normalizedBaseUrl();
                String token = this.requireToken();
                String messageId = "";
                try {
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, this.deviceId, text);
                } catch (Exception ex) {
                    if (!MainActivity.isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = this.refreshToken();
                    YanziApiClient.registerDevice(baseUrl, token, this.deviceId, this.buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, this.deviceId, text);
                }
                final String finalMsgId = messageId;
                this.runOnUiThread(() -> this.setStatus("\u6d88\u606f\u5df2\u53d1\u9001\uff0cid=" + finalMsgId));
            } catch (Exception ex) {
                this.runOnUiThread(() -> {
                    this.setStatus("\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage());
                    this.renderChatMessage("system", "text", "\u53d1\u9001\u5931\u8d25\uff1a" + ex.getMessage(), true);
                });
            }
        });
    }

    private LinearLayout buildChatContainer() {
        this.chatContainerLayout = new LinearLayout((Context)this);
        this.chatContainerLayout.setOrientation(LinearLayout.VERTICAL);
        
        ScrollView scroll = new ScrollView((Context)this);
        this.chatMessageListLayout = new LinearLayout((Context)this);
        this.chatMessageListLayout.setOrientation(LinearLayout.VERTICAL);
        this.chatMessageListLayout.setPadding(0, this.dp(8), 0, this.dp(8));
        scroll.addView((View)this.chatMessageListLayout);
        
        LinearLayout.LayoutParams scrollParams = new LinearLayout.LayoutParams(-1, 0, 1.0f);
        this.chatContainerLayout.addView((View)scroll, (ViewGroup.LayoutParams)scrollParams);
        
        LinearLayout inputRow = new LinearLayout((Context)this);
        inputRow.setOrientation(LinearLayout.HORIZONTAL);
        inputRow.setGravity(Gravity.CENTER_VERTICAL);
        inputRow.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        
        this.chatVoiceToggleBtn = new ImageView((Context)this);
        this.chatVoiceToggleBtn.setClickable(true);
        this.chatVoiceToggleBtn.setFocusable(true);
        this.chatVoiceToggleBtn.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        this.chatVoiceToggleBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("microphone"), Color.rgb(200, 200, 200)));
        this.chatVoiceToggleBtn.setOnClickListener(v -> {
            if (this.chatHoldToSpeakBtn.getVisibility() == View.GONE) {
                this.isChatVoiceActive = true;
                if (this.checkAudioPermission()) {
                    if (this.isBackendSpeechRecognizerWorkable()) {
                        this.switchToChatVoiceInput();
                    } else {
                        Toast.makeText((Context)this, "拉起系统语音输入...", Toast.LENGTH_SHORT).show();
                        this.startSpeechIntent();
                    }
                }
            } else {
                this.switchToChatTextInput();
            }
        });
        LinearLayout.LayoutParams voiceToggleParams = new LinearLayout.LayoutParams(this.dp(40), this.dp(40));
        voiceToggleParams.rightMargin = this.dp(8);
        inputRow.addView((View)this.chatVoiceToggleBtn, (ViewGroup.LayoutParams)voiceToggleParams);

        this.chatInputEditText = this.input("发送给电脑...", "");
        GradientDrawable inputBg = new GradientDrawable();
        inputBg.setColor(ThemeConfig.COLOR_CARD_BACKGROUND);
        inputBg.setCornerRadius((float)this.dp(8));
        this.chatInputEditText.setBackground((Drawable)inputBg);
        
        LinearLayout.LayoutParams editParams = new LinearLayout.LayoutParams(0, this.dp(40), 1.0f);
        editParams.rightMargin = this.dp(8);
        inputRow.addView((View)this.chatInputEditText, (ViewGroup.LayoutParams)editParams);

        this.chatHoldToSpeakBtn = new Button((Context)this);
        this.chatHoldToSpeakBtn.setText("按住 说话");
        this.chatHoldToSpeakBtn.setTextColor(-1);
        this.chatHoldToSpeakBtn.setTextSize(14f);
        GradientDrawable speakBg = new GradientDrawable();
        speakBg.setColor(Color.rgb(59, 130, 246));
        speakBg.setCornerRadius((float)this.dp(8));
        this.chatHoldToSpeakBtn.setBackground((Drawable)speakBg);
        this.chatHoldToSpeakBtn.setVisibility(View.GONE);
        this.chatHoldToSpeakBtn.setOnTouchListener((v, event) -> {
            switch (event.getAction()) {
                case 0: // ACTION_DOWN
                    this.isChatVoiceActive = true;
                    this.chatHoldToSpeakBtn.setText("松开 结束");
                    GradientDrawable downBg = new GradientDrawable();
                    downBg.setColor(Color.rgb(220, 68, 68));
                    downBg.setCornerRadius((float)this.dp(8));
                    this.chatHoldToSpeakBtn.setBackground((Drawable)downBg);
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
                    this.chatHoldToSpeakBtn.setText("按住 说话");
                    GradientDrawable upBg = new GradientDrawable();
                    upBg.setColor(Color.rgb(59, 130, 246));
                    upBg.setCornerRadius((float)this.dp(8));
                    this.chatHoldToSpeakBtn.setBackground((Drawable)upBg);
                    this.stopSpeechRecognition();
                    return true;
            }
            return false;
        });
        LinearLayout.LayoutParams speakParams = new LinearLayout.LayoutParams(0, this.dp(40), 1.0f);
        speakParams.rightMargin = this.dp(8);
        inputRow.addView((View)this.chatHoldToSpeakBtn, (ViewGroup.LayoutParams)speakParams);
        
        this.chatAttachBtn = new ImageView((Context)this);
        this.chatAttachBtn.setClickable(true);
        this.chatAttachBtn.setFocusable(true);
        this.chatAttachBtn.setPadding(this.dp(8), this.dp(8), this.dp(8), this.dp(8));
        this.chatAttachBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("plus"), Color.rgb(200, 200, 200)));
        this.chatAttachBtn.setOnClickListener(v -> {
            PopupMenu popup = new PopupMenu((Context)this, this.chatAttachBtn);
            popup.getMenu().add(0, 1, 0, "照片");
            popup.getMenu().add(0, 2, 0, "文件");
            popup.getMenu().add(0, 3, 0, "拍照");
            popup.setOnMenuItemClickListener(item -> {
                int id = item.getItemId();
                if (id == 1) {
                    this.pickChatPhoto();
                } else if (id == 2) {
                    this.pickChatFile();
                } else if (id == 3) {
                    this.takeCameraPhotoForChat();
                }
                return true;
            });
            popup.show();
        });
        LinearLayout.LayoutParams attachParams = new LinearLayout.LayoutParams(this.dp(40), this.dp(40));
        attachParams.rightMargin = this.dp(8);
        inputRow.addView((View)this.chatAttachBtn, (ViewGroup.LayoutParams)attachParams);

        this.chatSendButton = this.button("发送");
        this.chatSendButton.setOnClickListener(v -> this.handleSendChatMessageClick());
        LinearLayout.LayoutParams sendParams = new LinearLayout.LayoutParams(this.dp(60), this.dp(40));
        inputRow.addView((View)this.chatSendButton, (ViewGroup.LayoutParams)sendParams);
        
        this.chatContainerLayout.addView((View)inputRow);
        
        this.loadChatHistory();
        return this.chatContainerLayout;
    }

    private void switchToChatVoiceInput() {
        this.isChatVoiceActive = true;
        this.chatHoldToSpeakBtn.setVisibility(View.VISIBLE);
        this.chatHoldToSpeakBtn.setText("按住 说话");
        this.chatInputEditText.setVisibility(View.GONE);
        this.chatVoiceToggleBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("keyboard"), Color.rgb(200, 200, 200)));
        this.hideKeyboard((View)this.chatInputEditText);
    }

    private void switchToChatTextInput() {
        this.isChatVoiceActive = false;
        this.chatHoldToSpeakBtn.setVisibility(View.GONE);
        this.chatInputEditText.setVisibility(View.VISIBLE);
        this.chatVoiceToggleBtn.setImageDrawable(new PathDrawable(MobileIconLibrary.resolveOrDefault("microphone"), Color.rgb(200, 200, 200)));
    }

    private void takeCameraPhotoForChat() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            if (this.checkSelfPermission(android.Manifest.permission.CAMERA) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                this.requestPermissions(new String[]{android.Manifest.permission.CAMERA}, 9002);
                return;
            }
        }
        this.launchCameraForChat();
    }

    private void launchCameraForChat() {
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
                this.startActivityForResult(intent, 4104);
            } catch (Exception e) {
                this.setStatus("拍照初始化失败: " + e.getMessage());
            }
        }
    }

    private void saveChatMessageToLocal(String role, String kind, String content) {
        try {
            String historyJson = this.prefs.getString("desktop_chat_history", "[]");
            JSONArray arr = new JSONArray(historyJson);
            JSONObject obj = new JSONObject();
            obj.put("role", (Object)role);
            obj.put("kind", (Object)kind);
            obj.put("content", (Object)content);
            obj.put("time", System.currentTimeMillis());
            arr.put((Object)obj);
            
            if (arr.length() > 50) {
                JSONArray newArr = new JSONArray();
                for (int i = arr.length() - 50; i < arr.length(); ++i) {
                    newArr.put(arr.get(i));
                }
                arr = newArr;
            }
            this.prefs.edit().putString("desktop_chat_history", arr.toString()).apply();
        } catch (Exception ignored) {}
    }
    
    private void loadChatHistory() {
        android.util.Log.i("MainActivity", "loadChatHistory: chatMessageListLayout=" + this.chatMessageListLayout);
        if (this.chatMessageListLayout == null) return;
        this.chatMessageListLayout.removeAllViews();
        String historyJson = this.prefs.getString("desktop_chat_history", "[]");
        android.util.Log.i("MainActivity", "loadChatHistory: historyJson=" + historyJson);
        try {
            JSONArray arr = new JSONArray(historyJson);
            for (int i = 0; i < arr.length(); ++i) {
                JSONObject obj = arr.getJSONObject(i);
                this.renderChatMessage(obj.optString("role"), obj.optString("kind"), obj.optString("content"), false);
            }
        } catch (Exception e) {
            android.util.Log.e("MainActivity", "loadChatHistory error", e);
        }
    }

    public static void onReceivedChatMessage(String msg) {
        onReceivedChatMessage("text", msg);
    }

    public static void onReceivedChatMessage(String kind, String msg) {
        android.util.Log.i("MainActivity", "onReceivedChatMessage static callback, kind=" + kind + ", msg=" + msg + ", sInstance=" + sInstance);
        if (sInstance != null) {
            sInstance.runOnUiThread(() -> {
                sInstance.renderChatMessage("desktop", kind, msg, true);
            });
        }
    }

    private void renderChatMessage(String role, String kind, String content, boolean scrollToBottom) {
        this.runOnUiThread(() -> {
            if (this.chatMessageListLayout == null) return;
            
            LinearLayout bubbleContainer = new LinearLayout((Context)this);
            bubbleContainer.setOrientation(LinearLayout.HORIZONTAL);
            bubbleContainer.setPadding(0, this.dp(4), 0, this.dp(4));
            
            boolean isSelf = "self".equals(role);
            bubbleContainer.setGravity(isSelf ? Gravity.END : Gravity.START);
            
            LinearLayout bubble = new LinearLayout((Context)this);
            bubble.setOrientation(LinearLayout.VERTICAL);
            bubble.setPadding(this.dp(12), this.dp(8), this.dp(12), this.dp(8));
            
            GradientDrawable gd = new GradientDrawable();
            gd.setColor(isSelf ? Color.rgb(30, 41, 59) : ThemeConfig.COLOR_CARD_BACKGROUND);
            gd.setCornerRadius((float)this.dp(12));
            bubble.setBackground((Drawable)gd);
            
            if ("photo".equals(kind)) {
                TextView label = new TextView((Context)this);
                label.setText((CharSequence)"[\u7167\u7247]");
                label.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
                label.setTextSize(14f);
                bubble.addView((View)label);
                
                if (content.startsWith("content://") || content.startsWith("file://")) {
                    try {
                        ImageView iv = new ImageView((Context)this);
                        iv.setPadding(0, this.dp(4), 0, 0);
                        LinearLayout.LayoutParams imgLp = new LinearLayout.LayoutParams(this.dp(120), this.dp(120));
                        iv.setLayoutParams((ViewGroup.LayoutParams)imgLp);
                        iv.setImageURI(Uri.parse(content));
                        bubble.addView((View)iv);
                    } catch (Exception ignored) {}
                }
            } else if ("file".equals(kind)) {
                TextView fileLabel = new TextView((Context)this);
                fileLabel.setText((CharSequence)("[\u6587\u4ef6] " + content));
                fileLabel.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
                fileLabel.setTextSize(14f);
                bubble.addView((View)fileLabel);
            } else {
                TextView text = new TextView((Context)this);
                text.setText((CharSequence)content);
                text.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
                text.setTextSize(14f);
                bubble.addView((View)text);
            }
            
            bubble.setOnLongClickListener(v -> {
                PopupMenu popup = new PopupMenu((Context)MainActivity.this, bubble);
                popup.getMenu().add(0, 1, 0, "复制消息");
                popup.getMenu().add(0, 2, 0, "清理全部消息");
                popup.setOnMenuItemClickListener(item -> {
                    int id = item.getItemId();
                    if (id == 1) {
                        try {
                            android.content.ClipboardManager clipboard = (android.content.ClipboardManager) MainActivity.this.getSystemService(Context.CLIPBOARD_SERVICE);
                            if (clipboard != null) {
                                String clipText = content;
                                if ("photo".equals(kind)) {
                                    clipText = "[图片] " + content;
                                } else if ("file".equals(kind)) {
                                    clipText = "[文件] " + content;
                                }
                                android.content.ClipData clip = android.content.ClipData.newPlainText("Copied Chat Message", clipText);
                                clipboard.setPrimaryClip(clip);
                                Toast.makeText(MainActivity.this, "消息已复制到剪贴板", Toast.LENGTH_SHORT).show();
                            }
                        } catch (Exception ex) {
                            Toast.makeText(MainActivity.this, "复制失败: " + ex.getMessage(), Toast.LENGTH_SHORT).show();
                        }
                    } else if (id == 2) {
                        new android.app.AlertDialog.Builder((Context)MainActivity.this)
                            .setTitle("提示")
                            .setMessage("确定要清理全部聊天消息吗？")
                            .setPositiveButton("确定", (dialog, which) -> {
                                MainActivity.this.prefs.edit().putString("desktop_chat_history", "[]").apply();
                                MainActivity.this.loadChatHistory();
                                Toast.makeText(MainActivity.this, "聊天历史已清理", Toast.LENGTH_SHORT).show();
                            })
                            .setNegativeButton("取消", null)
                            .show();
                    }
                    return true;
                });
                popup.show();
                return true;
            });
            
            LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
            if (isSelf) {
                lp.leftMargin = this.dp(60);
            } else {
                lp.rightMargin = this.dp(60);
            }
            bubbleContainer.addView((View)bubble, (ViewGroup.LayoutParams)lp);
            this.chatMessageListLayout.addView((View)bubbleContainer);
            
            if (scrollToBottom) {
                if (this.chatMessageListLayout.getParent() instanceof ScrollView) {
                    ScrollView sv = (ScrollView)this.chatMessageListLayout.getParent();
                    sv.post(() -> sv.fullScroll(View.FOCUS_DOWN));
                }
            }
        });
    }

    private LinearLayout createSwitchListItem(String title, boolean checked, android.widget.CompoundButton.OnCheckedChangeListener listener) {
        LinearLayout row = new LinearLayout((Context)this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(this.dp(16), this.dp(10), this.dp(16), this.dp(10));
        
        TextView titleTv = new TextView((Context)this);
        titleTv.setText((CharSequence)title);
        titleTv.setTextSize(15f);
        titleTv.setTextColor(ThemeConfig.COLOR_TEXT_PRIMARY);
        row.addView((View)titleTv, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(0, -2, 1.0f));
        
        android.widget.Switch sw = new android.widget.Switch((Context)this);
        sw.setChecked(checked);
        sw.setOnCheckedChangeListener(listener);
        row.addView((View)sw, (ViewGroup.LayoutParams)new LinearLayout.LayoutParams(-2, -2));
        
        return row;
    }
}

