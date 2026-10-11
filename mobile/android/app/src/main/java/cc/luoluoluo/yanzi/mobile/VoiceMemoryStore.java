package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.os.Environment;
import java.io.File;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.text.SimpleDateFormat;
import java.util.Arrays;
import java.util.Date;
import java.util.Locale;

/** Local-only, device-scoped WAV archive. No network and no account synchronization. */
final class VoiceMemoryStore {
    static final int SAMPLE_RATE = 16000;
    static final int BYTES_PER_SECOND = SAMPLE_RATE * 2;
    static final int HEADER_BYTES = 44;
    private VoiceMemoryStore() {}

    static File directory(Context context) {
        File root = context.getExternalFilesDir(Environment.DIRECTORY_MUSIC);
        if (root == null) root = context.getFilesDir();
        File folder = new File(root, "VoiceMemory");
        if (!folder.isDirectory() && !folder.mkdirs() && !folder.isDirectory())
            throw new IllegalStateException("Cannot create voice archive");
        return folder;
    }

    static File[] recordings(Context context) {
        File[] files = directory(context).listFiles((dir, name) -> name.endsWith(".wav"));
        if (files == null) return new File[0];
        Arrays.sort(files, (a, b) -> Long.compare(b.lastModified(), a.lastModified()));
        return files;
    }

    static long durationMs(File file) {
        return Math.max(0L, file.length() - HEADER_BYTES) * 1000L / BYTES_PER_SECOND;
    }

    static String durationText(File file) {
        long seconds = durationMs(file) / 1000;
        return String.format(Locale.ROOT, "%02d:%02d", seconds / 60, seconds % 60);
    }

    static void recoverInterruptedSegments(Context context) {
        File[] interrupted = directory(context).listFiles((dir, name) -> name.endsWith(".wav.partial"));
        if (interrupted == null) return;
        for (File f : interrupted) {
            try { finishSegment(f); } catch (Exception ignored) { /* Leave file intact for later recovery. */ }
        }
    }

    static File newPartial(Context context) {
        String name = new SimpleDateFormat("yyyyMMdd_HHmmss_SSS", Locale.ROOT).format(new Date());
        return new File(directory(context), name + ".wav.partial");
    }

    static RandomAccessFile beginSegment(File partial) throws IOException {
        RandomAccessFile output = new RandomAccessFile(partial, "rw");
        output.setLength(0);
        writeHeader(output, 0);
        return output;
    }

    static void finishSegment(File partial) throws IOException {
        if (!partial.isFile()) return;
        try (RandomAccessFile output = new RandomAccessFile(partial, "rw")) {
            long bytes = output.length() - HEADER_BYTES;
            if (bytes <= 0) {
                output.close();
                if (!partial.delete()) throw new IOException("Cannot discard empty segment");
                return;
            }
            bytes -= bytes % 2;
            output.setLength(HEADER_BYTES + bytes);
            writeHeader(output, bytes);
            output.getFD().sync();
        }
        String filename = partial.getName();
        File completed = new File(partial.getParentFile(), filename.substring(0, filename.length() - ".partial".length()));
        if (completed.exists() || !partial.renameTo(completed)) throw new IOException("Cannot finalize audio segment");
    }

    private static void writeHeader(RandomAccessFile output, long bytes) throws IOException {
        output.seek(0);
        output.writeBytes("RIFF"); writeLE(output, 36 + bytes, 4);
        output.writeBytes("WAVEfmt "); writeLE(output, 16, 4);
        writeLE(output, 1, 2); writeLE(output, 1, 2);
        writeLE(output, SAMPLE_RATE, 4); writeLE(output, BYTES_PER_SECOND, 4);
        writeLE(output, 2, 2); writeLE(output, 16, 2);
        output.writeBytes("data"); writeLE(output, bytes, 4);
    }

    private static void writeLE(RandomAccessFile output, long value, int bytes) throws IOException {
        for (int i = 0; i < bytes; i++) output.write((int) (value >> (i * 8)) & 0xff);
    }
}
