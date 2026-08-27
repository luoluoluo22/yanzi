using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenQuickHost;

public static class MemoryOptimizationService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    public static void OptimizeMemoryInBackground()
    {
        _ = Task.Run(() =>
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

                if (OperatingSystem.IsWindows())
                {
                    using var process = Process.GetCurrentProcess();
                    SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));
                }
            }
            catch
            {
                // Ignore memory optimization errors
            }
        });
    }
}
