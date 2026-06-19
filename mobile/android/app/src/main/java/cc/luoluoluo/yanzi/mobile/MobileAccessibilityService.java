package cc.luoluoluo.yanzi.mobile;

import android.accessibilityservice.AccessibilityService;
import android.graphics.Bitmap;
import android.graphics.ColorSpace;
import android.hardware.HardwareBuffer;
import android.os.Build;
import android.view.Display;
import android.view.accessibility.AccessibilityEvent;

import java.io.ByteArrayOutputStream;
import java.util.Base64;
import java.util.concurrent.Executor;

public class MobileAccessibilityService extends AccessibilityService {
    private static volatile MobileAccessibilityService activeInstance;

    public interface ScreenshotCallback {
        void onSuccess(String jpegBase64, int width, int height);

        void onFailure(String message);
    }

    public static boolean isEnabled() {
        return activeInstance != null;
    }

    public static boolean captureJpegBase64(ScreenshotCallback callback) {
        MobileAccessibilityService service = activeInstance;
        if (service == null) {
            callback.onFailure("无障碍服务未开启");
            return false;
        }

        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) {
            callback.onFailure("当前 Android 版本不支持无障碍截图");
            return false;
        }

        Executor executor = Runnable::run;
        service.takeScreenshot(
            Display.DEFAULT_DISPLAY,
            executor,
            new TakeScreenshotCallback() {
                @Override
                public void onSuccess(ScreenshotResult screenshot) {
                    try {
                        HardwareBuffer buffer = screenshot.getHardwareBuffer();
                        ColorSpace colorSpace = screenshot.getColorSpace();
                        Bitmap hardwareBitmap = Bitmap.wrapHardwareBuffer(buffer, colorSpace);
                        if (hardwareBitmap == null) {
                            callback.onFailure("截图转换失败");
                            return;
                        }

                        Bitmap bitmap = hardwareBitmap.copy(Bitmap.Config.ARGB_8888, false);
                        int width = bitmap.getWidth();
                        int height = bitmap.getHeight();
                        Bitmap output = scaleDown(bitmap, 900);
                        ByteArrayOutputStream stream = new ByteArrayOutputStream();
                        output.compress(Bitmap.CompressFormat.JPEG, 55, stream);
                        String base64 = Base64.getEncoder().encodeToString(stream.toByteArray());
                        callback.onSuccess(base64, width, height);
                        if (output != bitmap) {
                            output.recycle();
                        }
                        bitmap.recycle();
                        hardwareBitmap.recycle();
                        buffer.close();
                    } catch (Exception ex) {
                        callback.onFailure(ex.getMessage());
                    }
                }

                @Override
                public void onFailure(int errorCode) {
                    callback.onFailure("截图失败，错误码 " + errorCode);
                }
            });
        return true;
    }

    private static Bitmap scaleDown(Bitmap bitmap, int maxSide) {
        int width = bitmap.getWidth();
        int height = bitmap.getHeight();
        int longest = Math.max(width, height);
        if (longest <= maxSide) {
            return bitmap;
        }

        float ratio = maxSide / (float) longest;
        int targetWidth = Math.max(1, Math.round(width * ratio));
        int targetHeight = Math.max(1, Math.round(height * ratio));
        return Bitmap.createScaledBitmap(bitmap, targetWidth, targetHeight, true);
    }

    @Override
    protected void onServiceConnected() {
        super.onServiceConnected();
        activeInstance = this;
        MobileDiagnostics.append(this, "无障碍服务已连接，当前仅启用截图能力。");
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {
    }

    @Override
    public void onInterrupt() {
    }

    @Override
    public void onDestroy() {
        if (activeInstance == this) {
            activeInstance = null;
        }
        super.onDestroy();
    }
}
