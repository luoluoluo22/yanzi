using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenQuickHost
{
    public static class ProcessHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static string? GetProcessExecutablePath(Process process)
        {
            try
            {
                // First try the built-in MainModule (faster but fails on permissions sometimes)
                return process.MainModule?.FileName;
            }
            catch
            {
                // Fallback to QueryFullProcessImageName
                try
                {
                    IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                    if (hProcess != IntPtr.Zero)
                    {
                        try
                        {
                            int capacity = 1024;
                            StringBuilder sb = new StringBuilder(capacity);
                            if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                            {
                                return sb.ToString();
                            }
                        }
                        finally
                        {
                            CloseHandle(hProcess);
                        }
                    }
                }
                catch
                {
                    // Ignore
                }
            }
            return null;
        }
    }
}
