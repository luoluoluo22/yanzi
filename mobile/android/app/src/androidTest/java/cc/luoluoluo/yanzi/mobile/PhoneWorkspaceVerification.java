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
            require(!contains(content,"应用商店"),"No duplicate store title");
            View storeRoot=((android.widget.ScrollView)content).getChildAt(0);
            require(((android.graphics.drawable.ColorDrawable)storeRoot.getBackground()).getColor()==YanziUiKit.BG,"Dark store surface");
            View phone=(View)field(screen,"mobileExtensionTabPage");
            require(!contains(phone,"文档"),"Documentation removed");
            require(!contains(phone,"手机应用与工具"),"Workspace caption removed");
            call(screen,"selectMobileSubTab",int.class,0);
            require(descendant((View)field(screen,"mobileExtensionGrid"),phone),"Phone owns local-only grid");
            require(!descendant((View)field(screen,"extensionsContainer"),phone),"Desktop list excluded from phone");
            android.widget.GridLayout localGrid=(android.widget.GridLayout)field(screen,"mobileExtensionGrid");
            if(localGrid.getChildCount()>1&&localGrid.getWidth()>0)for(int i=0;i<localGrid.getChildCount();i++)require(localGrid.getChildAt(i).getRight()<=localGrid.getWidth(),"Local grid fits viewport");
            View desktop=(View)field(screen,"desktopExtensionTabPage");
            require(contains(desktop,"小程序"),"Desktop shortcut restored");
            require(((androidx.viewpager.widget.ViewPager)field(screen,"desktopViewPager")).getAdapter().getCount()==4,"Desktop retains chat/programs/files/terminal");
            call(screen,"selectTab",String.class,"desktop");call(screen,"selectSubTab",int.class,2);
            require(((androidx.viewpager.widget.ViewPager)field(screen,"desktopViewPager")).getCurrentItem()==2,"Files mapping preserved");
            call(screen,"selectSubTab",int.class,3);require(((androidx.viewpager.widget.ViewPager)field(screen,"desktopViewPager")).getCurrentItem()==3,"Terminal mapping preserved");
            call(screen,"selectSubTab",int.class,1);require("desktop".equals(field(screen,"selectedTab")),"Desktop programs remain on desktop");
            @SuppressWarnings("unchecked") java.util.List<RemoteExtension> original=(java.util.List<RemoteExtension>)field(screen,"currentDesktopExtensions");
            android.widget.EditText search=(android.widget.EditText)field(screen,"searchDesktopExtensionsInput");String query=search.getText().toString();
            java.lang.reflect.Method render=MainActivity.class.getDeclaredMethod("renderExtensions",java.util.List.class);render.setAccessible(true);
            try{
                search.setText("");
                render.invoke(screen,java.util.Arrays.asList(new RemoteExtension("fixture-pc","Desktop fixture","","apps","",true,false),new RemoteExtension("fixture-phone","Phone-only fixture","","apps","",false,true),new RemoteExtension("fixture-both","Dual fixture","","apps","",true,true)));
                View list=(View)field(screen,"extensionList");
                require(contains(list,"Desktop fixture")&&contains(list,"Dual fixture")&&!contains(list,"Phone-only fixture"),"Desktop filters out phone-only runtimes");
                require(!contains(list,"电脑"),"Redundant desktop-only label removed");
            }finally{render.invoke(screen,original);search.setText(query);}
            call(screen,"selectTab",String.class,"mobile");
            call(screen,"selectMobileSubTab",int.class,1);
        }catch(Throwable e){failed[0]=e;}});
        if(failed[0]!=null)throw new AssertionError(failed[0]);
        result.putString("stream","PHONE_WORKSPACE=PASSED (dark embedded store, separate runtimes, no duplicate title/caption, desktop filters and navigation)");test.finish(Activity.RESULT_OK,result);
    }catch(Throwable e){result.putString("stream",android.util.Log.getStackTraceString(e));test.finish(Activity.RESULT_CANCELED,result);}}
}
