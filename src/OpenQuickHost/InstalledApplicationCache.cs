using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

internal static class InstalledApplicationCache
{
    private const int FileVersion = 1;

    private static string CachePath => Path.Combine(HostAssets.DataRootPath, "installed-applications-cache.json");

    public static IReadOnlyList<InstalledApplicationEntry> TryLoad()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return [];
            }

            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath));
            if (file is not { Version: FileVersion, Entries: { Count: > 0 } entries })
            {
                return [];
            }

            return entries.Select(static entry => new InstalledApplicationEntry(
                entry.ExtensionId ?? string.Empty,
                entry.Title ?? string.Empty,
                entry.Subtitle ?? string.Empty,
                entry.LaunchTarget ?? string.Empty,
                entry.DisplayPath ?? string.Empty,
                entry.IconPath,
                entry.Keywords ?? [],
                entry.Arguments,
                entry.WorkingDirectory)).ToArray();
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"InstalledApplicationCache read failed: {ex.Message}");
            return [];
        }
    }

    public static void Save(IReadOnlyList<InstalledApplicationEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(HostAssets.DataRootPath);
            var file = new CacheFile
            {
                Version = FileVersion,
                Entries = entries.Select(static entry => new CacheEntry
                {
                    ExtensionId = entry.ExtensionId,
                    Title = entry.Title,
                    Subtitle = entry.Subtitle,
                    LaunchTarget = entry.LaunchTarget,
                    DisplayPath = entry.DisplayPath,
                    IconPath = entry.IconPath,
                    Keywords = entry.Keywords.ToArray(),
                    Arguments = entry.Arguments,
                    WorkingDirectory = entry.WorkingDirectory
                }).ToList()
            };
            var json = JsonSerializer.Serialize(file);
            var temporary = CachePath + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, CachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"InstalledApplicationCache write failed: {ex.Message}");
        }
    }

    public static string Fingerprint(IReadOnlyList<InstalledApplicationEntry> entries)
    {
        var builder = new StringBuilder();
        foreach (var entry in entries.OrderBy(static item => item.ExtensionId, StringComparer.Ordinal))
        {
            builder.Append(entry.ExtensionId).Append('\n')
                .Append(entry.LaunchTarget).Append('\n')
                .Append(entry.Arguments).Append('\n')
                .Append(entry.IconPath).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public List<CacheEntry> Entries { get; set; } = [];
    }

    private sealed class CacheEntry
    {
        public string? ExtensionId { get; set; }
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
        public string? LaunchTarget { get; set; }
        public string? DisplayPath { get; set; }
        public string? IconPath { get; set; }
        public string[]? Keywords { get; set; }
        public string? Arguments { get; set; }
        public string? WorkingDirectory { get; set; }
    }
}
