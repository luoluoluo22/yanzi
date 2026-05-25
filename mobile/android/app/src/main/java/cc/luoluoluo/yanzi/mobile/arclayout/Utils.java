package cc.luoluoluo.yanzi.mobile.arclayout;

import android.util.Log;
import android.view.View;

class Utils {
    static final boolean DEBUG = false;

    private Utils() {
    }

    static void d(String tag, String format, Object... args) {
        Log.d(tag, String.format(format, args));
    }

    static int computeMeasureSize(int measureSpec, int defSize) {
        final int mode = View.MeasureSpec.getMode(measureSpec);
        switch (mode) {
            case View.MeasureSpec.EXACTLY:
                return View.MeasureSpec.getSize(measureSpec);
            case View.MeasureSpec.AT_MOST:
                return Math.min(defSize, View.MeasureSpec.getSize(measureSpec));
            default:
                return defSize;
        }
    }

    static float computeCircleX(float r, float degrees) {
        return (float) (r * Math.cos(Math.toRadians(degrees)));
    }

    static float computeCircleY(float r, float degrees) {
        return (float) (r * Math.sin(Math.toRadians(degrees)));
    }

    static int computeWidth(int origin, int size, int x) {
        switch (origin & ArcOrigin.HORIZONTAL_MASK) {
            case ArcOrigin.LEFT:
                return size - x;
            case ArcOrigin.RIGHT:
                return x;
            default:
                return Math.min(x, size - x) * 2;
        }
    }

    static int computeHeight(int origin, int size, int y) {
        switch (origin & ArcOrigin.VERTICAL_MASK) {
            case ArcOrigin.TOP:
                return size - y;
            case ArcOrigin.BOTTOM:
                return y;
            default:
                return Math.min(y, size - y) * 2;
        }
    }
}
