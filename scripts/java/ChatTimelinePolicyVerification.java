package cc.luoluoluo.yanzi.mobile;

public final class ChatTimelinePolicyVerification {
    private static void check(boolean value, String message) {
        if (!value) throw new AssertionError(message);
    }

    public static void main(String[] args) {
        long t = 1_000_000L;
        check(ChatTimelinePolicy.shouldShowTime(Long.MIN_VALUE, t), "first message must show time");
        check(!ChatTimelinePolicy.shouldShowTime(t, t + 1), "1 ms must stay grouped");
        check(!ChatTimelinePolicy.shouldShowTime(t, t + 179_999L), "under 3 minutes must stay grouped");
        check(ChatTimelinePolicy.shouldShowTime(t, t + 180_000L), "exactly 3 minutes must show time");
        check(ChatTimelinePolicy.shouldShowTime(t, t + 600_000L), "large gap must show time");
        check(!ChatTimelinePolicy.shouldShowTime(t + 1000L, t), "out-of-order older message must not create a future marker");
        System.out.println("PASS WeChat-like 3-minute timeline grouping");
    }
}
