package cc.luoluoluo.yanzi.mobile;

import java.io.ByteArrayInputStream;
import java.nio.charset.StandardCharsets;

public final class HttpResponseBodyVerification {
    public static void main(String[] args) throws Exception {
        if (!HttpResponseBody.read(null, 10).isEmpty()) throw new AssertionError("Empty HTTP error body");
        byte[] text = "中文\nmessage".getBytes(StandardCharsets.UTF_8);
        if (!"中文\nmessage".equals(HttpResponseBody.read(new ByteArrayInputStream(text), text.length)))
            throw new AssertionError("UTF-8 or newline changed");
        boolean[] closed = {false};
        try {
            HttpResponseBody.read(new ByteArrayInputStream(text) {
                public void close() { closed[0] = true; }
            }, text.length - 1);
            throw new AssertionError("Oversized response accepted");
        } catch (HttpResponseBody.TooLarge expected) {
            if (!closed[0]) throw new AssertionError("Failed response stream leaked");
        }
        int[] attempts = {0};
        if (CloudRequestRetry.retryable(new Exception(new HttpResponseBody.TooLarge())))
            throw new AssertionError("Wrapped oversized failure retried");
        Thread.currentThread().interrupt();
        try {
            if (CloudRequestRetry.retryable(new java.net.SocketException("reset")))
                throw new AssertionError("Interrupted request retried");
        } finally { Thread.interrupted(); }
        try {
            CloudRequestRetry.execute(true, system -> { attempts[0]++; throw new HttpResponseBody.TooLarge(); });
            throw new AssertionError("Oversized request retried");
        } catch (HttpResponseBody.TooLarge expected) {
            if (attempts[0] != 1) throw new AssertionError("Oversized response repeated");
        }
        System.out.println("PASS empty errors, UTF-8, bounded response, stream cleanup and no oversized retry");
    }
}
