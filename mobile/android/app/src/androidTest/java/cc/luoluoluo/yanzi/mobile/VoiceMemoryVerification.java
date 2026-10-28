package cc.luoluoluo.yanzi.mobile;

import android.app.Instrumentation;
import android.app.Activity;
import android.media.MediaPlayer;
import android.os.Bundle;
import java.io.File;
import java.io.RandomAccessFile;
import java.util.HashSet;

/** Deterministic offline codec check; it never opens the microphone. */
public final class VoiceMemoryVerification {
    private VoiceMemoryVerification() {}

    static void run(Instrumentation instrument) {
        Bundle result = new Bundle();
        File fresh = null;
        File recovered = null;
        try {
            HashSet<String> before = new HashSet<>();
            for (File f : VoiceMemoryStore.recordings(instrument.getTargetContext())) before.add(f.getName());
            VoiceMemoryAacWriter writer = new VoiceMemoryAacWriter(instrument.getTargetContext());
            byte[] sound = new byte[VoiceMemoryStore.SAMPLE_RATE * 2 / 5];
            for (int chunk = 0; chunk < 15; chunk++) {
                for (int j = 0; j < sound.length / 2; j++) {
                    double time = (chunk * sound.length / 2.0 + j) / VoiceMemoryStore.SAMPLE_RATE;
                    short s = (short) (Math.sin(time * 440.0 * Math.PI * 2.0) * 10000);
                    sound[j * 2] = (byte) s;
                    sound[j * 2 + 1] = (byte) (s >> 8);
                }
                writer.write(sound, sound.length);
            }
            writer.finish();
            for (File file : VoiceMemoryStore.recordings(instrument.getTargetContext()))
                if (!before.contains(file.getName()) && file.getName().endsWith(".aac")) fresh = file;
            require(fresh != null, "No AAC output after encoding");
            require(fresh.length() > 1500 && fresh.length() < 50000, "Unexpected AAC size " + fresh.length());
            long duration = VoiceMemoryStore.durationMs(fresh);
            require(duration >= 2800 && duration <= 3600, "Incorrect AAC duration " + duration);
            MediaPlayer player = new MediaPlayer();
            try {
                player.setDataSource(fresh.getAbsolutePath());
                player.prepare();
                require(player.getDuration() > 2500, "AAC playback metadata duration too short");
            } finally { player.release(); }
            // Deliberately close an unfinished encoder without EOS and verify that
            // fully written ADTS packets survive recovery on the next app launch.
            HashSet<String> beforeRecovery = new HashSet<>();
            for (File f : VoiceMemoryStore.recordings(instrument.getTargetContext())) beforeRecovery.add(f.getName());
            VoiceMemoryAacWriter interrupted = new VoiceMemoryAacWriter(instrument.getTargetContext());
            for (int n = 0; n < 12; n++) interrupted.write(sound, sound.length);
            interrupted.abort();
            // Simulate an abrupt shutdown midway through an ADTS header.
            File[] fragments = VoiceMemoryStore.directory(instrument.getTargetContext())
                    .listFiles((dir, name) -> name.endsWith(".aac.partial"));
            require(fragments != null && fragments.length > 0, "Missing interrupted ADTS fragment");
            try (RandomAccessFile torn = new RandomAccessFile(fragments[fragments.length - 1], "rw")) {
                torn.seek(torn.length());
                torn.write(new byte[] {(byte) 0xff, (byte) 0xf1, 0x60, 0x40});
            }
            VoiceMemoryStore.recoverInterruptedSegments(instrument.getTargetContext());
            for (File f : VoiceMemoryStore.recordings(instrument.getTargetContext()))
                if (!beforeRecovery.contains(f.getName()) && f.getName().endsWith(".aac"))
                    recovered = f;
            require(recovered != null, "Interrupted AAC fragment could not be recovered");
            long recoveredDuration = VoiceMemoryStore.durationMs(recovered);
            require(recoveredDuration > 1000, "Incomplete AAC recovery produced empty file");
            require(recovered.length() < 50000, "Unexpected growth after recovery");
            result.putString("stream", "VOICE_MEMORY_AAC=PASSED durationMs=" + duration
                    + " bytes=" + fresh.length() + " recoveredMs=" + recoveredDuration);
            instrument.finish(Activity.RESULT_OK, result);
        } catch (Throwable error) {
            result.putString("stream", "VOICE_MEMORY_AAC=FAILED " + android.util.Log.getStackTraceString(error));
            instrument.finish(Activity.RESULT_CANCELED, result);
        } finally {
            if (fresh != null && fresh.exists()) fresh.delete();
            if (recovered != null && recovered.exists()) recovered.delete();
        }
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
