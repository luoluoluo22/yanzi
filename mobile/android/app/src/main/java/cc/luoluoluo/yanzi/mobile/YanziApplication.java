package cc.luoluoluo.yanzi.mobile;

public final class YanziApplication extends android.app.Application {
    @Override public void onCreate() {
        super.onCreate();
        MobileApplicationContext.initialize(this);
        MobileNetworkRouting.initialize(this);
    }
}
