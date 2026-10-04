package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.os.Bundle;

/** Compatibility entry point; the phone Tab hosts the same component directly. */
public final class ApplicationCatalogActivity extends Activity {
    private ApplicationCatalogView store;
    @Override public void onCreate(Bundle saved){super.onCreate(saved);store=new ApplicationCatalogView(this,false);setContentView(store.createView());}
    @Override protected void onResume(){super.onResume();ExternalAccessManager.foreground(this);if(store!=null)store.resume();}
    @Override protected void onDestroy(){if(store!=null)store.close();super.onDestroy();}
}
