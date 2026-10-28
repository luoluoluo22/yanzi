package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.SharedPreferences;
import android.text.InputFilter;
import android.widget.EditText;
import android.widget.Toast;
import org.json.JSONArray;
import org.json.JSONObject;

/** Account labels use the shared device ID; offline targets need no command delivery. */
final class DeviceRemarksDialog {
    private static boolean current(Activity activity, String account) {
        return !activity.isFinishing() && !activity.isDestroyed()
            && account.equals(ChatHistoryStore.accountKey(activity));
    }

    static void show(Activity activity) {
        SharedPreferences prefs = activity.getSharedPreferences("yanzi-mobile", 0);
        String token = prefs.getString("token", "");
        String account = ChatHistoryStore.accountKey(activity);
        String base = prefs.getString("baseUrl", "https://sync.luoluoluo.cc.cd");
        if (token.isEmpty()) { Toast.makeText(activity, "请先登录燕子账号", Toast.LENGTH_SHORT).show(); return; }
        Toast.makeText(activity, "正在读取设备…", Toast.LENGTH_SHORT).show();
        new Thread(() -> {
            try {
                JSONArray devices = MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices", token, "GET", null).optJSONArray("items");
                activity.runOnUiThread(() -> {
                    if (!current(activity, account)) return;
                    if (devices == null || devices.length() == 0) { Toast.makeText(activity, "账号下暂无设备", Toast.LENGTH_SHORT).show(); return; }
                    String[] labels = new String[devices.length()];
                    for (int i = 0; i < devices.length(); i++) {
                        JSONObject device = devices.optJSONObject(i);
                        labels[i] = device.optString("displayName", device.optString("deviceId"))
                            + (device.optBoolean("online") ? " · 在线" : " · 离线")
                            + "\n" + device.optString("modelName", device.optString("displayName"))
                            + " · " + device.optString("deviceId");
                    }
                    new AlertDialog.Builder(activity).setTitle("设备备注 · 同账号共享")
                        .setItems(labels, (dialog, which) -> edit(activity, account, base, token, devices.optJSONObject(which)))
                        .setNegativeButton("关闭", null).show();
                });
            } catch (Exception error) {
                activity.runOnUiThread(() -> { if (current(activity, account)) Toast.makeText(activity, "设备读取失败，请重试", Toast.LENGTH_LONG).show(); });
            }
        }, "device-remarks-read").start();
    }

    private static void edit(Activity activity, String account, String base, String token, JSONObject device) {
        if (!current(activity, account)) return;
        EditText input = new EditText(activity);
        input.setSingleLine(true);
        input.setHint("最多 40 个字符，留空恢复型号名称");
        input.setFilters(new InputFilter[] { new InputFilter.LengthFilter(80) });
        input.setText(device.optString("remark", ""));
        AlertDialog dialog = new AlertDialog.Builder(activity).setTitle("修改设备备注")
            .setMessage(device.optString("modelName", device.optString("displayName")))
            .setView(input).setPositiveButton("保存", null).setNegativeButton("取消", null).create();
        dialog.setOnShowListener(ignored -> dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view -> {
            String remark = input.getText().toString().trim();
            if (remark.codePointCount(0, remark.length()) > 40 || remark.matches("(?s).*[\\x00-\\x1f\\x7f].*")) {
                input.setError("请输入最多 40 个字符，不含控制字符"); return;
            }
            if (!current(activity, account)) { dialog.dismiss(); return; }
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);
            dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(false);
            dialog.setCancelable(false);
            new Thread(() -> {
                try {
                    if (!current(activity, account)) return;
                    MobileMessageClient.requestWithoutQueue(base, "/v1/me/devices/" + android.net.Uri.encode(device.optString("deviceId")) + "/remark",
                        token, "PUT", new JSONObject().put("remark", remark));
                    activity.runOnUiThread(() -> {
                        dialog.dismiss();
                        if (current(activity, account)) { Toast.makeText(activity, "备注已同步", Toast.LENGTH_SHORT).show(); show(activity); }
                    });
                } catch (Exception error) {
                    activity.runOnUiThread(() -> {
                        if (!current(activity, account)) { dialog.dismiss(); return; }
                        dialog.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(true);
                        dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(true);
                        dialog.setCancelable(true);
                        input.setError("保存未确认，请刷新设备列表核对后再试");
                    });
                }
            }, "device-remarks-write").start();
        }));
        dialog.show();
    }
}
