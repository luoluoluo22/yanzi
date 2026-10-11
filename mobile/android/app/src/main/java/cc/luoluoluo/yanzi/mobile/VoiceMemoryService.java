package cc.luoluoluo.yanzi.mobile;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.content.pm.ServiceInfo;
import android.media.*;
import android.os.*;
import android.util.Log;
import java.io.File;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.util.concurrent.atomic.AtomicBoolean;

/** Foreground microphone service: capture only; AI and VAD are intentionally not on the audio thread. */
public final class VoiceMemoryService extends Service {
    static final String START = "cc.luoluoluo.yanzi.mobile.voice.START";
    static final String STOP = "cc.luoluoluo.yanzi.mobile.voice.STOP";
    static final String EXTRA_QA_SECONDS = "qa_segment_seconds";
    private static final String CHANNEL = "voice_memory_recording";
    private static final int NOTIFICATION_ID = 41013;
    private static final int DEFAULT_SEGMENT_SECONDS = 15 * 60;
    private static final AtomicBoolean RECORDING = new AtomicBoolean(false);
    private volatile AudioRecord recorder;
    private volatile Thread captureThread;
    private volatile boolean stopRequested;
    private PowerManager.WakeLock wakeLock;

    static boolean isRecording() { return RECORDING.get(); }

    @Override public void onCreate() {
        super.onCreate();
        NotificationChannel channel = new NotificationChannel(CHANNEL, "声音记忆录音", NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("显示正在进行的麦克风采集及停止入口");
        ((NotificationManager) getSystemService(NOTIFICATION_SERVICE)).createNotificationChannel(channel);
    }

    private Notification notification() {
        Intent open = new Intent(this, VoiceMemoryActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent openIntent = PendingIntent.getActivity(this, 41013, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Intent stop = new Intent(this, VoiceMemoryService.class).setAction(STOP);
        PendingIntent stopIntent = PendingIntent.getService(this, 41014, stop,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        return new Notification.Builder(this, CHANNEL)
                .setSmallIcon(android.R.drawable.ic_btn_speak_now)
                .setContentTitle("声音记忆正在录制")
                .setContentText("仅保存在此设备 · 点击查看")
                .setContentIntent(openIntent)
                .setOngoing(true)
                .setCategory(Notification.CATEGORY_SERVICE)
                .addAction(android.R.drawable.ic_media_pause, "停止录音", stopIntent)
                .build();
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null ? "" : intent.getAction();
        if (STOP.equals(action)) {
            requestStop();
            return START_NOT_STICKY;
        }
        if (!START.equals(action) || RECORDING.get()) return START_NOT_STICKY;
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            recordError("麦克风权限未授权");
            stopSelf(startId);
            return START_NOT_STICKY;
        }
        try {
            if (Build.VERSION.SDK_INT >= 29) {
                startForeground(NOTIFICATION_ID, notification(), ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE);
            } else startForeground(NOTIFICATION_ID, notification());
        } catch (RuntimeException error) {
            recordError("无法启动麦克风前台服务：" + error.getClass().getSimpleName());
            stopSelf(startId);
            return START_NOT_STICKY;
        }
        if (!RECORDING.compareAndSet(false, true)) return START_NOT_STICKY;
        stopRequested = false;
        final int segmentSeconds = BuildConfig.DEBUG && intent != null
                ? Math.max(5, Math.min(DEFAULT_SEGMENT_SECONDS, intent.getIntExtra(EXTRA_QA_SECONDS, DEFAULT_SEGMENT_SECONDS)))
                : DEFAULT_SEGMENT_SECONDS;
        captureThread = new Thread(() -> capture(segmentSeconds), "VoiceMemoryCapture");
        captureThread.start();
        return START_NOT_STICKY;
    }

    private void capture(int segmentSeconds) {
        android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_AUDIO);
        RandomAccessFile output = null;
        File partial = null;
        try {
            int minimum = AudioRecord.getMinBufferSize(VoiceMemoryStore.SAMPLE_RATE,
                    AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
            if (minimum <= 0) throw new IOException("Unsupported microphone parameters");
            int capacity = Math.max(minimum * 2, 8192);
            AudioRecord audio = new AudioRecord(MediaRecorder.AudioSource.MIC,
                    VoiceMemoryStore.SAMPLE_RATE, AudioFormat.CHANNEL_IN_MONO,
                    AudioFormat.ENCODING_PCM_16BIT, capacity);
            recorder = audio;
            if (audio.getState() != AudioRecord.STATE_INITIALIZED) throw new IOException("AudioRecord initialization failed");
            PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
            wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "yanzi:voice-memory");
            wakeLock.acquire();
            VoiceMemoryStore.recoverInterruptedSegments(this);
            audio.startRecording();
            if (audio.getRecordingState() != AudioRecord.RECORDSTATE_RECORDING)
                throw new IOException("Microphone failed to start");
            recordError("");
            byte[] buffer = new byte[8192];
            long startedAt = 0;
            while (!stopRequested) {
                if (output == null) {
                    partial = VoiceMemoryStore.newPartial(this);
                    output = VoiceMemoryStore.beginSegment(partial);
                    startedAt = SystemClock.elapsedRealtime();
                }
                int read = audio.read(buffer, 0, buffer.length, AudioRecord.READ_BLOCKING);
                if (read > 0) output.write(buffer, 0, read - (read % 2));
                else if (read == AudioRecord.ERROR_INVALID_OPERATION || read == AudioRecord.ERROR_DEAD_OBJECT)
                    throw new IOException("Audio capture interrupted: " + read);
                if (SystemClock.elapsedRealtime() - startedAt >= segmentSeconds * 1000L) {
                    output.getFD().sync();
                    output.close();
                    output = null;
                    VoiceMemoryStore.finishSegment(partial);
                    partial = null;
                }
            }
        } catch (Exception error) {
            Log.e("YanziVoiceMemory", "Recording stopped", error);
            recordError("录音中断：" + error.getClass().getSimpleName());
        } finally {
            if (output != null) {
                try { output.getFD().sync(); output.close(); } catch (Exception e) { Log.w("YanziVoiceMemory", "File close failed", e); }
                try { VoiceMemoryStore.finishSegment(partial); } catch (Exception e) { Log.e("YanziVoiceMemory", "Segment recovery pending", e); }
            }
            AudioRecord audio = recorder;
            recorder = null;
            if (audio != null) {
                try { if (audio.getRecordingState() == AudioRecord.RECORDSTATE_RECORDING) audio.stop(); } catch (Exception ignored) {}
                audio.release();
            }
            if (wakeLock != null && wakeLock.isHeld()) wakeLock.release();
            wakeLock = null;
            RECORDING.set(false);
            stopForeground(true);
            stopSelf();
        }
    }

    private void recordError(String message) {
        getSharedPreferences("voice-memory-state", MODE_PRIVATE).edit().putString("last_error", message).apply();
    }

    private void requestStop() {
        stopRequested = true;
        AudioRecord audio = recorder;
        if (audio != null) {
            try { if (audio.getRecordingState() == AudioRecord.RECORDSTATE_RECORDING) audio.stop(); }
            catch (Exception ignored) {}
        }
        if (captureThread == null) stopSelf();
    }

    @Override public void onDestroy() {
        requestStop();
        super.onDestroy();
    }

    @Override public IBinder onBind(Intent intent) { return null; }
}
