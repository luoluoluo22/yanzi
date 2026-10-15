package cc.luoluoluo.yanzi.mobile;
import java.util.Locale;
    final class RemoteExtension {
        final String extensionId;
        final String name;
        final String description;
        final String icon;
        final String accentHex;
        final boolean hasDesktopRuntime;
        final boolean hasMobileRuntime;

        RemoteExtension(
                String extensionId,
                String name,
                String description,
                String icon,
                String accentHex) {
            this(
                    extensionId,
                    name,
                    description,
                    icon,
                    accentHex,
                    true,
                    false);
        }

        RemoteExtension(
                String extensionId,
                String name,
                String description,
                String icon,
                String accentHex,
                boolean hasDesktopRuntime,
                boolean hasMobileRuntime) {
            this.extensionId = extensionId;
            this.name = name;
            this.description = description;
            this.icon = icon == null ? "" : icon;
            this.accentHex = accentHex == null ? "" : accentHex;
            this.hasDesktopRuntime = hasDesktopRuntime;
            this.hasMobileRuntime = hasMobileRuntime;
        }

        RemoteExtension(String extensionId, String name, String description, String icon) {
            this(extensionId, name, description, icon, "");
        }

        String runtimeLabel() {
            if (this.hasMobileRuntime && this.hasDesktopRuntime) {
                return "\u672c\u673a \u00b7 \u7535\u8111";
            }
            if (this.hasMobileRuntime) {
                return "\u672c\u673a";
            }
            return "\u7535\u8111";
        }

        String iconText() {
            String value = this.icon.trim();
            if (value.startsWith("mdi:")) {
                String namePart = value.substring(4).replace("-", " ").trim();
                return namePart.isEmpty() ? "\u71d5" : namePart.substring(0, 1).toUpperCase(Locale.ROOT);
            }
            String base = this.name.trim().isEmpty() ? this.extensionId : this.name.trim();
            return base.isEmpty() ? "\u71d5" : base.substring(0, 1).toUpperCase(Locale.ROOT);
        }
    }
