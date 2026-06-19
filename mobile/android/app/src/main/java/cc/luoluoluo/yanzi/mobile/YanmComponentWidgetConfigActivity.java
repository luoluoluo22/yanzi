package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.appwidget.AppWidgetManager;
import android.content.Intent;
import android.os.Bundle;
import android.view.View;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;

import cc.luoluoluo.yanzi.mobile.widget.YanmComponentWidgetProvider;
import cc.luoluoluo.yanzi.mobile.widget.YanmWidgetData;

public final class YanmComponentWidgetConfigActivity extends Activity {
    private int appWidgetId = AppWidgetManager.INVALID_APPWIDGET_ID;
    private final List<YanmWidgetData.ComponentInfo> components = new ArrayList<>();
    private int selectedIndex = -1;
    private EditText stateKeyInput;
    private TextView statusText;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setResult(RESULT_CANCELED);
        setContentView(R.layout.activity_yanm_component_widget_config);

        Intent intent = getIntent();
        Bundle extras = intent == null ? null : intent.getExtras();
        if (extras != null) {
            appWidgetId = extras.getInt(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID);
        }
        if (appWidgetId == AppWidgetManager.INVALID_APPWIDGET_ID) {
            finish();
            return;
        }

        statusText = findViewById(R.id.yanm_widget_config_status);
        stateKeyInput = findViewById(R.id.yanm_widget_state_key);
        Button confirm = findViewById(R.id.yanm_widget_confirm);
        ListView list = findViewById(R.id.yanm_widget_component_list);

        components.clear();
        components.addAll(YanmWidgetData.readComponents(this));
        List<String> labels = new ArrayList<>();
        for (YanmWidgetData.ComponentInfo component : components) {
            labels.add(component.title + "\n" + component.id);
        }

        ArrayAdapter<String> adapter = new ArrayAdapter<>(this, android.R.layout.simple_list_item_single_choice, labels);
        list.setChoiceMode(ListView.CHOICE_MODE_SINGLE);
        list.setAdapter(adapter);
        if (components.isEmpty()) {
            statusText.setText("暂无燕幕缓存。请先打开燕子 App 刷新燕幕。");
            confirm.setEnabled(false);
        } else {
            selectedIndex = 0;
            list.setItemChecked(0, true);
            stateKeyInput.setText(components.get(0).stateKey);
        }

        list.setOnItemClickListener((parent, view, position, id) -> {
            selectedIndex = position;
            stateKeyInput.setText(components.get(position).stateKey);
        });

        confirm.setOnClickListener(v -> saveSelection());
    }

    private void saveSelection() {
        if (selectedIndex < 0 || selectedIndex >= components.size()) {
            Toast.makeText(this, "请先选择一个燕幕组件。", Toast.LENGTH_SHORT).show();
            return;
        }

        YanmWidgetData.ComponentInfo component = components.get(selectedIndex);
        String stateKey = stateKeyInput.getText() == null ? "" : stateKeyInput.getText().toString().trim();
        if (stateKey.isEmpty()) {
            stateKey = "note";
        }
        YanmWidgetData.saveComponentWidget(this, appWidgetId, component.id, stateKey);

        AppWidgetManager manager = AppWidgetManager.getInstance(this);
        YanmComponentWidgetProvider.updateWidget(this, manager, appWidgetId);

        Intent result = new Intent();
        result.putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId);
        setResult(RESULT_OK, result);
        finish();
    }
}
