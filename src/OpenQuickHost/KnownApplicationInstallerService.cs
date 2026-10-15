using System.Collections.Concurrent;
using System.Diagnostics;

namespace OpenQuickHost;

internal sealed record KnownApplicationInstallResult(
    bool Success,
    InstalledApplicationEntry? InstalledEntry,
    string Message,
    string? Details = null);

internal static class KnownApplicationInstallerService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<KnownApplicationInstallResult> EnsureInstalledAsync(
        KnownApplicationDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var gate = InstallLocks.GetOrAdd(definition.Id, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = FindInstalledEntry(definition);
            if (existing != null)
            {
                return new KnownApplicationInstallResult(
                    true,
                    existing,
                    $"{definition.DisplayName} 已安装。");
            }

            var packageIds = new[] { definition.WingetPackageId }
                .Concat(definition.AlternateWingetPackageIds)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var failures = new List<string>();
            foreach (var packageId in packageIds)
            {
                HostAssets.AppendLog(
                    $"Known app install started: app={definition.Id}, package={packageId}, channel=winget.");

                var install = await RunWingetInstallAsync(packageId, cancellationToken);
                if (install.ExitCode != 0)
                {
                    failures.Add($"{packageId}: exit={install.ExitCode}, {Compact(install.Error)}");
                    HostAssets.AppendLog(
                        $"Known app install failed: app={definition.Id}, package={packageId}, exit={install.ExitCode}, error={Compact(install.Error)}");
                    continue;
                }

                for (var attempt = 0; attempt < 15; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var resolved = FindInstalledEntry(definition);
                    if (resolved != null)
                    {
                        HostAssets.AppendLog(
                            $"Known app install verified: app={definition.Id}, package={packageId}, target={resolved.LaunchTarget}.");
                        return new KnownApplicationInstallResult(
                            true,
                            resolved,
                            $"已安装并识别 {definition.DisplayName}。");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }

                failures.Add($"{packageId}: winget 成功返回，但燕子暂未识别到应用入口");
            }

            var details = failures.Count == 0 ? "没有可用的 winget 安装配方。" : string.Join(" | ", failures);
            return new KnownApplicationInstallResult(
                false,
                null,
                $"自动安装 {definition.DisplayName} 失败，可改用官方下载页。",
                details);
        }
        catch (OperationCanceledException)
        {
            return new KnownApplicationInstallResult(false, null, $"已取消安装 {definition.DisplayName}。");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Known app install exception: app={definition.Id}, error={ex}");
            return new KnownApplicationInstallResult(
                false,
                null,
                $"自动安装 {definition.DisplayName} 失败：{ex.Message}",
                ex.ToString());
        }
        finally
        {
            gate.Release();
        }
    }

    public static void OpenOfficialDownloadPage(KnownApplicationDefinition definition)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = definition.OfficialDownloadUrl,
            UseShellExecute = true
        });
    }

    private static InstalledApplicationEntry? FindInstalledEntry(KnownApplicationDefinition definition)
    {
        return InstalledApplicationCatalog.Load()
            .FirstOrDefault(entry =>
                entry.ExtensionId.Equals(definition.ExtensionId, StringComparison.OrdinalIgnoreCase) &&
                !KnownApplicationCatalog.IsInstallPlaceholder(entry.LaunchTarget));
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunWingetInstallAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "winget.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[]
        {
            "install",
            "--id", packageId,
            "--exact",
            "--source", "winget",
            "--accept-source-agreements",
            "--accept-package-agreements",
            "--disable-interactivity",
            "--silent"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 winget。");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string Compact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "无错误输出";
        var compact = string.Join(" ", text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= 400 ? compact : compact[..400];
    }
}
