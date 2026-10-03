package cc.luoluoluo.yanzi.mobile;
public final class EnvironmentPolicyVerification {
    private static int checks;
    private static void check(boolean value,String name){checks++;if(!value)throw new AssertionError(name);}
    public static void main(String[] args){
        check(EnvironmentPolicy.classify(50,20,200).equals("home"),"inside home");
        check(EnvironmentPolicy.classify(195,30,200).equals("unknown"),"boundary ambiguity");
        check(EnvironmentPolicy.classify(380,40,200).equals("away"),"outside hysteresis");
        check(EnvironmentPolicy.classify(250,500,200).equals("unknown"),"coarse location uncertainty");
        check(EnvironmentPolicy.classify(Double.NaN,1,200).equals("unknown"),"invalid location");
        check(!EnvironmentPolicy.fresh(1,500000),"stale cache");
        check(!EnvironmentPolicy.fresh(500000,1),"future cache");
        check(EnvironmentPolicy.fresh(400000,500000),"fresh cache");
        check(BackgroundCadence.HEARTBEAT_MS<120000,"presence within server window");
        check(BackgroundCadence.syncInterval(true)==300000,"realtime compensation");
        check(BackgroundCadence.syncInterval(false)==60000,"disconnected sync fallback");
        check(BackgroundCadence.pollInterval(false)==5000,"message recovery");
        check(BackgroundCadence.reconnectDelay(50)==60000,"reconnect cap");
        System.out.println("Environment and background cadence scenarios passed: "+checks);
    }
}
