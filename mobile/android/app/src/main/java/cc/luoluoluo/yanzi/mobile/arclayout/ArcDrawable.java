package cc.luoluoluo.yanzi.mobile.arclayout;

import android.annotation.TargetApi;
import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.Outline;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.drawable.Drawable;
import android.os.Build;

public class ArcDrawable extends Drawable {
    private final Paint arcPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private Path arcPath = null;
    private Arc arc;
    private int arcRadius;

    public ArcDrawable(Arc arc, int radius, int color) {
        this.arc = arc;
        this.arcRadius = radius;
        this.arcPaint.setColor(color);
    }

    public Arc getArc() {
        return arc;
    }

    public void setArc(Arc arc) {
        this.arc = arc;
        ensurePath();
    }

    public int getRadius() {
        return arcRadius;
    }

    public void setRadius(int radius) {
        arcRadius = radius;
        ensurePath();
    }

    public int getColor() {
        return arcPaint.getColor();
    }

    public void setColor(int color) {
        arcPaint.setColor(color);
    }

    @Override
    public void setBounds(int left, int top, int right, int bottom) {
        super.setBounds(left, top, right, bottom);
        ensurePath(left, top, right, bottom);
    }

    @Override
    public void draw(Canvas canvas) {
        canvas.drawPath(arcPath, arcPaint);
    }

    @Override
    public void setAlpha(int alpha) {
        arcPaint.setAlpha(alpha);
    }

    @Override
    public void setColorFilter(ColorFilter colorFilter) {
        arcPaint.setColorFilter(colorFilter);
    }

    @Override
    public int getOpacity() {
        return PixelFormat.TRANSLUCENT;
    }

    @Override
    public int getIntrinsicWidth() {
        return arc.computeWidth(arcRadius);
    }

    @Override
    public int getIntrinsicHeight() {
        return arc.computeHeight(arcRadius);
    }

    @TargetApi(Build.VERSION_CODES.LOLLIPOP)
    @Override
    public void getOutline(Outline outline) {
        if (arcPath == null || !arcPath.isConvex()) {
            super.getOutline(outline);
        } else {
            outline.setConvexPath(arcPath);
        }
    }

    protected void ensurePath() {
        final Rect rect = getBounds();
        ensurePath(rect.left, rect.top, rect.right, rect.bottom);
    }

    protected void ensurePath(int left, int top, int right, int bottom) {
        arcPath = arc.computePath(arcRadius, left, top, right, bottom);
    }
}
