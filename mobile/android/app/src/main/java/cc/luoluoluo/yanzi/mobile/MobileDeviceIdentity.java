package cc.luoluoluo.yanzi.mobile;
import android.os.Build;
import java.lang.reflect.Method;
final class MobileDeviceIdentity {
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
