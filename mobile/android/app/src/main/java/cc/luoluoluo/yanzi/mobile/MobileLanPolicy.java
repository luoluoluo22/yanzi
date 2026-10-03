package cc.luoluoluo.yanzi.mobile;

/** Cloud account APIs must never be used as a test of LAN availability. */
final class MobileLanPolicy {
    static boolean supports(String path) {
        String route = path.split("\\?", 2)[0];
        return route.startsWith("/v1/fs/") || route.equals("/v1/shell/run")
                || route.equals("/v1/me/devices/protocol") || route.equals("/v1/me/mobile/messages");
    }
}
