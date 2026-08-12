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
        public static string GetProcessNameByPid(uint processId)
        {
            if (processId == 0) return string.Empty;

            try
            {
                var proc = Process.GetProcessById((int)processId);
                var name = proc.ProcessName;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch
            {
                // Process.GetProcessById 在管理员权限进程/全屏游戏下可能抛出 Access Denied 异常
            }

            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)processId);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        int capacity = 1024;
                        StringBuilder sb = new StringBuilder(capacity);
                        if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                        {
                            var fullPath = sb.ToString();
                            return System.IO.Path.GetFileNameWithoutExtension(fullPath);
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

            return string.Empty;
        }

        public static bool ProcessNameMatches(string processName, string pattern)
        {
            if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            var normalizedProcess = NormalizeProcessName(processName);
            var normalizedPattern = NormalizeProcessName(pattern);

            if (normalizedPattern.Contains('*') || normalizedPattern.Contains('?'))
            {
                return FilePatternMatches(normalizedProcess, normalizedPattern);
            }

            if (pattern.Contains('\\') || pattern.Contains('/'))
            {
                var fileName = System.IO.Path.GetFileName(pattern);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return ProcessNameMatches(processName, fileName);
                }
            }

            return normalizedProcess.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeProcessName(string value)
        {
            value = (value ?? string.Empty).Trim();
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value[..^4]
                : value;
        }

        private static bool FilePatternMatches(string filename, string pattern)
        {
            var parts = pattern.Split(['*', '?'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return true;
            }

            var normalizedFileName = filename.ToLowerInvariant();
            var index = 0;
            foreach (var part in parts)
            {
                var lowerPart = part.ToLowerInvariant();
                var found = normalizedFileName.IndexOf(lowerPart, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    return false;
                }

                index = found + part.Length;
            }

            return true;
        }
    }
}
