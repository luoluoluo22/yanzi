package cc.luoluoluo.yanzi.mobile;

import org.json.JSONArray;
import org.json.JSONObject;
import java.util.UUID;
import java.util.function.BooleanSupplier;

/**
 * Phone -> authenticated same-account desktop -> desktop model API.
 * Only model IDs and conversation text travel over the account relay.
 */
final class MobileAccountModelChat {
    static final class DesktopRequired extends Exception {
        DesktopRequired(String message) { super(message); }
    }
    private MobileAccountModelChat() {}

    static JSONArray onlineDesktops(String base, String token) throws Exception {
        JSONObject reply = MobileMessageClient.readSystemFirst(base, "/v1/me/devices", token);
        JSONArray devices = reply.optJSONArray("items");
        JSONArray result = new JSONArray();
        if (devices != null) for (int i = 0; i < devices.length(); i++) {
            JSONObject device = devices.optJSONObject(i);
            if (device == null || !"desktop".equalsIgnoreCase(device.optString("platform")) ||
                !device.optBoolean("online", false) || device.optBoolean("disabled", false)) continue;
            String id = device.optString("deviceId", "").trim();
            if (!id.isEmpty()) result.put(device);
        }
        return result;
    }

    private static String findTarget(JSONArray devices, String preferred) throws Exception {
        for (int i = 0; i < devices.length(); i++) {
            JSONObject d = devices.optJSONObject(i);
            if (d != null && preferred != null && !preferred.isEmpty() &&
                preferred.equals(d.optString("deviceId"))) return preferred;
        }
        if (devices.length() == 1)
            return devices.getJSONObject(0).getString("deviceId");
        if (devices.length() == 0)
            throw new DesktopRequired("没有在线的燕子电脑，请先启动电脑上的燕子。");
        throw new DesktopRequired("有多台电脑在线，请点 AI 左上角的设置，选择运行模型的电脑。");
    }

    static JSONObject complete(String base, String token, String phoneDeviceId,
            String preferredDesktopId, String model, JSONArray incoming,
            BooleanSupplier cancelled) throws Exception {
        if (model == null || model.isEmpty())
            throw new IllegalArgumentException("请先在左上角设置中选择模型。");
        if (incoming == null || incoming.length() == 0)
            throw new IllegalArgumentException("empty_messages");
        JSONArray messages = new JSONArray();
        int textLength = 0;
        for (int i = 0; i < incoming.length(); i++) {
            JSONObject m = incoming.optJSONObject(i);
            if (m == null) continue;
            String role = m.optString("role", "");
            if (!role.equals("user") && !role.equals("assistant") && !role.equals("system")) continue;
            Object content = m.opt("content");
            if (!(content instanceof String))
                throw new IllegalArgumentException("当前电脑代理先支持文本对话，图片请改用支持图片的旧接口。");
            String text = (String)content;
            textLength += text.length();
            if (textLength > 32000)
                throw new IllegalArgumentException("对话上下文过长，请新建一个对话。");
            messages.put(new JSONObject().put("role", role).put("content", text));
        }
        if (messages.length() == 0) throw new IllegalArgumentException("empty_messages");

        JSONArray desktops = onlineDesktops(base, token);
        String target = findTarget(desktops, preferredDesktopId);
        JSONObject input = new JSONObject().put("model", model).put("messages", messages);
        JSONObject envelope = new JSONObject()
            .put("clientMessageId", UUID.randomUUID().toString())
            .put("sourceDeviceId", phoneDeviceId)
            .put("targetDeviceId", target)
            .put("targetPlatform", "desktop")
            .put("kind", "capability.invoke")
            .put("title", "手机 AI 对话")
            .put("text", "调用已注册模型")
            .put("expiresAt", java.time.Instant.now().plusSeconds(150).toString())
            .put("payload", new JSONObject()
                .put("name", "ai.chat.complete")
                .put("payload", input));

        if (cancelled.getAsBoolean()) throw new InterruptedException("AI 请求已取消");
        JSONObject queued = MobileMessageClient.request(base, "/v1/me/mobile/messages", token, "POST", envelope);
        String messageId = queued.optString("messageId", "");
        if (messageId.isEmpty()) throw new IllegalStateException("电脑消息提交失败：未返回消息 ID");
        long deadline = android.os.SystemClock.elapsedRealtime() + 130000L;
        while (android.os.SystemClock.elapsedRealtime() < deadline) {
            if (cancelled.getAsBoolean()) {
                try { MobileApiClient.cancelPendingExecution(base, token, messageId, phoneDeviceId); }
                catch (Exception ignored) {}
                throw new InterruptedException("AI 请求已取消");
            }
            JSONObject receipt = MobileApiClient.fetchMessageDetail(base, token, messageId);
            JSONObject message = receipt.optJSONObject("message");
            if (message == null) message = receipt;
            String status = message.optString("status", receipt.optString("status", ""));
            if ("completed".equals(status) || "failed".equals(status) ||
                "expired".equals(status) || "cancelled".equals(status)) {
                JSONObject payload = message.optJSONObject("payload");
                JSONObject execution = payload == null ? null : payload.optJSONObject("executionResult");
                String raw = execution == null ? "" : execution.optString("output", "");
                if (!"completed".equals(status))
                    throw new IllegalStateException("电脑模型调用未完成：" +
                        ("failed".equals(status) && !raw.isEmpty() ? parseError(raw) : status));
                JSONObject invoke = new JSONObject(raw);
                if (!invoke.optBoolean("success", false))
                    throw new IllegalStateException("模型调用失败：" + invoke.optString("errorCode", "unknown"));
                JSONObject data = invoke.optJSONObject("data");
                String answer = data == null ? "" : data.optString("content", "");
                if (answer.isEmpty()) throw new IllegalStateException("电脑返回了空模型回答");
                return new JSONObject().put("choices", new JSONArray().put(
                    new JSONObject().put("message",
                        new JSONObject().put("role", "assistant").put("content", answer))));
            }
            Thread.sleep(1500);
        }
        throw new java.net.SocketTimeoutException("电脑 AI 回复超时，请检查目标电脑是否在线。");
    }

    private static String parseError(String raw) {
        try {
            JSONObject result = new JSONObject(raw);
            String code = result.optString("errorCode", "");
            return code.isEmpty() ? "desktop_execution_failed" : code;
        } catch (Exception ignored) { return "desktop_execution_failed"; }
    }
}
