using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

/// <summary>
/// Detects the YanZi Chromium extension per browser profile and stages the bundled unpacked
/// extension at a stable, per-user path. It does not bypass browser permission prompts.
/// </summary>
internal static class BrowserHelperBootstrap
{
    internal sealed record BrowserProfile(
        string Browser, string Executable, string UserDataDirectory, string Profile,
        bool Installed, bool Enabled, string? ExtensionId, string? Version);

    internal static string StableDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost", "BrowserHelper");

    internal static string? FindBundledDirectory()
    {
        var root = AppContext.BaseDirectory;
        foreach (var path in new[]
        {
            Path.Combine(root, "browser-extension"),
            Path.GetFullPath(Path.Combine(root, @"..\..\..\..\browser-extension")),
            Path.GetFullPath(Path.Combine(root, @"..\..\..\browser-extension"))
        })
        {
            if (File.Exists(Path.Combine(path, "manifest.json")))
                return path;
        }
        return null;
    }

    private static bool IsYanziManifest(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            return false;
        var value = name.GetString() ?? "";
        return value.Contains("燕子浏览器助手", StringComparison.OrdinalIgnoreCase)
               || value.Contains("Yanzi Browser Helper", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<BrowserProfile> Inspect()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var browsers = new[]
        {
            ("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"), "msedge.exe", "Microsoft", "Edge", "Application"),
            ("Chrome", Path.Combine(local, "Google", "Chrome", "User Data"), "chrome.exe", "Google", "Chrome", "Application"),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), "brave.exe", "BraveSoftware", "Brave-Browser", "Application")
        };
        var results = new List<BrowserProfile>();
        foreach (var (browser, dataDir, exeName, company, product, application) in browsers)
        {
            var executable = FindExecutable(exeName, company, product, application);
            if (executable is null) continue;
            string[] profiles;
            try
            {
                profiles = Directory.Exists(dataDir)
                    ? Directory.GetDirectories(dataDir)
                        .Where(p => Path.GetFileName(p).Equals("Default", StringComparison.OrdinalIgnoreCase)
                                    || Path.GetFileName(p).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                    : Array.Empty<string>();
            }
            catch (IOException) { profiles = Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { profiles = Array.Empty<string>(); }

            if (profiles.Length == 0)
            {
                results.Add(new BrowserProfile(browser, executable, dataDir, "Default",
                    false, false, null, null));
                continue;
            }
            foreach (var profile in profiles)
            {
                var installed = InspectPreferences(Path.Combine(profile, "Preferences"));
                if (!installed.Installed)
                    installed = InspectPreferences(Path.Combine(profile, "Secure Preferences"));
                results.Add(new BrowserProfile(browser, executable, dataDir, Path.GetFileName(profile),
                    installed.Installed, installed.Enabled, installed.Id, installed.Version));
            }
        }
        return results;
    }

    internal static (bool Installed, bool Enabled, string? Id, string? Version) InspectPreferences(string preferencesPath)
    {
        try
        {
            var info = new FileInfo(preferencesPath);
            if (!info.Exists || info.Length > 32 * 1024 * 1024) return default;
            using var stream = File.Open(preferencesPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var prefs = JsonDocument.Parse(stream);
            if (!prefs.RootElement.TryGetProperty("extensions", out var extensions)
                || !extensions.TryGetProperty("settings", out var settings)
                || settings.ValueKind != JsonValueKind.Object)
                return default;

            foreach (var item in settings.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object) continue;
                var setting = item.Value;
                string? version = null;
                bool isYanzi = false;
                if (setting.TryGetProperty("manifest", out var manifest)
                    && manifest.ValueKind == JsonValueKind.Object)
                {
                    isYanzi = IsYanziManifest(manifest);
                    if (isYanzi && manifest.TryGetProperty("version", out var v)
                        && v.ValueKind == JsonValueKind.String)
                        version = v.GetString();
                }
                else if (setting.TryGetProperty("path", out var unpackedPath)
                         && unpackedPath.ValueKind == JsonValueKind.String)
                {
                    // Edge may record unpacked extensions only in "Secure Preferences",
                    // without a manifest or state property. Verify their real package identity.
                    var file = Path.Combine(unpackedPath.GetString() ?? "", "manifest.json");
                    if (File.Exists(file))
                    {
                        using var package = JsonDocument.Parse(File.ReadAllText(file));
                        isYanzi = IsYanziManifest(package.RootElement);
                        if (isYanzi && package.RootElement.TryGetProperty("version", out var v)
                            && v.ValueKind == JsonValueKind.String)
                            version = v.GetString();
                    }
                }
                if (!isYanzi) continue;

                var explicitDisabled = setting.TryGetProperty("state", out var state)
                                       && state.ValueKind == JsonValueKind.Number
                                       && state.TryGetInt32(out var stateValue) && stateValue != 1;
                var disableReasons = setting.TryGetProperty("disable_reasons", out var reasons)
                                     && reasons.ValueKind == JsonValueKind.Number
                                     && reasons.TryGetInt32(out var reasonValue) && reasonValue != 0;
                return (true, !explicitDisabled && !disableReasons, item.Name, version);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                  or ArgumentException)
        {
            // The browser can update these files concurrently; an unreadable profile is unknown.
        }
        return default;
    }

    /// <summary>
    /// Writes a public, port-only connection hint alongside the unpacked extension.
    /// No API token, password or private user setting is copied into the browser package.
    /// </summary>
    internal static string WriteRuntimeConfiguration(int port, string? destinationOverride = null)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var directory = destinationOverride ?? StableDirectory;
        if (!File.Exists(Path.Combine(directory, "manifest.json")))
            throw new FileNotFoundException("浏览器助手稳定目录尚未初始化。");
        var path = Path.Combine(directory, "yanzi-connection.json");
        var contents = JsonSerializer.Serialize(new { apiPort = port });
        if (File.Exists(path) && File.ReadAllText(path) == contents) return path;
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, contents, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return path;
    }

    private static string? FindExecutable(string exeName, params string[] relative)
    {
        foreach (var basePath in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        })
        {
            if (string.IsNullOrEmpty(basePath)) continue;
            var components = new[] { basePath }.Concat(relative).Append(exeName).ToArray();
            var path = Path.Combine(components);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// Publish the bundled extension outside the versioned installation directory.
    /// The final directory name remains unchanged across Yanzi application upgrades.
    /// </summary>
    internal static string EnsureStableBundle(string source, string? destinationOverride = null)
    {
        if (!File.Exists(Path.Combine(source, "manifest.json")))
            throw new FileNotFoundException("浏览器助手安装包缺少 manifest.json。");

        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "manifest.json"))))
            if (!IsYanziManifest(manifest.RootElement))
                throw new InvalidDataException("浏览器助手安装包身份校验失败。");

        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                return !relative.StartsWith("scratch/", StringComparison.OrdinalIgnoreCase)
                       && !relative.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase)
                       && !relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(source, file).Replace('\\', '/')));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }
        var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        var target = destinationOverride ?? StableDirectory;
        var marker = Path.Combine(target, ".yanzi-bundle-sha256");
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == fingerprint)
            return target;

        // Preserve a verified independent browser helper update across host restarts.
        // A newer bundled extension is still allowed to replace this channel version.
        var remoteCommitMarker = Path.Combine(target, ".yanzi-source-commit");
        var remoteTreeMarker = Path.Combine(target, ".yanzi-source-tree-sha256");
        if (File.Exists(remoteCommitMarker) && File.Exists(remoteTreeMarker)
            && File.Exists(Path.Combine(target, "manifest.json")))
        {
            try
            {
                var commit = File.ReadAllText(remoteCommitMarker).Trim();
                var digest = File.ReadAllText(remoteTreeMarker).Trim();
                if (commit.Length == 40 && commit.All(Uri.IsHexDigit)
                    && digest.Length == 64 && digest.All(Uri.IsHexDigit))
                {
                    using var installed = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "manifest.json")));
                    using var bundled = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")));
                    if (IsYanziManifest(installed.RootElement) && IsYanziManifest(bundled.RootElement)
                        && installed.RootElement.TryGetProperty("version", out var installedVersion)
                        && bundled.RootElement.TryGetProperty("version", out var bundledVersion)
                        && Version.TryParse(installedVersion.GetString(), out var installedParsed)
                        && Version.TryParse(bundledVersion.GetString(), out var bundledParsed)
                        && installedParsed >= bundledParsed)
                        return target;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException or ArgumentException)
            {
                // Ignore corrupt remote metadata and retain the bundled fallback.
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = target + ".previous";
        try
        {
            foreach (var file in files)
            {
                var dest = Path.Combine(staging, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
            File.WriteAllText(Path.Combine(staging, ".yanzi-bundle-sha256"), fingerprint);
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(staging, target); }
            catch
            {
                if (!Directory.Exists(target) && Directory.Exists(backup))
                    Directory.Move(backup, target);
                throw;
            }
            // Keep only one rollback copy and never accumulate per-version extension caches.
            return target;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static void OpenExtensionSettings(BrowserProfile profile)
    {
        var url = profile.Browser switch
        {
            "Edge" => "edge://extensions",
            "Brave" => "brave://extensions",
            _ => "chrome://extensions"
        };
        var start = new ProcessStartInfo(profile.Executable) { UseShellExecute = true };
        start.ArgumentList.Add(url);
        Process.Start(start);
    }
}
