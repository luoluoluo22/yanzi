package cc.luoluoluo.yanzi.mobile;

/** JVM smoke for the explicit, deliberately narrow target-app allowlist. */
public final class MobilePublicFeedSnapshotSmoke {
    public static void main(String[] args) {
        for (String pkg : new String[]{
                "tv.danmaku.bili", "com.xingin.xhs", "com.ss.android.ugc.aweme"}) {
            if (!MobilePublicFeedSnapshot.allowedPackage(pkg)) {
                throw new AssertionError("Public target incorrectly denied: " + pkg);
            }
        }
        for (String pkg : new String[]{
                "com.tencent.mm", "com.android.settings",
                "com.example.bank", "", "tv.danmaku.bili.extra", null}) {
            if (MobilePublicFeedSnapshot.allowedPackage(pkg)) {
                throw new AssertionError("Non-public target incorrectly allowed: " + pkg);
            }
        }
        System.out.println("PUBLIC_FEED_ALLOWLIST_PASS=True targets=3 rejected=6");
    }
}
