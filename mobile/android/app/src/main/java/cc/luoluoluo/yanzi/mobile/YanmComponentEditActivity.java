package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.appwidget.AppWidgetManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONObject;

import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import cc.luoluoluo.yanzi.mobile.widget.YanmComponentWidgetProvider;
import cc.luoluoluo.yanzi.mobile.widget.YanmWidgetData;
import cc.luoluoluo.yanzi.mobile.widget.YanmWidgetProvider;

public final class YanmComponentEditActivity extends Activity {
    private static final String DEFAULT_BASE_URL = "https://sync.luoluoluo.cc.cd";

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private SharedPreferences prefs;
    private int appWidgetId = AppWidgetManager.INVALID_APPWIDGET_ID;
    private String componentId = "";
    private String stateKey = "note";
    private EditText contentInput;
    private TextView titleText;
    private TextView metaText;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        prefs = getSharedPreferences(YanmWidgetData.PREFS_NAME, Context.MODE_PRIVATE);
        setContentView(R.layout.activity_yanm_component_edit);

        titleText = findViewById(R.id.yanm_component_edit_title);
        metaText = findViewById(R.id.yanm_component_edit_meta);
        contentInput = findViewById(R.id.yanm_component_edit_content);
        Button saveButton = findViewById(R.id.yanm_component_edit_save);

        readIntent(getIntent());
        bindComponent();
        saveButton.setOnClickListener(v -> saveAndSync());

        contentInput.requestFocus();
        mainHandler.postDelayed(() -> {
            InputMethodManager manager = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
            if (manager != null) {
                manager.showSoftInput(contentInput, InputMethodManager.SHOW_IMPLICIT);
            }
        }, 180);
    }

    private void readIntent(Intent intent) {
        if (intent == null) {
            return;
        }
        appWidgetId = intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID);
        componentId = firstNonEmpty(intent.getStringExtra("component_id"), "");
        stateKey = firstNonEmpty(intent.getStringExtra("state_key"), "note");
        if (componentId.isEmpty() && appWidgetId != AppWidgetManager.INVALID_APPWIDGET_ID) {
            componentId = YanmWidgetData.getWidgetComponentId(this, appWidgetId);
        }
        if (appWidgetId != AppWidgetManager.INVALID_APPWIDGET_ID) {
            stateKey = firstNonEmpty(YanmWidgetData.getWidgetStateKey(this, appWidgetId), stateKey, "note");
        }
    }

    private void bindComponent() {
        YanmWidgetData.ComponentInfo component = YanmWidgetData.findComponent(this, componentId);
        if (component == null) {
            titleText.setText("编辑燕幕组件");
            metaText.setText("组件不存在或燕幕缓存为空");
            contentInput.setText("");
            return;
        }

        titleText.setText(component.title);
        metaText.setText("key: " + stateKey);
        JSONObject state = YanmWidgetData.readComponentState(this);
        contentInput.setText(state.optString(stateKey, ""));
        contentInput.setSelection(contentInput.getText().length());
    }

    private void saveAndSync() {
        String value = contentInput.getText() == null ? "" : contentInput.getText().toString();
        JSONObject yanm = YanmWidgetData.readYanm(this);
        if (yanm == null) {
            Toast.makeText(this, "没有燕幕缓存，请先刷新燕幕。", Toast.LENGTH_SHORT).show();
            return;
        }

        try {
            JSONObject state = YanmWidgetData.firstObject(yanm, "componentState", "ComponentState");
            if (state == null) {
                state = new JSONObject();
            }
            state.put(stateKey, value);
            yanm.put("componentState", state);
            prefs.edit().putString(YanmWidgetData.CACHE_YANM, yanm.toString()).apply();
            refreshYanmWidgets();
            Toast.makeText(this, "已保存，正在同步。", Toast.LENGTH_SHORT).show();
            syncYanmStateAsync(yanm);
        } catch (Exception ex) {
            Toast.makeText(this, "保存失败：" + ex.getMessage(), Toast.LENGTH_SHORT).show();
        }
    }

    private void syncYanmStateAsync(JSONObject yanm) {
        executor.execute(() -> {
            try {
                String baseUrl = normalizedBaseUrl();
                String token = requireToken(baseUrl);
                try {
                    MainActivity.YanziApiClient.putYanmState(baseUrl, token, yanm);
                } catch (Exception ex) {
                    if (!isUnauthorized(ex)) {
                        throw ex;
                    }
                    token = refreshToken(baseUrl);
                    MainActivity.YanziApiClient.putYanmState(baseUrl, token, yanm);
                }
                mainHandler.post(() -> {
                    Toast.makeText(this, "燕幕已同步。", Toast.LENGTH_SHORT).show();
                    finish();
                });
            } catch (Exception ex) {
                mainHandler.post(() -> Toast.makeText(this, "同步失败：" + ex.getMessage(), Toast.LENGTH_LONG).show());
            }
        });
    }

    private void refreshYanmWidgets() {
        YanmWidgetData.refreshComponentWidgets(this);

        AppWidgetManager manager = AppWidgetManager.getInstance(this);
        int[] listIds = manager.getAppWidgetIds(new ComponentName(this, YanmWidgetProvider.class));
        if (listIds.length > 0) {
            Intent intent = new Intent(this, YanmWidgetProvider.class);
            intent.setAction(AppWidgetManager.ACTION_APPWIDGET_UPDATE);
            intent.putExtra(AppWidgetManager.EXTRA_APPWIDGET_IDS, listIds);
            sendBroadcast(intent);
        }
    }

    private String requireToken(String baseUrl) throws Exception {
        String token = prefs.getString("token", "");
        if (token != null && !token.trim().isEmpty()) {
            return token.trim();
        }
        return refreshToken(baseUrl);
    }

    private String refreshToken(String baseUrl) throws Exception {
        String email = prefs.getString("email", "");
        String password = prefs.getString("password", "");
        if (email == null || email.trim().isEmpty() || password == null || password.isEmpty()) {
            throw new IllegalStateException("未登录，无法同步燕幕。");
        }
        String token = MainActivity.YanziApiClient.login(baseUrl, email.trim(), password);
        prefs.edit().putString("baseUrl", baseUrl).putString("token", token).apply();
        return token;
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

    private static boolean isUnauthorized(Exception ex) {
        String message = ex.getMessage();
        if (message == null) {
            return false;
        }
        String lower = message.toLowerCase(Locale.ROOT);
        return message.contains("401") || lower.contains("token expired") || lower.contains("unauthorized");
    }

    private static String firstNonEmpty(String... values) {
        for (String value : values) {
            if (value != null && !value.trim().isEmpty()) {
                return value.trim();
            }
        }
        return "";
    }

    @Override
    protected void onDestroy() {
        executor.shutdownNow();
        super.onDestroy();
    }
}
