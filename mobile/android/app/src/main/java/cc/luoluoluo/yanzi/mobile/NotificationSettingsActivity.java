package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Intent;
import android.media.AudioManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

/** Displays actual system preferences instead of keeping competing app switches. */
public final class NotificationSettingsActivity extends Activity {
    private TextView status;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        MobileNotificationManager.ensureChannels(this);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(20), dp(20), dp(24));
        root.setBackgroundColor(YanziUiKit.BG);
        TextView title = label("消息通知", 24);
        root.addView(title);
        status = label("", 15);
        status.setPadding(0, dp(20), 0, dp(20));
        root.addView(status);
        row(root, "聊天提醒设置", "选择提示音、振动、横幅和锁屏显示", () -> openSystem(MobileNotificationManager.CHANNEL_CHAT));
        row(root, "全部通知设置", "管理通知权限、同步提醒和实时连接", () -> openSystem(null));
        row(root, "手机声音设置", "调整响铃模式与通知音量", () -> launch(new Intent(Settings.ACTION_SOUND_SETTINGS)));
        row(root, "发送测试提醒", "发送一条本机提醒，检查声音、振动和横幅", () -> {
            try {
                boolean posted = MobileEventNotifier.notifyChatMessage(this,
                        "notification-test-" + java.util.UUID.randomUUID(), "燕子 · 测试提醒", "消息提醒已开启");
                Toast.makeText(this, posted ? "已发送，请查看通知提醒" : "通知被关闭，请打开系统通知设置", Toast.LENGTH_LONG).show();
            } catch (Exception error) { Toast.makeText(this, "提醒发送失败，请检查系统通知设置", Toast.LENGTH_LONG).show(); }
            refresh();
        });
        TextView hint = label("声音和振动遵循手机的响铃、静音与勿扰设置。横幅、锁屏显示还受系统通知设置控制。关闭提醒后，聊天消息仍会保存。", 14);
        hint.setPadding(0, dp(16), 0, dp(16));
        root.addView(hint);
        row(root, "返回", "返回燕子", this::finish);
        ScrollView scroll = new ScrollView(this);
        scroll.addView(root);
        setContentView(scroll);
    }
    private int dp(int value) { return (int)(getResources().getDisplayMetrics().density * value + .5f); }
    private TextView label(String value, int size) {
        TextView text = new TextView(this); text.setText(value); text.setTextSize(size);
        text.setTextColor(YanziUiKit.TEXT); return text;
    }
    private void row(LinearLayout root, String title, String subtitle, Runnable click) {
        root.addView(YanziUiKit.row(this, "bell-outline", YanziUiKit.ORANGE, title, subtitle, click), YanziUiKit.cardLp(this));
    }
    @Override protected void onResume() { super.onResume(); if (status != null) refresh(); }
    private void refresh() {
        NotificationManager manager = getSystemService(NotificationManager.class);
        NotificationChannel chat = manager == null ? null : manager.getNotificationChannel(MobileNotificationManager.CHANNEL_CHAT);
        AudioManager audio = getSystemService(AudioManager.class);
        int mode = audio == null ? -1 : audio.getRingerMode();
        String modeName = mode == AudioManager.RINGER_MODE_NORMAL ? "响铃" :
                mode == AudioManager.RINGER_MODE_VIBRATE ? "振动（不播放提示音）" : mode == AudioManager.RINGER_MODE_SILENT ? "静音" : "未知";
        boolean enabled = MobileEventNotifier.canNotifyChat(this);
        status.setText("聊天通知：" + (enabled ? "已开启" : "已关闭")
                + "\n手机模式：" + modeName
                + "\n提示音：" + (chat != null && chat.getSound() != null ? "已设置" : "关闭")
                + "\n振动：" + (chat != null && chat.shouldVibrate() ? "已开启" : "关闭")
                + "\n横幅级别：" + (chat != null && chat.getImportance() >= NotificationManager.IMPORTANCE_HIGH ? "已开启，显示由系统决定" : "未开启")
                + "\n通知音量：" + (audio == null ? "未知" : audio.getStreamVolume(AudioManager.STREAM_NOTIFICATION) + "/" + audio.getStreamMaxVolume(AudioManager.STREAM_NOTIFICATION))
                + "\n勿扰模式：" + (manager != null && manager.getCurrentInterruptionFilter() == NotificationManager.INTERRUPTION_FILTER_ALL ? "关闭" : "已开启或未知"));
    }
    static Intent systemSettingsIntent(android.content.Context context, String channel) {
        Intent intent = new Intent(channel == null ? Settings.ACTION_APP_NOTIFICATION_SETTINGS : Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS);
        intent.putExtra(Settings.EXTRA_APP_PACKAGE, context.getPackageName());
        if (channel != null) intent.putExtra(Settings.EXTRA_CHANNEL_ID, channel);
        return intent;
    }
    private void openSystem(String channel) {
        MobileNotificationManager.ensureChannels(this);
        launch(systemSettingsIntent(this, channel));
    }
    private void launch(Intent intent) {
        try { startActivity(intent); }
        catch (android.content.ActivityNotFoundException unavailable) {
            try { startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName()))); }
            catch (android.content.ActivityNotFoundException ignored) { Toast.makeText(this, "请在手机设置中打开燕子通知", Toast.LENGTH_LONG).show(); }
        }
    }
}
