package cc.luoluoluo.yanzi.mobile;

import android.graphics.Canvas;
import android.graphics.ColorFilter;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.drawable.Drawable;

public class PathDrawable extends Drawable {
    private final Path path;
    private final Paint paint;

    public PathDrawable(Path path, int color) {
        this.path = path;
        this.paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        this.paint.setColor(color);
        this.paint.setStyle(Paint.Style.FILL);
    }

    @Override
    public void draw(Canvas canvas) {
        if (path == null) return;
        Rect bounds = getBounds();
        canvas.save();
        
        RectF pathBounds = new RectF();
        path.computeBounds(pathBounds, true);
        float pathWidth = pathBounds.width();
        float pathHeight = pathBounds.height();
        
        if (pathWidth > 0 && pathHeight > 0) {
            float scaleX = bounds.width() / pathWidth;
            float scaleY = bounds.height() / pathHeight;
            float scale = Math.min(scaleX, scaleY) * 0.65f; // 缩放并保留部分外边距，比0.8稍微内缩，让图标更精致
            
            canvas.translate(bounds.left + bounds.width() / 2f, bounds.top + bounds.height() / 2f);
            canvas.scale(scale, scale);
            canvas.translate(-pathBounds.centerX(), -pathBounds.centerY());
            canvas.drawPath(path, paint);
        }
        canvas.restore();
    }

    @Override
    public void setAlpha(int alpha) {
        paint.setAlpha(alpha);
    }

    @Override
    public void setColorFilter(ColorFilter colorFilter) {
        paint.setColorFilter(colorFilter);
    }

    @Override
    public int getOpacity() {
        return PixelFormat.TRANSLUCENT;
    }
}
