package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.os.Environment;
import java.io.File;
import java.io.IOException;
import java.io.EOFException;
import java.io.RandomAccessFile;
import java.util.concurrent.ConcurrentHashMap;
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
    private static final ConcurrentHashMap<String, Long> AAC_DURATION_CACHE = new ConcurrentHashMap<>();

    static File directory(Context context) {
        File root = context.getExternalFilesDir(Environment.DIRECTORY_MUSIC);
        if (root == null) root = context.getFilesDir();
        File folder = new File(root, "VoiceMemory");
        if (!folder.isDirectory() && !folder.mkdirs() && !folder.isDirectory())
            throw new IllegalStateException("Cannot create voice archive");
        return folder;
    }

    static File[] recordings(Context context) {
        File[] files = directory(context).listFiles((dir, name) -> name.endsWith(".wav") || name.endsWith(".aac"));
        if (files == null) return new File[0];
        Arrays.sort(files, (a, b) -> Long.compare(b.lastModified(), a.lastModified()));
        return files;
    }

    static long durationMs(File file) {
        if (file.getName().endsWith(".aac")) {
            String key = file.getAbsolutePath() + ":" + file.length() + ":" + file.lastModified();
            Long previous = AAC_DURATION_CACHE.get(key);
            if (previous != null) return previous;
            try {
                long length = validAacBytes(file, false);
                long frames = countAacFrames(file, length);
                long ms = frames * 1024L * 1000L / SAMPLE_RATE;
                AAC_DURATION_CACHE.put(key, ms);
                return ms;
            } catch (IOException ignored) { return 0; }
        }
        return Math.max(0L, file.length() - HEADER_BYTES) * 1000L / BYTES_PER_SECOND;
    }

    static String durationText(File file) {
        long seconds = durationMs(file) / 1000;
        return String.format(Locale.ROOT, "%02d:%02d", seconds / 60, seconds % 60);
    }

    static void recoverInterruptedSegments(Context context) {
        File[] interrupted = directory(context).listFiles((dir, name) -> name.endsWith(".wav.partial") || name.endsWith(".aac.partial"));
        if (interrupted == null) return;
        for (File f : interrupted) {
            try {
                if (f.getName().endsWith(".aac.partial")) finalizeAacSegment(f);
                else finishSegment(f);
            } catch (Exception ignored) { /* Leave file intact for later recovery. */ }
        }
    }

    static File newPartial(Context context) { return newPartial(context, ".wav"); }

    static File newPartial(Context context, String extension) {
        if (!".wav".equals(extension) && !".aac".equals(extension))
            throw new IllegalArgumentException("Unsupported format");
        String name = new SimpleDateFormat("yyyyMMdd_HHmmss_SSS", Locale.ROOT).format(new Date());
        File candidate = new File(directory(context), name + extension + ".partial");
        if (candidate.exists() || new File(directory(context), name + extension).exists())
            throw new IllegalStateException("Audio segment filename collision");
        return candidate;
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

    /** Validate ADTS sync words and trim an interrupted final frame before publishing. */
    static void finalizeAacSegment(File partial) throws IOException {
        if (!partial.isFile()) return;
        long length = validAacBytes(partial, true);
        if (length == 0) {
            if (!partial.delete()) throw new IOException("Cannot discard empty ADTS fragment");
            return;
        }
        String filename = partial.getName();
        File completed = new File(partial.getParentFile(), filename.substring(0, filename.length() - ".partial".length()));
        if (completed.exists() || !partial.renameTo(completed)) throw new IOException("Cannot finalize ADTS segment");
    }

    private static long validAacBytes(File file, boolean truncate) throws IOException {
        long valid = 0;
        try (RandomAccessFile stream = new RandomAccessFile(file, truncate ? "rw" : "r")) {
            byte[] header = new byte[7];
            long total = stream.length();
            while (valid + 7 <= total) {
                stream.seek(valid);
                stream.readFully(header);
                int h0 = header[0] & 0xff, h1 = header[1] & 0xff;
                int sampleRateIndex = ((header[2] & 0xff) >> 2) & 15;
                int channels = ((header[2] & 1) << 2) | ((header[3] & 0xc0) >> 6);
                int frame = ((header[3] & 3) << 11) | ((header[4] & 0xff) << 3) | ((header[5] & 0xe0) >> 5);
                if (h0 != 0xff || (h1 & 0xf6) != 0xf0 || sampleRateIndex != 8 || channels != 1 || frame < 8 || valid + frame > total) break;
                valid += frame;
            }
            if (truncate && valid < total) {
                stream.setLength(valid);
                stream.getFD().sync();
            }
        }
        return valid;
    }

    private static long countAacFrames(File file, long validLength) throws IOException {
        long pos = 0, frames = 0;
        try (RandomAccessFile input = new RandomAccessFile(file, "r")) {
            while (pos + 7 <= validLength) {
                input.seek(pos + 3);
                int h3 = input.readUnsignedByte();
                int h4 = input.readUnsignedByte();
                int h5 = input.readUnsignedByte();
                int size = ((h3 & 3) << 11) | (h4 << 3) | ((h5 & 0xe0) >> 5);
                if (size < 8 || pos + size > validLength) throw new EOFException("Broken AAC frame");
                pos += size;
                frames++;
            }
        }
        return frames;
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
