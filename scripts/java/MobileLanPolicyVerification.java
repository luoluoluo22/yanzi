package cc.luoluoluo.yanzi.mobile;

public final class MobileLanPolicyVerification {
    public static void main(String[] args) {
        for (String route : new String[]{"/v1/me/devices", "/v1/me/devices/lan-links",
                "/v1/me/extensions", "/v1/me/mobile/extensions", "/v1/me/yanm-state",
                "/v1/sync/objects", "/v1/auth/login", "/v1/applications/catalog"}) {
            if (MobileLanPolicy.supports(route)) throw new AssertionError("Cloud API incorrectly routed to LAN: " + route);
        }
        for (String route : new String[]{"/v1/me/devices/protocol?notificationPort=42981",
                "/v1/me/devices/protocol?notificationPort=42982", "/v1/me/mobile/messages",
                "/v1/fs/list?path=Documents", "/v1/shell/run"}) {
            if (!MobileLanPolicy.supports(route)) throw new AssertionError("Missing LAN route: " + route);
        }
        System.out.println("PASS 13 LAN routing checks (cloud account APIs, Dev/production probes and desktop actions)");
    }
}
