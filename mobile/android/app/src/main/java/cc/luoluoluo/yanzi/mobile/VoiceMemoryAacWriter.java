package cc.luoluoluo.yanzi.mobile;

import android.content.Context;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.os.SystemClock;
import java.io.File;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.nio.ByteBuffer;

/**
 * AAC-LC encoder producing append-only ADTS frames. A crash can lose only the
 * incomplete final frame; unlike MP4/M4A, no closing index is required.
 * All methods are called exclusively by the capture thread.
 */
final class VoiceMemoryAacWriter {
    static final int BIT_RATE = 32000;
    private final File partial;
    private final RandomAccessFile file;
    private final MediaCodec codec;
    private final MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
    private long consumedSamples;
    private boolean closed;
    private boolean ended;

    VoiceMemoryAacWriter(Context context) throws IOException {
        File temp = VoiceMemoryStore.newPartial(context, ".aac");
        RandomAccessFile output = null;
        MediaCodec encoder = null;
        try {
            output = new RandomAccessFile(temp, "rw");
            output.setLength(0);
            encoder = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
            MediaFormat format = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC,
                    VoiceMemoryStore.SAMPLE_RATE, 1);
            format.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
            format.setInteger(MediaFormat.KEY_BIT_RATE, BIT_RATE);
            format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, 16384);
            encoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            encoder.start();
        } catch (Exception failure) {
            if (encoder != null) try { encoder.release(); } catch (Exception ignored) {}
            if (output != null) try { output.close(); } catch (Exception ignored) {}
            if (temp.exists()) temp.delete();
            throw new IOException("AAC encoder startup failed", failure);
        }
        partial = temp;
        file = output;
        codec = encoder;
    }

    void write(byte[] pcm, int bytes) throws IOException {
        if (closed) throw new IOException("AAC segment closed");
        int offset = 0;
        bytes -= bytes % 2;
        long deadline = SystemClock.elapsedRealtime() + 5000;
        while (offset < bytes) {
            int slot = codec.dequeueInputBuffer(10000);
            if (slot >= 0) {
                ByteBuffer target = codec.getInputBuffer(slot);
                if (target == null) throw new IOException("Encoder input unavailable");
                target.clear();
                int count = Math.min(bytes - offset, target.remaining()) & ~1;
                if (count == 0) throw new IOException("Encoder input buffer too small");
                target.put(pcm, offset, count);
                codec.queueInputBuffer(slot, 0, count, consumedSamples * 1000000L / VoiceMemoryStore.SAMPLE_RATE, 0);
                consumedSamples += count / 2;
                offset += count;
                deadline = SystemClock.elapsedRealtime() + 5000;
            }
            drain(false);
            if (SystemClock.elapsedRealtime() > deadline) throw new IOException("AAC encoder input timeout");
        }
    }

    private void drain(boolean endOfStream) throws IOException {
        long deadline = SystemClock.elapsedRealtime() + (endOfStream ? 5000 : 0);
        while (true) {
            int slot = codec.dequeueOutputBuffer(info, endOfStream ? 10000 : 0);
            if (slot == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!endOfStream) return;
                if (SystemClock.elapsedRealtime() >= deadline)
                    throw new IOException("AAC encoder end-of-stream timeout");
                continue;
            }
            if (slot == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED || slot == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED) continue;
            if (slot < 0) continue;
            if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0 && info.size > 0) {
                ByteBuffer output = codec.getOutputBuffer(slot);
                if (output == null) throw new IOException("Encoder output unavailable");
                output.position(info.offset);
                output.limit(info.offset + info.size);
                writeAdts(output, info.size);
            }
            boolean eos = (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            codec.releaseOutputBuffer(slot, false);
            if (eos) {
                ended = true;
                return;
            }
        }
    }

    private void writeAdts(ByteBuffer bytes, int payload) throws IOException {
        final int frameLength = payload + 7;
        if (frameLength > 8191 || frameLength <= 7) throw new IOException("Invalid ADTS frame length");
        // AAC LC, 16 kHz samplingFrequencyIndex=8, one channel, no CRC.
        byte[] header = new byte[] {
                (byte) 0xff, (byte) 0xf1,
                (byte) ((1 << 6) | (8 << 2)),
                (byte) ((1 << 6) | ((frameLength >> 11) & 3)),
                (byte) ((frameLength >> 3) & 0xff),
                (byte) (((frameLength & 7) << 5) | 0x1f),
                (byte) 0xfc
        };
        file.write(header);
        byte[] data = new byte[payload];
        bytes.get(data);
        file.write(data);
    }

    void finish() throws IOException {
        if (closed) return;
        try {
            long deadline = SystemClock.elapsedRealtime() + 5000;
            int slot;
            while ((slot = codec.dequeueInputBuffer(10000)) < 0) {
                drain(false);
                if (SystemClock.elapsedRealtime() >= deadline)
                    throw new IOException("AAC end-of-stream input timeout");
            }
            codec.queueInputBuffer(slot, 0, 0,
                    consumedSamples * 1000000L / VoiceMemoryStore.SAMPLE_RATE,
                    MediaCodec.BUFFER_FLAG_END_OF_STREAM);
            drain(true);
            if (!ended) throw new IOException("AAC missing final frame");
            file.getFD().sync();
        } finally {
            close();
        }
        VoiceMemoryStore.finalizeAacSegment(partial);
    }

    void abort() {
        if (closed) return;
        close();
        // A partial ADTS file remains on disk; next launch safely recovers whole frames.
    }

    private void close() {
        if (closed) return;
        closed = true;
        try { codec.stop(); } catch (Exception ignored) {}
        try { codec.release(); } catch (Exception ignored) {}
        try { file.close(); } catch (Exception ignored) {}
    }
}
