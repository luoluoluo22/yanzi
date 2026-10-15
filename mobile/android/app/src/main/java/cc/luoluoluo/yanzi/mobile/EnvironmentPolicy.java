package cc.luoluoluo.yanzi.mobile;

/** Pure rules shared by native collection and JVM user-scenario tests. */
final class EnvironmentPolicy {
    static String classify(double distance, double accuracy, double radius) {
        if (!Double.isFinite(distance) || !Double.isFinite(accuracy) || accuracy < 0 || radius < 50) return "unknown";
        if (distance + accuracy <= radius) return "home";
        if (distance - accuracy >= radius + 100) return "away";
        return "unknown";
    }
    static boolean fresh(long sampleMillis, long nowMillis) {
        return sampleMillis > 0 && sampleMillis <= nowMillis + 5000 && nowMillis - sampleMillis <= 120000;
    }
}
