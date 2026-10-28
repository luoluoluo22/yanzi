package cc.luoluoluo.yanzi.mobile;

/** WeChat-like timeline grouping: show a marker for the first message and after a 3-minute gap. */
final class ChatTimelinePolicy {
    static final long TIME_MARKER_GAP_MS = 3L * 60L * 1000L;

    private ChatTimelinePolicy() {}

    static boolean shouldShowTime(long previousTimeMs, long currentTimeMs) {
        return previousTimeMs == Long.MIN_VALUE
                || currentTimeMs - previousTimeMs >= TIME_MARKER_GAP_MS;
    }
}
