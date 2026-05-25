package cc.luoluoluo.yanzi.mobile;

import android.app.Service;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.Point;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import android.util.Log;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import androidx.core.graphics.PathParser;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.Base64;
import java.util.Date;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class FloatingWheelService extends Service {
    private static final String TAG = "YanziFloatingWheel";
    private static final String YANZI_SITE_URL = "https://yanzi.luoluoluo.cc.cd";
    private static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";
    public static final String ACTION_OPEN_WHEEL_FROM_GESTURE = "cc.luoluoluo.yanzi.mobile.OPEN_WHEEL_FROM_GESTURE";
    private static final int BUBBLE_SIZE_DP = 58;
    private static final int BUBBLE_PADDING_DP = 12;
    private static final int EDGE_MARGIN_DP = 10;
    private static final int MENU_PANEL_WIDTH_DP = 286;
    private static final int MENU_PANEL_HEIGHT_DP = 390;
    private static final int MENU_ANCHOR_OFFSET_DP = 18;
    private static final int LONG_PRESS_TIMEOUT_MS = 260;
    private static final int INNER_MENU_SLOTS = 6;

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private WindowManager windowManager;
    private View bubbleView;
    private View wheelView;
    private View panelView;
    private View progressView;
    private SharedPreferences prefs;
    private int startX;
    private int startY;
    private float touchStartX;
    private float touchStartY;
    private float currentTouchX;
    private float currentTouchY;
    private boolean bubbleDragging;
    private boolean bubbleMoveMode;
    private boolean wheelTracking;
    private boolean ignoreBubbleGestureUntilUp;
    private Runnable pendingLongPress;
    private Runnable pendingSlotMenu;
    private SectorWheelView currentSectorWheel;
    private WebView activeMobileScriptRunner;

    private static final class WheelItem {
        final String id;
        final String icon;
        final String label;
        final Runnable action;
        final String json;
        final int wheelSlot;

        WheelItem(String id, String icon, String label, Runnable action) {
            this(id, icon, label, action, null, -1);
        }

        WheelItem(String id, String icon, String label, Runnable action, String json) {
            this(id, icon, label, action, json, -1);
        }

        WheelItem(String id, String icon, String label, Runnable action, String json, int wheelSlot) {
            this.id = id;
            this.icon = icon;
            this.label = label;
            this.action = action;
            this.json = json;
            this.wheelSlot = wheelSlot;
        }
    }

    private static final class MobileIconLibrary {
        private static final Map<String, String> ICONS = new HashMap<>();
        private static final Map<String, String> ALIASES = new HashMap<>();
        private static final Map<String, Path> CACHE = new HashMap<>();

        static {
            ICONS.put("chat", "M4,4H20A2,2 0,0 1,22 6V15A2,2 0,0 1,20 17H7L3,21V6A2,2 0,0 1,4 4Z");
            ICONS.put("camera", "M4,4H7L9,2H15L17,4H20A2,2 0,0 1,22 6V18A2,2 0,0 1,20 20H4A2,2 0,0 1,2 18V6A2,2 0,0 1,4 4M12,17A5,5 0,1 0,12 7A5,5 0,0 0,12 17M12,15A3,3 0,1 1,12 9A3,3 0,0 1,12 15Z");
            ICONS.put("image", "M21,19V5A2,2 0,0 0,19 3H5A2,2 0,0 0,3 5V19A2,2 0,0 0,5 21H19A2,2 0,0 0,21 19M8.5,11A1.5,1.5 0,1 1,10 9.5A1.5,1.5 0,0 1,8.5 11M5,19L9,14L12,17L16,12L19,16V19H5Z");
            ICONS.put("globe", "M12,2A10,10 0,1 0,22 12A10,10 0,0 0,12 2M4,12A8,8 0,0 1,12 4C10.44,6.22 9.5,8.97 9.5,12C9.5,15.03 10.44,17.78 12,20A8,8 0,0 1,4 12M12,20C13.56,17.78 14.5,15.03 14.5,12C14.5,8.97 13.56,6.22 12,4A8,8 0,0 1,20 12A8,8 0,0 1,12 20M11.5,6.05C10.54,7.85 10,9.86 10,12C10,14.14 10.54,16.15 11.5,17.95C12.46,16.15 13,14.14 13,12C13,9.86 12.46,7.85 11.5,6.05Z");
            ICONS.put("clipboard", "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0,0 0,3 5V19A2,2 0,0 0,5 21H19A2,2 0,0 0,21 19V5A2,2 0,0 0,19 3M12,3A1,1 0,0 1,13 4A1,1 0,0 1,12 5A1,1 0,0 1,11 4A1,1 0,0 1,12 3M19,19H5V5H19V19Z");
            ICONS.put("dashboard", "M3,13H11V3H3V13M3,21H11V15H3V21M13,21H21V11H13V21M13,3V9H21V3H13Z");
            ICONS.put("plus", "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");
            ICONS.put("file", "M14,2H6A2,2 0,0 0,4 4V20A2,2 0,0 0,6 22H18A2,2 0,0 0,20 20V8L14 2Z");
            ICONS.put("folder", "M10,4H2C0.89,4 0,4.89 0,6V18A2,2 0,0 0,2 20H22A2,2 0,0 0,24 18V8C24,6.89 23.1,6 22,6H12L10,4Z");
            ICONS.put("code", "M8.59,16.59L4,12L8.59,7.41L10,8.83L6.83,12L10,15.17L8.59,16.59M15.41,16.59L14,15.17L17.17,12L14,8.83L15.41,7.41L20,12L15.41,16.59Z");
            ICONS.put("settings", "M12,8A4,4 0,0 1,16 12A4,4 0,0 1,12 16A4,4 0,0 1,8 12A4,4 0,0 1,12 8M10,22C9.75,22 9.54,21.82 9.5,21.58L9.13,18.93C8.5,18.68 7.96,18.34 7.44,17.94L4.95,18.95C4.73,19.03 4.46,18.95 4.34,18.73L2.34,15.27C2.21,15.05 2.27,14.78 2.46,14.63L4.57,12.97L4.5,12L4.57,11L2.46,9.37C2.27,9.22 2.21,8.95 2.34,8.73L4.34,5.27C4.46,5.05 4.73,4.96 4.95,5.05L7.44,6.05C7.96,5.66 8.5,5.32 9.13,5.07L9.5,2.42C9.54,2.18 9.75,2 10,2H14C14.25,2 14.46,2.18 14.5,2.42L14.87,5.07C15.5,5.32 16.04,5.66 16.56,6.05L19.05,5.05C19.27,4.96 19.54,5.05 19.66,5.27L21.66,8.73C21.79,8.95 21.73,9.22 21.54,9.37L19.43,11L19.5,12L19.43,13L21.54,14.63C21.73,14.78 21.79,15.05 21.66,15.27L19.66,18.73C19.54,18.95 19.27,19.04 19.05,18.95L16.56,17.95C16.04,18.34 15.5,18.68 14.87,18.93L14.5,21.58C14.46,21.82 14.25,22 14,22H10Z");

            ALIASES.put("web", "globe");
            ALIASES.put("content-copy", "clipboard");
            ALIASES.put("monitor-dashboard", "dashboard");
            ALIASES.put("view-dashboard-outline", "dashboard");
            ALIASES.put("cellphone-arrow-down", "chat");
            ALIASES.put("file-search-outline", "file");
            ALIASES.put("file-document-edit-outline", "file");
            ALIASES.put("folder-search-outline", "folder");
            ALIASES.put("folder-cog-outline", "folder");
            ALIASES.put("code-tags", "code");
            ALIASES.put("code-json", "code");
            ALIASES.put("cog-outline", "settings");
        }

        static Path resolve(String reference) {
            String key = normalize(reference);
            if (key.isEmpty()) {
                return null;
            }
            if (CACHE.containsKey(key)) {
                return CACHE.get(key);
            }
            String pathData = ICONS.get(key);
            if (pathData == null) {
                return null;
            }
            Path path = PathParser.createPathFromPathData(pathData);
            CACHE.put(key, path);
            return path;
        }

        private static String normalize(String reference) {
            if (reference == null) {
                return "";
            }
            String value = reference.trim();
            if (value.startsWith("mdi:") || value.startsWith("app:")) {
                value = value.substring(4);
            }
            value = value.toLowerCase(Locale.ROOT);
            return ALIASES.containsKey(value) ? ALIASES.get(value) : value;
        }
    }

    private static final class BubbleGeometry {
        final int bubbleX;
        final int bubbleY;
        final int bubbleCenterX;
        final int bubbleCenterY;
        final boolean alignRight;

        BubbleGeometry(int bubbleX, int bubbleY, int bubbleCenterX, int bubbleCenterY, boolean alignRight) {
            this.bubbleX = bubbleX;
            this.bubbleY = bubbleY;
            this.bubbleCenterX = bubbleCenterX;
            this.bubbleCenterY = bubbleCenterY;
            this.alignRight = alignRight;
        }
    }

    @Override
    public void onCreate() {
        super.onCreate();
        prefs = getSharedPreferences("yanzi-mobile", Context.MODE_PRIVATE);
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        showBubble();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (bubbleView == null) {
            showBubble();
        }
        if (intent != null && ACTION_OPEN_WHEEL_FROM_GESTURE.equals(intent.getAction())) {
            openWheelFromGesture();
        }
        return START_STICKY;
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onDestroy() {
        removeView(wheelView);
        removeView(panelView);
        removeView(progressView);
        removeView(bubbleView);
        executor.shutdownNow();
        super.onDestroy();
    }

    private void showBubble() {
        if (!Settings.canDrawOverlays(this) || windowManager == null || bubbleView != null) {
            return;
        }

        ImageView bubble = new ImageView(this);
        bubble.setImageResource(getResources().getIdentifier("yanzi_launcher_bitmap", "drawable", getPackageName()));
        bubble.setBackground(circleDrawable(Color.rgb(5, 8, 13), Color.rgb(34, 211, 238), 2));
        bubble.setPadding(dp(BUBBLE_PADDING_DP), dp(BUBBLE_PADDING_DP), dp(BUBBLE_PADDING_DP), dp(BUBBLE_PADDING_DP));
        bubble.setScaleType(ImageView.ScaleType.CENTER_INSIDE);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            bubble.setClipToOutline(true);
        }

        WindowManager.LayoutParams params = overlayParams(BUBBLE_SIZE_DP, BUBBLE_SIZE_DP);
        params.gravity = Gravity.TOP | Gravity.START;
        params.x = prefs.getInt("floatingBubbleX", 24);
        params.y = prefs.getInt("floatingBubbleY", 240);

        bubble.setOnTouchListener((view, event) -> {
            switch (event.getAction()) {
                case MotionEvent.ACTION_DOWN:
                    ignoreBubbleGestureUntilUp = false;
                    startX = params.x;
                    startY = params.y;
                    touchStartX = event.getRawX();
                    touchStartY = event.getRawY();
                    currentTouchX = event.getRawX();
                    currentTouchY = event.getRawY();
                    bubbleDragging = false;
                    bubbleMoveMode = false;
                    wheelTracking = false;
                    cancelPendingLongPress();
                    pendingLongPress = () -> {
                        bubbleMoveMode = true;
                        closeOverlayUi();
                        applyBubbleMoveState(view, true);
                        toast("移动模式：拖动调整位置，松手吸边");
                    };
                    mainHandler.postDelayed(pendingLongPress, LONG_PRESS_TIMEOUT_MS);
                    return true;
                case MotionEvent.ACTION_MOVE:
                    if (ignoreBubbleGestureUntilUp) {
                        return true;
                    }
                    currentTouchX = event.getRawX();
                    currentTouchY = event.getRawY();
                    if (wheelTracking) {
                        updateWheelSelection(event.getRawX(), event.getRawY());
                        return true;
                    }
                    if (!bubbleMoveMode && (Math.abs(event.getRawX() - touchStartX) >= dp(8) || Math.abs(event.getRawY() - touchStartY) >= dp(8))) {
                        cancelPendingLongPress();
                        wheelTracking = true;
                        showWheelForBubble(params.x, params.y);
                        updateWheelSelection(event.getRawX(), event.getRawY());
                        return true;
                    }
                    if (Math.abs(event.getRawX() - touchStartX) >= dp(6) || Math.abs(event.getRawY() - touchStartY) >= dp(6)) {
                        bubbleDragging = true;
                    }
                    if (bubbleDragging) {
                        params.x = startX + (int) (event.getRawX() - touchStartX);
                        params.y = startY + (int) (event.getRawY() - touchStartY);
                        params.y = clamp(params.y, 0, Math.max(0, displaySize().y - dp(BUBBLE_SIZE_DP)));
                        windowManager.updateViewLayout(view, params);
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    if (ignoreBubbleGestureUntilUp) {
                        ignoreBubbleGestureUntilUp = false;
                        cancelPendingLongPress();
                        bubbleDragging = false;
                        bubbleMoveMode = false;
                        wheelTracking = false;
                        applyBubbleMoveState(view, false);
                        return true;
                    }
                    cancelPendingLongPress();
                    if (wheelTracking) {
                        finishWheelSelection(event.getRawX(), event.getRawY());
                        wheelTracking = false;
                        return true;
                    }
                    if (bubbleMoveMode) {
                        snapBubbleToEdge(params);
                        prefs.edit().putInt("floatingBubbleX", params.x).putInt("floatingBubbleY", params.y).apply();
                        bubbleDragging = false;
                        bubbleMoveMode = false;
                        applyBubbleMoveState(view, false);
                        return true;
                    }
                    prefs.edit().putInt("floatingBubbleX", params.x).putInt("floatingBubbleY", params.y).apply();
                    if (Math.abs(event.getRawX() - touchStartX) < 8 && Math.abs(event.getRawY() - touchStartY) < 8) {
                        toggleWheel(params.x, params.y);
                    }
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    if (ignoreBubbleGestureUntilUp) {
                        ignoreBubbleGestureUntilUp = false;
                        cancelPendingLongPress();
                        bubbleDragging = false;
                        bubbleMoveMode = false;
                        wheelTracking = false;
                        applyBubbleMoveState(view, false);
                        return true;
                    }
                    cancelPendingLongPress();
                    applyBubbleMoveState(view, false);
                    if (wheelTracking) {
                        closeOverlayUi();
                        wheelTracking = false;
                    }
                    bubbleMoveMode = false;
                    return true;
                default:
                    return false;
            }
        });

        bubbleView = bubble;
        windowManager.addView(bubbleView, params);
        snapBubbleToEdge(params);
        windowManager.updateViewLayout(bubbleView, params);
    }

    private void toggleWheel(int x, int y) {
        if (wheelView != null) {
            closeOverlayUi();
            return;
        }

        showWheelForBubble(x, y);
    }

    private void openWheelFromGesture() {
        if (wheelView != null) {
            return;
        }

        if (bubbleView != null && bubbleView.getLayoutParams() instanceof WindowManager.LayoutParams) {
            WindowManager.LayoutParams params = (WindowManager.LayoutParams) bubbleView.getLayoutParams();
            showWheelForBubble(params.x, params.y);
        }
    }

    private void showWheelForBubble(int bubbleX, int bubbleY) {
        closeOverlayUi();

        BubbleGeometry geometry = buildBubbleGeometry(bubbleX, bubbleY);
        WindowManager.LayoutParams params = overlayParams(MENU_PANEL_WIDTH_DP, MENU_PANEL_HEIGHT_DP);
        params.gravity = Gravity.TOP | Gravity.START;
        params.x = geometry.alignRight ? Math.max(0, displaySize().x - dp(MENU_PANEL_WIDTH_DP)) : 0;
        params.y = clamp(geometry.bubbleCenterY - dp(MENU_PANEL_HEIGHT_DP / 2), 0, Math.max(0, displaySize().y - dp(MENU_PANEL_HEIGHT_DP)));

        SectorWheelView wheel = new SectorWheelView(this, geometry.alignRight, buildWheelItems());
        wheel.setOnCloseRequested(this::closeOverlayUi);
        wheelView = wheel;
        currentSectorWheel = wheel;
        windowManager.addView(wheelView, params);
        showWheelActionButtons(geometry);
    }

    private WheelItem[] buildWheelItems() {
        java.util.ArrayList<WheelItem> items = new java.util.ArrayList<>();
        items.add(new WheelItem("compose-text", "mdi:chat", "发消息", () -> openMain("compose-text")));
        items.add(new WheelItem("screenshot", "mdi:camera", "发截图", () -> sendScreenshotToDesktop()));
        items.add(new WheelItem("pick-photo", "mdi:image", "发照片", () -> openMain("pick-photo")));
        items.add(new WheelItem("open-yanzi-site", "mdi:web", "官网", () -> openUrl(YANZI_SITE_URL)));
        items.add(new WheelItem("extensions", "mdi:monitor-dashboard", "远程扩展", () -> openMain("extensions")));
        items.add(new WheelItem("yanm", "mdi:monitor-dashboard", "燕幕", () -> openMain("yanm")));

        WheelItem[] outerSlots = new WheelItem[6];
        JSONArray extensions = readMobileExtensions();
        for (int i = 0; i < extensions.length(); i++) {
            JSONObject extension = extensions.optJSONObject(i);
            if (extension == null) {
                continue;
            }
            String id = firstNonEmpty(extension.optString("id"), "mobile-extension-" + i);
            String name = firstNonEmpty(extension.optString("name"), extension.optString("displayName"), "本机扩展");
            String icon = firstNonEmpty(extension.optString("icon"), firstGlyph(name));
            String json = extension.toString();
            int slot = extension.optInt("_wheelSlot", -1);
            if (slot < 0 || slot >= outerSlots.length || outerSlots[slot] != null) {
                slot = firstEmptySlot(outerSlots);
            }
            if (slot < 0) {
                continue;
            }
            outerSlots[slot] = new WheelItem("local:" + id, icon, shortLabel(name), () -> {
                prefs.edit()
                    .putString("mobileExtensionDraft", json)
                    .putString("mobileExtensionDraftId", id)
                    .putString("mobileExtensionDraftName", name)
                    .apply();
                runMobileExtensionJson(json);
            }, json, slot);
        }
        for (int slot = 0; slot < outerSlots.length; slot++) {
            WheelItem item = outerSlots[slot];
            if (item == null) {
                final int preferredSlot = slot;
                item = new WheelItem("empty-" + (slot + 1), "+", "空槽", () -> showMobileExtensionPanel(null, preferredSlot), null, preferredSlot);
            }
            items.add(item);
        }
        return items.toArray(new WheelItem[0]);
    }

    private void showWheelActionButtons(BubbleGeometry geometry) {
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(dp(8), dp(8), dp(8), dp(8));
        panel.setBackground(roundedRectDrawable(Color.argb(230, 6, 17, 31), Color.argb(150, 34, 211, 238), 16));

        Button edit = panelButton("编辑");
        Button delete = panelButton("删除");
        panel.addView(edit, new LinearLayout.LayoutParams(dp(78), dp(42)));
        panel.addView(delete, new LinearLayout.LayoutParams(dp(78), dp(42)));

        edit.setOnClickListener(v -> {
            WheelItem item = currentSectorWheel == null ? null : currentSectorWheel.selectedItem();
            if (item == null || item.json == null || !item.id.startsWith("local:")) {
                toast("先选中一个本机扩展");
                return;
            }
            pauseBubbleGestureForPanel();
            showMobileExtensionPanel(item.json, item.wheelSlot);
        });
        delete.setOnClickListener(v -> {
            WheelItem item = currentSectorWheel == null ? null : currentSectorWheel.selectedItem();
            if (item == null || item.json == null || !item.id.startsWith("local:")) {
                toast("先选中一个本机扩展");
                return;
            }
            deleteMobileExtension(item.id.substring("local:".length()));
            closeOverlayUi();
        });

        WindowManager.LayoutParams params = overlayParams(96, 104);
        params.gravity = Gravity.TOP | Gravity.START;
        params.x = geometry.alignRight ? dp(14) : Math.max(0, displaySize().x - dp(110));
        params.y = clamp(geometry.bubbleCenterY - dp(52), dp(20), Math.max(dp(20), displaySize().y - dp(124)));
        panelView = panel;
        windowManager.addView(panelView, params);
    }

    private static int firstEmptySlot(WheelItem[] slots) {
        for (int i = 0; i < slots.length; i++) {
            if (slots[i] == null) {
                return i;
            }
        }
        return -1;
    }

    private JSONArray readMobileExtensions() {
        try {
            String value = prefs.getString("mobileExtensions", "[]");
            JSONArray array = new JSONArray(value == null || value.trim().isEmpty() ? "[]" : value);
            if (array.length() == 0) {
                String draft = prefs.getString("mobileExtensionDraft", "");
                if (draft != null && draft.trim().startsWith("{")) {
                    array.put(new JSONObject(draft));
                }
            }
            return array;
        } catch (Exception ex) {
            log("读取手机扩展列表失败：" + ex.getMessage());
            return new JSONArray();
        }
    }

    private void upsertMobileExtension(JSONObject json) throws Exception {
        upsertMobileExtension(json, json.optInt("_wheelSlot", -1));
    }

    private void upsertMobileExtension(JSONObject json, int preferredSlot) throws Exception {
        String id = firstNonEmpty(json.optString("id"), "mobile-extension-" + System.currentTimeMillis());
        json.put("id", id);
        if (preferredSlot >= 0 && preferredSlot < 6) {
            json.put("_wheelSlot", preferredSlot);
        }
        JSONArray array = readMobileExtensions();
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
        prefs.edit()
            .putString("mobileExtensions", next.toString())
            .putString("mobileExtensionDraft", json.toString(2))
            .putString("mobileExtensionDraftId", id)
            .putString("mobileExtensionDraftName", firstNonEmpty(json.optString("name"), json.optString("displayName"), "手机扩展"))
            .apply();
        log("手机扩展已保存到槽位：id=" + id + " count=" + next.length());
    }

    private void deleteMobileExtension(String id) {
        JSONArray array = readMobileExtensions();
        JSONArray next = new JSONArray();
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item != null && !id.equals(item.optString("id"))) {
                next.put(item);
            }
        }
        prefs.edit().putString("mobileExtensions", next.toString()).apply();
        log("手机扩展已删除：id=" + id + " count=" + next.length());
        toast("已删除扩展");
    }

    private void runMobileExtensionJson(String jsonText) {
        try {
            String source = extractMobileScriptSource(jsonText);
            if (source.trim().isEmpty()) {
                throw new IllegalStateException("脚本为空");
            }

            WebView runner = new WebView(this);
            activeMobileScriptRunner = runner;
            runner.getSettings().setJavaScriptEnabled(true);
            runner.addJavascriptInterface(new FloatingMobileJsBridge(), "yanziMobileJsHost");
            String html = "<!doctype html><html><body><script>" +
                "window.context={mobile:{" +
                "toast:function(text){yanziMobileJsHost.toast(String(text||''));}," +
                "sendToDesktop:function(text){yanziMobileJsHost.sendToDesktop(String(text||''));}," +
                "getSharedText:function(){return yanziMobileJsHost.getSharedText();}," +
                "getClipboardText:function(){return Promise.resolve(yanziMobileJsHost.getClipboardText());}," +
                "setClipboardText:function(text){return Promise.resolve(yanziMobileJsHost.setClipboardText(String(text||'')));}," +
                "openUrl:function(url){return Promise.resolve(yanziMobileJsHost.openUrl(String(url||'')));}," +
                "readTextFile:function(name){return Promise.resolve(JSON.parse(yanziMobileJsHost.unsupported('readTextFile')));}," +
                "saveTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.unsupported('saveTextFile')));}," +
                "appendTextFile:function(name,text){return Promise.resolve(JSON.parse(yanziMobileJsHost.unsupported('appendTextFile')));}," +
                "httpGet:function(url){return Promise.resolve(JSON.parse(yanziMobileJsHost.unsupported('httpGet')));}," +
                "httpPostJson:function(url,jsonText){return Promise.resolve(JSON.parse(yanziMobileJsHost.unsupported('httpPostJson')));}" +
                "}};" +
                "async function __run(){try{" + source + "\n;if(typeof run==='function'){await run(window.context);}yanziMobileJsHost.done('扩展执行完成');}" +
                "catch(e){yanziMobileJsHost.fail(String(e&&e.message?e.message:e));}}" +
                "__run();" +
                "</script></body></html>";
            runner.loadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
            log("手机扩展开始执行。");
        } catch (Exception ex) {
            log("手机扩展执行失败：" + ex.getMessage());
            toast("扩展执行失败：" + ex.getMessage());
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

    private final class FloatingMobileJsBridge {
        @JavascriptInterface
        public void toast(String text) {
            FloatingWheelService.this.toast(text);
        }

        @JavascriptInterface
        public void done(String message) {
            log("手机扩展执行完成：" + message);
            FloatingWheelService.this.toast(message);
        }

        @JavascriptInterface
        public void fail(String message) {
            log("手机扩展执行失败：" + message);
            FloatingWheelService.this.toast("扩展执行失败：" + message);
        }

        @JavascriptInterface
        public String getSharedText() {
            return readClipboardText();
        }

        @JavascriptInterface
        public String getClipboardText() {
            return readClipboardText();
        }

        @JavascriptInterface
        public String setClipboardText(String text) {
            copyToClipboard("Yanzi mobile script", text == null ? "" : text);
            return text == null ? "" : text;
        }

        @JavascriptInterface
        public String openUrl(String url) {
            mainHandler.post(() -> {
                try {
                    Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    startActivity(intent);
                } catch (Exception ex) {
                    toast("打开链接失败：" + ex.getMessage());
                }
            });
            return url;
        }

        @JavascriptInterface
        public void sendToDesktop(String text) {
            sendTextToDesktop(text);
        }

        @JavascriptInterface
        public String unsupported(String api) {
            return "{\"ok\":false,\"error\":\"Service 轮盘暂不支持 " + api + "\"}";
        }
    }

    private static String firstGlyph(String value) {
        if (value == null || value.trim().isEmpty()) {
            return "+";
        }
        String trimmed = value.trim();
        return trimmed.substring(0, Math.min(trimmed.length(), 1));
    }

    private static String shortLabel(String value) {
        if (value == null || value.trim().isEmpty()) {
            return "扩展";
        }
        String trimmed = value.trim();
        return trimmed.length() > 3 ? trimmed.substring(0, 3) : trimmed;
    }

    private static String firstNonEmpty(String... values) {
        if (values == null) {
            return "";
        }
        for (String value : values) {
            if (value != null && !value.trim().isEmpty()) {
                return value.trim();
            }
        }
        return "";
    }

    private void updateWheelSelection(float rawX, float rawY) {
        if (currentSectorWheel == null) {
            return;
        }
        currentSectorWheel.updateSelectionFromRaw(rawX, rawY);
    }

    private void finishWheelSelection(float rawX, float rawY) {
        if (currentSectorWheel == null) {
            closeOverlayUi();
            return;
        }
        Runnable action = currentSectorWheel.actionFromRaw(rawX, rawY);
        if (action == null) {
            closeOverlayUi();
            return;
        }
        closeOverlayUi();
        action.run();
    }

    private void pauseBubbleGestureForPanel() {
        ignoreBubbleGestureUntilUp = true;
        wheelTracking = false;
        bubbleDragging = false;
        bubbleMoveMode = false;
        cancelPendingLongPress();
        if (bubbleView != null) {
            applyBubbleMoveState(bubbleView, false);
        }
    }

    private void scheduleSlotMenu(WheelItem item) {
        cancelPendingSlotMenu();
        if (item == null || item.json == null || !item.id.startsWith("local:")) {
            return;
        }
        pendingSlotMenu = () -> {
            closeOverlayUi();
            showMobileExtensionManagePanel(item);
        };
        mainHandler.postDelayed(pendingSlotMenu, 3000);
    }

    private void cancelPendingSlotMenu() {
        if (pendingSlotMenu != null) {
            mainHandler.removeCallbacks(pendingSlotMenu);
            pendingSlotMenu = null;
        }
    }

    private BubbleGeometry buildBubbleGeometry(int bubbleX, int bubbleY) {
        int bubbleSize = dp(BUBBLE_SIZE_DP);
        int hiddenSize = bubbleSize / 4;
        Point size = displaySize();
        int safeX = clamp(bubbleX, -hiddenSize, Math.max(-hiddenSize, size.x - bubbleSize + hiddenSize));
        int safeY = clamp(bubbleY, 0, Math.max(0, size.y - bubbleSize));
        int centerX = safeX + bubbleSize / 2;
        int centerY = safeY + bubbleSize / 2;
        boolean alignRight = centerX >= size.x / 2;
        return new BubbleGeometry(safeX, safeY, centerX, centerY, alignRight);
    }

    private void snapBubbleToEdge(WindowManager.LayoutParams params) {
        Point size = displaySize();
        int bubbleSize = dp(BUBBLE_SIZE_DP);
        int hiddenSize = bubbleSize / 4;
        int leftX = -hiddenSize;
        int rightX = Math.max(leftX, size.x - bubbleSize + hiddenSize);
        int centerX = params.x + bubbleSize / 2;
        params.x = centerX >= size.x / 2 ? rightX : leftX;
        params.y = clamp(params.y, dp(EDGE_MARGIN_DP), Math.max(dp(EDGE_MARGIN_DP), size.y - bubbleSize - dp(EDGE_MARGIN_DP)));
    }

    private void cancelPendingLongPress() {
        if (pendingLongPress != null) {
            mainHandler.removeCallbacks(pendingLongPress);
            pendingLongPress = null;
        }
    }

    private void applyBubbleMoveState(View view, boolean moving) {
        view.setAlpha(moving ? 1f : 0.92f);
        view.setScaleX(moving ? 1.18f : 1f);
        view.setScaleY(moving ? 1.18f : 1f);
        view.setBackground(circleDrawable(
            moving ? Color.rgb(14, 116, 144) : Color.rgb(5, 8, 13),
            moving ? Color.rgb(165, 243, 252) : Color.rgb(34, 211, 238),
            moving ? 2 : 1));
    }

    private void openUrl(String url) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(intent);
        } catch (Exception ex) {
            toast("打开失败：" + ex.getMessage());
        }
    }

    private final class SectorWheelView extends View {
        private static final float START_LEFT = -90f;
        private static final float START_RIGHT = 90f;
        private static final float TOTAL_SWEEP = 180f;

        private final boolean alignRight;
        private final WheelItem[] items;
        private final Paint fillPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint strokePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path sectorPath = new Path();
        private final Bitmap logoBitmap;
        private Runnable onCloseRequested;
        private int selectedIndex = -1;

        SectorWheelView(Context context, boolean alignRight, WheelItem[] items) {
            super(context);
            this.alignRight = alignRight;
            this.items = items;
            setWillNotDraw(false);
            setClickable(true);
            fillPaint.setStyle(Paint.Style.FILL);
            strokePaint.setStyle(Paint.Style.STROKE);
            strokePaint.setStrokeWidth(dp(1));
            strokePaint.setColor(Color.argb(120, 148, 163, 184));
            textPaint.setColor(Color.WHITE);
            textPaint.setTextAlign(Paint.Align.CENTER);
            logoBitmap = BitmapFactory.decodeResource(getResources(), R.drawable.yanzi_launcher_bitmap);
        }

        void setOnCloseRequested(Runnable onCloseRequested) {
            this.onCloseRequested = onCloseRequested;
        }

        int innerRadius() {
            return dp(46);
        }

        int middleRadius() {
            return dp(108);
        }

        int outerRadius() {
            return dp(170);
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            int cx = alignRight ? getWidth() : 0;
            int cy = getHeight() / 2;
            float start = alignRight ? START_RIGHT : START_LEFT;
            float per = TOTAL_SWEEP / INNER_MENU_SLOTS;
            RectF outer = new RectF(cx - outerRadius(), cy - outerRadius(), cx + outerRadius(), cy + outerRadius());
            RectF middle = new RectF(cx - middleRadius(), cy - middleRadius(), cx + middleRadius(), cy + middleRadius());
            RectF inner = new RectF(cx - innerRadius(), cy - innerRadius(), cx + innerRadius(), cy + innerRadius());

            for (int i = 0; i < items.length; i++) {
                int ring = i / INNER_MENU_SLOTS;
                int slot = i % INNER_MENU_SLOTS;
                float sectorStart = start + per * slot;
                RectF ringOuter = ring == 0 ? middle : outer;
                RectF ringInner = ring == 0 ? inner : middle;
                sectorPath.reset();
                sectorPath.arcTo(ringOuter, sectorStart, per, false);
                sectorPath.arcTo(ringInner, sectorStart + per, -per, false);
                sectorPath.close();
                fillPaint.setColor(i == selectedIndex ? Color.argb(224, 14, 116, 144) : Color.argb(204, 4, 12, 24));
                canvas.drawPath(sectorPath, fillPaint);
                canvas.drawPath(sectorPath, strokePaint);

                float angle = sectorStart + per / 2f;
                float iconRadius = (ring == 0 ? innerRadius() + middleRadius() : middleRadius() + outerRadius()) / 2f;
                float iconX = cx + (float) Math.cos(Math.toRadians(angle)) * iconRadius;
                float iconY = cy + (float) Math.sin(Math.toRadians(angle)) * iconRadius;
                drawItem(canvas, items[i], iconX, iconY, i == selectedIndex);
            }

            strokePaint.setColor(Color.argb(180, 34, 211, 238));
            strokePaint.setStrokeWidth(dp(1));
            canvas.drawArc(inner, start, TOTAL_SWEEP, false, strokePaint);
            drawCenterLogo(canvas, cx, cy);
        }

        private void drawCenterLogo(Canvas canvas, int cx, int cy) {
            if (logoBitmap == null) {
                return;
            }
            int size = dp(38);
            Rect target = new Rect(cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2);
            canvas.drawBitmap(logoBitmap, null, target, null);
        }

        private void drawItem(Canvas canvas, WheelItem item, float x, float y, boolean selected) {
            Path iconPath = MobileIconLibrary.resolve(item.icon);
            textPaint.setColor(selected ? Color.rgb(165, 243, 252) : Color.WHITE);
            textPaint.setFakeBoldText(true);
            if (iconPath != null) {
                drawIconPath(canvas, iconPath, x, y - dp(8), selected);
            } else {
                textPaint.setTextSize(dp(item.icon.length() > 1 ? 10 : 16));
                canvas.drawText(item.icon, x, y - dp(4), textPaint);
            }
            textPaint.setFakeBoldText(false);
            textPaint.setColor(Color.rgb(226, 232, 240));
            textPaint.setTextSize(dp(9));
            canvas.drawText(item.label, x, y + dp(17), textPaint);
            textPaint.setColor(Color.WHITE);
        }

        private void drawIconPath(Canvas canvas, Path source, float centerX, float centerY, boolean selected) {
            Path icon = new Path(source);
            RectF bounds = new RectF();
            icon.computeBounds(bounds, true);
            if (bounds.width() <= 0 || bounds.height() <= 0) {
                return;
            }
            float size = dp(18);
            float scale = size / Math.max(bounds.width(), bounds.height());
            Matrix matrix = new Matrix();
            matrix.postTranslate(-bounds.centerX(), -bounds.centerY());
            matrix.postScale(scale, scale);
            matrix.postTranslate(centerX, centerY);
            icon.transform(matrix);
            fillPaint.setColor(selected ? Color.rgb(165, 243, 252) : Color.WHITE);
            canvas.drawPath(icon, fillPaint);
        }

        @Override
        public boolean onTouchEvent(MotionEvent event) {
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                case MotionEvent.ACTION_MOVE:
                    updateSelection(event.getX(), event.getY());
                    return true;
                case MotionEvent.ACTION_UP:
                    Runnable action = actionAt(event.getX(), event.getY());
                    if (action == null) {
                        requestClose();
                    } else {
                        requestClose();
                        action.run();
                    }
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    requestClose();
                    return true;
                default:
                    return true;
            }
        }

        void updateSelectionFromRaw(float rawX, float rawY) {
            int[] location = new int[2];
            getLocationOnScreen(location);
            updateSelection(rawX - location[0], rawY - location[1]);
        }

        Runnable actionFromRaw(float rawX, float rawY) {
            int[] location = new int[2];
            getLocationOnScreen(location);
            return actionAt(rawX - location[0], rawY - location[1]);
        }

        private void updateSelection(float x, float y) {
            int index = indexAt(x, y);
            if (selectedIndex != index) {
                selectedIndex = index;
                invalidate();
            }
        }

        private WheelItem selectedItem() {
            return selectedIndex >= 0 && selectedIndex < items.length ? items[selectedIndex] : null;
        }

        private Runnable actionAt(float x, float y) {
            int index = indexAt(x, y);
            if (index < 0 || index >= items.length) {
                return null;
            }
            return items[index].action;
        }

        private int indexAt(float x, float y) {
            int cx = alignRight ? getWidth() : 0;
            int cy = getHeight() / 2;
            float dx = x - cx;
            float dy = y - cy;
            double distance = Math.sqrt(dx * dx + dy * dy);
            if (distance < innerRadius() || distance > outerRadius()) {
                return -1;
            }

            float angle = (float) Math.toDegrees(Math.atan2(dy, dx));
            float relative = normalizeDegrees(angle - (alignRight ? START_RIGHT : START_LEFT));
            if (relative < 0 || relative > TOTAL_SWEEP) {
                return -1;
            }
            int ring = distance <= middleRadius() ? 0 : 1;
            int slot = (int) Math.floor(relative / (TOTAL_SWEEP / INNER_MENU_SLOTS));
            int index = ring * INNER_MENU_SLOTS + clamp(slot, 0, INNER_MENU_SLOTS - 1);
            return index >= items.length ? -1 : index;
        }

        private float normalizeDegrees(float value) {
            while (value < 0) {
                value += 360f;
            }
            while (value >= 360f) {
                value -= 360f;
            }
            return value;
        }

        private void requestClose() {
            cancelPendingSlotMenu();
            selectedIndex = -1;
            invalidate();
            if (onCloseRequested != null) {
                onCloseRequested.run();
            }
        }
    }

    private void showTextPanel() {
        removeView(panelView);
        LinearLayout panel = overlayPanel();
        panel.addView(panelTitle("发送到电脑"));
        EditText input = panelInput("输入要发送给电脑的文本", "燕子", 3);
        panel.addView(input);
        LinearLayout buttons = row();
        Button send = panelButton("发送");
        Button copy = panelButton("复制");
        Button close = panelButton("关闭");
        buttons.addView(send, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(copy, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(close, new LinearLayout.LayoutParams(0, dp(42), 1));
        panel.addView(buttons);

        send.setOnClickListener(v -> {
            String text = input.getText().toString().trim();
            if (text.isEmpty()) {
                text = "燕子";
                input.setText(text);
            }
            copyToClipboard("Yanzi mobile text", text);
            sendTextToDesktop(text);
        });
        copy.setOnClickListener(v -> copyToClipboard("Yanzi mobile text", input.getText().toString().trim().isEmpty() ? "燕子" : input.getText().toString()));
        close.setOnClickListener(v -> {
            closePanel();
        });
        showPanel(panel, 220);
        focusInput(input);
    }

    private void showMobileExtensionPanel() {
        closeOverlayUi();
        showMobileExtensionPanel(null, -1);
    }

    private void showMobileExtensionPanel(String initialJson) {
        showMobileExtensionPanel(initialJson, -1);
    }

    private void showMobileExtensionPanel(String initialJson, int preferredSlot) {
        LinearLayout panel = overlayPanel();
        panel.addView(panelTitle("添加手机扩展"));
        EditText input = panelInput("粘贴手机扩展 JSON", firstNonEmpty(initialJson, prefs.getString("mobileExtensionDraft", ""), defaultMobileExtensionJson()), 8);
        panel.addView(input, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1));
        TextView result = new TextView(this);
        result.setText("粘贴后会自动检测 JSON 格式。");
        result.setTextColor(Color.rgb(148, 163, 184));
        result.setTextSize(12);
        result.setTextIsSelectable(true);
        result.setPadding(0, dp(8), 0, 0);
        panel.addView(result);

        LinearLayout buttons = row();
        Button paste = panelButton("粘贴JSON");
        Button test = panelButton("测试扩展");
        Button save = panelButton("保存扩展");
        Button close = panelButton("关闭");
        buttons.addView(paste, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(test, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(save, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(close, new LinearLayout.LayoutParams(0, dp(42), 1));
        panel.addView(buttons);

        paste.setOnClickListener(v -> {
            try {
                String text = readClipboardText().trim();
                if (text.isEmpty()) {
                    throw new IllegalStateException("剪贴板没有 JSON 内容");
                }
                input.setText("");
                JSONObject json = new JSONObject(text);
                String pretty = json.toString(2);
                input.setText(pretty);
                input.setSelection(pretty.length());
                result.setText("JSON 格式正确：" + firstNonEmpty(json.optString("name"), json.optString("id"), "未命名扩展"));
                result.setTextColor(Color.rgb(125, 211, 252));
            } catch (Exception ex) {
                input.setText("");
                result.setText("JSON 格式错误：" + ex.getMessage());
                result.setTextColor(Color.rgb(248, 113, 113));
            }
        });
        test.setOnClickListener(v -> {
            try {
                JSONObject json = new JSONObject(input.getText().toString().trim());
                result.setText("测试通过：" + firstNonEmpty(json.optString("name"), json.optString("id"), "未命名扩展"));
                result.setTextColor(Color.rgb(125, 211, 252));
            } catch (Exception ex) {
                result.setText("测试失败：" + ex.getMessage());
                result.setTextColor(Color.rgb(248, 113, 113));
            }
        });
        save.setOnClickListener(v -> {
            try {
                JSONObject json = new JSONObject(input.getText().toString().trim());
                String name = firstNonEmpty(json.optString("name"), json.optString("displayName"), "手机扩展");
                upsertMobileExtension(json, preferredSlot);
                toast("已保存扩展：" + name);
                closePanel();
            } catch (Exception ex) {
                result.setText("保存失败：" + ex.getMessage());
                result.setTextColor(Color.rgb(248, 113, 113));
            }
        });
        close.setOnClickListener(v -> closePanel());
        showPanel(panel, 420);
        focusInput(input);
    }

    private void showMobileExtensionManagePanel(WheelItem item) {
        LinearLayout panel = overlayPanel();
        panel.addView(panelTitle("扩展操作"));
        panel.addView(textView("扩展：" + item.label, 14, Color.WHITE, true));
        LinearLayout buttons = row();
        Button edit = panelButton("编辑");
        Button delete = panelButton("删除");
        Button close = panelButton("关闭");
        buttons.addView(edit, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(delete, new LinearLayout.LayoutParams(0, dp(42), 1));
        buttons.addView(close, new LinearLayout.LayoutParams(0, dp(42), 1));
        panel.addView(buttons);
        edit.setOnClickListener(v -> {
            closePanel();
            showMobileExtensionPanel(item.json);
        });
        delete.setOnClickListener(v -> {
            deleteMobileExtension(item.id.substring("local:".length()));
            closePanel();
        });
        close.setOnClickListener(v -> closePanel());
        showPanel(panel, 170);
    }

    private String readClipboardText() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        ClipData clip = clipboard == null ? null : clipboard.getPrimaryClip();
        CharSequence value = clip == null || clip.getItemCount() == 0 ? "" : clip.getItemAt(0).coerceToText(this);
        return value == null ? "" : value.toString();
    }

    private LinearLayout overlayPanel() {
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setFocusable(true);
        panel.setFocusableInTouchMode(true);
        panel.setPadding(dp(14), dp(12), dp(14), dp(12));
        panel.setOnKeyListener((v, keyCode, event) -> {
            if (keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP) {
                closePanel();
                return true;
            }
            return false;
        });
        GradientDrawable background = new GradientDrawable();
        background.setColor(Color.argb(246, 6, 17, 31));
        background.setCornerRadius(dp(18));
        background.setStroke(dp(1), Color.argb(140, 34, 211, 238));
        panel.setBackground(background);
        return panel;
    }

    private void showPanel(View panel, int heightDp) {
        removeView(panelView);
        panelView = null;
        WindowManager.LayoutParams params = overlayParamsFocusable(-1, heightDp);
        params.gravity = Gravity.BOTTOM | Gravity.START;
        params.x = dp(12);
        params.y = dp(18);
        panelView = panel;
        windowManager.addView(panelView, params);
        panelView.requestFocus();
    }

    private void showProgress(String text) {
        removeView(progressView);
        LinearLayout panel = new LinearLayout(this);
        panel.setOrientation(LinearLayout.HORIZONTAL);
        panel.setGravity(Gravity.CENTER_VERTICAL);
        panel.setPadding(dp(14), dp(10), dp(14), dp(10));
        panel.setBackground(roundedRectDrawable(Color.argb(238, 6, 17, 31), Color.argb(160, 34, 211, 238), 16));
        TextView spinner = new TextView(this);
        spinner.setText("...");
        spinner.setTextColor(Color.rgb(34, 211, 238));
        spinner.setTextSize(18);
        TextView label = new TextView(this);
        label.setText(text);
        label.setTextColor(Color.WHITE);
        label.setTextSize(14);
        label.setPadding(dp(10), 0, 0, 0);
        panel.addView(spinner, new LinearLayout.LayoutParams(dp(34), dp(34)));
        panel.addView(label, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT));
        WindowManager.LayoutParams params = overlayParams(220, 56);
        params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        params.y = dp(92);
        progressView = panel;
        windowManager.addView(progressView, params);
    }

    private void hideProgress() {
        android.os.Handler handler = new android.os.Handler(getMainLooper());
        handler.post(() -> {
            removeView(progressView);
            progressView = null;
        });
    }

    private LinearLayout panelTitle(String text) {
        LinearLayout header = new LinearLayout(this);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = new TextView(this);
        title.setText(text);
        title.setTextColor(Color.WHITE);
        title.setTextSize(16);
        title.setGravity(Gravity.START);
        title.setPadding(0, 0, 0, dp(8));
        Button close = panelButton("退出");
        close.setOnClickListener(v -> closePanel());
        header.addView(title, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        header.addView(close, new LinearLayout.LayoutParams(dp(76), dp(38)));
        return header;
    }

    private EditText panelInput(String hint, String value, int minLines) {
        EditText input = new EditText(this);
        input.setHint(hint);
        input.setText(value);
        input.setTextColor(Color.WHITE);
        input.setHintTextColor(Color.rgb(148, 163, 184));
        input.setMinLines(minLines);
        input.setGravity(Gravity.TOP);
        input.setSingleLine(false);
        input.setBackgroundColor(Color.rgb(15, 23, 42));
        input.setPadding(dp(10), dp(8), dp(10), dp(8));
        input.setOnKeyListener((v, keyCode, event) -> {
            if (keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP) {
                closePanel();
                return true;
            }
            return false;
        });
        return input;
    }

    private LinearLayout row() {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setPadding(0, dp(10), 0, 0);
        return row;
    }

    private Button panelButton(String text) {
        Button button = new Button(this);
        button.setText(text);
        return button;
    }

    private TextView textView(String text, int sp, int color, boolean bold) {
        TextView view = new TextView(this);
        view.setText(text);
        view.setTextSize(sp);
        view.setTextColor(color);
        view.setTypeface(null, bold ? android.graphics.Typeface.BOLD : android.graphics.Typeface.NORMAL);
        view.setPadding(0, dp(4), 0, dp(4));
        return view;
    }

    private void focusInput(EditText input) {
        input.requestFocus();
        input.postDelayed(() -> {
            InputMethodManager manager = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
            if (manager != null) {
                manager.showSoftInput(input, InputMethodManager.SHOW_IMPLICIT);
            }
        }, 180);
    }

    private void sendClipboardTextToDesktop() {
        ClipboardManager clipboard = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        ClipData clip = clipboard == null ? null : clipboard.getPrimaryClip();
        CharSequence value = clip == null || clip.getItemCount() == 0 ? "" : clip.getItemAt(0).coerceToText(this);
        String text = value == null ? "" : value.toString().trim();
        if (text.isEmpty()) {
            openMain("compose-text");
            toast("系统限制后台读取剪贴板，请在输入框发送。");
            return;
        }
        sendTextToDesktop(text);
    }

    private void sendScreenshotToDesktop() {
        log("截图：用户点击截图。");
        if (!MobileAccessibilityService.isEnabled()) {
            log("截图：无障碍未开启，跳转设置。");
            openAccessibilitySettings();
            toast("请开启燕子无障碍服务后再使用截图。");
            return;
        }

        closeOverlayUi();
        showProgress("正在截图并发送...");
        log("截图：开始调用无障碍截图。");
        MobileAccessibilityService.captureJpegBase64(new MobileAccessibilityService.ScreenshotCallback() {
            @Override
            public void onSuccess(String jpegBase64, int width, int height) {
                log("截图：无障碍截图成功，尺寸=" + width + "x" + height + "。");
                sendScreenshotPayloadToDesktop(jpegBase64, width, height);
            }

            @Override
            public void onFailure(String message) {
                hideProgress();
                log("截图：无障碍截图失败，" + message);
                toast("截图失败：" + message);
            }
        });
    }

    private void sendTextToDesktop(String text) {
        executor.execute(() -> {
            try {
                String token = requireToken();
                String deviceId = getOrCreateDeviceId();
                String messageId;
                try {
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    messageId = postMessage(normalizedBaseUrl(), token, deviceId, text);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken();
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    messageId = postMessage(normalizedBaseUrl(), token, deviceId, text);
                }
                toast("已发送到电脑：" + messageId);
            } catch (Exception ex) {
                toast("发送失败：" + ex.getMessage());
            }
        });
    }

    private void sendScreenshotPayloadToDesktop(String jpegBase64, int width, int height) {
        executor.execute(() -> {
            try {
                String token = requireToken();
                String deviceId = getOrCreateDeviceId();
                byte[] imageBytes = Base64.getDecoder().decode(jpegBase64);
                log("截图：准备上传 WebDAV，bytes=" + imageBytes.length + "。");
                String messageId;
                try {
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    log("截图：设备注册完成，正在读取 WebDAV 配置。");
                    WebDavConfig webDav = fetchWebDavConfig(normalizedBaseUrl(), token);
                    log("截图：WebDAV 配置读取完成，开始上传。");
                    String remotePath = uploadScreenshotToWebDav(webDav, imageBytes);
                    log("截图：WebDAV 上传完成，path=" + remotePath + "。");
                    messageId = postScreenshotWebDavMessage(normalizedBaseUrl(), token, deviceId, remotePath, imageBytes.length, width, height);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    log("截图：Token 过期，刷新后重试。");
                    token = refreshToken();
                    registerDevice(normalizedBaseUrl(), token, deviceId, buildDeviceName());
                    WebDavConfig webDav = fetchWebDavConfig(normalizedBaseUrl(), token);
                    String remotePath = uploadScreenshotToWebDav(webDav, imageBytes);
                    log("截图：WebDAV 重试上传完成，path=" + remotePath + "。");
                    messageId = postScreenshotWebDavMessage(normalizedBaseUrl(), token, deviceId, remotePath, imageBytes.length, width, height);
                }
                log("截图：消息已发送到云端，messageId=" + messageId + "。");
                toast("截图已发送到电脑：" + messageId);
                hideProgress();
            } catch (Exception ex) {
                log("截图：发送失败，" + ex.getMessage());
                toast("截图发送失败：" + ex.getMessage());
                hideProgress();
            }
        });
    }

    private void openMain(String action) {
        Intent intent = new Intent(this, MainActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        intent.setAction("cc.luoluoluo.yanzi.mobile." + action);
        startActivity(intent);
    }

    private void openAccessibilitySettings() {
        Intent intent = new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        startActivity(intent);
    }

    private WindowManager.LayoutParams overlayParams(int widthDp, int heightDp) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            : WindowManager.LayoutParams.TYPE_PHONE;
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
            dp(widthDp),
            dp(heightDp),
            type,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.TOP | Gravity.START;
        return params;
    }

    private WindowManager.LayoutParams overlayParamsFocusable(int widthDp, int heightDp) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
            : WindowManager.LayoutParams.TYPE_PHONE;
        int width = widthDp < 0 ? WindowManager.LayoutParams.MATCH_PARENT : dp(widthDp);
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
            width,
            dp(heightDp),
            type,
            WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
            PixelFormat.TRANSLUCENT);
        params.softInputMode = WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE;
        return params;
    }

    private GradientDrawable circleDrawable(int fillColor, int strokeColor, int strokeDp) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setShape(GradientDrawable.OVAL);
        drawable.setColor(fillColor);
        drawable.setStroke(dp(strokeDp), strokeColor);
        return drawable;
    }

    private GradientDrawable roundedRectDrawable(int fillColor, int strokeColor, int radiusDp) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(fillColor);
        drawable.setCornerRadius(dp(radiusDp));
        drawable.setStroke(dp(1), strokeColor);
        return drawable;
    }

    private void removeView(View view) {
        if (view == null || windowManager == null) {
            return;
        }
        try {
            windowManager.removeView(view);
        } catch (Exception ignored) {
        }
    }

    private void closePanel() {
        removeView(panelView);
        panelView = null;
    }

    private void closeOverlayUi() {
        closePanel();
        cancelPendingSlotMenu();
        removeView(wheelView);
        wheelView = null;
        currentSectorWheel = null;
    }

    private void toast(String message) {
        android.os.Handler handler = new android.os.Handler(getMainLooper());
        handler.post(() -> Toast.makeText(this, message, Toast.LENGTH_SHORT).show());
    }

    private void log(String message) {
        Log.d(TAG, message);
        MobileDiagnostics.append(this, message);
    }

    private void copyToClipboard(String label, String text) {
        ClipboardManager manager = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
        if (manager != null) {
            manager.setPrimaryClip(ClipData.newPlainText(label, text == null || text.trim().isEmpty() ? "燕子" : text));
            toast("已复制到剪贴板");
        }
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private Point displaySize() {
        Point size = new Point();
        if (windowManager != null) {
            windowManager.getDefaultDisplay().getSize(size);
        }
        if (size.x <= 0 || size.y <= 0) {
            size.x = getResources().getDisplayMetrics().widthPixels;
            size.y = getResources().getDisplayMetrics().heightPixels;
        }
        return size;
    }

    private static int clamp(int value, int min, int max) {
        return Math.max(min, Math.min(max, value));
    }

    private String normalizedBaseUrl() {
        String value = prefs.getString("baseUrl", DEFAULT_BASE_URL);
        if (value == null || value.trim().isEmpty()) {
            return DEFAULT_BASE_URL;
        }
        value = value.trim();
        int v1Index = value.indexOf("/v1/");
        if (v1Index >= 0) {
            value = value.substring(0, v1Index);
        }
        while (value.endsWith("/")) {
            value = value.substring(0, value.length() - 1);
        }
        return value.isEmpty() ? DEFAULT_BASE_URL : value;
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
        return name.isEmpty() ? "Android 手机" : name;
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

    private String requireToken() {
        String token = prefs.getString("token", "");
        if (token == null || token.trim().isEmpty()) {
            return refreshToken();
        }
        return token;
    }

    private String refreshToken() {
        try {
            String email = prefs.getString("email", "");
            String password = prefs.getString("password", "");
            if (email == null || email.trim().isEmpty() || password == null || password.isEmpty()) {
                throw new IllegalStateException("请先在燕子移动端登录。");
            }

            String token = login(normalizedBaseUrl(), email.trim(), password);
            prefs.edit().putString("token", token).apply();
            return token;
        } catch (Exception ex) {
            throw new IllegalStateException("登录态已失效，请回到燕子移动端重新登录：" + ex.getMessage());
        }
    }

    private static boolean isUnauthorized(Exception ex) {
        String message = ex.getMessage();
        return message != null && message.contains("HTTP 401");
    }

    private static String login(String baseUrl, String email, String password) throws Exception {
        JSONObject payload = new JSONObject()
            .put("email", email)
            .put("password", password);
        return postJson(baseUrl, "/v1/auth/login", payload, null).getString("accessToken");
    }

    private static String mobileExtensionPrompt() {
        return "你正在为燕子移动端编写手机扩展。只允许输出 JSON，不要解释。\n" +
            "运行时使用 runtime=\"mobile-js\"，不要使用 C#、PowerShell、Windows 路径、WPF 或桌面 API。\n" +
            "优先设计本机可执行能力，再按需补充发到电脑。可用 permissions：clipboard.read、clipboard.write、browser.open、file.read、file.write、http.request、desktop.message、share.text。\n" +
            "脚本入口使用 async function run(context)。可调用 context.mobile.toast(text)、getSharedText()、getClipboardText()、setClipboardText(text)、openUrl(url)、readTextFile(name)、saveTextFile(name,text)、appendTextFile(name,text)、httpGet(url)、httpPostJson(url,jsonText)、sendToDesktop(text)。";
    }

    private static String defaultMobileExtensionJson() {
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

    private static void registerDevice(String baseUrl, String token, String deviceId, String displayName) throws Exception {
        JSONObject payload = new JSONObject()
            .put("deviceId", deviceId)
            .put("platform", "android")
            .put("displayName", displayName)
            .put("capabilities", new JSONObject()
                .put("shareText", true)
                .put("sendToDesktop", true)
                .put("floatingWheel", true)
                .put("mobileExtension", true)
                .put("accessibilityEnabled", MobileAccessibilityService.isEnabled()));
        postJson(baseUrl, "/v1/me/devices", payload, token);
    }

    private static String postMessage(String baseUrl, String token, String sourceDeviceId, String text) throws Exception {
        JSONObject payload = new JSONObject()
            .put("sourceDeviceId", sourceDeviceId)
            .put("targetPlatform", "desktop")
            .put("kind", "text")
            .put("title", "手机轮盘发来消息")
            .put("text", text)
            .put("payload", new JSONObject()
                .put("source", "android-floating-wheel")
                .put("sourceDeviceName", buildDeviceDisplayName())
                .put("createdAt", System.currentTimeMillis()));
        return postJson(baseUrl, "/v1/me/mobile/messages", payload, token).optString("messageId", "unknown");
    }

    private static String postScreenshotWebDavMessage(String baseUrl, String token, String sourceDeviceId, String webDavPath, int bytes, int width, int height) throws Exception {
        JSONObject payload = new JSONObject()
            .put("sourceDeviceId", sourceDeviceId)
            .put("targetPlatform", "desktop")
            .put("kind", "screenshot")
            .put("title", "手机截图")
            .put("text", "手机截图：" + width + "x" + height)
            .put("payload", new JSONObject()
                .put("source", "android-floating-wheel")
                .put("sourceDeviceName", buildDeviceDisplayName())
                .put("screenshotMime", "image/jpeg")
                .put("screenshotWidth", width)
                .put("screenshotHeight", height)
                .put("screenshotBytes", bytes)
                .put("webDavPath", webDavPath)
                .put("expiresAt", System.currentTimeMillis() + 30L * 24L * 60L * 60L * 1000L)
                .put("createdAt", System.currentTimeMillis()));
        return postJson(baseUrl, "/v1/me/mobile/messages", payload, token).optString("messageId", "unknown");
    }

    private static WebDavConfig fetchWebDavConfig(String baseUrl, String token) throws Exception {
        JSONObject json = getJson(baseUrl, "/v1/sync/webdav-config", token);
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

    private static String uploadScreenshotToWebDav(WebDavConfig config, byte[] bytes) throws Exception {
        String day = new SimpleDateFormat("yyyyMMdd", Locale.ROOT).format(new Date());
        String fileName = "mobile-screenshot-" + day + "-" + UUID.randomUUID().toString().replace("-", "") + ".jpg";
        cleanupExpiredWebDavTempFiles(config);
        String path = fileName;
        putWebDavBytes(config, path, bytes, "image/jpeg");
        upsertWebDavTempIndex(config, path, bytes.length);
        return path;
    }

    private static void cleanupExpiredWebDavTempFiles(WebDavConfig config) {
        try {
            JSONObject index = readWebDavJson(config, "mobile-screenshots-index.json");
            long now = System.currentTimeMillis();
            org.json.JSONArray items = index.optJSONArray("items");
            org.json.JSONArray kept = new org.json.JSONArray();
            if (items != null) {
                for (int i = 0; i < items.length(); i++) {
                    JSONObject item = items.optJSONObject(i);
                    if (item == null) {
                        continue;
                    }
                    String path = item.optString("path", "");
                    long expiresAt = item.optLong("expiresAt", 0);
                    if (expiresAt > 0 && expiresAt < now) {
                        deleteWebDav(config, path);
                    } else {
                        kept.put(item);
                    }
                }
            }
            index.put("items", kept);
            putWebDavBytes(config, "mobile-screenshots-index.json", index.toString().getBytes(StandardCharsets.UTF_8), "application/json");
        } catch (Exception ignored) {
        }
    }

    private static void upsertWebDavTempIndex(WebDavConfig config, String path, int bytes) {
        try {
            JSONObject index = readWebDavJson(config, "mobile-screenshots-index.json");
            org.json.JSONArray items = index.optJSONArray("items");
            if (items == null) {
                items = new org.json.JSONArray();
            }
            items.put(new JSONObject()
                .put("path", path)
                .put("bytes", bytes)
                .put("createdAt", System.currentTimeMillis())
                .put("expiresAt", System.currentTimeMillis() + 30L * 24L * 60L * 60L * 1000L));
            index.put("items", items);
            putWebDavBytes(config, "mobile-screenshots-index.json", index.toString().getBytes(StandardCharsets.UTF_8), "application/json");
        } catch (Exception ignored) {
        }
    }

    private static JSONObject readWebDavJson(WebDavConfig config, String path) {
        try {
            String text = new String(getWebDavBytes(config, path), StandardCharsets.UTF_8);
            return new JSONObject(text);
        } catch (Exception ignored) {
            return new JSONObject();
        }
    }

    private static JSONObject postJson(String baseUrl, String path, JSONObject payload, String token) throws Exception {
        URL url = new URL(baseUrl + path);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setRequestMethod("POST");
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
        if (token != null && !token.trim().isEmpty()) {
            connection.setRequestProperty("Authorization", "Bearer " + token);
        }
        try (OutputStreamWriter writer = new OutputStreamWriter(connection.getOutputStream(), StandardCharsets.UTF_8)) {
            writer.write(payload.toString());
        }
        int status = connection.getResponseCode();
        String body = readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("HTTP " + status + ": " + body);
        }
        return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
    }

    private static JSONObject getJson(String baseUrl, String path, String token) throws Exception {
        URL url = new URL(baseUrl + path);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setRequestMethod("GET");
        connection.setConnectTimeout(15000);
        connection.setReadTimeout(15000);
        connection.setRequestProperty("Accept", "application/json");
        if (token != null && !token.trim().isEmpty()) {
            connection.setRequestProperty("Authorization", "Bearer " + token);
        }
        int status = connection.getResponseCode();
        String body = readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("HTTP " + status + ": " + body);
        }
        return body.trim().isEmpty() ? new JSONObject() : new JSONObject(body);
    }

    private static void ensureWebDavCollection(WebDavConfig config, String path) throws Exception {
        HttpURLConnection connection = openWebDav(config, path);
        connection.setRequestMethod("MKCOL");
        int status = connection.getResponseCode();
        readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status >= 200 && status < 300 || status == 405) {
            return;
        }
        if (status == 409 && path.contains("/")) {
            String parent = path.substring(0, path.lastIndexOf('/'));
            ensureWebDavCollection(config, parent);
            ensureWebDavCollection(config, path);
            return;
        }
        throw new IllegalStateException("WebDAV MKCOL failed " + status + ": " + path);
    }

    private static void putWebDavBytes(WebDavConfig config, String path, byte[] bytes, String contentType) throws Exception {
        HttpURLConnection connection = openWebDav(config, path);
        connection.setRequestMethod("PUT");
        connection.setConnectTimeout(20000);
        connection.setReadTimeout(30000);
        connection.setDoOutput(true);
        connection.setRequestProperty("Content-Type", contentType);
        connection.setRequestProperty("Content-Length", String.valueOf(bytes.length));
        connection.getOutputStream().write(bytes);
        int status = connection.getResponseCode();
        String body = readBody(status >= 400 ? connection.getErrorStream() : connection.getInputStream());
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("WebDAV PUT failed " + status + ": " + body);
        }
    }

    private static byte[] getWebDavBytes(WebDavConfig config, String path) throws Exception {
        HttpURLConnection connection = openWebDav(config, path);
        connection.setRequestMethod("GET");
        int status = connection.getResponseCode();
        if (status < 200 || status >= 300) {
            throw new IllegalStateException("WebDAV GET failed " + status);
        }
        InputStream stream = connection.getInputStream();
        java.io.ByteArrayOutputStream buffer = new java.io.ByteArrayOutputStream();
        byte[] data = new byte[8192];
        int read;
        while ((read = stream.read(data)) >= 0) {
            buffer.write(data, 0, read);
        }
        return buffer.toByteArray();
    }

    private static void deleteWebDav(WebDavConfig config, String path) {
        try {
            HttpURLConnection connection = openWebDav(config, path);
            connection.setRequestMethod("DELETE");
            connection.getResponseCode();
        } catch (Exception ignored) {
        }
    }

    private static HttpURLConnection openWebDav(WebDavConfig config, String path) throws Exception {
        URL url = new URL(buildWebDavUrl(config, path));
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        String auth = Base64.getEncoder().encodeToString((config.username + ":" + config.password).getBytes(StandardCharsets.UTF_8));
        connection.setRequestProperty("Authorization", "Basic " + auth);
        connection.setRequestProperty("Accept", "*/*");
        return connection;
    }

    private static String buildWebDavUrl(WebDavConfig config, String path) throws Exception {
        String base = config.serverUrl == null || config.serverUrl.trim().isEmpty()
            ? "https://dav.jianguoyun.com/dav/"
            : config.serverUrl.trim();
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        String root = config.rootPath == null || config.rootPath.trim().isEmpty() ? "yanzi" : config.rootPath.trim();
        root = trimSlashes(root);
        String relative = trimSlashes(path);
        String full = root.isEmpty() ? relative : (relative.isEmpty() ? root : root + "/" + relative);
        String[] parts = full.split("/");
        StringBuilder encoded = new StringBuilder(base);
        for (String part : parts) {
            if (!part.isEmpty()) {
                encoded.append('/').append(java.net.URLEncoder.encode(part, "UTF-8").replace("+", "%20"));
            }
        }
        return encoded.toString();
    }

    private static String trimSlashes(String value) {
        String result = value == null ? "" : value.trim();
        while (result.startsWith("/")) {
            result = result.substring(1);
        }
        while (result.endsWith("/")) {
            result = result.substring(0, result.length() - 1);
        }
        return result;
    }

    private static String readBody(InputStream stream) throws Exception {
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

    private static final class WebDavConfig {
        String serverUrl;
        String rootPath;
        String username;
        String password;
    }
}
