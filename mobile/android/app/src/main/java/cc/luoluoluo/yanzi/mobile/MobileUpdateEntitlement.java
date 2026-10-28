package cc.luoluoluo.yanzi.mobile;

import java.time.Instant;

/** Local advisory UX gate. Cloud access is enforced independently by the Worker. */
public final class MobileUpdateEntitlement {
    private MobileUpdateEntitlement() {}

    public static boolean isFreeVersion(String version) {
        if (version == null || !version.matches("(?:v)?\\d+\\.\\d+\\.\\d+")) return false;
        String[] parts = version.replaceFirst("^v", "").split("\\.");
        try {
            long major=Long.parseLong(parts[0]);
            long minor=Long.parseLong(parts[1]);
            long patch=Long.parseLong(parts[2]);
            return major<1 || (major==1 && minor==0 && patch==0);
        } catch (NumberFormatException ignored) { return false; }
    }

    public static boolean mayInstall(String targetVersion, String releasedAt, String vipType, String vipExpireAt) {
        if (isFreeVersion(targetVersion)) return true;
        if ("lifetime".equalsIgnoreCase(vipType)) return true;
        try {
            // Previously purchased releases remain installable after VIP expiry.
            return !Instant.parse(vipExpireAt).isBefore(Instant.parse(releasedAt));
        } catch (Exception ignored) {
            return false;
        }
    }
}
