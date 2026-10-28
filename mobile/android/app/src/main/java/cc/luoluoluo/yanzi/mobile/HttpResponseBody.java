package cc.luoluoluo.yanzi.mobile;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.nio.charset.StandardCharsets;

/** Bounded UTF-8 response reading shared by foreground and background clients. */
final class HttpResponseBody {
    static final class TooLarge extends IOException {
        TooLarge() { super("Response too large"); }
    }

    static String read(HttpURLConnection connection) throws IOException {
        int status = connection.getResponseCode();
        return read(status >= 200 && status < 300 ? connection.getInputStream() : connection.getErrorStream(),
                32 * 1024 * 1024);
    }

    static String read(InputStream stream, int maximumBytes) throws IOException {
        if (stream == null) return "";
        try (InputStream input = stream; ByteArrayOutputStream buffer = new ByteArrayOutputStream()) {
            byte[] bytes = new byte[4096];
            int count;
            while ((count = input.read(bytes)) != -1) {
                if (count > maximumBytes - buffer.size()) throw new TooLarge();
                buffer.write(bytes, 0, count);
            }
            return new String(buffer.toByteArray(), StandardCharsets.UTF_8);
        }
    }
}
