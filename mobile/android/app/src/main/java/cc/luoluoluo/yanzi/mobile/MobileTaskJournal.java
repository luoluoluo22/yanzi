package cc.luoluoluo.yanzi.mobile;

import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** A receipt journal, never an execution queue. No tokens or original request bodies are stored. */
final class MobileTaskJournal {
    private static final Object gate = new Object();
    static String account(String base, String token) throws Exception {
        JSONObject claims = new JSONObject(new String(android.util.Base64.decode(token.split("\\.")[1], 11), StandardCharsets.UTF_8));
        return base.replaceAll("/+$", "") + "\n" + claims.getString("sub");
    }
    private static File root(String base, String token) throws Exception {
        byte[] hash = MessageDigest.getInstance("SHA-256").digest(account(base, token).getBytes(StandardCharsets.UTF_8));
        StringBuilder name = new StringBuilder(); for (byte b : hash) name.append(String.format("%02x", b));
        File dir = new File(MobileApplicationContext.get().getFilesDir(), "task-journal/" + name); dir.mkdirs(); return dir;
    }
    private static File path(String base, String token, String id) throws Exception {
        if (!id.matches("[a-zA-Z0-9_-]{8,100}")) throw new IOException("invalid_task_id");
        return new File(root(base, token), id + ".json");
    }
    private static void write(File target, JSONObject record) throws Exception {
        File temp = new File(target.getPath() + ".tmp");
        try (FileOutputStream out = new FileOutputStream(temp)) { out.write(record.toString().getBytes(StandardCharsets.UTF_8)); out.getFD().sync(); }
        if (!temp.renameTo(target)) throw new IOException("task_commit_failed");
    }
    static boolean supported(String kind) {
        return Arrays.asList("text","photo","file","screenshot","extension.handoff","capability.invoke").contains(kind)||kind.startsWith("run-")||kind.startsWith("fs-");
    }
    static void saved(String base, String token, JSONObject envelope) throws Exception {
        if(!supported(envelope.optString("kind")))return;
        synchronized (gate) {
            String id = envelope.getString("clientMessageId"); File target = path(base, token, id);
            if (target.exists()) return;
            JSONObject payload = envelope.optJSONObject("payload");
            String targetId = envelope.optString("targetDeviceId");
            write(target, new JSONObject().put("id", id).put("kind", envelope.optString("kind"))
                .put("title", envelope.optString("title", "设备任务")).put("target", targetId)
                .put("extensionId", payload == null ? "" : payload.optString("extensionId"))
                .put("createdAt", System.currentTimeMillis()).put("updatedAt", System.currentTimeMillis())
                .put("processId", android.os.Process.myPid()).put("status", "queued").put("expiresAt", envelope.optString("expiresAt")));
            File[] files = target.getParentFile().listFiles((d, n) -> n.endsWith(".json"));
            if (files != null && files.length > 1000) {
                Arrays.sort(files, Comparator.comparingLong(File::lastModified));
                int excess = files.length - 1000;
                for (File file : files) { if (excess <= 0) break; if (terminal(MobileMessageOutbox.read(file).optString("status"))) { file.delete(); excess--; } }
            }
        }
    }
    static boolean terminal(String status) { return Arrays.asList("completed", "failed", "cancelled", "expired", "acked").contains(status); }
    static void accepted(String base, String token, JSONObject envelope, JSONObject response) throws Exception {
        if(!supported(envelope.optString("kind")))return;
        synchronized (gate) {
            saved(base,token,envelope);
            File target = path(base, token, envelope.getString("clientMessageId"));
            JSONObject record = MobileMessageOutbox.read(target);
            if (terminal(record.optString("status"))) return;
            String id = response.optString("messageId"); if (!id.isEmpty()) record.put("messageId", id);
            String state = response.optString("status", "submitted");
            if ("lan".equals(response.optString("_transport"))) {
                state = response.optBoolean("success") ? "completed" : "failed";
                record.put("result", clip(response.optString("output", response.optString("error"))));
                record.put("transport", "lan");
            }
            record.put("processId", android.os.Process.myPid()).put("status", state).put("updatedAt", System.currentTimeMillis()).remove("error"); write(target, record);
        }
    }
    static void observed(String base, String token, String messageId, JSONObject detail) throws Exception {
        synchronized (gate) {
            for (JSONObject record : list(base, token)) {
                if (!messageId.equals(record.optString("messageId")) || terminal(record.optString("status"))) continue;
                JSONObject payload = detail.optJSONObject("payload"); JSONObject result = payload == null ? null : payload.optJSONObject("executionResult");
                record.put("processId", android.os.Process.myPid()).put("status", detail.optString("status", record.optString("status"))).put("updatedAt", System.currentTimeMillis());
                if (result != null) record.put("result", clip(result.optString("output", result.optString("result", result.toString()))));
                write(path(base, token, record.getString("id")), record);
            }
        }
    }
    static void transportError(String base, String token, JSONObject envelope, Exception error) throws Exception {
        synchronized (gate) {
            File target = path(base, token, envelope.getString("clientMessageId")); if (!target.exists()) return;
            JSONObject record = MobileMessageOutbox.read(target); if (terminal(record.optString("status"))) return;
            boolean rejected = error instanceof MobileMessageClient.HttpFailure && Arrays.asList(400,403,404,409,410,413,426).contains(((MobileMessageClient.HttpFailure)error).status);
            record.put("status", rejected ? "failed" : "unknown").put("error", clip(error.getMessage())).put("updatedAt", System.currentTimeMillis()); write(target, record);
        }
    }
    static List<JSONObject> list(String base, String token) throws Exception {
        synchronized (gate) {
            List<JSONObject> records = new ArrayList<>(); File[] files = root(base, token).listFiles((d,n) -> n.endsWith(".json"));
            if (files != null) for (File file : files) {
                JSONObject record=MobileMessageOutbox.read(file);
                if("executing".equals(record.optString("status")) && record.optInt("processId")!=android.os.Process.myPid()) record.put("status","unknown");
                if("queued".equals(record.optString("status"))&&!record.optString("expiresAt").isEmpty()&&java.time.Instant.parse(record.getString("expiresAt")).toEpochMilli()<System.currentTimeMillis())record.put("status","expired");
                records.add(record);
            }
            if(MobileSessionStore.snapshot(MobileApplicationContext.get()).matches(base,token)) {
                for(JSONObject transfer:CompanionTransferProvider.taskSnapshots(MobileApplicationContext.get())) {
                    records.removeIf(item->item.optString("id").equals(transfer.optString("id"))); records.add(transfer);
                }
            }
            records.sort((a,b) -> Long.compare(b.optLong("createdAt"), a.optLong("createdAt"))); return records;
        }
    }
    static String clip(String value) { if (value == null) return ""; return value.length() > 8000 ? value.substring(0,8000) + "…" : value; }
    static String label(String status) {
        switch(status) { case "queued": return "等待投递"; case "submitted": case "pending": return "等待设备处理";
            case "executing": return "执行中"; case "completed": return "已完成"; case "acked": return "已接收";
            case "failed": return "失败"; case "cancelled": return "已取消"; case "expired": return "已过期"; default: return "结果待确认"; }
    }
}
