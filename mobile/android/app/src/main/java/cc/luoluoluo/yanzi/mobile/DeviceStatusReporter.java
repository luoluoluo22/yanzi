package cc.luoluoluo.yanzi.mobile;

import org.json.JSONObject;

/**
 * Mobile device presence payload builder.
 * lastSeenAt and online are assigned by the server; never trust the phone clock.
 */
public final class DeviceStatusReporter {
    private DeviceStatusReporter() {}

    public static String platform() {
        return "android";
    }

    public static JSONObject buildPresencePayload(String deviceId) {
        JSONObject payload = new JSONObject();
        try {
            payload.put("deviceId", deviceId == null ? "" : deviceId);
            payload.put("platform", platform());
        } catch (Exception ignored) {
        }
        return payload;
    }
}
