package cc.luoluoluo.yanzi.mobile;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

/** Runs without Android to exercise failures before a response and during its body. */
public final class UpdateManifestClientVerification {
    static final class Response extends HttpURLConnection {
        final int status;
        final byte[] body;
        final boolean reset;
        boolean disconnected;
        Response(int status, byte[] body, boolean reset) throws Exception {
            super(new URL("https://example.invalid/releases"));
            this.status = status; this.body = body; this.reset = reset;
        }
        public void connect() {}
        public void disconnect() { disconnected = true; }
        public boolean usingProxy() { return false; }
        public int getResponseCode() throws IOException {
            if (status == -1) throw new SocketException("Connection reset");
            return status;
        }
        public InputStream getInputStream() throws IOException {
            if (reset) throw new SocketException("Connection reset during body");
            return new ByteArrayInputStream(body);
        }
    }
    static void scenario(String name, Response... responses) throws Exception {
        List<String> attempts = new ArrayList<>();
        int[] position = {0};
        String result = UpdateManifestClient.fetch(new String[]{"https://primary.invalid", "https://backup.invalid"},
                "test", (url, system) -> {
                    attempts.add(url.getHost() + ":" + system);
                    return responses[position[0]++];
                }, body -> { if (!body.equals("[]")) throw new IOException("Malformed manifest"); }, message -> {});
        if (!result.equals("[]") || position[0] != responses.length) throw new AssertionError(name);
        for (Response response : responses) if (!response.disconnected) throw new AssertionError("Connection leak");
        if (responses.length > 2 && !attempts.get(2).equals("backup.invalid:true")) throw new AssertionError("No source fallback");
        if (!attempts.get(0).equals("primary.invalid:true")) throw new AssertionError("System/VPN must be first");
        if (responses.length > 1 && !attempts.get(1).equals("primary.invalid:false")) throw new AssertionError("Physical route fallback missing");
        System.out.println("PASS " + name);
    }
    static Response response(int status, String body, boolean reset) throws Exception {
        return new Response(status, body.getBytes(StandardCharsets.UTF_8), reset);
    }
    public static void main(String[] args) throws Exception {
        scenario("primary success", response(200, "[]", false));
        scenario("reset before response changes route", response(-1, "", false), response(200, "[]", false));
        scenario("reset during body changes route", response(200, "", true), response(200, "[]", false));
        scenario("HTTP failure falls back to backup", response(503, "", false), response(404, "", false), response(200, "[]", false));
        scenario("malformed manifest falls back", response(200, "<html>", false), response(200, "{}", false), response(200, "[]", false));
        scenario("bounded response size", new Response(200, new byte[2 * 1024 * 1024 + 1], false), response(200, "[]", false));
        int[] attempts = {0};
        try {
            UpdateManifestClient.fetch(new String[]{"https://primary.invalid", "https://backup.invalid"}, "test",
                    (url, system) -> { attempts[0]++; throw new SocketException("Connection reset"); }, body -> {}, message -> {});
            throw new AssertionError("Failure swallowed");
        } catch (IOException expected) {
            if (attempts[0] != 4 || expected.getCause() == null) throw new AssertionError("Unbounded retries");
        }
        System.out.println("PASS all sources fail after exactly four attempts");
    }
}
