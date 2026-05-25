package cc.luoluoluo.yanzi.mobile.arclayout;

import android.annotation.TargetApi;
import android.content.Context;
import android.content.res.TypedArray;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Point;
import android.os.Build;
import android.util.AttributeSet;
import android.view.View;
import android.view.ViewGroup;

import java.util.WeakHashMap;

import cc.luoluoluo.yanzi.mobile.R;

import static android.os.Build.VERSION_CODES.JELLY_BEAN_MR1;

public class ArcLayout extends ViewGroup {
    private static final String TAG = "ArcLayout";
    private static final float DEFAULT_CHILD_ANGLE = 0f;
    private static final int DEFAULT_CHILD_ORIGIN = ArcOrigin.CENTER;
    private static final int DEFAULT_ORIGIN = ArcOrigin.CENTER;
    private static final int DEFAULT_COLOR = Color.TRANSPARENT;
    private static final int DEFAULT_RADIUS = 144;
    private static final int DEFAULT_AXIS_RADIUS = -1;
    private static final boolean DEFAULT_FREE_ANGLE = false;
    private static final boolean DEFAULT_REVERSE_ANGLE = false;

    private final WeakHashMap<View, Float> childAngleHolder = new WeakHashMap<>();
    private Arc arc = Arc.CENTER;
    private ArcDrawable arcDrawable;
    private int axisRadius;
    private Point size = new Point();
    private boolean isFreeAngle = DEFAULT_FREE_ANGLE;
    private boolean isReverseAngle = DEFAULT_REVERSE_ANGLE;

    public ArcLayout(Context context) {
        this(context, null);
    }

    public ArcLayout(Context context, AttributeSet attrs) {
        this(context, attrs, 0);
    }

    public ArcLayout(Context context, AttributeSet attrs, int defStyleAttr) {
        super(context, attrs, defStyleAttr);
        init(context, attrs, defStyleAttr, 0);
    }

    @TargetApi(Build.VERSION_CODES.LOLLIPOP)
    public ArcLayout(Context context, AttributeSet attrs, int defStyleAttr, int defStyleRes) {
        super(context, attrs, defStyleAttr, defStyleRes);
        init(context, attrs, defStyleAttr, defStyleRes);
    }

    protected void init(Context context, AttributeSet attrs, int defStyleAttr, int defStyleRes) {
        setWillNotDraw(false);
        final TypedArray a = context.getTheme().obtainStyledAttributes(attrs, R.styleable.arc_ArcLayout, defStyleAttr, defStyleRes);
        int arcOrigin = a.getInt(R.styleable.arc_ArcLayout_arc_origin, DEFAULT_ORIGIN);
        int arcColor = a.getColor(R.styleable.arc_ArcLayout_arc_color, DEFAULT_COLOR);
        int arcRadius = a.getDimensionPixelSize(R.styleable.arc_ArcLayout_arc_radius, DEFAULT_RADIUS);
        int arcAxisRadius = a.getDimensionPixelSize(R.styleable.arc_ArcLayout_arc_axisRadius, DEFAULT_AXIS_RADIUS);
        boolean arcFreeAngle = a.getBoolean(R.styleable.arc_ArcLayout_arc_freeAngle, DEFAULT_FREE_ANGLE);
        boolean arcReverseAngle = a.getBoolean(R.styleable.arc_ArcLayout_arc_reverseAngle, DEFAULT_REVERSE_ANGLE);
        a.recycle();

        if (Build.VERSION.SDK_INT >= JELLY_BEAN_MR1) {
            arcOrigin = ArcOrigin.getAbsoluteOrigin(arcOrigin, getLayoutDirection());
        }

        arc = Arc.of(arcOrigin);
        arcDrawable = new ArcDrawable(arc, arcRadius, arcColor);
        axisRadius = arcAxisRadius;
        isFreeAngle = arcFreeAngle;
        isReverseAngle = arcReverseAngle;
    }

    @Override
    protected void onMeasure(int widthMeasureSpec, int heightMeasureSpec) {
        size.x = Utils.computeMeasureSize(widthMeasureSpec, arcDrawable.getIntrinsicWidth());
        size.y = Utils.computeMeasureSize(heightMeasureSpec, arcDrawable.getIntrinsicHeight());
        setMeasuredDimension(size.x, size.y);
    }

    @Override
    protected void onLayout(boolean changed, int l, int t, int r, int b) {
        if (isInEditMode()) {
            return;
        }
        arcDrawable.setBounds(0, 0, r - l, b - t);
        final Point origin = arc.computeOrigin(0, 0, size.x, size.y);
        final int radius = axisRadius == DEFAULT_AXIS_RADIUS ? arcDrawable.getRadius() / 2 : axisRadius;
        final float perDegrees = arc.computePerDegrees(getChildCountWithoutGone());
        int arcIndex = 0;

        for (int i = 0, count = getChildCount(); i < count; i++) {
            final View child = getChildAt(i);
            if (child.getVisibility() == View.GONE) {
                continue;
            }

            final LayoutParams lp = (LayoutParams) child.getLayoutParams();
            final float childAngle;
            if (isFreeAngle) {
                childAngle = arc.startAngle + lp.angle;
            } else if (isReverseAngle) {
                childAngle = arc.computeReverseDegrees(arcIndex++, perDegrees);
            } else {
                childAngle = arc.computeDegrees(arcIndex++, perDegrees);
            }

            final int x = origin.x + Arc.x(radius, childAngle);
            final int y = origin.y + Arc.y(radius, childAngle);
            childMeasureBy(child, x, y);
            childLayoutBy(child, x, y);
            childAngleHolder.put(child, childAngle);
        }
    }

    @Override
    protected void onDraw(Canvas canvas) {
        if (isInEditMode()) {
            return;
        }
        super.onDraw(canvas);
        arcDrawable.draw(canvas);
    }

    public int getChildCountWithoutGone() {
        int count = 0;
        for (int i = 0, len = getChildCount(); i < len; i++) {
            if (getChildAt(i).getVisibility() != View.GONE) {
                count++;
            }
        }
        return count;
    }

    protected void childMeasureBy(View child, int x, int y) {
        final LayoutParams lp = (LayoutParams) child.getLayoutParams();
        int origin = lp.origin;
        if (Build.VERSION.SDK_INT >= JELLY_BEAN_MR1) {
            origin = ArcOrigin.getAbsoluteOrigin(origin, getLayoutDirection());
        }

        final int widthSize;
        final int widthMode;
        switch (lp.width) {
            case LayoutParams.MATCH_PARENT:
                widthSize = Utils.computeWidth(origin, size.x, x);
                widthMode = MeasureSpec.EXACTLY;
                break;
            case LayoutParams.WRAP_CONTENT:
                widthSize = Utils.computeWidth(origin, size.x, x);
                widthMode = MeasureSpec.AT_MOST;
                break;
            default:
                widthSize = lp.width;
                widthMode = MeasureSpec.EXACTLY;
        }

        final int heightSize;
        final int heightMode;
        switch (lp.height) {
            case LayoutParams.MATCH_PARENT:
                heightSize = Utils.computeHeight(origin, size.y, y);
                heightMode = MeasureSpec.EXACTLY;
                break;
            case LayoutParams.WRAP_CONTENT:
                heightSize = Utils.computeHeight(origin, size.y, y);
                heightMode = MeasureSpec.AT_MOST;
                break;
            default:
                heightSize = lp.height;
                heightMode = MeasureSpec.EXACTLY;
        }

        child.measure(
            MeasureSpec.makeMeasureSpec(widthSize, widthMode),
            MeasureSpec.makeMeasureSpec(heightSize, heightMode)
        );
    }

    protected void childLayoutBy(View child, int x, int y) {
        final LayoutParams lp = (LayoutParams) child.getLayoutParams();
        int origin = lp.origin;
        if (Build.VERSION.SDK_INT >= JELLY_BEAN_MR1) {
            origin = ArcOrigin.getAbsoluteOrigin(origin, getLayoutDirection());
        }
        final int width = child.getMeasuredWidth();
        final int height = child.getMeasuredHeight();

        final int left;
        switch (origin & ArcOrigin.HORIZONTAL_MASK) {
            case ArcOrigin.LEFT:
                left = x;
                break;
            case ArcOrigin.RIGHT:
                left = x - width;
                break;
            default:
                left = x - (width / 2);
        }

        final int top;
        switch (origin & ArcOrigin.VERTICAL_MASK) {
            case ArcOrigin.TOP:
                top = y;
                break;
            case ArcOrigin.BOTTOM:
                top = y - height;
                break;
            default:
                top = y - (height / 2);
        }

        child.layout(left, top, left + width, top + height);
    }

    @Override
    protected ViewGroup.LayoutParams generateLayoutParams(ViewGroup.LayoutParams params) {
        return new LayoutParams(params);
    }

    @Override
    public ViewGroup.LayoutParams generateLayoutParams(AttributeSet attrs) {
        return new LayoutParams(getContext(), attrs);
    }

    @Override
    protected ViewGroup.LayoutParams generateDefaultLayoutParams() {
        return new LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT);
    }

    public static class LayoutParams extends MarginLayoutParams {
        public int origin = DEFAULT_CHILD_ORIGIN;
        public float angle = DEFAULT_CHILD_ANGLE;

        public LayoutParams(Context c, AttributeSet attrs) {
            super(c, attrs);
            TypedArray a = c.getTheme().obtainStyledAttributes(attrs, R.styleable.arc_ArcLayout_Layout, 0, 0);
            origin = a.getInt(R.styleable.arc_ArcLayout_Layout_arc_origin, DEFAULT_CHILD_ORIGIN);
            angle = a.getFloat(R.styleable.arc_ArcLayout_Layout_arc_angle, DEFAULT_CHILD_ANGLE);
            a.recycle();
        }

        public LayoutParams(int width, int height) {
            super(width, height);
        }

        public LayoutParams(ViewGroup.LayoutParams source) {
            super(source);
        }

        public LayoutParams(MarginLayoutParams source) {
            super(source);
        }
    }
}
