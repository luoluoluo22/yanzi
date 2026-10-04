package cc.luoluoluo.yanzi.mobile;
import android.os.Build;
import java.lang.reflect.Method;
final class MobileDeviceIdentity {
    static String stableDeviceId(android.content.Context context) {
        String id = android.provider.Settings.Secure.getString(context.getContentResolver(), android.provider.Settings.Secure.ANDROID_ID);
        if (id == null || id.isEmpty() || "9774d56d682e549c".equals(id)) {
            String existing = context.getSharedPreferences("yanzi-mobile", 0).getString("deviceId", "");
            return existing.isEmpty() ? "android-" + java.util.UUID.randomUUID() : existing;
        }
        try {
            byte[] digest = java.security.MessageDigest.getInstance("SHA-256").digest(("yanzi-device-v1:" + id + ":" + context.getSharedPreferences("yanzi-mobile", 0).getString("email", "").trim().toLowerCase(java.util.Locale.ROOT)).getBytes(java.nio.charset.StandardCharsets.UTF_8));
            StringBuilder encoded = new StringBuilder("android-device-");
            for (byte value : digest) encoded.append(String.format(java.util.Locale.ROOT, "%02x", value & 255));
            return encoded.toString();
        } catch (java.security.NoSuchAlgorithmException impossible) { throw new IllegalStateException(impossible); }
    }

    static String buildDeviceDisplayName() {
        String marketName = MobileJson.firstNonEmpty(getSystemProperty("ro.product.marketname"), getSystemProperty("ro.vendor.product.marketname"), getSystemProperty("ro.product.vendor.marketname"), getSystemProperty("ro.product.odm.marketname"), getSystemProperty("ro.config.marketing_name"));
        if (!marketName.isEmpty()) {
            return marketName;
        }
        String maker = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER.trim();
        String model = Build.MODEL == null ? "" : Build.MODEL.trim();
        String name = (maker + " " + model).trim();
        return name.trim().isEmpty() ? "Android \u624b\u673a" : name;
    }

    private static String getSystemProperty(String key) {
        try {
            Class<?> systemProperties = Class.forName("android.os.SystemProperties");
            Method get = systemProperties.getMethod("get", String.class);
            Object value = get.invoke(null, key);
            return value == null ? "" : value.toString().trim();
        }
        catch (Exception ignored) {
            return "";
        }
    }

}
