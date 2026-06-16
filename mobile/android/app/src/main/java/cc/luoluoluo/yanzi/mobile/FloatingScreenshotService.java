package cc.luoluoluo.yanzi.mobile;

import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import android.util.Log;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.Toast;

public class FloatingScreenshotService extends Service {
    private WindowManager windowManager;
    private View floatView;
    private WindowManager.LayoutParams params;

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        showFloatButton();
    }

    private void showFloatButton() {
        if (!Settings.canDrawOverlays(this)) {
            Toast.makeText(this, "需要悬浮窗权限，请先开启悬浮轮盘以获取权限", Toast.LENGTH_LONG).show();
            stopSelf();
            return;
        }

        Button btn = new Button(this);
        btn.setText("📸 截图");
        btn.setTextColor(Color.WHITE);
        btn.setTextSize(14.0f);
        
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.rgb(59, 130, 246));
        bg.setCornerRadius(dp2px(24));
        bg.setStroke(dp2px(2), Color.WHITE);
        btn.setBackground(bg);
        btn.setPadding(dp2px(16), dp2px(10), dp2px(16), dp2px(10));

        int layoutType;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            layoutType = WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY;
        } else {
            layoutType = WindowManager.LayoutParams.TYPE_PHONE;
        }

        params = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                layoutType,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
                PixelFormat.TRANSLUCENT
        );

        params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        params.x = 0;
        params.y = dp2px(100);

        btn.setOnTouchListener(new View.OnTouchListener() {
            private int initialX;
            private int initialY;
            private float initialTouchX;
            private float initialTouchY;
            private boolean isMoving = false;

            @Override
            public boolean onTouch(View v, MotionEvent event) {
                switch (event.getAction()) {
                    case MotionEvent.ACTION_DOWN:
                        initialX = params.x;
                        initialY = params.y;
                        initialTouchX = event.getRawX();
                        initialTouchY = event.getRawY();
                        isMoving = false;
                        return true;
                    case MotionEvent.ACTION_MOVE:
                        float dx = event.getRawX() - initialTouchX;
                        float dy = event.getRawY() - initialTouchY;
                        if (Math.abs(dx) > dp2px(8) || Math.abs(dy) > dp2px(8)) {
                            isMoving = true;
                        }
                        if (isMoving) {
                            params.x = initialX + (int) dx;
                            params.y = initialY + (int) dy;
                            windowManager.updateViewLayout(floatView, params);
                        }
                        return true;
                    case MotionEvent.ACTION_UP:
                        if (!isMoving) {
                            v.performClick();
                        }
                        return true;
                }
                return false;
            }
        });

        btn.setOnClickListener(v -> takeScreenshotAndBack());

        floatView = btn;
        windowManager.addView(floatView, params);
    }

    private void takeScreenshotAndBack() {
        if (!MobileAccessibilityService.isEnabled()) {
            Toast.makeText(this, "无障碍服务未开启，无法截图", Toast.LENGTH_LONG).show();
            stopSelf();
            return;
        }

        floatView.setVisibility(View.GONE);

        new Handler(Looper.getMainLooper()).postDelayed(() -> {
            MobileAccessibilityService.captureJpegBase64(new MobileAccessibilityService.ScreenshotCallback() {
                @Override
                public void onSuccess(String jpegBase64, int width, int height) {
                    Intent broadcast = new Intent("cc.luoluoluo.yanzi.mobile.SCREENSHOT_SUCCESS");
                    broadcast.putExtra("image_base64", jpegBase64);
                    sendBroadcast(broadcast);

                    try {
                        Intent backIntent = new Intent(FloatingScreenshotService.this, MainActivity.class);
                        backIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
                        startActivity(backIntent);
                    } catch (Exception e) {
                        Log.e("Yanzi", "Failed to bring MainActivity to front", e);
                    }

                    new Handler(Looper.getMainLooper()).post(FloatingScreenshotService.this::stopSelf);
                }

                @Override
                public void onFailure(String message) {
                    new Handler(Looper.getMainLooper()).post(() -> {
                        floatView.setVisibility(View.VISIBLE);
                        Toast.makeText(FloatingScreenshotService.this, "截图失败: " + message, Toast.LENGTH_LONG).show();
                    });
                }
            });
        }, 150);
    }

    private int dp2px(int dp) {
        return (int) (dp * getResources().getDisplayMetrics().density + 0.5f);
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        if (windowManager != null && floatView != null) {
            try {
                windowManager.removeView(floatView);
            } catch (Exception e) {}
        }
    }
}
