using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenQuickHost;

public sealed class EverythingSearchResponse
{
    public bool Success { get; init; }

    public bool IsAvailable { get; init; }

    public string? ErrorMessage { get; init; }

    public int TotalCount { get; init; }

    public IReadOnlyList<EverythingSearchResult> Results { get; init; } = [];
}

public sealed class EverythingSearchResult
{
    public required string FullPath { get; init; }

    public required string Name { get; init; }

    public required string DirectoryPath { get; init; }

    public required bool IsFolder { get; init; }

    public string? SizeText { get; init; }
}

public static class EverythingSearchService
{
    private const uint EverythingRequestFileName = 0x00000001;
    private const uint EverythingRequestPath = 0x00000002;
    private const uint EverythingRequestSize = 0x00000010;
    private const uint EverythingSortNameAscending = 1;
    private const uint EverythingErrorIpc = 2;
    private static readonly Lock QueryLock = new();

    public static EverythingSearchResponse Search(string? rawQuery, int maxResults = 256)
    {
        var query = (rawQuery ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new EverythingSearchResponse
            {
                Success = true,
                IsAvailable = true,
                TotalCount = 0,
                Results = []
            };
        }

        lock (QueryLock)
        {
            var attemptedRuntimeStart = false;

        retry:
            try
            {
                EverythingApi.Everything_Reset();
                EverythingApi.Everything_SetSearchW(query);
                EverythingApi.Everything_SetRequestFlags(EverythingRequestFileName | EverythingRequestPath | EverythingRequestSize);
                EverythingApi.Everything_SetSort(EverythingSortNameAscending);
                EverythingApi.Everything_SetMax((uint)Math.Max(1, maxResults));
                EverythingApi.Everything_SetOffset(0);

                if (!EverythingApi.Everything_QueryW(true))
                {
                    var errorCode = EverythingApi.Everything_GetLastError();
                    if (!attemptedRuntimeStart && errorCode == EverythingErrorIpc)
                    {
                        attemptedRuntimeStart = true;
                        if (EverythingRuntimeService.EnsureRunning())
                        {
                            goto retry;
                        }
                    }

                    return BuildFailureResponse(errorCode);
                }

                var visibleCount = (int)EverythingApi.Everything_GetNumResults();
                var totalCount = (int)EverythingApi.Everything_GetTotResults();
                var results = new List<EverythingSearchResult>(visibleCount);
                for (uint index = 0; index < visibleCount; index++)
                {
                    var fullPath = GetResultFullPath(index);
                    if (string.IsNullOrWhiteSpace(fullPath))
                    {
                        continue;
                    }

                    var isFolder = EverythingApi.Everything_IsFolderResult(index);
                    var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = fullPath;
                    }

                    var directoryPath = ResolveDirectoryPath(fullPath, isFolder);
                    results.Add(new EverythingSearchResult
                    {
                        FullPath = fullPath,
                        Name = name,
                        DirectoryPath = directoryPath,
                        IsFolder = isFolder,
                        SizeText = isFolder ? null : TryGetSizeText(index)
                    });
                }

                return new EverythingSearchResponse
                {
                    Success = true,
                    IsAvailable = true,
                    TotalCount = totalCount,
                    Results = results
                };
            }
            catch (DllNotFoundException)
            {
                return new EverythingSearchResponse
                {
                    Success = false,
                    IsAvailable = false,
                    ErrorMessage = "缺少 Everything64.dll 运行库。"
                };
            }
            catch (EntryPointNotFoundException)
            {
                return new EverythingSearchResponse
                {
                    Success = false,
                    IsAvailable = false,
                    ErrorMessage = "Everything SDK 版本不兼容。"
                };
            }
            catch (Exception ex)
            {
                return new EverythingSearchResponse
                {
                    Success = false,
                    IsAvailable = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }

    public static bool IsIpcReachable()
    {
        try
        {
            var majorVersion = EverythingApi.Everything_GetMajorVersion();
            return majorVersion > 0 || EverythingApi.Everything_GetLastError() != EverythingErrorIpc;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDatabaseLoaded()
    {
        try
        {
            return EverythingApi.Everything_IsDBLoaded();
        }
        catch
        {
            return false;
        }
    }

    private static EverythingSearchResponse BuildFailureResponse(uint errorCode)
    {
        var message = errorCode switch
        {
            EverythingErrorIpc => "Everything 未运行，或 IPC 不可用。",
            _ => $"Everything 查询失败，错误码：{errorCode}"
        };

        return new EverythingSearchResponse
        {
            Success = false,
            IsAvailable = errorCode != EverythingErrorIpc,
            ErrorMessage = message
        };
    }

    [ThreadStatic]
    private static StringBuilder? _threadPathBuffer;

    private static StringBuilder GetThreadPathBuffer()
    {
        var buffer = _threadPathBuffer;
        if (buffer == null)
        {
            buffer = new StringBuilder(1024);
            _threadPathBuffer = buffer;
        }
        else
        {
            buffer.Clear();
        }

        return buffer;
    }

    private static string GetResultFullPath(uint index)
    {
        var buffer = GetThreadPathBuffer();
        if (buffer.Capacity < 1024)
        {
            buffer.EnsureCapacity(1024);
        }

        EverythingApi.Everything_GetResultFullPathNameW(index, buffer, (uint)buffer.Capacity);
        if (buffer.Length > 0)
        {
            return buffer.ToString();
        }

        buffer.EnsureCapacity(32768);
        EverythingApi.Everything_GetResultFullPathNameW(index, buffer, (uint)buffer.Capacity);
        return buffer.ToString();
    }

    private static string ResolveDirectoryPath(string fullPath, bool isFolder)
    {
        if (isFolder)
        {
            return Path.GetDirectoryName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                ?? fullPath;
        }

        return Path.GetDirectoryName(fullPath) ?? fullPath;
    }

    private static string? TryGetSizeText(uint index)
    {
        if (!EverythingApi.Everything_GetResultSize(index, out var size) || size < 0)
        {
            return null;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.##} {units[unitIndex]}";
    }

    private static class EverythingApi
    {
        [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
        internal static extern void Everything_SetSearchW(string lpSearchString);

        [DllImport("Everything64.dll")]
        internal static extern void Everything_SetRequestFlags(uint requestFlags);

        [DllImport("Everything64.dll")]
        internal static extern void Everything_SetSort(uint sortType);

        [DllImport("Everything64.dll")]
        internal static extern void Everything_SetMax(uint max);

        [DllImport("Everything64.dll")]
        internal static extern void Everything_SetOffset(uint offset);

        [DllImport("Everything64.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("Everything64.dll")]
        internal static extern uint Everything_GetLastError();

        [DllImport("Everything64.dll")]
        internal static extern uint Everything_GetNumResults();

        [DllImport("Everything64.dll")]
        internal static extern uint Everything_GetTotResults();

        [DllImport("Everything64.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_IsFolderResult(uint index);

        [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
        internal static extern uint Everything_GetResultFullPathNameW(uint index, StringBuilder buffer, uint maxCount);

        [DllImport("Everything64.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_GetResultSize(uint index, out long size);

        [DllImport("Everything64.dll")]
        internal static extern void Everything_Reset();

        [DllImport("Everything64.dll")]
        internal static extern uint Everything_GetMajorVersion();

        [DllImport("Everything64.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Everything_IsDBLoaded();
    }
}
