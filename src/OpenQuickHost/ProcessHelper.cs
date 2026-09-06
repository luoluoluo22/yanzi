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
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<uint, (string Name, long ExpireTick)> _pidNameCache = new();

        public static string GetProcessNameByPid(uint processId)
        {
            if (processId == 0) return string.Empty;

            var now = Environment.TickCount64;
            if (_pidNameCache.TryGetValue(processId, out var cached) && now < cached.ExpireTick)
            {
                return cached.Name;
            }

            string name = string.Empty;
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
                            name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
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

            if (!string.IsNullOrEmpty(name))
            {
                _pidNameCache[processId] = (name, now + 5000); // 缓存 5 秒
            }

            return name;
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

        /// <summary>
        /// 统一判定目标进程是否被允许通过黑白名单校验。
        /// 规则：
        /// 1. 若配置了白名单且非空：必须命中白名单中的任意一项才允许通过；
        /// 2. 若命中黑名单中的任意一项：直接拦截（不允许通过）；
        /// 3. 其他情况默认允许通过。
        /// </summary>
        public static bool IsProcessAllowed(string? processName, IEnumerable<string>? whitelist, IEnumerable<string>? blacklist)
        {
            if (string.IsNullOrWhiteSpace(processName)) return true;

            if (whitelist != null)
            {
                var whiteListItems = whitelist.Where(w => !string.IsNullOrWhiteSpace(w)).ToList();
                if (whiteListItems.Count > 0 && !whiteListItems.Any(item => ProcessNameMatches(processName, item)))
                {
                    return false;
                }
            }

            if (blacklist != null)
            {
                var blackListItems = blacklist.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
                if (blackListItems.Count > 0 && blackListItems.Any(item => ProcessNameMatches(processName, item)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 判定目标进程是否命中给定的进程列表（如黑名单或白名单）
        /// </summary>
        public static bool IsProcessInList(string? processName, IEnumerable<string>? list)
        {
            if (string.IsNullOrWhiteSpace(processName) || list == null) return false;
            return list.Any(item => !string.IsNullOrWhiteSpace(item) && ProcessNameMatches(processName, item));
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
