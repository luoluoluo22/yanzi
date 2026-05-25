package cc.luoluoluo.yanzi.mobile.arclayout;

import android.annotation.TargetApi;
import android.os.Build;
import android.view.Gravity;

public final class ArcOrigin {
    public static final int TOP = Gravity.TOP;
    public static final int BOTTOM = Gravity.BOTTOM;
    public static final int LEFT = Gravity.LEFT;
    public static final int RIGHT = Gravity.RIGHT;
    public static final int CENTER = Gravity.CENTER;
    public static final int CENTER_HORIZONTAL = Gravity.CENTER_HORIZONTAL;
    public static final int CENTER_VERTICAL = Gravity.CENTER_VERTICAL;
    public static final int START = Gravity.START;
    public static final int END = Gravity.END;
    public static final int VERTICAL_MASK = Gravity.VERTICAL_GRAVITY_MASK;
    public static final int HORIZONTAL_MASK = Gravity.HORIZONTAL_GRAVITY_MASK;

    private ArcOrigin() {
    }

    @TargetApi(Build.VERSION_CODES.ICE_CREAM_SANDWICH)
    public static int getAbsoluteOrigin(int origin, int layoutDirection) {
        return Gravity.getAbsoluteGravity(origin, layoutDirection);
    }
}
