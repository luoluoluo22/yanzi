package cc.luoluoluo.yanzi.mobile;

import android.app.Activity;
import android.app.Instrumentation;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

/** Read-only native navigation coverage. Safe on an explicitly selected Dev phone. */
final class PhoneWorkspaceVerification {
    static Object field(Object owner,String name)throws Exception{java.lang.reflect.Field f=owner.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(owner);}
    static void call(Object owner,String name,Class<?> type,Object value)throws Exception{java.lang.reflect.Method m=owner.getClass().getDeclaredMethod(name,type);m.setAccessible(true);m.invoke(owner,value);}
    static boolean contains(View view,String text){if(view instanceof TextView&&text.contentEquals(((TextView)view).getText()))return true;if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++)if(contains(group.getChildAt(i),text))return true;}return false;}
    static boolean descendant(View child,View parent){for(android.view.ViewParent p=child.getParent();p!=null;p=p.getParent())if(p==parent)return true;return false;}
    static void require(boolean value,String label){if(!value)throw new AssertionError(label);}
    static void run(Instrumentation test){Bundle result=new Bundle();try{
        require(test.getTargetContext().getPackageName().endsWith(".dev"),"Dev only");
        MainActivity screen=(MainActivity)test.startActivitySync(new android.content.Intent(test.getTargetContext(),MainActivity.class).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK));
        test.waitForIdleSync();final Throwable[] failed=new Throwable[1];
        test.runOnMainSync(()->{try{call(screen,"selectTab",String.class,"mobile");call(screen,"selectMobileSubTab",int.class,1);}catch(Exception e){failed[0]=e;}});
        test.waitForIdleSync();android.os.SystemClock.sleep(400);
        test.runOnMainSync(()->{try{
            call(screen,"selectTab",String.class,"mobile");call(screen,"selectMobileSubTab",int.class,1);
            androidx.viewpager.widget.ViewPager mobile=(androidx.viewpager.widget.ViewPager)field(screen,"mobileViewPager");
            require(mobile.getCurrentItem()==1&&mobile.getAdapter().getCount()==3,"Phone store workspace selected");
            Object store=field(screen,"applicationStore");View content=(View)field(store,"content");
            require(descendant(content,mobile),"Store is embedded in phone pager");
            require(contains(content,"应用商店"),"Real store content");
            View phone=(View)field(screen,"mobileExtensionTabPage");
            require(!contains(phone,"文档"),"Documentation removed");
            call(screen,"selectMobileSubTab",int.class,0);
            require(descendant((View)field(screen,"extensionsContainer"),phone),"Unified extensions belong to phone");
            View desktop=(View)field(screen,"desktopExtensionTabPage");
            require(!contains(desktop,"小程序"),"Desktop shortcut removed");
            require(((androidx.viewpager.widget.ViewPager)field(screen,"desktopViewPager")).getAdapter().getCount()==3,"Desktop retains chat/files/terminal");
            call(screen,"selectTab",String.class,"desktop");call(screen,"selectSubTab",int.class,2);
            require(((androidx.viewpager.widget.ViewPager)field(screen,"desktopViewPager")).getCurrentItem()==1,"Files mapping preserved");
            call(screen,"selectSubTab",int.class,3);require(((androidx.viewpager.widget.ViewPager)field(screen,"desktopViewPager")).getCurrentItem()==2,"Terminal mapping preserved");
            call(screen,"selectSubTab",int.class,1);require("mobile".equals(field(screen,"selectedTab"))&&mobile.getCurrentItem()==0,"Legacy extension navigation redirects to phone");
            call(screen,"selectMobileSubTab",int.class,1);
        }catch(Throwable e){failed[0]=e;}});
        if(failed[0]!=null)throw new AssertionError(failed[0]);
        result.putString("stream","PHONE_WORKSPACE=PASSED (embedded store, unified extensions, removed docs, desktop page mapping, legacy navigation)");test.finish(Activity.RESULT_OK,result);
    }catch(Throwable e){result.putString("stream",android.util.Log.getStackTraceString(e));test.finish(Activity.RESULT_CANCELED,result);}}
}
