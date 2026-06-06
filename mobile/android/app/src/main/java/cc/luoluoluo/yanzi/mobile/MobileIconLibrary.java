package cc.luoluoluo.yanzi.mobile;

import android.graphics.Path;
import androidx.core.graphics.PathParser;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

public final class MobileIconLibrary {
    private static final Map<String, String> ICONS = new HashMap<>();
    private static final Map<String, String> ALIASES = new HashMap<>();
    private static final Map<String, Path> CACHE = new HashMap<>();

    static {
        ICONS.put("chat", "M4,4H20A2,2 0,0 1,22 6V15A2,2 0,0 1,20 17H7L3,21V6A2,2 0,0 1,4 4Z");
        ICONS.put("camera", "M4,4H7L9,2H15L17,4H20A2,2 0,0 1,22 6V18A2,2 0,0 1,20 20H4A2,2 0,0 1,2 18V6A2,2 0,0 1,4 4M12,17A5,5 0,1 0,12 7A5,5 0,0 0,12 17M12,15A3,3 0,1 1,12 9A3,3 0,0 1,12 15Z");
        ICONS.put("image", "M21,19V5A2,2 0,0 0,19 3H5A2,2 0,0 0,3 5V19A2,2 0,0 0,5 21H19A2,2 0,0 0,21 19M8.5,11A1.5,1.5 0,1 1,10 9.5A1.5,1.5 0,0 1,8.5 11M5,19L9,14L12,17L16,12L19,16V19H5Z");
        ICONS.put("globe", "M12,2A10,10 0,1 0,22 12A10,10 0,0 0,12 2M4,12A8,8 0,0 1,12 4C10.44,6.22 9.5,8.97 9.5,12C9.5,15.03 10.44,17.78 12,20A8,8 0,0 1,4 12M12,20C13.56,17.78 14.5,15.03 14.5,12C14.5,8.97 13.56,6.22 12,4A8,8 0,0 1,20 12A8,8 0,0 1,12 20M11.5,6.05C10.54,7.85 10,9.86 10,12C10,14.14 10.54,16.15 11.5,17.95C12.46,16.15 13,14.14 13,12C13,9.86 12.46,7.85 11.5,6.05Z");
        ICONS.put("clipboard", "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0,0 0,3 5V19A2,2 0,0 0,5 21H19A2,2 0,0 0,21 19V5A2,2 0,0 0,19 3M12,3A1,1 0,0 1,13 4A1,1 0,0 1,12 5A1,1 0,0 1,11 4A1,1 0,0 1,12 3M19,19H5V5H19V19Z");
        ICONS.put("dashboard", "M3,13H11V3H3V13M3,21H11V15H3V21M13,21H21V11H13V21M13,3V9H21V3H13Z");
        ICONS.put("plus", "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");
        ICONS.put("file", "M14,2H6A2,2 0,0 0,4 4V20A2,2 0,0 0,6 22H18A2,2 0,0 0,20 20V8L14 2Z");
        ICONS.put("folder", "M10,4H2C0.89,4 0,4.89 0,6V18A2,2 0,0 0,2 20H22A2,2 0,0 0,24 18V8C24,6.89 23.1,6 22,6H12L10,4Z");
        ICONS.put("code", "M8.59,16.59L4,12L8.59,7.41L10,8.83L6.83,12L10,15.17L8.59,16.59M15.41,16.59L14,15.17L17.17,12L14,8.83L15.41,7.41L20,12L15.41,16.59Z");
        ICONS.put("settings", "M12,8A4,4 0,0 1,16 12A4,4 0,0 1,12 16A4,4 0,0 1,8 12A4,4 0,0 1,12 8M10,22C9.75,22 9.54,21.82 9.5,21.58L9.13,18.93C8.5,18.68 7.96,18.34 7.44,17.94L4.95,18.95C4.73,19.03 4.46,18.95 4.34,18.73L2.34,15.27C2.21,15.05 2.27,14.78 2.46,14.63L4.57,12.97L4.5,12L4.57,11L2.46,9.37C2.27,9.22 2.21,8.95 2.34,8.73L4.34,5.27C4.46,5.05 4.73,4.96 4.95,5.05L7.44,6.05C7.96,5.66 8.5,5.32 9.13,5.07L9.5,2.42C9.54,2.18 9.75,2 10,2H14C14.25,2 14.46,2.18 14.5,2.42L14.87,5.07C15.5,5.32 16.04,5.66 16.56,6.05L19.05,5.05C19.27,4.96 19.54,5.05 19.66,5.27L21.66,8.73C21.79,8.95 21.73,9.22 21.54,9.37L19.43,11L19.5,12L19.43,13L21.54,14.63C21.73,14.78 21.79,15.05 21.66,15.27L19.66,18.73C19.54,18.95 19.27,19.04 19.05,18.95L16.56,17.95C16.04,18.34 15.5,18.68 14.87,18.93L14.5,21.58C14.46,21.82 14.25,22 14,22H10Z");

        ALIASES.put("web", "globe");
        ALIASES.put("content-copy", "clipboard");
        ALIASES.put("monitor-dashboard", "dashboard");
        ALIASES.put("view-dashboard-outline", "dashboard");
        ALIASES.put("cellphone-arrow-down", "chat");
        ALIASES.put("file-search-outline", "file");
        ALIASES.put("file-document-edit-outline", "file");
        ALIASES.put("folder-search-outline", "folder");
        ALIASES.put("folder-cog-outline", "folder");
        ALIASES.put("code-tags", "code");
        ALIASES.put("code-json", "code");
        ALIASES.put("cog-outline", "settings");
    }

    public static Path resolve(String reference) {
        String key = normalize(reference);
        if (key.isEmpty()) {
            return null;
        }
        synchronized (CACHE) {
            if (CACHE.containsKey(key)) {
                return CACHE.get(key);
            }
            String pathData = ICONS.get(key);
            if (pathData == null) {
                return null;
            }
            try {
                Path path = PathParser.createPathFromPathData(pathData);
                CACHE.put(key, path);
                return path;
            } catch (Exception ignored) {
                return null;
            }
        }
    }

    private static String normalize(String reference) {
        if (reference == null) {
            return "";
        }
        String value = reference.trim();
        if (value.startsWith("mdi:") || value.startsWith("app:")) {
            value = value.substring(4);
        }
        value = value.toLowerCase(Locale.ROOT);
        return ALIASES.containsKey(value) ? ALIASES.get(value) : value;
    }
}
