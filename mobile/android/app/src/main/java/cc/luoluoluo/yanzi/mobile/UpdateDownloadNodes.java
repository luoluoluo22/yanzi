package cc.luoluoluo.yanzi.mobile;

import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.BooleanSupplier;
import java.util.function.Consumer;

/** Small, bounded APK samples select a route; full package identity/hash checks remain mandatory. */
final class UpdateDownloadNodes {
    interface Connections { HttpURLConnection open(URL url, boolean systemRoute) throws Exception; }
    static final class Node {
        final String name, url;
        final boolean systemRoute;
        long elapsedNanos;
        Node(String name, String url, boolean systemRoute) { this.name=name; this.url=url; this.systemRoute=systemRoute; }
    }
    static List<Node> candidates(String original) {
        List<Node> nodes = new ArrayList<>();
        nodes.add(new Node(original.startsWith("https://sync.luoluoluo.cc.cd/") ? "燕子云端" : "发布源", original, true));
        String github = original.startsWith("https://github.com/luoluoluo22/yanzi/releases/download/") ? original : null;
        if (original.matches("https://sync\\.luoluoluo\\.cc\\.cd/downloads/android/yanzi-mobile-[0-9]+\\.[0-9]+\\.[0-9]+\\.apk")) {
            String filename = original.substring(original.lastIndexOf('/')+1);
            String version = filename.substring("yanzi-mobile-".length(), filename.length()-4);
            github = "https://github.com/luoluoluo22/yanzi/releases/download/android-v"+version+"/"+filename;
        }
        if (github != null) {
            if (!github.equals(original)) nodes.add(new Node("GitHub 官方", github, true));
            nodes.add(new Node("ghfast 镜像", "https://ghfast.top/"+github, true));
            nodes.add(new Node("ddlc 镜像", "https://gh.ddlc.top/"+github, true));
        }
        if (original.startsWith("https://sync.luoluoluo.cc.cd/")) nodes.add(new Node("燕子云端 · 备用网络", original, false));
        return nodes;
    }

    static List<Node> select(String original, Connections connections, BooleanSupplier canceled, Consumer<String> log) {
        List<Node> candidates = candidates(original), available = new ArrayList<>();
        ExecutorService executor = Executors.newFixedThreadPool(candidates.size());
        List<Probe> probes = new ArrayList<>();
        List<Future<Node>> futures = new ArrayList<>();
        long deadline = System.nanoTime()+TimeUnit.SECONDS.toNanos(7);
        try {
            for (Node node : candidates) { Probe probe = new Probe(node, connections, canceled, log); probes.add(probe); futures.add(executor.submit(probe)); }
            for (Future<Node> future : futures) {
                if (canceled.getAsBoolean()) break;
                try { Node node = future.get(Math.max(1, deadline-System.nanoTime()), TimeUnit.NANOSECONDS); if (node != null) available.add(node); }
                catch (Exception ignored) {}
            }
        } finally {
            for (Future<Node> future : futures) future.cancel(true);
            for (Probe probe : probes) probe.close();
            executor.shutdownNow();
        }
        available.sort(Comparator.comparingLong(node -> node.elapsedNanos));
        // A probe timeout is not proof the source is unusable. Keep one explicit final fallback.
        if (available.isEmpty() && !canceled.getAsBoolean()) available.add(candidates.get(0));
        return available;
    }

    static boolean apkSample(byte[] bytes, int length, int status, String contentType, String range) {
        if (status != 200 && status != 206 || length != 32768) return false;
        if (contentType != null && (contentType.toLowerCase(Locale.ROOT).contains("text/") || contentType.toLowerCase(Locale.ROOT).contains("json"))) return false;
        if (status == 206 && (range == null || !range.matches("bytes 0-[0-9]+/[0-9]+"))) return false;
        return bytes[0]=='P' && bytes[1]=='K' && bytes[2]==3 && bytes[3]==4;
    }

    private static final class Probe implements Callable<Node> {
        final Node node; final Connections connections; final BooleanSupplier canceled; final Consumer<String> log;
        volatile HttpURLConnection connection;
        Probe(Node node, Connections connections, BooleanSupplier canceled, Consumer<String> log) { this.node=node;this.connections=connections;this.canceled=canceled;this.log=log; }
        public Node call() {
            long start=System.nanoTime();
            try {
                if (canceled.getAsBoolean() || Thread.currentThread().isInterrupted()) return null;
                connection=connections.open(new URL(node.url), node.systemRoute);
                connection.setConnectTimeout(3000);connection.setReadTimeout(3000);
                connection.setRequestProperty("Range", "bytes=0-32767");
                connection.setRequestProperty("Accept-Encoding", "identity");
                connection.setRequestProperty("User-Agent", "YanziClient-Mobile");
                int status=connection.getResponseCode();
                if (status!=200 && status!=206) throw new java.io.IOException("HTTP "+status);
                byte[] bytes=new byte[32768];int length=0;
                try(InputStream in=connection.getInputStream()) {
                    while(length<bytes.length && !canceled.getAsBoolean() && !Thread.currentThread().isInterrupted() && System.nanoTime()-start<TimeUnit.SECONDS.toNanos(6)) {
                        int n=in.read(bytes,length,bytes.length-length);if(n<0)break;length+=n;
                    }
                }
                if (!apkSample(bytes,length,status,connection.getContentType(),connection.getHeaderField("Content-Range"))) throw new java.io.IOException("非完整 APK 样本");
                node.elapsedNanos=System.nanoTime()-start;
                log.accept("更新节点可用: "+node.name+", sampleMs="+TimeUnit.NANOSECONDS.toMillis(node.elapsedNanos));
                return node;
            } catch(Exception e) { log.accept("更新节点未通过检测: "+node.name+", "+e.getMessage());return null; }
            finally { close(); }
        }
        void close() { HttpURLConnection c=connection;if(c!=null)c.disconnect(); }
    }
}
