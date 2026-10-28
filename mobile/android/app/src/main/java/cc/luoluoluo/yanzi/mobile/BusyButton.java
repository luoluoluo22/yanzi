package cc.luoluoluo.yanzi.mobile;

import android.view.View;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;

/** Main-thread state for asynchronous actions; retain the original label on failure too. */
final class BusyButton {
    private final View button;
    private final TextView label;
    private final CharSequence original;
    private final ProgressBar spinner;
    private boolean busy;

    BusyButton(View button) {
        this.button = button;
        label = button instanceof TextView ? (TextView) button : null;
        original = label == null ? null : label.getText();
        if (button instanceof LinearLayout) {
            spinner = new ProgressBar(button.getContext());
            int size = YanziUiKit.dp(button.getContext(), 22);
            ((LinearLayout) button).addView(spinner, new LinearLayout.LayoutParams(size, size));
            spinner.setVisibility(View.GONE);
        } else spinner = null;
    }

    boolean begin(String message) {
        if (busy) return false;
        busy = true;
        button.setEnabled(false);
        button.setAlpha(0.55f);
        button.setContentDescription(message);
        if (label != null) label.setText(message);
        if (spinner != null) spinner.setVisibility(View.VISIBLE);
        return true;
    }

    void finish() {
        busy = false;
        button.setEnabled(true);
        button.setAlpha(1f);
        button.setContentDescription(null);
        if (label != null) label.setText(original);
        if (spinner != null) spinner.setVisibility(View.GONE);
    }
}
