using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Yanzi.Capture;

internal static class CaptureClipboard
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int maxCount);

    public static void SetImage(BitmapSource image)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            using var bitmap = new System.Drawing.Bitmap(stream);

            Forms.Clipboard.SetDataObject(bitmap, true, 3, 60);
            CaptureDiagnostics.Mark(
                "clipboard.image.copied",
                ("durationMs", Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        }
        catch (Exception ex)
        {
            CaptureDiagnostics.Mark(
                "clipboard.image.failed",
                ("durationMs", Stopwatch.GetElapsedTime(start).TotalMilliseconds),
                ("openProcess", GetOpenClipboardProcess()),
                ("error", ex));
            throw;
        }
    }

    private static string GetOpenClipboardProcess()
    {
        try
        {
            var window = GetOpenClipboardWindow();
            if (window == IntPtr.Zero)
                return "unknown";

            var threadId = GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
                return "unknown";

            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName + " (pid " + processId +
                ", tid " + threadId +
                ", hwnd 0x" + window.ToInt64().ToString("X") +
                ", class " + className + ")";
        }
        catch
        {
            return "unknown";
        }
    }
}
