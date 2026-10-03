package cc.luoluoluo.yanzi.mobile;
import org.json.*;
final class MobileJson {
    static String firstNonEmpty(String ... values) {
        for (String value : values) {
            if (value == null || value.trim().isEmpty()) continue;
            return value.trim();
        }
        return "";
    }

    static JSONArray firstArray(JSONObject object, String ... keys) {
        for (String key : keys) {
            JSONArray value = object.optJSONArray(key);
            if (value == null) continue;
            return value;
        }
        return null;
    }

    static JSONObject firstObject(JSONObject object, String ... keys) {
        for (String key : keys) {
            JSONObject value = object.optJSONObject(key);
            if (value == null) continue;
            return value;
        }
        return null;
    }

}
