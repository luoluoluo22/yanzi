package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.provider.Settings;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends Activity {
    private static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";
    private static final String CACHE_REMOTE_EXTENSIONS = "cacheRemoteExtensionsJson";
    private static final String CACHE_YANM = "cacheYanmJson";
    private static final int REQUEST_PICK_PHOTO = 4101;

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
    private View yanmTabButton;
    private View mobileExtensionTabButton;
    private View desktopExtensionTabButton;
    private View profileTabButton;
    private Button loginButton;
    private SwipeRefreshLayout swipeRefresh;
    private final java.util.Set<String> expandedComponentIds = new java.util.HashSet<>();
    private final java.util.List<String> sortedComponentIds = new java.util.ArrayList<>();
    private final java.util.Map<String, WebView> activeYanmWebViews = new java.util.HashMap<>();
    private WebView activeMobileScriptRunner;
    private View photoProgressView;
    private final android.os.Handler yanmSyncHandler = new android.os.Handler(android.os.Looper.getMainLooper());
    private final android.os.Handler diagnosticRefreshHandler = new android.os.Handler(android.os.Looper.getMainLooper());
    private final Runnable diagnosticRefreshRunnable = new Runnable() {
        @Override
        public void run() {
            refreshDiagnosticLogFromStore();
            diagnosticRefreshHandler.postDelayed(this, 1000);
        }
    };
    private JSONObject currentYanmState;
    private JSONObject currentYanmSnapshot;
    private Runnable pendingYanmSync;
    private final StringBuilder diagnosticLog = new StringBuilder();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        MobileIconLibrary.init(this);
        deviceId = getOrCreateDeviceId();

        // 还原组件展开状态
        String expandedJson = prefs.getString("expandedComponentIds", "[]");
        try {
            JSONArray arr = new JSONArray(expandedJson);
            expandedComponentIds.clear();
            for (int i = 0; i < arr.length(); i++) {
                expandedComponentIds.add(arr.getString(i));
            }
        } catch (Exception ignored) {}

        // 还原组件自定义排序
        String sortedJson = prefs.getString("sortedComponentIds", "[]");
        try {
            JSONArray arr = new JSONArray(sortedJson);
            sortedComponentIds.clear();
            for (int i = 0; i < arr.length(); i++) {
                sortedComponentIds.add(arr.getString(i));
            }
        } catch (Exception ignored) {}

        buildUi(extractSharedText(getIntent()));
        handleExternalAction(getIntent());
        startFloatingWheelIfPermitted();
    }

    @Override
    protected void onResume() {
        super.onResume();
        refreshDiagnosticLogFromStore();
        diagnosticRefreshHandler.removeCallbacks(diagnosticRefreshRunnable);
        diagnosticRefreshHandler.postDelayed(diagnosticRefreshRunnable, 1000);
    }

    @Override
    protected void onPause() {
        diagnosticRefreshHandler.removeCallbacks(diagnosticRefreshRunnable);
        super.onPause();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        String text = extractSharedText(intent);
        if (text != null && !text.trim().isEmpty()) {
            textInput.setText(text);
            setStatus("已接收系统分享内容，确认后可发送到电脑。");
        }
        handleExternalAction(intent);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQUEST_PICK_PHOTO && resultCode == RESULT_OK && data != null) {
            Uri uri = data.getData();
            if (uri != null) {
                sendPhotoToDesktop(uri);
            }
        }
    }

    private void handleExternalAction(Intent intent) {
        if (intent == null || intent.getAction() == null) {
            return;
        }

        String action = intent.getAction();
        if (action.endsWith(".extensions")) {
            selectTab("desktop");
            setStatus("已从悬浮轮盘进入远程扩展。点击扩展图标会让电脑端执行。");
            refreshExtensions(true);
            scrollToView(extensionList);
        } else if (action.endsWith(".pick-photo")) {
            selectTab("profile");
            setStatus("选择照片后将发送到同账号电脑端。");
            pickPhotoFromGallery();
        } else if (action.endsWith(".create-mobile-extension")) {
            selectTab("mobile");
            openMobileExtensionEditor("添加手机扩展：可粘贴 AI 生成的 mobile-js JSON，保存后运行。");
        } else if (action.endsWith(".run-mobile-extension")) {
            selectTab("mobile");
            openMobileExtensionEditor("运行手机扩展：确认 JSON 后点击“运行手机脚本”。");
        } else if (action.endsWith(".compose-text")) {
            selectTab("profile");
            focusTextComposer("从悬浮轮盘进入文本发送。输入内容后点击“发送到电脑”。");
        } else if (action.endsWith(".yanm")) {
            selectTab("yanm");
            setStatus("已从悬浮轮盘进入手机燕幕。");
            refreshYanm(true);
            scrollToView(yanmList);
        } else if (action.endsWith(".refresh")) {
            setStatus("正在刷新移动端数据...");
            refreshExtensions();
            refreshYanm();
        }
    }

    private void buildUi(String sharedText) {
        LinearLayout shell = new LinearLayout(this);
        shell.setOrientation(LinearLayout.VERTICAL);
        shell.setBackgroundColor(Color.rgb(22, 22, 22));

        ScrollView scrollView = new ScrollView(this);
        mainScrollView = scrollView;
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(24), dp(20), dp(24));
        scrollView.addView(root);

        swipeRefresh = new SwipeRefreshLayout(this);
        swipeRefresh.addView(scrollView);
        swipeRefresh.setColorSchemeColors(Color.rgb(59, 130, 246));
        swipeRefresh.setProgressBackgroundColorSchemeColor(Color.rgb(30, 30, 30));
        swipeRefresh.setOnRefreshListener(() -> {
            if (yanmTabPage != null && yanmTabPage.getVisibility() == View.VISIBLE) {
                refreshYanm();
            } else if (desktopExtensionTabPage != null && desktopExtensionTabPage.getVisibility() == View.VISIBLE) {
                refreshExtensions();
            } else {
                swipeRefresh.setRefreshing(false);
            }
        });

        yanmTabPage = createTabPage();
        mobileExtensionTabPage = createTabPage();
        desktopExtensionTabPage = createTabPage();
        profileTabPage = createTabPage();

        root.addView(yanmTabPage);
        root.addView(mobileExtensionTabPage);
        root.addView(desktopExtensionTabPage);
        root.addView(profileTabPage);

        TextView yanmTitle = textView("燕幕", 28, Color.WHITE, true);
        yanmTabPage.addView(yanmTitle);
        yanmTabPage.addView(textView("查看和操作电脑端同步的燕幕组件。", 14, Color.rgb(182, 194, 214), false));
        Button refreshYanmButton = button("刷新");
        yanmTabPage.addView(refreshYanmButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        yanmList = new GridLayout(this);
        yanmList.setColumnCount(2);
        yanmList.setAlignmentMode(GridLayout.ALIGN_BOUNDS);
        yanmList.setUseDefaultMargins(false);
        yanmTabPage.addView(yanmList, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        mobileExtensionTabPage.addView(textView("手机扩展", 28, Color.WHITE, true));
        mobileExtensionTabPage.addView(textView("管理和测试只在手机端运行的 mobile-js 扩展。", 14, Color.rgb(182, 194, 214), false));
        buildMobileExtensionEditor(mobileExtensionTabPage);

        desktopExtensionTabPage.addView(textView("电脑扩展", 28, Color.WHITE, true));
        desktopExtensionTabPage.addView(textView("从手机触发同账号电脑端已同步的扩展。", 14, Color.rgb(182, 194, 214), false));
        Button refreshExtensionsButton = button("刷新");
        desktopExtensionTabPage.addView(refreshExtensionsButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        extensionList = new LinearLayout(this);
        extensionList.setOrientation(LinearLayout.VERTICAL);
        desktopExtensionTabPage.addView(extensionList);
        renderCachedExtensions();

        profileTabPage.addView(textView("我的", 28, Color.WHITE, true));
        profileTabPage.addView(textView("登录、发送消息、悬浮轮盘和诊断信息。", 14, Color.rgb(182, 194, 214), false));

        baseUrlInput = input("云端地址", prefs.getString("baseUrl", DEFAULT_BASE_URL));
        baseUrlInput.setVisibility(View.GONE); // 隐藏后端请求的地址，不展示出来
        emailInput = input("邮箱", prefs.getString("email", ""));
        passwordInput = input("密码", prefs.getString("password", ""));
        passwordInput.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        String initialText = sharedText == null || sharedText.trim().isEmpty() ? "hi" : sharedText;
        textInput = multiInput("发送给电脑的文本 / 链接", initialText);
        statusText = textView("", 14, Color.rgb(147, 197, 253), false);
        statusText.setTextIsSelectable(true);
        statusText.setMinLines(3);

        profileTabPage.addView(baseUrlInput);
        profileTabPage.addView(emailInput);
        profileTabPage.addView(passwordInput);

        LinearLayout buttons = new LinearLayout(this);
        buttons.setOrientation(LinearLayout.HORIZONTAL);
        buttons.setGravity(Gravity.CENTER_VERTICAL);
        loginButton = button("登录"); // 赋值给类成员变量，文案精简为“登录”
        Button logoutButton = button("退出");
        buttons.addView(loginButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        buttons.addView(logoutButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        profileTabPage.addView(buttons);

        profileTabPage.addView(textInput);
        Button sendButton = button("发送");
        profileTabPage.addView(sendButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(48)));

        profileTabPage.addView(sectionTitle("全局轮盘"));
        LinearLayout wheelButtons = new LinearLayout(this);
        wheelButtons.setOrientation(LinearLayout.VERTICAL);
        Button overlayButton = button("悬浮轮盘");
        Button accessibilityButton = button("无障碍服务");
        wheelButtons.addView(overlayButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        wheelButtons.addView(accessibilityButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(44)));
        profileTabPage.addView(wheelButtons);

        profileTabPage.addView(statusText);

        LinearLayout logButtons = new LinearLayout(this);
        logButtons.setOrientation(LinearLayout.HORIZONTAL);
        Button copyLogButton = button("复制");
        Button clearLogButton = button("清空");
        logButtons.addView(copyLogButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        logButtons.addView(clearLogButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        profileTabPage.addView(logButtons);
        profileTabPage.addView(textView("设备 ID：" + deviceId, 11, Color.rgb(100, 116, 139), false));

        loginButton.setOnClickListener(v -> loginAndRegister());
        logoutButton.setOnClickListener(v -> {
            prefs.edit().putString("token", "").apply();
            setStatus("已清除本地登录态。");
            if (loginButton != null) {
                loginButton.setEnabled(true);
            }
        });
        sendButton.setOnClickListener(v -> sendToDesktop());
        overlayButton.setOnClickListener(v -> startFloatingWheel());
        accessibilityButton.setOnClickListener(v -> openAccessibilitySettings());
        refreshExtensionsButton.setOnClickListener(v -> refreshExtensions());
        refreshYanmButton.setOnClickListener(v -> refreshYanm());
        copyLogButton.setOnClickListener(v -> copyDiagnostics());
        clearLogButton.setOnClickListener(v -> {
            diagnosticLog.setLength(0);
            MobileDiagnostics.clear(this);
            statusText.setText("");
            setStatus("日志已清空。");
        });

        shell.addView(swipeRefresh, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1));
        shell.addView(buildBottomTabs(), new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(64)));
        setContentView(shell);
        selectTab("yanm");
        setStatus(prefs.getString("token", "").trim().isEmpty() ? "请先登录燕子账号。" : "已加载本地登录态。");
        renderCachedYanm();
        if (!prefs.getString("token", "").trim().isEmpty()) {
            if (loginButton != null) {
                loginButton.setEnabled(false); // 自动登录状态下置灰登录按钮，防止手动重复触发
            }
            refreshExtensions(true);
            refreshYanm(true);
        }
    }

    private LinearLayout createTabPage() {
        LinearLayout page = new LinearLayout(this);
        page.setOrientation(LinearLayout.VERTICAL);
        page.setVisibility(View.GONE);
        return page;
    }

    private LinearLayout buildBottomTabs() {
        LinearLayout tabs = new LinearLayout(this);
        tabs.setOrientation(LinearLayout.HORIZONTAL);
        tabs.setGravity(Gravity.CENTER_VERTICAL);
        tabs.setPadding(dp(4), dp(2), dp(4), dp(2));
        tabs.setBackgroundColor(Color.rgb(17, 17, 17));

        yanmTabButton = tabButton("燕幕", android.R.drawable.ic_menu_view, "yanm");
        mobileExtensionTabButton = tabButton("手机扩展", android.R.drawable.ic_menu_edit, "mobile");
        desktopExtensionTabButton = tabButton("电脑扩展", android.R.drawable.ic_menu_share, "desktop");
        profileTabButton = tabButton("我的", android.R.drawable.ic_menu_preferences, "profile");

        tabs.addView(yanmTabButton, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, 1));
        tabs.addView(mobileExtensionTabButton, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, 1));
        tabs.addView(desktopExtensionTabButton, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, 1));
        tabs.addView(profileTabButton, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.MATCH_PARENT, 1));
        return tabs;
    }

    private View tabButton(String text, int iconResId, String key) {
        LinearLayout container = new LinearLayout(this);
        container.setOrientation(LinearLayout.VERTICAL);
        container.setGravity(Gravity.CENTER);
        container.setPadding(0, dp(6), 0, dp(6));
        container.setClickable(true);
        container.setFocusable(true);

        ImageView iconView = new ImageView(this);
        iconView.setImageResource(iconResId);
        LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(dp(22), dp(22));
        iconView.setLayoutParams(iconParams);

        TextView textView = new TextView(this);
        textView.setText(text);
        textView.setTextSize(10);
        textView.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams textParams = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WRAP_CONTENT,
            LinearLayout.LayoutParams.WRAP_CONTENT);
        textParams.setMargins(0, dp(3), 0, 0);
        textView.setLayoutParams(textParams);

        container.addView(iconView);
        container.addView(textView);

        container.setTag(new View[]{iconView, textView});
        container.setOnClickListener(v -> selectTab(key));
        return container;
    }

    private void selectTab(String key) {
        if (yanmTabPage == null || mobileExtensionTabPage == null || desktopExtensionTabPage == null || profileTabPage == null) {
            return;
        }

        boolean isYanm = "yanm".equals(key);
        boolean isMobile = "mobile".equals(key);
        boolean isDesktop = "desktop".equals(key);
        boolean isProfile = "profile".equals(key);

        yanmTabPage.setVisibility(isYanm ? View.VISIBLE : View.GONE);
        mobileExtensionTabPage.setVisibility(isMobile ? View.VISIBLE : View.GONE);
        desktopExtensionTabPage.setVisibility(isDesktop ? View.VISIBLE : View.GONE);
        profileTabPage.setVisibility(isProfile ? View.VISIBLE : View.GONE);

        styleTabButton(yanmTabButton, isYanm);
        styleTabButton(mobileExtensionTabButton, isMobile);
        styleTabButton(desktopExtensionTabButton, isDesktop);
        styleTabButton(profileTabButton, isProfile);

        if (mainScrollView != null) {
            mainScrollView.post(() -> mainScrollView.smoothScrollTo(0, 0));
        }
    }

    private void styleTabButton(View tabView, boolean selected) {
        if (tabView == null) {
            return;
        }

        int color = selected ? Color.rgb(34, 211, 238) : Color.rgb(100, 116, 139);
        View[] tag = (View[]) tabView.getTag();
        if (tag != null && tag.length == 2) {
            ImageView iconView = (ImageView) tag[0];
            TextView textView = (TextView) tag[1];
            iconView.setColorFilter(color);
            textView.setTextColor(color);
        }

        GradientDrawable background = new GradientDrawable();
        background.setCornerRadius(dp(12));
        background.setColor(selected ? Color.argb(20, 34, 211, 238) : Color.TRANSPARENT);
        tabView.setBackground(background);
    }

    private void focusTextComposer(String status) {
        setStatus(status);
        textInput.requestFocus();
        scrollToView(textInput);
        showKeyboard(textInput);
    }

    private void buildMobileExtensionEditor(LinearLayout root) {
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        mobileExtensionSectionTitle = sectionTitle("手机扩展编辑器");
        header.addView(mobileExtensionSectionTitle, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        Button promptButton = button("复制提示词");
        header.addView(promptButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, dp(40)));
        root.addView(header);

        HorizontalScrollView editorScroll = new HorizontalScrollView(this);
        editorScroll.setHorizontalScrollBarEnabled(false);
        LinearLayout editorRow = new LinearLayout(this);
        editorRow.setOrientation(LinearLayout.HORIZONTAL);
        editorRow.setPadding(0, 0, dp(8), 0);

        LinearLayout helperPanel = card();
        helperPanel.setLayoutParams(new LinearLayout.LayoutParams(dp(280), LinearLayout.LayoutParams.WRAP_CONTENT));
        helperPanel.addView(textView("手动调整", 16, Color.WHITE, true));
        helperPanel.addView(textView("优先做本机可执行扩展，再补充发到电脑。模板点击后会覆盖右侧 JSON 区。", 12, Color.rgb(182, 194, 214), false));
        mobileExtensionIdInput = input("扩展 ID", "mobile-copy-shared-text");
        mobileExtensionNameInput = input("扩展名称", "复制当前输入");
        mobileExtensionIconInput = input("图标", "mdi:content-copy");
        mobileExtensionDescriptionInput = multiInput("描述", "把当前输入框内容复制到手机剪贴板。");
        mobileExtensionDescriptionInput.setMinLines(3);
        helperPanel.addView(mobileExtensionIdInput);
        helperPanel.addView(mobileExtensionNameInput);
        helperPanel.addView(mobileExtensionIconInput);
        helperPanel.addView(mobileExtensionDescriptionInput);

        LinearLayout helperActions = new LinearLayout(this);
        helperActions.setOrientation(LinearLayout.HORIZONTAL);
        Button applyMetaButton = button("应用左侧字段");
        Button saveDraftButton = button("保存扩展");
        helperActions.addView(applyMetaButton, new LinearLayout.LayoutParams(0, dp(42), 1));
        helperActions.addView(saveDraftButton, new LinearLayout.LayoutParams(0, dp(42), 1));
        helperPanel.addView(helperActions);

        helperPanel.addView(textView("模板示例", 15, Color.WHITE, true));
        helperPanel.addView(textView("本机能力优先：剪贴板、浏览器、文件、网络请求。", 12, Color.rgb(103, 232, 249), false));
        for (MobileExtensionTemplate template : buildMobileExtensionTemplates()) {
            Button templateButton = button(template.name);
            templateButton.setAllCaps(false);
            templateButton.setOnClickListener(v -> replaceDraftWithTemplate(template));
            helperPanel.addView(templateButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(42)));
            helperPanel.addView(textView(template.description, 11, Color.rgb(148, 163, 184), false));
        }

        LinearLayout codePanel = card();
        LinearLayout.LayoutParams codeParams = new LinearLayout.LayoutParams(dp(460), LinearLayout.LayoutParams.WRAP_CONTENT);
        codeParams.setMargins(dp(12), dp(8), 0, dp(8));
        codePanel.setLayoutParams(codeParams);
        codePanel.addView(textView("JSON 区", 16, Color.WHITE, true));
        mobileExtensionInput = multiInput("手机扩展 JSON / mobile-js", prefs.getString("mobileExtensionDraft", defaultMobileExtensionJson()));
        mobileExtensionInput.setMinLines(18);
        codePanel.addView(mobileExtensionInput, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        Button pasteJsonButton = button("一键粘贴 JSON");
        codePanel.addView(pasteJsonButton, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(42)));

        LinearLayout bottomActions = new LinearLayout(this);
        bottomActions.setOrientation(LinearLayout.HORIZONTAL);
        Button testButton = button("测试扩展");
        Button runButton = button("保存扩展");
        bottomActions.addView(testButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        bottomActions.addView(runButton, new LinearLayout.LayoutParams(0, dp(44), 1));
        codePanel.addView(bottomActions);

        mobileExtensionTestResult = textView("测试结果会显示在这里。", 12, Color.rgb(148, 163, 184), false);
        mobileExtensionTestResult.setTextIsSelectable(true);
        mobileExtensionTestResult.setPadding(dp(10), dp(10), dp(10), dp(10));
        mobileExtensionTestResult.setBackgroundColor(Color.rgb(22, 22, 22));
        codePanel.addView(mobileExtensionTestResult);

        editorRow.addView(helperPanel);
        editorRow.addView(codePanel);
        editorScroll.addView(editorRow);
        root.addView(editorScroll);

        mobileExtensionManagerList = card();
        mobileExtensionManagerList.addView(textView("本机手机扩展", 16, Color.WHITE, true));
        root.addView(mobileExtensionManagerList);

        promptButton.setOnClickListener(v -> copyMobileExtensionPrompt());
        applyMetaButton.setOnClickListener(v -> applyMetadataToDraft());
        saveDraftButton.setOnClickListener(v -> saveMobileExtensionDraft());
        pasteJsonButton.setOnClickListener(v -> pasteJsonIntoMobileExtensionEditor());
        testButton.setOnClickListener(v -> runMobileScript());
        runButton.setOnClickListener(v -> saveMobileExtensionDraft());

        updateMobileExtensionFieldsFromDraft();
        renderLocalMobileExtensions();
    }

    private void openMobileExtensionEditor(String status) {
        setStatus(status);
        updateMobileExtensionFieldsFromDraft();
        mobileExtensionInput.requestFocus();
        scrollToView(mobileExtensionSectionTitle);
        showKeyboard(mobileExtensionInput);
    }

    private void replaceDraftWithTemplate(MobileExtensionTemplate template) {
        mobileExtensionInput.setText(template.json);
        mobileExtensionInput.setSelection(template.json.length());
        mobileExtensionNameInput.setText(template.name);
        mobileExtensionDescriptionInput.setText(template.description);
        validateMobileExtensionJson(true);
        setStatus("模板已覆盖 JSON：" + template.name);
    }

    private void pasteJsonIntoMobileExtensionEditor() {
        try {
            ClipboardManager manager = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
            ClipData clip = manager == null ? null : manager.getPrimaryClip();
            CharSequence value = clip == null || clip.getItemCount() == 0 ? "" : clip.getItemAt(0).coerceToText(this);
            String text = value == null ? "" : value.toString().trim();
            if (text.isEmpty()) {
                throw new IllegalStateException("剪贴板没有 JSON 内容。");
            }
            mobileExtensionInput.setText("");
            JSONObject json = new JSONObject(text);
            String pretty = json.toString(2);
            mobileExtensionInput.setText(pretty);
            mobileExtensionInput.setSelection(pretty.length());
            updateMobileExtensionFieldsFromDraft();
            updateMobileScriptResult("JSON 格式正确：" + firstNonEmpty(json.optString("name"), json.optString("id"), "未命名扩展"), false);
            setStatus("已粘贴并检测 JSON 格式。");
        } catch (Exception ex) {
            mobileExtensionInput.setText("");
            updateMobileScriptResult("JSON 格式错误：" + ex.getMessage(), true);
            setStatus("粘贴 JSON 失败：" + ex.getMessage());
        }
    }

    private boolean validateMobileExtensionJson(boolean updateResult) {
        try {
            JSONObject json = parseDraftObject();
            if (updateResult) {
                updateMobileScriptResult("JSON 格式正确：" + firstNonEmpty(json.optString("name"), json.optString("id"), "未命名扩展"), false);
            }
            return true;
        } catch (Exception ex) {
            if (updateResult) {
                updateMobileScriptResult("JSON 格式错误：" + ex.getMessage(), true);
            }
            return false;
        }
    }

    private void applyMetadataToDraft() {
        try {
            JSONObject json = parseDraftObject();
            json.put("id", mobileExtensionIdInput.getText().toString().trim());
            json.put("name", mobileExtensionNameInput.getText().toString().trim());
            json.put("description", mobileExtensionDescriptionInput.getText().toString().trim());
            json.put("icon", mobileExtensionIconInput.getText().toString().trim());
            String pretty = json.toString(2);
            mobileExtensionInput.setText(pretty);
            setStatus("左侧字段已应用到 JSON。");
        } catch (Exception ex) {
            setStatus("应用左侧字段失败：" + ex.getMessage());
        }
    }

    private JSONObject parseDraftObject() throws Exception {
        String draft = mobileExtensionInput.getText().toString().trim();
        if (draft.isEmpty()) {
            return new JSONObject(defaultMobileExtensionJson());
        }
        if (!draft.startsWith("{")) {
            throw new IllegalStateException("右侧不是 JSON 对象，无法应用字段。");
        }
        return new JSONObject(draft);
    }

    private void updateMobileExtensionFieldsFromDraft() {
        try {
            JSONObject json = parseDraftObject();
            mobileExtensionIdInput.setText(firstNonEmpty(json.optString("id"), "mobile-copy-shared-text"));
            mobileExtensionNameInput.setText(firstNonEmpty(json.optString("name"), "复制当前输入"));
            mobileExtensionDescriptionInput.setText(firstNonEmpty(json.optString("description"), "手机本地扩展"));
            mobileExtensionIconInput.setText(firstNonEmpty(json.optString("icon"), "mdi:content-copy"));
        } catch (Exception ignored) {
        }
    }

    private File resolveMobileScriptFile(String name) throws Exception {
        String value = firstNonEmpty(name, "notes.txt")
            .replace("\\", "_")
            .replace("/", "_")
            .replace("..", "_");
        File dir = getExternalFilesDir(Environment.DIRECTORY_DOCUMENTS);
        if (dir == null) {
            dir = new File(getFilesDir(), "mobile-script-files");
        }
        if (!dir.exists() && !dir.mkdirs()) {
            throw new IllegalStateException("无法创建手机扩展文件目录");
        }
        return new File(dir, value);
    }

    private static String buildJsonErrorResult(String message) {
        try {
            return new JSONObject().put("ok", false).put("error", firstNonEmpty(message, "unknown error")).toString();
        } catch (Exception ignored) {
            return "{\"ok\":false,\"error\":\"unknown error\"}";
        }
    }

    private void scrollToView(View view) {
        if (mainScrollView == null || view == null) {
            return;
        }

        mainScrollView.post(() -> mainScrollView.smoothScrollTo(0, Math.max(0, view.getTop() - dp(16))));
    }

    private void showKeyboard(View view) {
        view.postDelayed(() -> {
            InputMethodManager manager = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
            if (manager != null) {
                manager.showSoftInput(view, InputMethodManager.SHOW_IMPLICIT);
            }
        }, 250);
    }

    private void startFloatingWheel() {
        if (!Settings.canDrawOverlays(this)) {
            Intent intent = new Intent(
                Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                Uri.parse("package:" + getPackageName()));
            startActivity(intent);
            setStatus("请开启“允许显示在其他应用上层”，返回后再次点击开启悬浮轮盘。");
            return;
        }

        Intent intent = new Intent(this, FloatingWheelService.class);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startService(intent);
        } else {
            startService(intent);
        }
        setStatus("悬浮轮盘已启动。点击屏幕上的“燕”按钮打开手机轮盘。");
    }

    private void startFloatingWheelIfPermitted() {
        if (!Settings.canDrawOverlays(this)) {
            return;
        }
        try {
            startService(new Intent(this, FloatingWheelService.class));
        } catch (Exception ex) {
            setStatus("悬浮轮盘自动启动失败：" + ex.getMessage());
        }
    }

    private void openAccessibilitySettings() {
        Intent intent = new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS);
        startActivity(intent);
        setStatus("请在无障碍设置中开启“燕子移动端”，用于截图和后续全局手势能力。");
    }

    private void copyMobileExtensionPrompt() {
        String prompt =
            "你正在为燕子移动端编写手机扩展。只允许输出 JSON，不要解释。\\n" +
            "运行时使用 runtime=\\\"mobile-js\\\"，不要使用 C#、PowerShell、Windows 路径、WPF 或桌面 API。\\n" +
            "优先设计本机可执行能力，再按需补充发到电脑。可用 permissions：clipboard.read、clipboard.write、browser.open、file.read、file.write、http.request、desktop.message、share.text。\\n" +
            "脚本入口使用 async function run(context)，可调用 context.mobile.toast(text)、getSharedText()、getClipboardText()、setClipboardText(text)、openUrl(url)、pickPhoto()、readTextFile(name)、saveTextFile(name,text)、appendTextFile(name,text)、httpGet(url)、httpPostJson(url,jsonText)、sendToDesktop(text)。\\n" +
            "输出字段至少包含 id、name、version、category、description、icon、runtime、permissions、script.source。";
        ClipboardManager manager = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        manager.setPrimaryClip(ClipData.newPlainText("Yanzi mobile extension prompt", prompt));
        setStatus("已复制手机端扩展提示词。");
    }

    private void saveMobileExtensionDraft() {
        try {
            String draft = mobileExtensionInput.getText().toString();
            String id = firstNonEmpty(mobileExtensionIdInput.getText().toString(), "mobile-extension-draft");
            String name = firstNonEmpty(mobileExtensionNameInput.getText().toString(), "手机扩展草稿");
            if (draft.trim().startsWith("{") && draft.trim().endsWith("}")) {
                JSONObject json = new JSONObject(draft);
                id = firstNonEmpty(json.optString("id"), id);
                name = firstNonEmpty(json.optString("name"), json.optString("displayName"), name);
            }
            prefs.edit()
                .putString("mobileExtensionDraft", draft)
                .putString("mobileExtensionDraftId", id)
                .putString("mobileExtensionDraftName", name)
                .apply();
            if (draft.trim().startsWith("{") && draft.trim().endsWith("}")) {
                upsertLocalMobileExtension(new JSONObject(draft));
            }
            updateMobileExtensionFieldsFromDraft();
            renderLocalMobileExtensions();
            setStatus("手机扩展草稿已保存：" + name + "。可继续编辑或测试。");
        } catch (Exception ex) {
            setStatus("手机扩展保存失败：" + ex.getMessage());
        }
    }

    private void runMobileScript() {
        try {
            String draft = mobileExtensionInput.getText().toString();
            prefs.edit().putString("mobileExtensionDraft", draft).apply();
            updateMobileExtensionFieldsFromDraft();
            String source = extractMobileScriptSource(draft);
            if (source.trim().isEmpty()) {
                throw new IllegalStateException("脚本为空。");
            }

            updateMobileScriptResult("正在测试 JSON...", false);
            WebView runner = new WebView(this);
            activeMobileScriptRunner = runner;
            runner.getSettings().setJavaScriptEnabled(true);
            runner.addJavascriptInterface(new MobileJsBridge(), "yanziMobileJsHost");
            String html = "<!doctype html><html><body><script>" +
                "window.context={mobile:{" +
                "toast:function(text){yanziMobileJsHost.toast(String(text||''));}," +
                "sendToDesktop:function(text){yanziMobileJsHost.sendToDesktop(String(text||''));}," +
                "getSharedText:function(){return yanziMobileJsHost.getSharedText();}," +
                "getClipboardText:function(){return Promise.resolve(yanziMobileJsHost.getClipboardText());}," +
                "setClipboardText:function(text){return Promise.resolve(yanziMobileJsHost.setClipboardText(String(text||'')));}," +
                "openUrl:function(url){return Promise.resolve(yanziMobileJsHost.openUrl(String(url||'')));}," +
                "pickPhoto:function(){return Promise.resolve(yanziMobileJsHost.pickPhoto());}," +
                "readTextFile:function(name){return Promise.resolve(JSON.parse(yanziMobileJsHost.readTextFile(String(name||''))));}," +
                "saveTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.saveTextFile(String(name||''),String(text||''))));}," +
                "appendTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.appendTextFile(String(name||''),String(text||''))));}," +
                "httpGet:function(url){return Promise.resolve(JSON.parse(yanziMobileJsHost.httpGet(String(url||''))));}," +
                "httpPostJson:function(url,jsonText){return Promise.resolve(JSON.parse(yanziMobileJsHost.httpPostJson(String(url||''),String(jsonText||''))));}" +
                "}};" +
                "async function __run(){try{" + source + "\n;if(typeof run==='function'){await run(window.context);}yanziMobileJsHost.done('脚本执行完成');}" +
                "catch(e){yanziMobileJsHost.fail(String(e&&e.message?e.message:e));}}" +
                "__run();" +
                "</script></body></html>";
            runner.loadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
            setStatus("手机脚本已启动。");
        } catch (Exception ex) {
            updateMobileScriptResult("测试失败： " + ex.getMessage(), true);
            setStatus("手机脚本启动失败：" + ex.getMessage());
        }
    }

    private static String extractMobileScriptSource(String draft) throws Exception {
        String text = draft == null ? "" : draft.trim();
        if (text.startsWith("{")) {
            JSONObject json = new JSONObject(text);
            JSONObject script = json.optJSONObject("script");
            if (script != null) {
                return script.optString("source", "");
            }
        }
        return text;
    }

    private String defaultMobileExtensionJson() {
        return "{\n" +
            "  \"id\": \"mobile-open-yanzi-site\",\n" +
            "  \"name\": \"打开燕子官网\",\n" +
            "  \"version\": \"0.1.0\",\n" +
            "  \"category\": \"手机浏览\",\n" +
            "  \"description\": \"在手机浏览器打开燕子官网。\",\n" +
            "  \"icon\": \"mdi:web\",\n" +
            "  \"runtime\": \"mobile-js\",\n" +
            "  \"permissions\": [\"browser.open\"],\n" +
            "  \"script\": {\n" +
            "    \"source\": \"async function run(context) {\\n  await context.mobile.openUrl('https://yanzi.luoluoluo.cc');\\n  context.mobile.toast('已打开燕子官网');\\n}\"\n" +
            "  }\n" +
            "}";
    }

    private void updateMobileScriptResult(String text, boolean isError) {
        if (mobileExtensionTestResult == null) {
            return;
        }

        mobileExtensionTestResult.setText(text == null || text.trim().isEmpty() ? "暂无测试结果。" : text);
        mobileExtensionTestResult.setTextColor(isError ? Color.rgb(248, 113, 113) : Color.rgb(125, 211, 252));
    }

    private JSONArray readLocalMobileExtensions() {
        try {
            return new JSONArray(prefs.getString("mobileExtensions", "[]"));
        } catch (Exception ex) {
            return new JSONArray();
        }
    }

    private void upsertLocalMobileExtension(JSONObject json) throws Exception {
        String id = firstNonEmpty(json.optString("id"), "mobile-extension-" + System.currentTimeMillis());
        json.put("id", id);
        JSONArray array = readLocalMobileExtensions();
        JSONArray next = new JSONArray();
        boolean replaced = false;
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item == null) {
                continue;
            }
            if (id.equals(item.optString("id"))) {
                next.put(json);
                replaced = true;
            } else {
                next.put(item);
            }
        }
        if (!replaced) {
            next.put(json);
        }
        prefs.edit().putString("mobileExtensions", next.toString()).apply();
    }

    private void deleteLocalMobileExtension(String id) {
        JSONArray array = readLocalMobileExtensions();
        JSONArray next = new JSONArray();
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item != null && !id.equals(item.optString("id"))) {
                next.put(item);
            }
        }
        prefs.edit().putString("mobileExtensions", next.toString()).apply();
        renderLocalMobileExtensions();
        setStatus("已删除手机扩展：" + id);
    }

    private void renderLocalMobileExtensions() {
        if (mobileExtensionManagerList == null) {
            return;
        }
        mobileExtensionManagerList.removeAllViews();
        mobileExtensionManagerList.addView(textView("本机手机扩展", 16, Color.WHITE, true));
        JSONArray array = readLocalMobileExtensions();
        if (array.length() == 0) {
            mobileExtensionManagerList.addView(textView("暂无本机扩展。可通过空槽或编辑器保存。", 12, Color.rgb(148, 163, 184), false));
            return;
        }
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item == null) {
                continue;
            }
            String id = item.optString("id");
            String name = firstNonEmpty(item.optString("name"), item.optString("displayName"), id);
            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            TextView title = textView(name + "\n" + id, 12, Color.WHITE, false);
            row.addView(title, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
            Button edit = button("编辑");
            Button delete = button("删除");
            row.addView(edit, new LinearLayout.LayoutParams(dp(72), dp(40)));
            row.addView(delete, new LinearLayout.LayoutParams(dp(72), dp(40)));
            mobileExtensionManagerList.addView(row);
            edit.setOnClickListener(v -> {
                String pretty = item.toString();
                try {
                    pretty = item.toString(2);
                } catch (Exception ignored) {
                }
                mobileExtensionInput.setText(pretty);
                updateMobileExtensionFieldsFromDraft();
                scrollToView(mobileExtensionSectionTitle);
                setStatus("正在编辑手机扩展：" + name);
            });
            delete.setOnClickListener(v -> deleteLocalMobileExtension(id));
        }
    }

    private List<MobileExtensionTemplate> buildMobileExtensionTemplates() {
        List<MobileExtensionTemplate> items = new ArrayList<>();
        items.add(new MobileExtensionTemplate(
            "发消息到电脑",
            "对应扇形菜单“发消息”，默认把输入框内容发给同账号电脑。",
            mobileTemplateJson("mobile-send-message-to-desktop", "发消息到电脑", "跨端协同", "把输入框内容发送到电脑。", "mdi:chat",
                new String[] {"desktop.message", "share.text"},
                "async function run(context) {\n  const text = context.mobile.getSharedText() || 'hi';\n  context.mobile.toast('正在发送到电脑');\n  context.mobile.sendToDesktop(text);\n}")));
        items.add(new MobileExtensionTemplate(
            "发照片到电脑",
            "对应扇形菜单“发照片”，点击后选择本机相册照片并发送。",
            mobileTemplateJson("mobile-pick-photo-to-desktop", "发照片到电脑", "跨端协同", "选择本机相册照片并发送到电脑。", "mdi:image",
                new String[] {"photo.read", "desktop.message"},
                "async function run(context) {\n  context.mobile.toast('请选择照片');\n  await context.mobile.pickPhoto();\n}")));
        items.add(new MobileExtensionTemplate(
            "发截图到电脑",
            "对应扇形菜单“发截图”，通过悬浮轮盘截图并发送。",
            mobileTemplateJson("mobile-send-screenshot-to-desktop", "发截图到电脑", "跨端协同", "提示使用悬浮轮盘截图并发送到电脑。", "mdi:camera",
                new String[] {"screen.capture", "desktop.message"},
                "async function run(context) {\n  context.mobile.toast('请从扇形菜单点击发截图');\n}")));
        items.add(new MobileExtensionTemplate(
            "打开燕子官网",
            "对应扇形菜单“官网”，直接打开燕子官网。",
            mobileTemplateJson("mobile-open-yanzi-site", "打开燕子官网", "手机浏览", "在手机浏览器打开燕子官网。", "mdi:web",
                new String[] {"browser.open"},
                "async function run(context) {\n  await context.mobile.openUrl('https://yanzi.luoluoluo.cc.cd');\n  context.mobile.toast('已打开燕子官网');\n}")));
        items.add(new MobileExtensionTemplate(
            "远程扩展入口",
            "对应扇形菜单“远程扩展”，用于从手机进入远程扩展列表。",
            mobileTemplateJson("mobile-open-remote-extensions", "远程扩展入口", "跨端协同", "提示使用扇形菜单进入远程扩展列表。", "mdi:monitor-dashboard",
                new String[] {"desktop.extension"},
                "async function run(context) {\n  context.mobile.toast('请从扇形菜单点击远程扩展');\n}")));
        items.add(new MobileExtensionTemplate(
            "燕幕入口",
            "对应扇形菜单“燕幕”，用于从手机进入燕幕。",
            mobileTemplateJson("mobile-open-yanm", "燕幕入口", "手机燕幕", "提示使用扇形菜单进入燕幕。", "mdi:monitor-dashboard",
                new String[] {"yanm.open"},
                "async function run(context) {\n  context.mobile.toast('请从扇形菜单点击燕幕');\n}")));
        return items;
    }

    private static String mobileTemplateJson(String id, String name, String category, String description, String icon, String[] permissions, String source) {
        try {
            JSONArray permissionArray = new JSONArray();
            for (String permission : permissions) {
                permissionArray.put(permission);
            }
            return new JSONObject()
                .put("id", id)
                .put("name", name)
                .put("version", "0.1.0")
                .put("category", category)
                .put("description", description)
                .put("icon", icon)
                .put("runtime", "mobile-js")
                .put("permissions", permissionArray)
                .put("script", new JSONObject().put("source", source))
                .toString(2);
        } catch (Exception ex) {
            return "{}";
        }
    }

    private void loginAndRegister() {
        if (loginButton != null) {
            loginButton.setEnabled(false);
        }
        setStatus("正在登录...");
        executor.execute(() -> {
            String baseUrl = normalizedBaseUrl();
            String email = emailInput.getText().toString().trim();
            String token;
            try {
                token = YanziApiClient.login(baseUrl, email, passwordInput.getText().toString());
            } catch (Exception ex) {
                runOnUiThread(() -> {
                    setStatus("登录失败：" + ex.getMessage());
                    if (loginButton != null) {
                        loginButton.setEnabled(true);
                    }
                });
                return;
            }

            runOnUiThread(() -> setStatus("登录成功，正在注册手机设备..."));
            try {
                YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                prefs.edit()
                    .putString("baseUrl", baseUrl)
                    .putString("email", email)
                    .putString("password", passwordInput.getText().toString())
                    .putString("token", token)
                    .apply();
                runOnUiThread(() -> {
                    setStatus("登录成功，设备已注册。");
                    if (loginButton != null) {
                        loginButton.setEnabled(true);
                    }
                    refreshExtensions();
                    refreshYanm();
                });
            } catch (Exception ex) {
                prefs.edit()
                    .putString("baseUrl", baseUrl)
                    .putString("email", email)
                    .putString("password", passwordInput.getText().toString())
                    .putString("token", token)
                    .apply();
                runOnUiThread(() -> {
                    setStatus("登录成功，但设备注册失败：" + ex.getMessage());
                    if (loginButton != null) {
                        loginButton.setEnabled(true);
                    }
                });
            }
        });
    }

    private void sendToDesktop() {
        sendTextValueToDesktop(textInput.getText().toString(), "正在发送到电脑...");
    }

    private void sendTextValueToDesktop(String text, String pendingStatus) {
        setStatus(pendingStatus);
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                String messageId;
                try {
                    YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, deviceId, text);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                    messageId = YanziApiClient.sendTextToDesktop(baseUrl, token, deviceId, text);
                }
                String sentMessageId = messageId;
                runOnUiThread(() -> setStatus("已发送到云端，messageId=" + sentMessageId + "。电脑端在线时会在 5 秒内收到。"));
            } catch (Exception ex) {
                runOnUiThread(() -> setStatus("发送失败：" + ex.getMessage()));
            }
        });
    }

    private void pickPhotoFromGallery() {
        try {
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("image/*");
            startActivityForResult(intent, REQUEST_PICK_PHOTO);
        } catch (Exception ex) {
            setStatus("打开相册失败：" + ex.getMessage());
        }
    }

    private void sendPhotoToDesktop(Uri uri) {
        setStatus("正在处理照片...");
        showPhotoProgress("正在发送照片...");
        executor.execute(() -> {
            try {
                byte[] jpegBytes = readJpegBytesFromUri(uri);
                int[] size = readImageSizeFromJpegBytes(jpegBytes);
                int width = size[0];
                int height = size[1];

                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                String messageId;
                try {
                    YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                    messageId = YanziApiClient.sendPhotoToDesktop(baseUrl, token, deviceId, jpegBytes, width, height);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    YanziApiClient.registerDevice(baseUrl, token, deviceId, buildDeviceName());
                    messageId = YanziApiClient.sendPhotoToDesktop(baseUrl, token, deviceId, jpegBytes, width, height);
                }
                String sentMessageId = messageId;
                runOnUiThread(() -> {
                    hidePhotoProgress();
                    setStatus("照片已发送到云端，messageId=" + sentMessageId + "。电脑端在线时会在 5 秒内收到。");
                });
            } catch (Exception ex) {
                runOnUiThread(() -> {
                    hidePhotoProgress();
                    setStatus("照片发送失败：" + ex.getMessage());
                });
            }
        });
    }

    private byte[] readJpegBytesFromUri(Uri uri) throws Exception {
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        try (InputStream stream = getContentResolver().openInputStream(uri)) {
            BitmapFactory.decodeStream(stream, null, bounds);
        }

        int maxEdge = Math.max(bounds.outWidth, bounds.outHeight);
        int sample = 1;
        while (maxEdge / sample > 1600) {
            sample *= 2;
        }

        BitmapFactory.Options decode = new BitmapFactory.Options();
        decode.inSampleSize = Math.max(1, sample);
        Bitmap bitmap;
        try (InputStream stream = getContentResolver().openInputStream(uri)) {
            bitmap = BitmapFactory.decodeStream(stream, null, decode);
        }
        if (bitmap == null) {
            throw new IllegalStateException("无法读取图片内容。");
        }

        try (ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            bitmap.compress(Bitmap.CompressFormat.JPEG, 90, output);
            return output.toByteArray();
        } finally {
            bitmap.recycle();
        }
    }

    private static int[] readImageSizeFromJpegBytes(byte[] jpegBytes) {
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inJustDecodeBounds = true;
        BitmapFactory.decodeByteArray(jpegBytes, 0, jpegBytes.length, options);
        return new int[] { Math.max(0, options.outWidth), Math.max(0, options.outHeight) };
    }

    private void refreshExtensions() {
        refreshExtensions(false);
    }

    private void refreshExtensions(boolean keepExisting) {
        if (!keepExisting || extensionList.getChildCount() == 0) {
            extensionList.removeAllViews();
            extensionList.addView(textView("正在读取账号扩展...", 13, Color.rgb(148, 163, 184), false));
        } else {
            setStatus("正在后台刷新电脑扩展...");
        }
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                List<RemoteExtension> extensions;
                try {
                    extensions = YanziApiClient.fetchRunnableExtensions(baseUrl, token);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    extensions = YanziApiClient.fetchRunnableExtensions(baseUrl, token);
                }
                List<RemoteExtension> loadedExtensions = extensions;
                runOnUiThread(() -> {
                    cacheRemoteExtensions(loadedExtensions);
                    renderExtensions(loadedExtensions);
                    if (swipeRefresh != null) {
                        swipeRefresh.setRefreshing(false);
                    }
                });
            } catch (Exception ex) {
                runOnUiThread(() -> {
                    if (!keepExisting || extensionList.getChildCount() == 0) {
                        extensionList.removeAllViews();
                        extensionList.addView(textView("扩展列表读取失败。", 13, Color.rgb(248, 113, 113), false));
                    }
                    setStatus("扩展列表读取失败：" + ex.getMessage());
                    if (swipeRefresh != null) {
                        swipeRefresh.setRefreshing(false);
                    }
                });
            }
        });
    }

    private void renderCachedExtensions() {
        List<RemoteExtension> cached = readCachedExtensions();
        if (cached.isEmpty()) {
            extensionList.removeAllViews();
            extensionList.addView(textView("暂无电脑扩展缓存。进入后会后台拉取，也可点击“刷新扩展列表”。", 13, Color.rgb(148, 163, 184), false));
            return;
        }

        renderExtensions(cached);
        extensionList.addView(textView("当前显示缓存，后台会自动刷新。", 11, Color.rgb(103, 232, 249), false));
    }

    private void cacheRemoteExtensions(List<RemoteExtension> extensions) {
        try {
            JSONArray array = new JSONArray();
            for (RemoteExtension extension : extensions) {
                array.put(new JSONObject()
                    .put("extensionId", extension.extensionId)
                    .put("name", extension.name)
                    .put("description", extension.description)
                    .put("icon", extension.icon)
                    .put("accentHex", extension.accentHex));
            }
            prefs.edit().putString(CACHE_REMOTE_EXTENSIONS, array.toString()).apply();
        } catch (Exception ignored) {
        }
    }

    private List<RemoteExtension> readCachedExtensions() {
        List<RemoteExtension> items = new ArrayList<>();
        try {
            JSONArray array = new JSONArray(prefs.getString(CACHE_REMOTE_EXTENSIONS, "[]"));
            for (int i = 0; i < array.length(); i++) {
                JSONObject item = array.optJSONObject(i);
                if (item == null) {
                    continue;
                }
                String extensionId = firstNonEmpty(
                    item.optString("extensionId"),
                    item.optString("extension_id"),
                    item.optString("ExtensionId"),
                    item.optString("Extension_id"));
                if (extensionId.isEmpty()) {
                    continue;
                }
                String accentHex = firstNonEmpty(
                    item.optString("accentHex"),
                    item.optString("accent_hex"),
                    item.optString("AccentHex"));
                items.add(new RemoteExtension(
                    extensionId,
                    firstNonEmpty(item.optString("name"), item.optString("Name"), extensionId),
                    firstNonEmpty(item.optString("description"), item.optString("Description")),
                    firstNonEmpty(item.optString("icon"), item.optString("Icon")),
                    accentHex));
            }
        } catch (Exception ignored) {
        }
        return items;
    }

    private void renderExtensions(List<RemoteExtension> extensions) {
        extensionList.removeAllViews();
        if (extensions.isEmpty()) {
            extensionList.addView(textView("暂无可远程执行扩展。请先在电脑端发布/同步扩展。", 13, Color.rgb(148, 163, 184), false));
            return;
        }

        GridLayout grid = new GridLayout(this);
        grid.setColumnCount(4);
        extensionList.addView(grid);

        int screenWidth = getResources().getDisplayMetrics().widthPixels;
        int cellWidth = Math.max(dp(72), (screenWidth - dp(56)) / 4);
        for (RemoteExtension extension : extensions) {
            LinearLayout card = iconCard();
            card.setGravity(Gravity.CENTER);
            card.setOnClickListener(v -> runRemoteExtension(extension, card));
            GridLayout.LayoutParams cardParams = new GridLayout.LayoutParams();
            cardParams.width = cellWidth;
            cardParams.height = GridLayout.LayoutParams.WRAP_CONTENT;
            cardParams.setMargins(dp(3), dp(6), dp(3), dp(6));
            card.setLayoutParams(cardParams);

            View iconView;
            android.graphics.Path path = MobileIconLibrary.resolveOrDefault(extension.icon);
            ImageView img = new ImageView(this);
            android.graphics.drawable.GradientDrawable gd = new android.graphics.drawable.GradientDrawable();
            int baseColor = Color.rgb(45, 45, 45); // 与电脑端一致的暗灰色 #2D2D2D
            if (extension.accentHex != null && !extension.accentHex.trim().isEmpty()) {
                try {
                    String colorStr = extension.accentHex.trim();
                    if (!colorStr.startsWith("#")) {
                        colorStr = "#" + colorStr;
                    }
                    baseColor = Color.parseColor(colorStr);
                } catch (Exception ignored) {
                }
            }
            gd.setColor(baseColor);
            gd.setCornerRadius(dp(10)); // 圆角半径 10dp
            img.setBackground(gd);
            img.setImageDrawable(new PathDrawable(path, Color.WHITE));
            img.setPadding(dp(8), dp(8), dp(8), dp(8));
            iconView = img;
            LinearLayout.LayoutParams iconParams = new LinearLayout.LayoutParams(dp(54), dp(54));
            iconParams.setMargins(0, 0, 0, dp(6));
            iconParams.gravity = Gravity.CENTER_HORIZONTAL;
            card.addView(iconView, iconParams);

            TextView name = textView(extension.name, 11, Color.WHITE, false);
            name.setGravity(Gravity.CENTER);
            name.setMaxLines(2);
            LinearLayout.LayoutParams nameParams = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
            nameParams.gravity = Gravity.CENTER_HORIZONTAL;
            card.addView(name, nameParams);
            grid.addView(card);
        }
    }

    private void runRemoteExtension(RemoteExtension extension, final View cardView) {
        cardView.setEnabled(false);

        final android.view.ViewGroup cardGroup = (android.view.ViewGroup) cardView;
        final View originalIcon = cardGroup.getChildAt(0);

        final android.widget.ProgressBar progressBar = new android.widget.ProgressBar(this, null, android.R.attr.progressBarStyleSmall);
        progressBar.setLayoutParams(originalIcon.getLayoutParams());
        progressBar.setPadding(dp(12), dp(12), dp(12), dp(12));

        originalIcon.setVisibility(View.GONE);
        cardGroup.addView(progressBar, 0);

        setStatus("正在发送扩展执行请求：" + extension.name);

        final Runnable restoreUi = () -> {
            runOnUiThread(() -> {
                cardGroup.removeView(progressBar);
                originalIcon.setVisibility(View.VISIBLE);
                cardView.setEnabled(true);
            });
        };

        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                String messageId;
                try {
                    messageId = YanziApiClient.runExtensionOnDesktop(
                        baseUrl,
                        token,
                        deviceId,
                        buildDeviceName(),
                        extension.extensionId,
                        textInput.getText().toString());
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    messageId = YanziApiClient.runExtensionOnDesktop(
                        baseUrl,
                        token,
                        deviceId,
                        buildDeviceName(),
                        extension.extensionId,
                        textInput.getText().toString());
                }

                final String sentMessageId = messageId;
                runOnUiThread(() -> setStatus("扩展请求已发送，开始轮询执行状态..."));

                boolean finished = false;
                long startTime = System.currentTimeMillis();
                long timeout = 20000;
                String statusResult = "timeout";
                String execOutput = "";

                while (System.currentTimeMillis() - startTime < timeout) {
                    Thread.sleep(1000);
                    try {
                        JSONObject msgDetail = YanziApiClient.fetchMessageDetail(baseUrl, token, sentMessageId);
                        String status = msgDetail.optString("status", "pending");
                        if ("completed".equals(status)) {
                            statusResult = "completed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null) {
                                JSONObject execRes = payloadObj.optJSONObject("executionResult");
                                if (execRes != null) {
                                    execOutput = execRes.optString("output", "");
                                }
                            }
                            finished = true;
                            break;
                        } else if ("failed".equals(status)) {
                            statusResult = "failed";
                            JSONObject payloadObj = msgDetail.optJSONObject("payload");
                            if (payloadObj != null) {
                                JSONObject execRes = payloadObj.optJSONObject("executionResult");
                                if (execRes != null) {
                                    execOutput = execRes.optString("output", "");
                                }
                            }
                            finished = true;
                            break;
                        } else if ("acked".equals(status)) {
                            statusResult = "acked";
                            finished = true;
                            break;
                        }
                    } catch (Exception pollEx) {
                    }
                }

                restoreUi.run();

                final String finalStatus = statusResult;
                final String finalOutput = execOutput;
                runOnUiThread(() -> {
                    if ("completed".equals(finalStatus)) {
                        new android.app.AlertDialog.Builder(MainActivity.this)
                            .setTitle("执行成功")
                            .setMessage("扩展 [" + extension.name + "] 执行成功！\n\n返回结果：\n" + finalOutput)
                            .setPositiveButton("确定", null)
                            .show();
                        setStatus("扩展执行成功：" + extension.name);
                    } else if ("failed".equals(finalStatus)) {
                        new android.app.AlertDialog.Builder(MainActivity.this)
                            .setTitle("执行失败")
                            .setMessage("扩展 [" + extension.name + "] 执行失败！\n\n错误信息：\n" + finalOutput)
                            .setPositiveButton("确定", null)
                            .show();
                        setStatus("扩展执行失败：" + extension.name);
                    } else if ("acked".equals(finalStatus)) {
                        new android.app.AlertDialog.Builder(MainActivity.this)
                            .setTitle("执行完成")
                            .setMessage("扩展 [" + extension.name + "] 已执行完成（未返回结果数据）。")
                            .setPositiveButton("确定", null)
                            .show();
                        setStatus("扩展执行完成：" + extension.name);
                    } else {
                        new android.app.AlertDialog.Builder(MainActivity.this)
                            .setTitle("执行超时")
                            .setMessage("扩展 [" + extension.name + "] 执行超时，请确认电脑端是否已离线。")
                            .setPositiveButton("确定", null)
                            .show();
                        setStatus("扩展执行超时：" + extension.name);
                    }
                });

            } catch (Exception ex) {
                restoreUi.run();
                runOnUiThread(() -> {
                    new android.app.AlertDialog.Builder(MainActivity.this)
                        .setTitle("发送请求失败")
                        .setMessage(ex.getMessage())
                        .setPositiveButton("确定", null)
                        .show();
                    setStatus("扩展执行发送失败：" + ex.getMessage());
                });
            }
        });
    }

    private void refreshYanm() {
        refreshYanm(false);
    }

    private void refreshYanm(boolean keepExisting) {
        if (!keepExisting || yanmList.getChildCount() == 0) {
            yanmList.removeAllViews();
            yanmList.addView(textView("正在读取燕幕...", 13, Color.rgb(148, 163, 184), false));
        } else {
            setStatus("正在后台刷新燕幕...");
        }
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                JSONObject yanm;
                try {
                    yanm = YanziApiClient.fetchYanmState(baseUrl, token);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    yanm = YanziApiClient.fetchYanmState(baseUrl, token);
                }
                JSONObject loadedYanm = yanm;
                runOnUiThread(() -> {
                    prefs.edit().putString(CACHE_YANM, loadedYanm.toString()).apply();
                    renderYanm(loadedYanm);
                    if (swipeRefresh != null) {
                        swipeRefresh.setRefreshing(false);
                    }
                });
            } catch (Exception ex) {
                runOnUiThread(() -> {
                    if (!keepExisting || yanmList.getChildCount() == 0) {
                        yanmList.removeAllViews();
                        yanmList.addView(textView("燕幕读取失败。", 13, Color.rgb(248, 113, 113), false));
                    }
                    setStatus("燕幕读取失败：" + ex.getMessage());
                    if (swipeRefresh != null) {
                        swipeRefresh.setRefreshing(false);
                    }
                });
            }
        });
    }

    private void renderCachedYanm() {
        String cached = prefs.getString(CACHE_YANM, "");
        if (cached == null || cached.trim().isEmpty()) {
            yanmList.removeAllViews();
            yanmList.addView(textView("暂无燕幕缓存。进入后会自动后台拉取，也可点击“刷新”。", 13, Color.rgb(148, 163, 184), false));
            return;
        }

        try {
            renderYanm(new JSONObject(cached));
            yanmList.addView(textView("当前显示缓存，后台会自动刷新。", 11, Color.rgb(103, 232, 249), false));
        } catch (Exception ex) {
            yanmList.removeAllViews();
            yanmList.addView(textView("燕幕缓存不可用，正在等待刷新。", 13, Color.rgb(148, 163, 184), false));
        }
    }

    private void saveSortedState() {
        try {
            JSONArray arr = new JSONArray();
            for (String id : sortedComponentIds) {
                arr.put(id);
            }
            prefs.edit().putString("sortedComponentIds", arr.toString()).apply();
        } catch (Exception ignored) {
        }
    }

    private void saveExpandedState() {
        try {
            JSONArray arr = new JSONArray();
            for (String id : expandedComponentIds) {
                arr.put(id);
            }
            prefs.edit().putString("expandedComponentIds", arr.toString()).apply();
        } catch (Exception ignored) {
        }
    }

    private void showSortDialog(String componentId, int currentIndex, List<JSONObject> components) {
        if (components.size() <= 1) {
            return;
        }

        String[] options = new String[]{"置顶", "上移", "下移", "置底"};
        new android.app.AlertDialog.Builder(this)
            .setTitle("调整组件顺序")
            .setItems(options, (dialog, which) -> {
                List<String> list = new ArrayList<>();
                for (JSONObject comp : components) {
                    String id = firstNonEmpty(comp.optString("id"), comp.optString("Id"),
                        comp.optString("title"), comp.optString("Title"), comp.optString("name"), comp.optString("Name"));
                    list.add(id);
                }

                String target = list.remove(currentIndex);
                if (which == 0) { // 置顶
                    list.add(0, target);
                } else if (which == 1) { // 上移
                    int newIndex = Math.max(0, currentIndex - 1);
                    list.add(newIndex, target);
                } else if (which == 2) { // 下移
                    int newIndex = Math.min(list.size(), currentIndex + 1);
                    list.add(newIndex, target);
                } else if (which == 3) { // 置底
                    list.add(target);
                }

                sortedComponentIds.clear();
                sortedComponentIds.addAll(list);
                saveSortedState();

                if (currentYanmSnapshot != null) {
                    renderYanm(currentYanmSnapshot);
                }
            })
            .show();
    }

    private void renderYanm(JSONObject yanm) {
        currentYanmSnapshot = yanm;
        currentYanmState = firstObject(yanm, "componentState", "ComponentState");
        if (currentYanmState == null) {
            currentYanmState = new JSONObject();
            try {
                currentYanmSnapshot.put("componentState", currentYanmState);
            } catch (Exception ignored) {
            }
        }

        // 清空列表前销毁所有的 WebView 防止内存泄露
        for (WebView webView : activeYanmWebViews.values()) {
            if (webView != null) {
                try {
                    webView.destroy();
                } catch (Exception ignored) {}
            }
        }
        activeYanmWebViews.clear();
        yanmList.removeAllViews();

        JSONArray components = firstArray(yanm, "components", "Components");
        if (components == null || components.length() == 0) {
            yanmList.addView(textView("暂无燕幕组件。", 13, Color.rgb(148, 163, 184), false));
            return;
        }

        // 运用用户自定义排序列表进行排序
        List<JSONObject> sortedList = new ArrayList<>();
        List<JSONObject> remainingList = new ArrayList<>();
        for (int i = 0; i < components.length(); i++) {
            JSONObject comp = components.optJSONObject(i);
            if (comp != null) {
                String compId = firstNonEmpty(comp.optString("id"), comp.optString("Id"),
                    comp.optString("title"), comp.optString("Title"), comp.optString("name"), comp.optString("Name"), "comp_" + i);
                int sortedIndex = sortedComponentIds.indexOf(compId);
                if (sortedIndex >= 0) {
                    sortedList.add(comp);
                } else {
                    remainingList.add(comp);
                }
            }
        }

        // 对已自定义排序的组件按照其相对顺序排序
        sortedList.sort((c1, c2) -> {
            String id1 = firstNonEmpty(c1.optString("id"), c1.optString("Id"),
                c1.optString("title"), c1.optString("Title"), c1.optString("name"), c1.optString("Name"));
            String id2 = firstNonEmpty(c2.optString("id"), c2.optString("Id"),
                c2.optString("title"), c2.optString("Title"), c2.optString("name"), c2.optString("Name"));
            return Integer.compare(sortedComponentIds.indexOf(id1), sortedComponentIds.indexOf(id2));
        });

        List<JSONObject> finalComponents = new ArrayList<>(sortedList);
        finalComponents.addAll(remainingList);

        for (int i = 0; i < finalComponents.size(); i++) {
            JSONObject component = finalComponents.get(i);
            String title = firstNonEmpty(
                component.optString("title"),
                component.optString("Title"),
                component.optString("name"),
                component.optString("Name"),
                "组件 " + (i + 1));
            String type = firstNonEmpty(
                component.optString("type"),
                component.optString("Type"),
                component.optString("kind"),
                component.optString("Kind"),
                "component");
            String componentId = firstNonEmpty(component.optString("id"), component.optString("Id"), title);

            LinearLayout card = card();
            // 改回单列布局，宽度填充父级
            LinearLayout.LayoutParams cardParams = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            );
            cardParams.setMargins(0, dp(8), 0, dp(8));
            card.setLayoutParams(cardParams);

            // 卡片头部布局（水平）：左侧放标题，右侧放下拉折叠箭头
            LinearLayout headerLayout = new LinearLayout(this);
            headerLayout.setOrientation(LinearLayout.HORIZONTAL);
            headerLayout.setGravity(Gravity.CENTER_VERTICAL);

            TextView titleView = textView(title, 16, Color.WHITE, true);
            headerLayout.addView(titleView, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));

            String html = firstNonEmpty(
                component.optString("html"),
                component.optString("Html"),
                component.optString("markup"),
                component.optString("Markup"),
                component.optString("contentHtml"),
                component.optString("ContentHtml"));

            TextView arrowView = null;
            if (!html.isEmpty()) {
                boolean isExpanded = expandedComponentIds.contains(componentId);
                arrowView = textView(isExpanded ? "▲" : "▼", 14, Color.rgb(34, 211, 238), false);
                arrowView.setPadding(dp(8), dp(4), dp(8), dp(4));
                headerLayout.addView(arrowView, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT));
            }
            card.addView(headerLayout);

            // 移除非 "component" 类型的多余中间显示
            if (!type.isEmpty() && !type.equalsIgnoreCase("component")) {
                card.addView(textView(type, 11, Color.rgb(94, 234, 212), false));
            }

            if (!html.isEmpty()) {
                LinearLayout previewHost = new LinearLayout(this);
                previewHost.setOrientation(LinearLayout.VERTICAL);
                card.addView(previewHost);
                String htmlForPreview = html;
                final TextView finalArrow = arrowView;

                // 还原展开状态
                if (expandedComponentIds.contains(componentId)) {
                    toggleYanmPreview(previewHost, htmlForPreview, componentId, title, finalArrow, true);
                }

                card.setOnClickListener(v -> toggleYanmPreview(previewHost, htmlForPreview, componentId, title, finalArrow, false));
            } else {
                String summary = summarizeYanmComponent(component);
                card.addView(textView(summary, 12, Color.rgb(182, 194, 214), false));
            }

            // 长按进行位置重排
            final int index = i;
            final List<JSONObject> finalCompsRef = finalComponents;
            card.setOnLongClickListener(v -> {
                showSortDialog(componentId, index, finalCompsRef);
                return true;
            });

            yanmList.addView(card);
        }

        setStatus("燕幕已加载：" + finalComponents.size() + " 个组件。");
    }

    private void toggleYanmPreview(LinearLayout previewHost, String html, String componentId, String componentTitle, TextView arrowView, boolean forceExpand) {
        WebView existingWebView = activeYanmWebViews.get(componentId);
        boolean isCurrentlyExpanded = (existingWebView != null && previewHost.getChildCount() > 0);

        if (isCurrentlyExpanded && !forceExpand) {
            // 折叠逻辑
            previewHost.removeAllViews();
            try {
                existingWebView.destroy();
            } catch (Exception ignored) {}
            activeYanmWebViews.remove(componentId);
            expandedComponentIds.remove(componentId);
            saveExpandedState();
            if (arrowView != null) {
                arrowView.setText("▼");
            }
            return;
        }

        if (existingWebView != null) {
            previewHost.removeAllViews();
            try {
                existingWebView.destroy();
            } catch (Exception ignored) {}
            activeYanmWebViews.remove(componentId);
        }

        WebView webView = new WebView(this);
        activeYanmWebViews.put(componentId, webView);
        expandedComponentIds.add(componentId);
        saveExpandedState();
        if (arrowView != null) {
            arrowView.setText("▲");
        }

        webView.setBackgroundColor(Color.TRANSPARENT);
        webView.setVerticalScrollBarEnabled(false);
        webView.setHorizontalScrollBarEnabled(false);
        webView.getSettings().setJavaScriptEnabled(true);
        webView.getSettings().setDomStorageEnabled(true);
        webView.getSettings().setLoadWithOverviewMode(false);
        webView.getSettings().setUseWideViewPort(false);
        webView.getSettings().setTextZoom(145);
        webView.setInitialScale(145);
        webView.addJavascriptInterface(new YanmMobileBridge(componentId, componentTitle), "yanmMobileHost");
        webView.loadDataWithBaseURL(null, wrapYanmHtml(html, componentId, componentTitle), "text/html", "UTF-8", null);
        previewHost.addView(webView, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(420)));
    }

    private String requireToken() {
        String token = prefs.getString("token", "");
        if (token == null || token.trim().isEmpty()) {
            return refreshToken();
        }

        return token;
    }

    private String refreshToken() {
        try {
            String baseUrl = normalizedBaseUrl();
            String email = prefs.getString("email", "");
            String password = prefs.getString("password", "");
            if (email == null || email.trim().isEmpty() || password == null || password.isEmpty()) {
                throw new IllegalStateException("请先登录。");
            }

            String token = YanziApiClient.login(baseUrl, email.trim(), password);
            prefs.edit().putString("baseUrl", baseUrl).putString("token", token).apply();
            return token;
        } catch (Exception ex) {
            throw new IllegalStateException("登录态已失效，请重新登录：" + ex.getMessage());
        }
    }

    private static boolean isUnauthorized(Exception ex) {
        String message = ex.getMessage();
        return message != null && (
            message.contains("401") ||
            message.toLowerCase(Locale.ROOT).contains("token expired") ||
            message.toLowerCase(Locale.ROOT).contains("unauthorized"));
    }

    private String normalizedBaseUrl() {
        String value = baseUrlInput.getText().toString().trim();
        if (value.trim().isEmpty()) {
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
        String existing = prefs.getString("deviceId", null);
        if (existing != null && !existing.trim().isEmpty()) {
            return existing;
        }

        String created = "android-" + UUID.randomUUID();
        prefs.edit().putString("deviceId", created).apply();
        return created;
    }

    private String buildDeviceName() {
        return buildDeviceDisplayName();
    }

    private static String buildDeviceDisplayName() {
        String marketName = firstNonEmpty(
            getSystemProperty("ro.product.marketname"),
            getSystemProperty("ro.vendor.product.marketname"),
            getSystemProperty("ro.product.vendor.marketname"),
            getSystemProperty("ro.product.odm.marketname"),
            getSystemProperty("ro.config.marketing_name"));
        if (!marketName.isEmpty()) {
            return marketName;
        }

        String maker = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.trim();
        String model = Build.MODEL == null ? "" : Build.MODEL.trim();
        String name = (maker + " " + model).trim();
        return name.trim().isEmpty() ? "Android 手机" : name;
    }

    private static String getSystemProperty(String key) {
        try {
            Class<?> systemProperties = Class.forName("android.os.SystemProperties");
            java.lang.reflect.Method get = systemProperties.getMethod("get", String.class);
            Object value = get.invoke(null, key);
            return value == null ? "" : value.toString().trim();
        } catch (Exception ignored) {
            return "";
        }
    }

    private void setStatus(String status) {
        diagnosticLog.setLength(0);
        diagnosticLog.append(MobileDiagnostics.append(this, status));
        statusText.setText(diagnosticLog.toString());
    }

    private void refreshDiagnosticLogFromStore() {
        if (statusText == null) {
            return;
        }

        String stored = MobileDiagnostics.get(this);
        if (!stored.equals(diagnosticLog.toString())) {
            diagnosticLog.setLength(0);
            diagnosticLog.append(stored);
            statusText.setText(stored);
        }
    }

    private void copyDiagnostics() {
        refreshDiagnosticLogFromStore();
        String value = diagnosticLog.length() == 0 ? statusText.getText().toString() : diagnosticLog.toString();
        ClipboardManager manager = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        manager.setPrimaryClip(ClipData.newPlainText("Yanzi mobile diagnostics", value));
        Toast.makeText(this, "已复制日志", Toast.LENGTH_SHORT).show();
    }

    private void trimDiagnosticLog() {
        int maxLength = 6000;
        if (diagnosticLog.length() <= maxLength) {
            return;
        }

        diagnosticLog.delete(0, diagnosticLog.length() - maxLength);
    }

    private void scheduleYanmCloudSync(String reason) {
        if (pendingYanmSync != null) {
            yanmSyncHandler.removeCallbacks(pendingYanmSync);
        }

        pendingYanmSync = () -> syncYanmStateToCloud(reason);
        yanmSyncHandler.postDelayed(pendingYanmSync, 1000);
        setStatus("燕幕状态待同步到云端：" + reason);
    }

    private void syncYanmStateToCloud(String reason) {
        JSONObject snapshot = currentYanmSnapshot;
        if (snapshot == null) {
            setStatus("燕幕同步跳过：没有完整快照。");
            return;
        }

        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken();
                try {
                    YanziApiClient.putYanmState(baseUrl, token, snapshot);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    YanziApiClient.putYanmState(baseUrl, token, snapshot);
                }
                runOnUiThread(() -> setStatus("燕幕状态已同步到云端：" + reason));
            } catch (Exception ex) {
                runOnUiThread(() -> setStatus("燕幕状态同步失败：" + ex.getMessage()));
            }
        });
    }

    private TextView textView(String text, int sp, int color, boolean bold) {
        TextView view = new TextView(this);
        view.setText(text);
        view.setTextColor(color);
        view.setTextSize(sp);
        view.setPadding(0, dp(6), 0, dp(6));
        if (bold) {
            view.setTypeface(view.getTypeface(), android.graphics.Typeface.BOLD);
        }
        return view;
    }

    private TextView sectionTitle(String text) {
        TextView view = textView(text, 18, Color.WHITE, true);
        view.setPadding(0, dp(18), 0, dp(8));
        return view;
    }

    private LinearLayout card() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(14), dp(12), dp(14), dp(12));
        card.setBackgroundColor(Color.rgb(30, 30, 30));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            LinearLayout.LayoutParams.WRAP_CONTENT);
        params.setMargins(0, dp(8), 0, dp(8));
        card.setLayoutParams(params);
        return card;
    }

    private LinearLayout iconCard() {
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setGravity(Gravity.CENTER);
        card.setPadding(dp(6), dp(8), dp(6), dp(8));
        return card;
    }

    private EditText input(String hint, String value) {
        EditText input = new EditText(this);
        input.setHint(hint);
        input.setText(value == null ? "" : value);
        input.setSingleLine(true);
        input.setTextColor(Color.WHITE);
        input.setHintTextColor(Color.rgb(148, 163, 184));
        input.setPadding(dp(12), dp(10), dp(12), dp(10));
        return input;
    }

    private EditText multiInput(String hint, String value) {
        EditText input = input(hint, value);
        input.setSingleLine(false);
        input.setMinLines(5);
        input.setGravity(Gravity.TOP);
        return input;
    }

    private Button button(String text) {
        Button button = new Button(this);
        button.setText(text);
        return button;
    }

    private void showPhotoProgress(String text) {
        hidePhotoProgress();
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.HORIZONTAL);
        panel.setGravity(Gravity.CENTER_VERTICAL);
        panel.setPadding(dp(14), dp(10), dp(14), dp(10));
        GradientDrawable background = new GradientDrawable();
        background.setColor(Color.argb(238, 6, 17, 31));
        background.setCornerRadius(dp(16));
        background.setStroke(dp(1), Color.argb(160, 34, 211, 238));
        panel.setBackground(background);

        TextView spinner = textView("...", 18, Color.rgb(34, 211, 238), true);
        TextView label = textView(text, 14, Color.WHITE, false);
        label.setPadding(dp(10), 0, 0, 0);
        panel.addView(spinner, new LinearLayout.LayoutParams(dp(34), dp(34)));
        panel.addView(label, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT));

        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(dp(230), dp(56));
        params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        params.topMargin = dp(72);
        photoProgressView = panel;
        addContentView(photoProgressView, params);
    }

    private void hidePhotoProgress() {
        if (photoProgressView != null && photoProgressView.getParent() instanceof android.view.ViewGroup) {
            ((android.view.ViewGroup) photoProgressView.getParent()).removeView(photoProgressView);
        }
        photoProgressView = null;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private static String extractSharedText(Intent intent) {
        if (intent == null || !Intent.ACTION_SEND.equals(intent.getAction()) || !"text/plain".equals(intent.getType())) {
            return null;
        }
        return intent.getStringExtra(Intent.EXTRA_TEXT);
    }

    private static String firstNonEmpty(String... values) {
        for (String value : values) {
            if (value != null && !value.trim().isEmpty()) {
                return value.trim();
            }
        }
        return "";
    }

    private static JSONArray firstArray(JSONObject object, String... keys) {
        for (String key : keys) {
            JSONArray value = object.optJSONArray(key);
            if (value != null) {
                return value;
            }
        }
        return null;
    }

    private static JSONObject firstObject(JSONObject object, String... keys) {
        for (String key : keys) {
            JSONObject value = object.optJSONObject(key);
            if (value != null) {
                return value;
            }
        }
        return null;
    }

    private static String summarizeYanmComponent(JSONObject component) {
        String text = firstNonEmpty(
            component.optString("text"),
            component.optString("Text"),
            component.optString("content"),
            component.optString("Content"),
            component.optString("note"),
            component.optString("Note"),
            component.optString("description"),
            component.optString("Description"));
        if (text.isEmpty()) {
            text = component.toString();
        }
        text = text.replaceAll("\\s+", " ").trim();
        return text.length() > 140 ? text.substring(0, 140) + "..." : text;
    }

    private static String wrapYanmHtml(String html, String componentId, String componentTitle) {
        String trimmed = html == null ? "" : html.trim();
        String mobileHead = "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,maximum-scale=1,user-scalable=no\" />" +
            "<style id=\"yanm-mobile-adapter\">" +
            "html,body{margin:0!important;padding:0!important;background:#07111f!important;color:#fff;min-width:0!important;overflow:auto!important;}" +
            "body{font-size:18px!important;line-height:1.45!important;-webkit-text-size-adjust:145%;text-size-adjust:145%;}" +
            "*{box-sizing:border-box;max-width:100%!important;}" +
            "button,input,textarea,select{font-size:16px!important;}" +
            "img,svg,canvas,video{max-width:100%!important;height:auto;}" +
            "</style>";
        String bridge = "<script>(function(){var componentId=" + JSONObject.quote(componentId) + ";var componentTitle=" + JSONObject.quote(componentTitle) + ";" +
            "window.yanm=window.yanm||{};window.yanm.componentId=componentId;window.yanm.componentTitle=componentTitle;" +
            "window.yanmHost=window.yanmHost||{};" +
            "function emit(d){try{window.dispatchEvent(new CustomEvent('yanm:message',{detail:d||{}}));}catch(e){}}" +
            "window.yanmHost.getState=function(key){key=String(key||'');var value=String(yanmMobileHost.getState(key)||'');var res={key:key,value:value};emit({type:'host.state',key:key,value:value});return res;};" +
            "window.yanmHost.setState=function(key,value){key=String(key||'');value=String(value||'');yanmMobileHost.setState(key,value);emit({type:'host.state',key:key,value:value});return {key:key,value:value};};" +
            "window.yanmHost.requestSystemInfo=function(){var data=JSON.parse(yanmMobileHost.getSystemInfo());data.type='host.systemInfo';emit(data);return data;};" +
            "window.yanm.invoke=function(method,args){args=args||{};if(method==='state.get')return Promise.resolve(window.yanmHost.getState(args.key));if(method==='state.set')return Promise.resolve(window.yanmHost.setState(args.key,args.value));if(method==='system.info')return Promise.resolve(window.yanmHost.requestSystemInfo());return Promise.reject(new Error('unsupported mobile method '+method));};" +
            "window.dispatchEvent(new CustomEvent('yanm:message',{detail:{type:'host.ready',componentId:componentId}}));})();</script>";
        if (trimmed.toLowerCase(Locale.ROOT).contains("<html")) {
            String lower = trimmed.toLowerCase(Locale.ROOT);
            int headEnd = lower.indexOf("</head>");
            String withHead = headEnd >= 0
                ? trimmed.substring(0, headEnd) + mobileHead + trimmed.substring(headEnd)
                : trimmed.replaceFirst("(?i)<html[^>]*>", "$0<head>" + mobileHead + "</head>");
            String lowerWithHead = withHead.toLowerCase(Locale.ROOT);
            int bodyEnd = lowerWithHead.lastIndexOf("</body>");
            return bodyEnd >= 0 ? withHead.substring(0, bodyEnd) + bridge + withHead.substring(bodyEnd) : withHead + bridge;
        }

        return "<!doctype html><html><head>" + mobileHead +
            "</head><body>" + trimmed + bridge + "</body></html>";
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
            JSONObject state = currentYanmState == null ? new JSONObject() : currentYanmState;
            return state.optString(key, "");
        }

        @JavascriptInterface
        public void setState(String key, String value) {
            try {
                if (currentYanmState == null) {
                    currentYanmState = new JSONObject();
                }
                currentYanmState.put(key, value);
                if (currentYanmSnapshot == null) {
                    currentYanmSnapshot = new JSONObject();
                }
                currentYanmSnapshot.put("componentState", currentYanmState);
                runOnUiThread(() -> {
                    setStatus("燕幕状态已在手机端更新：" + componentTitle + " / " + key);
                    scheduleYanmCloudSync(componentTitle + " / " + key);
                });
            } catch (Exception ignored) {
            }
        }

        @JavascriptInterface
        public String getSystemInfo() {
            try {
                return new JSONObject()
                    .put("machineName", buildDeviceDisplayName())
                    .put("osVersion", "Android " + Build.VERSION.RELEASE)
                    .put("isNetworkAvailable", true)
                    .put("time", new SimpleDateFormat("HH:mm", Locale.getDefault()).format(new Date()))
                    .put("componentId", componentId)
                    .toString();
            } catch (Exception ex) {
                return "{}";
            }
        }
    }

    private final class MobileJsBridge {
        @JavascriptInterface
        public void toast(String text) {
            runOnUiThread(() -> Toast.makeText(MainActivity.this, text, Toast.LENGTH_SHORT).show());
        }

        @JavascriptInterface
        public void sendToDesktop(String text) {
            runOnUiThread(() -> sendTextValueToDesktop(text, "手机脚本正在发送到电脑..."));
        }

        @JavascriptInterface
        public String getSharedText() {
            return textInput == null ? "" : textInput.getText().toString();
        }

        @JavascriptInterface
        public String getClipboardText() {
            ClipboardManager manager = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            if (manager == null || manager.getPrimaryClip() == null || manager.getPrimaryClip().getItemCount() == 0) {
                return "";
            }
            CharSequence value = manager.getPrimaryClip().getItemAt(0).coerceToText(MainActivity.this);
            return value == null ? "" : value.toString();
        }

        @JavascriptInterface
        public String setClipboardText(String text) {
            ClipboardManager manager = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
            if (manager != null) {
                manager.setPrimaryClip(ClipData.newPlainText("Yanzi mobile script", text == null ? "" : text));
            }
            return text == null ? "" : text;
        }

        @JavascriptInterface
        public String openUrl(String url) {
            runOnUiThread(() -> {
                Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
                startActivity(intent);
            });
            return url;
        }

        @JavascriptInterface
        public String pickPhoto() {
            runOnUiThread(MainActivity.this::pickPhotoFromGallery);
            return "ok";
        }

        @JavascriptInterface
        public String readTextFile(String name) {
            try {
                File file = resolveMobileScriptFile(name);
                if (!file.exists()) {
                    return new JSONObject().put("ok", false).put("error", "文件不存在").put("path", file.getAbsolutePath()).toString();
                }
                try (FileInputStream stream = new FileInputStream(file);
                     ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = stream.read(buffer)) >= 0) {
                        output.write(buffer, 0, read);
                    }
                    return new JSONObject()
                        .put("ok", true)
                        .put("path", file.getAbsolutePath())
                        .put("text", output.toString(StandardCharsets.UTF_8.name()))
                        .toString();
                }
            } catch (Exception ex) {
                return buildJsonErrorResult(ex.getMessage());
            }
        }

        @JavascriptInterface
        public String saveTextFile(String name, String text) {
            return writeTextFile(name, text, false);
        }

        @JavascriptInterface
        public String appendTextFile(String name, String text) {
            return writeTextFile(name, text, true);
        }

        @JavascriptInterface
        public String httpGet(String url) {
            return runHttpRequest("GET", url, null, null);
        }

        @JavascriptInterface
        public String httpPostJson(String url, String jsonText) {
            return runHttpRequest("POST", url, jsonText, "application/json; charset=utf-8");
        }

        @JavascriptInterface
        public void done(String text) {
            runOnUiThread(() -> {
                updateMobileScriptResult(text, false);
                setStatus(text);
            });
        }

        @JavascriptInterface
        public void fail(String text) {
            runOnUiThread(() -> {
                updateMobileScriptResult("测试失败： " + text, true);
                setStatus("手机脚本执行失败：" + text);
            });
        }

        private String writeTextFile(String name, String text, boolean append) {
            try {
                File file = resolveMobileScriptFile(name);
                try (FileOutputStream stream = new FileOutputStream(file, append)) {
                    stream.write((text == null ? "" : text).getBytes(StandardCharsets.UTF_8));
                }
                return new JSONObject()
                    .put("ok", true)
                    .put("path", file.getAbsolutePath())
                    .put("bytes", file.length())
                    .toString();
            } catch (Exception ex) {
                return buildJsonErrorResult(ex.getMessage());
            }
        }

        private String runHttpRequest(String method, String url, String body, String contentType) {
            HttpURLConnection connection = null;
            try {
                connection = (HttpURLConnection) new URL(url).openConnection();
                connection.setRequestMethod(method);
                connection.setConnectTimeout(15000);
                connection.setReadTimeout(15000);
                connection.setRequestProperty("Accept", "application/json, text/plain, */*");
                connection.setRequestProperty("User-Agent", "YanziMobile/1.0");
                if (body != null) {
                    connection.setDoOutput(true);
                    connection.setRequestProperty("Content-Type", contentType == null ? "text/plain; charset=utf-8" : contentType);
                    try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8)) {
                        writer.write(body);
                    }
                }

                int status = connection.getResponseCode();
                String responseBody = readConnectionBody(connection);
                return new JSONObject()
                    .put("ok", status >= 200 && status < 300)
                    .put("status", status)
                    .put("body", responseBody)
                    .toString();
            } catch (Exception ex) {
                return buildJsonErrorResult(ex.getMessage());
            } finally {
                if (connection != null) {
                    connection.disconnect();
                }
            }
        }

        private String readConnectionBody(HttpURLConnection connection) throws Exception {
            InputStream stream = connection.getResponseCode() >= 200 && connection.getResponseCode() < 300
                ? connection.getInputStream()
                : connection.getErrorStream();
            if (stream == null) {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8))) {
                String line;
                while ((line = reader.readLine()) != null) {
                    builder.append(line);
                }
            }
            return builder.toString();
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
            String value = icon.trim();
            if (value.startsWith("mdi:")) {
                String namePart = value.substring(4).replace("-", " ").trim();
                return namePart.isEmpty() ? "燕" : namePart.substring(0, 1).toUpperCase(Locale.ROOT);
            }

            String base = name.trim().isEmpty() ? extensionId : name.trim();
            return base.isEmpty() ? "燕" : base.substring(0, 1).toUpperCase(Locale.ROOT);
        }
    }

    private static final class YanziApiClient {
        static String login(String baseUrl, String email, String password) throws Exception {
            JSONObject payload = new JSONObject()
                .put("email", email)
                .put("password", password);
            return postJson(baseUrl, "/v1/auth/login", payload, null, "登录").getString("accessToken");
        }

        static void registerDevice(String baseUrl, String token, String deviceId, String displayName) throws Exception {
            JSONObject capabilities = new JSONObject()
                .put("shareText", true)
                .put("sendToDesktop", true);
            JSONObject payload = new JSONObject()
                .put("deviceId", deviceId)
                .put("platform", "android")
                .put("displayName", displayName)
                .put("capabilities", capabilities);
            postJson(baseUrl, "/v1/me/devices", payload, token, "设备注册");
        }

        static String sendTextToDesktop(String baseUrl, String token, String sourceDeviceId, String text) throws Exception {
            JSONObject payload = new JSONObject()
                .put("sourceDeviceId", sourceDeviceId)
                .put("targetPlatform", "desktop")
                .put("kind", "text")
                .put("title", "手机发来消息")
                .put("text", text)
                .put("payload", new JSONObject()
                    .put("source", "android")
                    .put("sourceDeviceName", buildDeviceDisplayName())
                    .put("createdAt", System.currentTimeMillis()));
            return postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "发送消息").optString("messageId", "unknown");
        }

        static String sendPhotoToDesktop(String baseUrl, String token, String sourceDeviceId, byte[] jpegBytes, int width, int height) throws Exception {
            WebDavConfig webDav = fetchWebDavConfig(baseUrl, token);
            String remotePath = uploadMobilePhotoToWebDav(webDav, jpegBytes);
            return postScreenshotWebDavMessage(baseUrl, token, sourceDeviceId, remotePath, jpegBytes.length, width, height);
        }

        private static String postScreenshotWebDavMessage(String baseUrl, String token, String sourceDeviceId, String webDavPath, int bytes, int width, int height) throws Exception {
            JSONObject payload = new JSONObject()
                .put("sourceDeviceId", sourceDeviceId)
                .put("targetPlatform", "desktop")
                .put("kind", "screenshot")
                .put("title", "手机照片")
                .put("text", "手机照片：" + width + "x" + height)
                .put("payload", new JSONObject()
                    .put("source", "android-mobile")
                    .put("sourceDeviceName", buildDeviceDisplayName())
                    .put("screenshotMime", "image/jpeg")
                    .put("screenshotWidth", width)
                    .put("screenshotHeight", height)
                    .put("screenshotBytes", bytes)
                    .put("webDavPath", webDavPath)
                    .put("expiresAt", System.currentTimeMillis() + 30L * 24L * 60L * 60L * 1000L)
                    .put("createdAt", System.currentTimeMillis()));
            return postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "发送照片").optString("messageId", "unknown");
        }

        private static WebDavConfig fetchWebDavConfig(String baseUrl, String token) throws Exception {
            JSONObject json = getJson(baseUrl, "/v1/sync/webdav-config", token, "读取 WebDAV");
            WebDavConfig config = new WebDavConfig();
            config.serverUrl = json.optString("serverUrl", "https://dav.jianguoyun.com/dav/");
            config.rootPath = json.optString("rootPath", "/yanzi");
            config.username = json.optString("username", "");
            config.password = json.optString("password", "");
            if (!json.optBoolean("enabled", false) || config.username.trim().isEmpty() || config.password.trim().isEmpty()) {
                throw new IllegalStateException("账号未配置可用的坚果云 WebDAV。");
            }
            return config;
        }

        private static String uploadMobilePhotoToWebDav(WebDavConfig config, byte[] bytes) throws Exception {
            String day = new SimpleDateFormat("yyyyMMdd", Locale.ROOT).format(new Date());
            String fileName = "mobile-photo-" + day + "-" + UUID.randomUUID().toString().replace("-", "") + ".jpg";
            putWebDavBytes(config, fileName, bytes, "image/jpeg");
            return fileName;
        }

        private static void putWebDavBytes(WebDavConfig config, String relativePath, byte[] bytes, String contentType) throws Exception {
            HttpURLConnection connection = openWebDav(config, relativePath);
            connection.setRequestMethod("PUT");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(30000);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", contentType);
            connection.setFixedLengthStreamingMode(bytes.length);
            connection.connect();
            try (java.io.OutputStream output = connection.getOutputStream()) {
                output.write(bytes);
            }
            String body = readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                throw new IllegalStateException("WebDAV 上传失败，HTTP " + connection.getResponseCode() + "：" + body);
            }
        }

        private static HttpURLConnection openWebDav(WebDavConfig config, String relativePath) throws Exception {
            String server = config.serverUrl == null ? "" : config.serverUrl.trim();
            if (!server.endsWith("/")) {
                server = server + "/";
            }
            String root = config.rootPath == null ? "" : config.rootPath.trim();
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
            HttpURLConnection connection = (HttpURLConnection) url.openConnection();
            connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.1.0");
            String userpass = (config.username == null ? "" : config.username) + ":" + (config.password == null ? "" : config.password);
            String encoded = android.util.Base64.encodeToString(userpass.getBytes(StandardCharsets.UTF_8), android.util.Base64.NO_WRAP);
            connection.setRequestProperty("Authorization", "Basic " + encoded);
            return connection;
        }

        private static final class WebDavConfig {
            String serverUrl;
            String rootPath;
            String username;
            String password;
        }

        static String runExtensionOnDesktop(String baseUrl, String token, String sourceDeviceId, String sourceDeviceName, String extensionId, String inputText) throws Exception {
            JSONObject payload = new JSONObject()
                .put("sourceDeviceId", sourceDeviceId)
                .put("targetPlatform", "desktop")
                .put("kind", "run-extension")
                .put("title", "手机请求执行扩展")
                .put("text", inputText == null ? "" : inputText)
                .put("payload", new JSONObject()
                    .put("source", "android")
                    .put("sourceDeviceName", sourceDeviceName)
                    .put("extensionId", extensionId)
                    .put("createdAt", System.currentTimeMillis()));
            return postJson(baseUrl, "/v1/me/mobile/messages", payload, token, "执行扩展").optString("messageId", "unknown");
        }

        static JSONObject fetchMessageDetail(String baseUrl, String token, String messageId) throws Exception {
            return getJson(baseUrl, "/v1/me/mobile/messages/" + encodePath(messageId), token, "获取消息详情");
        }

        static List<RemoteExtension> fetchRunnableExtensions(String baseUrl, String token) throws Exception {
            JSONObject payload = getJson(baseUrl, "/v1/me/extensions", token, "读取扩展列表");
            JSONArray items = payload.optJSONArray("items");
            List<RemoteExtension> result = new ArrayList<>();
            if (items == null) {
                return result;
            }

            for (int i = 0; i < items.length(); i++) {
                JSONObject item = items.optJSONObject(i);
                if (item == null || item.optInt("enabled", 1) == 0) {
                    continue;
                }

                String extensionId = firstNonEmpty(
                    item.optString("extension_id"),
                    item.optString("extensionId"),
                    item.optString("ExtensionId"),
                    item.optString("Extension_id")
                );
                if (extensionId.isEmpty()) {
                    continue;
                }
                if ("yanzi-webdav-settings".equals(extensionId) || 
                    "yanzi-webdav-setting".equals(extensionId) || 
                    "yanzi-quickpanel-settings".equals(extensionId) || 
                    "yanzi-quickpanel-setting".equals(extensionId) ||
                    "yanzi-personal-sync-settings".equals(extensionId) ||
                    "yanzi-personal-sync-setting".equals(extensionId) ||
                    "yanzi-ai-settings".equals(extensionId) ||
                    "yanzi-ai-setting".equals(extensionId) ||
                    "yanzi-general-settings".equals(extensionId) ||
                    "yanzi-general-setting".equals(extensionId)) {
                    continue;
                }

                try {
                    JSONObject detail = getJson(baseUrl, "/v1/extensions/" + encodePath(extensionId), token, "读取扩展详情");
                    JSONObject manifest = detail.optJSONObject("manifest");
                    String name = firstNonEmpty(
                        detail.optString("display_name"),
                        detail.optString("displayName"),
                        detail.optString("DisplayName"),
                        detail.optString("name"),
                        detail.optString("Name"),
                        manifest == null ? "" : manifest.optString("name"),
                        manifest == null ? "" : manifest.optString("Name"),
                        manifest == null ? "" : manifest.optString("display_name"),
                        manifest == null ? "" : manifest.optString("displayName"),
                        manifest == null ? "" : manifest.optString("DisplayName"),
                        extensionId
                    );
                    String description = firstNonEmpty(
                        detail.optString("description"),
                        detail.optString("Description"),
                        manifest == null ? "" : manifest.optString("description"),
                        manifest == null ? "" : manifest.optString("Description")
                    );
                    String icon = firstNonEmpty(
                        detail.optString("icon"),
                        detail.optString("Icon"),
                        manifest == null ? "" : manifest.optString("icon"),
                        manifest == null ? "" : manifest.optString("Icon")
                    );
                    String accentHex = firstNonEmpty(
                        detail.optString("accent_hex"),
                        detail.optString("accentHex"),
                        detail.optString("AccentHex"),
                        manifest == null ? "" : manifest.optString("accent_hex"),
                        manifest == null ? "" : manifest.optString("accentHex"),
                        manifest == null ? "" : manifest.optString("AccentHex")
                    );
                    result.add(new RemoteExtension(extensionId, name, description, icon, accentHex));
                } catch (Exception ignored) {
                    result.add(new RemoteExtension(extensionId, extensionId, "扩展详情暂不可用，仍可尝试远程执行。", "", ""));
                }
            }
            return result;
        }

        static JSONObject fetchYanmState(String baseUrl, String token) throws Exception {
            JSONObject payload = getJson(baseUrl, "/v1/me/yanm-state", token, "读取燕幕");
            JSONObject yanm = payload.optJSONObject("yanm");
            if (yanm == null) {
                throw new IllegalStateException("账号云端没有燕幕数据。");
            }
            return yanm;
        }

        static JSONObject putYanmState(String baseUrl, String token, JSONObject yanm) throws Exception {
            JSONObject payload = new JSONObject()
                .put("updatedAtUtc", new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT).format(new Date()))
                .put("yanm", yanm);
            return putJson(baseUrl, "/v1/me/yanm-state", payload, token, "同步燕幕");
        }

        private static JSONObject putJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            HttpURLConnection connection = (HttpURLConnection) new URL(baseUrl + path).openConnection();
            connection.setRequestMethod("PUT");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(15000);
            connection.setDoOutput(true);
            connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.1.0");
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setRequestProperty("Accept", "application/json");
            if (token != null && !token.trim().isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8)) {
                writer.write(payload.toString());
            }

            String body = readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                String message = body;
                try {
                    message = new JSONObject(body).optString("message", body);
                } catch (Exception ignored) {
                }
                throw new IllegalStateException(formatError(action, path, connection.getResponseCode(), message));
            }

            return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
        }

        private static JSONObject postJson(String baseUrl, String path, JSONObject payload, String token, String action) throws Exception {
            HttpURLConnection connection = (HttpURLConnection) new URL(baseUrl + path).openConnection();
            connection.setRequestMethod("POST");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(15000);
            connection.setDoOutput(true);
            connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.1.0");
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setRequestProperty("Accept", "application/json");
            if (token != null && !token.trim().isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8)) {
                writer.write(payload.toString());
            }

            String body = readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                String message = body;
                try {
                    message = new JSONObject(body).optString("message", body);
                } catch (Exception ignored) {
                }
                throw new IllegalStateException(formatError(action, path, connection.getResponseCode(), message));
            }

            return new JSONObject(body);
        }

        private static JSONObject getJson(String baseUrl, String path, String token, String action) throws Exception {
            HttpURLConnection connection = (HttpURLConnection) new URL(baseUrl + path).openConnection();
            connection.setRequestMethod("GET");
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(15000);
            connection.setRequestProperty("User-Agent", "YanziClient-Mobile/0.1.0");
            connection.setRequestProperty("Accept", "application/json");
            if (token != null && !token.trim().isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            String body = readBody(connection);
            if (connection.getResponseCode() < 200 || connection.getResponseCode() >= 300) {
                String message = body;
                try {
                    message = new JSONObject(body).optString("message", body);
                } catch (Exception ignored) {
                }
                throw new IllegalStateException(formatError(action, path, connection.getResponseCode(), message));
            }

            return new JSONObject(body);
        }

        private static String encodePath(String value) {
            return value.replace(" ", "%20").replace("/", "%2F");
        }

        private static String formatError(String action, String path, int statusCode, String message) {
            String trimmed = message == null ? "" : message.trim();
            if (statusCode == 404 && trimmed.toLowerCase().contains("route not found")) {
                return action + "接口不存在，请确认云端地址是 " + DEFAULT_BASE_URL + "，并确认 Worker 已发布移动端接口：" + path;
            }
            if (trimmed.isEmpty()) {
                return action + "失败，HTTP " + statusCode;
            }
            return trimmed;
        }

        private static String readBody(HttpURLConnection connection) throws Exception {
            InputStream stream = connection.getResponseCode() >= 200 && connection.getResponseCode() < 300
                ? connection.getInputStream()
                : connection.getErrorStream();
            StringBuilder builder = new StringBuilder();
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8))) {
                String line;
                while ((line = reader.readLine()) != null) {
                    builder.append(line);
                }
            }
            return builder.toString();
        }
    }
}
