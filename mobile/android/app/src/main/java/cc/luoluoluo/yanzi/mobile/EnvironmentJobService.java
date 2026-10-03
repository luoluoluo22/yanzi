package cc.luoluoluo.yanzi.mobile;
import android.app.job.*;
import java.util.concurrent.Future;
public final class EnvironmentJobService extends JobService {
    private Future<?> task;
    @Override public boolean onStartJob(JobParameters p) {
        task=MobileEnvironment.WORK.submit(()->{try { MobileEnvironment.runScheduled(this); } finally { jobFinished(p,false); }});
        return true;
    }
    @Override public boolean onStopJob(JobParameters p) { if(task!=null)task.cancel(true); return true; }
}
