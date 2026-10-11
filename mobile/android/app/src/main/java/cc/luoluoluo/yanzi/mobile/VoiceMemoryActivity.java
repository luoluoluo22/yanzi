package cc.luoluoluo.yanzi.mobile;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import java.io.FileInputStream;
import java.io.OutputStream;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.media.MediaPlayer;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.io.File;
import java.util.Locale;

/** Native, device-local recording screen. Never starts recording without explicit interaction. */
public final class VoiceMemoryActivity extends Activity {
    private static final int AUDIO_PERMISSION_REQUEST = 5201;
    private static final int EXPORT_REQUEST = 5202;
    private File pendingExport;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private TextView status;
    private LinearLayout list;
    private Button startButton;
    private Button stopButton;
    private RadioGroup formatGroup;
    private TextView archiveSummary;
    private MediaPlayer player;
    private File playing;
    private long lastArchiveRefresh;
    private int qaSeconds;
    private final Runnable refreshTask = new Runnable() {
        @Override public void run() {
            refreshStatus();
            if (SystemClock.elapsedRealtime() - lastArchiveRefresh > 3000) {
                reloadArchive();
                lastArchiveRefresh = SystemClock.elapsedRealtime();
            }
            handler.postDelayed(this, 1000);
        }
    };
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(Color.rgb(16, 20, 30));
        getWindow().setNavigationBarColor(Color.rgb(16, 20, 30));
        if (!VoiceMemoryService.isRecording()) VoiceMemoryStore.recoverInterruptedSegments(this);
        qaSeconds = BuildConfig.DEBUG ? getIntent().getIntExtra(VoiceMemoryService.EXTRA_QA_SECONDS, 0) : 0;
        buildScreen();
    }

    private int dp(int size) { return (int) (size * getResources().getDisplayMetrics().density + .5f); }
    private TextView label(String text, int sp, int color) {
        TextView view = new TextView(this);
        view.setText(text);
        view.setTextSize(sp);
        view.setTextColor(color);
        return view;
    }
    private LinearLayout.LayoutParams spacing(int height) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, height == -2 ? -2 : dp(height));
        lp.setMargins(0, 0, 0, dp(12));
        return lp;
    }
    private Button button(String text) {
        Button b = new Button(this);
        b.setAllCaps(false);
        b.setText(text);
        b.setTextColor(Color.WHITE);
        b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(Color.rgb(38, 88, 162)));
        return b;
    }
    private void buildScreen() {
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(Color.rgb(16, 20, 30));
        LinearLayout column = new LinearLayout(this);
        column.setOrientation(LinearLayout.VERTICAL);
        column.setPadding(dp(18), dp(22), dp(18), dp(20));
        scroll.addView(column);

        TextView heading = label("声音记忆", 27, Color.WHITE);
        heading.setTypeface(null, 1);
        column.addView(heading, spacing(-2));
        column.addView(label("本机保存 · 不上传云端 · 明确启动后才录音", 13, 0xffaab8cb), spacing(-2));
        status = label("", 16, 0xffd9e5f5);
        status.setPadding(dp(12), dp(16), dp(12), dp(16));
        status.setBackgroundColor(0xff263045);
        column.addView(status, spacing(-2));

        LinearLayout controls = new LinearLayout(this);
        controls.setOrientation(LinearLayout.HORIZONTAL);
        startButton = button("开始录音");
        stopButton = button("停止录音");
        controls.addView(startButton, new LinearLayout.LayoutParams(0, dp(55), 1f));
        controls.addView(stopButton, new LinearLayout.LayoutParams(0, dp(55), 1f));
        column.addView(controls, spacing(-2));
        startButton.setOnClickListener(v -> startCapture());
        stopButton.setOnClickListener(v -> {
            startService(new Intent(this, VoiceMemoryService.class).setAction(VoiceMemoryService.STOP));
            status.setText("正在停止并保存最后一段……");
            handler.postDelayed(this::reloadArchive, 1500);
        });

        column.addView(label("默认每 15 分钟保存一个音频文件；可在锁屏时继续，通知栏可以停止。", 13, 0xffaab8cb), spacing(-2));
        column.addView(label("录制格式（开始前选择）", 16, Color.WHITE), spacing(-2));
        formatGroup = new RadioGroup(this);
        formatGroup.setOrientation(RadioGroup.VERTICAL);
        RadioButton aac = new RadioButton(this);
        aac.setId(View.generateViewId());
        aac.setText("省空间 · AAC 32 kbps（推荐）");
        aac.setTextColor(Color.WHITE);
        RadioButton wav = new RadioButton(this);
        wav.setId(View.generateViewId());
        wav.setText("无损原始 WAV（兼容回退）");
        wav.setTextColor(Color.WHITE);
        formatGroup.addView(aac);
        formatGroup.addView(wav);
        String currentFormat = getSharedPreferences("voice-memory-state", MODE_PRIVATE).getString("format", "aac");
        formatGroup.check("wav".equals(currentFormat) ? wav.getId() : aac.getId());
        formatGroup.setOnCheckedChangeListener((group, id) -> {
            if (VoiceMemoryService.isRecording()) return;
            getSharedPreferences("voice-memory-state", MODE_PRIVATE).edit()
                    .putString("format", id == aac.getId() ? "aac" : "wav").apply();
        });
        column.addView(formatGroup, spacing(-2));
        if (BuildConfig.DEBUG) {
            CheckBox qa = new CheckBox(this);
            qa.setText("开发验收：每 8 秒分段");
            qa.setTextColor(0xfff5c777);
            qa.setChecked(qaSeconds == 8);
            qa.setOnCheckedChangeListener((view, checked) -> qaSeconds = checked ? 8 : 0);
            column.addView(qa, spacing(-2));
        }
        TextView archiveTitle = label("本地录音", 20, Color.WHITE);
        archiveTitle.setTypeface(null, 1);
        column.addView(archiveTitle, spacing(-2));
        archiveSummary = label("", 13, 0xffaab8cb);
        column.addView(archiveSummary, spacing(-2));
        Button refresh = button("刷新列表");
        refresh.setOnClickListener(v -> reloadArchive());
        column.addView(refresh, spacing(-2));
        list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        column.addView(list);
        setContentView(scroll);
        reloadArchive();
        refreshStatus();
    }
    private void startCapture() {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, AUDIO_PERMISSION_REQUEST);
            return;
        }
        if (VoiceMemoryService.isRecording()) return;
        Intent intent = new Intent(this, VoiceMemoryService.class).setAction(VoiceMemoryService.START);
        if (BuildConfig.DEBUG && qaSeconds > 0) intent.putExtra(VoiceMemoryService.EXTRA_QA_SECONDS, qaSeconds);
        try {
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent);
            else startService(intent);
            status.setText("正在启动麦克风……");
        } catch (RuntimeException error) {
            status.setText("启动失败：" + error.getClass().getSimpleName());
        }
    }
    @Override public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == AUDIO_PERMISSION_REQUEST && grantResults.length > 0
                && grantResults[0] == PackageManager.PERMISSION_GRANTED) startCapture();
        else if (requestCode == AUDIO_PERMISSION_REQUEST) status.setText("请先授予麦克风权限");
    }
    private void refreshStatus() {
        boolean active = VoiceMemoryService.isRecording();
        startButton.setEnabled(!active);
        stopButton.setEnabled(active);
        if (formatGroup != null) {
            for (int i = 0; i < formatGroup.getChildCount(); i++)
                formatGroup.getChildAt(i).setEnabled(!active);
        }
        String error = getSharedPreferences("voice-memory-state", MODE_PRIVATE).getString("last_error", "");
        long started = getSharedPreferences("voice-memory-state", MODE_PRIVATE).getLong("started_at", 0);
        String elapsed = active && started > 0
                ? String.format(Locale.ROOT, " · 已录 %d 分 %02d 秒",
                        Math.max(0, (System.currentTimeMillis() - started) / 60000),
                        Math.max(0, (System.currentTimeMillis() - started) / 1000) % 60) : "";
        status.setText(active ? "● 正在录音" + elapsed + " · 可锁屏继续" :
                !error.isEmpty() ? "未录音 · " + error : "○ 未录音 · 等待手动启动");
    }
    private void reloadArchive() {
        if (list == null) return;
        list.removeAllViews();
        File[] files = VoiceMemoryStore.recordings(this);
        long totalBytes = 0;
        long totalTime = 0;
        for (File file : files) {
            totalBytes += file.length();
            totalTime += VoiceMemoryStore.durationMs(file);
        }
        if (archiveSummary != null)
            archiveSummary.setText(String.format(Locale.ROOT,
                    "%d 段 · 约 %d 分 %02d 秒 · %.1f MB · 仅保存在本机",
                    files.length, totalTime / 60000, (totalTime / 1000) % 60, totalBytes / 1048576.0));
        if (files.length == 0) {
            list.addView(label("暂无录音，文件仅保存在当前手机。", 14, 0xffaab8cb), spacing(-2));
            return;
        }
        for (int i = 0; i < Math.min(files.length, 80); i++) {
            File file = files[i];
            LinearLayout row = new LinearLayout(this);
            row.setOrientation(LinearLayout.HORIZONTAL);
            row.setGravity(Gravity.CENTER_VERTICAL);
            TextView name = label(file.getName() + "\n" + VoiceMemoryStore.durationText(file)
                    + " · " + String.format(Locale.ROOT, "%.1f MB", file.length() / 1048576.0), 14, Color.WHITE);
            name.setPadding(dp(10), dp(10), 0, dp(10));
            row.addView(name, new LinearLayout.LayoutParams(0, -2, 1f));
            Button play = button(playing != null && playing.equals(file) ? "停止" : "播放");
            play.setContentDescription("播放 " + file.getName());
            play.setOnClickListener(v -> playOrStop(file));
            row.addView(play, new LinearLayout.LayoutParams(dp(70), dp(48)));
            Button export = button("导出");
            export.setContentDescription("导出 " + file.getName());
            export.setOnClickListener(v -> beginExport(file));
            row.addView(export, new LinearLayout.LayoutParams(dp(70), dp(48)));
            row.setBackgroundColor(0xff263045);
            list.addView(row, spacing(-2));
        }
    }
    private void playOrStop(File file) {
        if (playing != null && playing.equals(file)) { stopPlayback(); reloadArchive(); return; }
        stopPlayback();
        try {
            MediaPlayer media = new MediaPlayer();
            media.setDataSource(file.getAbsolutePath());
            media.setOnCompletionListener(p -> { stopPlayback(); reloadArchive(); });
            media.prepare();
            media.start();
            player = media;
            playing = file;
        } catch (Exception error) {
            stopPlayback();
            Toast.makeText(this, "播放失败：" + error.getClass().getSimpleName(), Toast.LENGTH_SHORT).show();
        }
        reloadArchive();
    }
    private void beginExport(File file) {
        if (VoiceMemoryService.isRecording()) {
            Toast.makeText(this, "请停止录音后再导出", Toast.LENGTH_SHORT).show();
            return;
        }
        pendingExport = file;
        Intent destination = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        destination.setType(file.getName().endsWith(".aac") ? "audio/aac" : "audio/wav");
        destination.addCategory(Intent.CATEGORY_OPENABLE);
        destination.putExtra(Intent.EXTRA_TITLE, file.getName());
        try { startActivityForResult(destination, EXPORT_REQUEST); }
        catch (Exception e) {
            pendingExport = null;
            Toast.makeText(this, "无法打开文件保存界面", Toast.LENGTH_SHORT).show();
        }
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != EXPORT_REQUEST) return;
        File source = pendingExport;
        pendingExport = null;
        if (resultCode != RESULT_OK || source == null || data == null || data.getData() == null) return;
        Uri destination = data.getData();
        new Thread(() -> {
            try (FileInputStream input = new FileInputStream(source);
                 OutputStream output = getContentResolver().openOutputStream(destination, "w")) {
                if (output == null) throw new java.io.IOException("Destination not writable");
                byte[] bytes = new byte[65536];
                int count;
                while ((count = input.read(bytes)) >= 0) if (count > 0) output.write(bytes, 0, count);
                output.flush();
                runOnUiThread(() -> Toast.makeText(this, "录音已导出", Toast.LENGTH_SHORT).show());
            } catch (Exception error) {
                runOnUiThread(() -> Toast.makeText(this, "导出失败：" + error.getClass().getSimpleName(), Toast.LENGTH_LONG).show());
            }
        }, "VoiceMemoryExport").start();
    }

    private void stopPlayback() {
        if (player != null) { try { player.stop(); } catch (Exception ignored) {} player.release(); player = null; }
        playing = null;
    }
    @Override protected void onResume() {
        super.onResume();
        handler.removeCallbacks(refreshTask);
        handler.post(refreshTask);
    }
    @Override protected void onPause() {
        handler.removeCallbacks(refreshTask);
        super.onPause();
    }
    @Override protected void onDestroy() {
        stopPlayback();
        super.onDestroy();
    }
}
