using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenQuickHost.Sync;

/// <summary>
/// Per-device Git update preferences; deliberately excluded from extension
/// packages. Off by default, and never writes into the Git object directory.
/// </summary>
internal static class GitExtensionUpdatePolicyStore
{
    private static readonly object Gate = new();
    private static string StatePath => HostAssets.ResolveDataFilePath("git-extension-update-policies.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    internal static IReadOnlyDictionary<string, GitExtensionUpdatePolicy> Load() => LoadFrom(StatePath);

    internal static IReadOnlyDictionary<string, GitExtensionUpdatePolicy> LoadFrom(string path)
    {
        lock (Gate)
        {
            if (!File.Exists(path)) return new Dictionary<string, GitExtensionUpdatePolicy>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var list = JsonSerializer.Deserialize<List<GitExtensionUpdatePolicy>>(File.ReadAllText(path), JsonOptions) ?? [];
                return list.Where(p => ValidId(p.ExtensionId))
                    .GroupBy(p => p.ExtensionId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Last().Normalized(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                HostAssets.AppendLog($"Git extension update policy read failed: {ex.GetType().Name}; defaults to OFF.");
                return new Dictionary<string, GitExtensionUpdatePolicy>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    internal static void Save(GitExtensionUpdatePolicy policy) => SaveTo(StatePath, policy);

    internal static void SaveTo(string path, GitExtensionUpdatePolicy policy)
    {
        if (!ValidId(policy.ExtensionId)) throw new ArgumentException("Invalid extension id.");
        var normalized = policy.Normalized();
        if (!GitExtensionUpdateService.ValidRemote(normalized.Remote) ||
            !GitExtensionUpdateService.ValidBranch(normalized.Branch))
            throw new ArgumentException("Invalid Git remote or branch.");

        lock (Gate)
        {
            var map = new Dictionary<string, GitExtensionUpdatePolicy>(LoadFrom(path), StringComparer.OrdinalIgnoreCase);
            map[normalized.ExtensionId] = normalized;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(map.Values.OrderBy(x => x.ExtensionId).ToArray(), JsonOptions));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    private static bool ValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length < 150 &&
        !id.StartsWith('.') && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !id.Contains('/') && !id.Contains('\\');
}

internal sealed record GitExtensionUpdatePolicy
{
    public string ExtensionId { get; init; } = "";
    public string Mode { get; init; } = "off"; // off | notify | auto
    public string Frequency { get; init; } = "startup"; // startup | hourly | daily
    public string Remote { get; init; } = "origin";
    public string Branch { get; init; } = "main";
    public DateTimeOffset LastCheckedUtc { get; init; }
    public string LastSeenCommit { get; init; } = "";

    internal GitExtensionUpdatePolicy Normalized() => this with
    {
        Mode = Mode is "notify" or "auto" ? Mode : "off",
        Frequency = Frequency is "hourly" or "daily" ? Frequency : "startup",
        Remote = string.IsNullOrWhiteSpace(Remote) ? "origin" : Remote.Trim(),
        Branch = string.IsNullOrWhiteSpace(Branch) ? "main" : Branch.Trim()
    };
}

internal sealed record GitExtensionUpdateOutcome(
    string ExtensionId, string Status, string Message, string RemoteCommit = "")
{
    public bool NotifyUser => Status is "update-available" or "updated" or "conflict" or "error";
}

/// <summary>
/// Explicit per-repository checks: git fetch only (never push/commit/merge),
/// except a clean worktree's verified fast-forward when user opted in.
/// </summary>
internal static class GitExtensionUpdateService
{
    private static readonly Regex GitName = new(@"^[A-Za-z0-9][A-Za-z0-9._/-]{0,150}$", RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RepositoryLocks = new(StringComparer.OrdinalIgnoreCase);

    internal static bool ValidRemote(string value) =>
        !string.IsNullOrWhiteSpace(value) && GitName.IsMatch(value) &&
        !value.Contains("..", StringComparison.Ordinal) && !value.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);

    internal static bool ValidBranch(string value) =>
        ValidRemote(value) && !value.Contains("//", StringComparison.Ordinal) &&
        !value.EndsWith('/') && !value.EndsWith('.');

    internal static bool IsDue(GitExtensionUpdatePolicy policy, DateTimeOffset now, bool onStartup)
    {
        var p = policy.Normalized();
        if (p.Mode == "off") return false;
        return p.Frequency switch
        {
            "startup" => onStartup,
            "hourly" => now - p.LastCheckedUtc >= TimeSpan.FromHours(1),
            "daily" => now - p.LastCheckedUtc >= TimeSpan.FromDays(1),
            _ => false
        };
    }

    internal static async Task<GitExtensionUpdateOutcome> CheckAsync(
        string directory, GitExtensionUpdatePolicy policy, CancellationToken cancellationToken = default)
    {
        var p = policy.Normalized();
        if (p.Mode == "off") return new(p.ExtensionId, "disabled", "Git 更新检查已关闭");
        if (!ValidRemote(p.Remote) || !ValidBranch(p.Branch))
            return new(p.ExtensionId, "error", "Git 远端或分支名称不合法");
        if (!Directory.Exists(directory) || !ExtensionGitSafety.HasGitWorktree(directory))
            return new(p.ExtensionId, "not-a-repo", "此小程序还不是 Git 仓库");

        var gate = RepositoryLocks.GetOrAdd(Path.GetFullPath(directory), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var repoTop = await Git(directory, cancellationToken, "rev-parse", "--show-toplevel");
            if (repoTop.ExitCode != 0 ||
                !Path.GetFullPath(repoTop.Stdout.Trim()).TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return new(p.ExtensionId, "error", "小程序目录必须是 Git 仓库根目录");

            var remote = await Git(directory, cancellationToken, "remote", "get-url", p.Remote);
            if (remote.ExitCode != 0) return new(p.ExtensionId, "error", $"没有配置 Git remote：{p.Remote}");

            // Only fetch from the locally configured remote, never from metadata
            // contained in a cloud ZIP or an untrusted remote-provided URL.
            var fetch = await Git(directory, cancellationToken, "fetch", "--no-tags", "--quiet", p.Remote, p.Branch);
            if (fetch.ExitCode != 0)
                return new(p.ExtensionId, "error", $"获取远端提交失败（{p.Remote}/{p.Branch}）：{ShortError(fetch.Stderr)}");

            var head = await Git(directory, cancellationToken, "rev-parse", "HEAD");
            var incoming = await Git(directory, cancellationToken, "rev-parse", "FETCH_HEAD");
            if (head.ExitCode != 0 || incoming.ExitCode != 0)
                return new(p.ExtensionId, "error", "无法比较本地与远端 Git 提交");
            var sha = incoming.Stdout.Trim();
            if (head.Stdout.Trim() == sha) return new(p.ExtensionId, "unchanged", "Git 已是最新", sha);

            var ancestor = await Git(directory, cancellationToken, "merge-base", "--is-ancestor", "HEAD", "FETCH_HEAD");
            if (ancestor.ExitCode != 0)
                return new(p.ExtensionId, "conflict", "Git 分支已分叉：需要手动合并，不会覆盖本地源码", sha);

            if (p.Mode == "notify")
                return new(p.ExtensionId, "update-available", $"发现 {p.Remote}/{p.Branch} 新提交 {sha[..Math.Min(8, sha.Length)]}", sha);

            var clean = await Git(directory, cancellationToken, "status", "--porcelain", "--untracked-files=all");
            var symbolic = await Git(directory, cancellationToken, "symbolic-ref", "-q", "HEAD");
            if (clean.ExitCode != 0 || clean.Stdout.Length != 0 || symbolic.ExitCode != 0)
                return new(p.ExtensionId, "conflict", "Git 有未提交文件或处于分离 HEAD，已停止自动更新", sha);

            // This never forces a merge, commits, pushes or restarts services.
            var merge = await Git(directory, cancellationToken, "merge", "--ff-only", "FETCH_HEAD");
            if (merge.ExitCode != 0)
                return new(p.ExtensionId, "conflict", $"无法安全快进：{ShortError(merge.Stderr)}", sha);
            return new(p.ExtensionId, "updated", $"Git 已安全更新到 {sha[..Math.Min(8, sha.Length)]}，运行中的服务未重启", sha);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new(p.ExtensionId, "error", $"Git 检查失败：{ex.Message}");
        }
        finally { gate.Release(); }
    }

    private static string ShortError(string s) =>
        string.IsNullOrWhiteSpace(s) ? "请检查 Git 日志或网络" : s.Trim()[..Math.Min(s.Trim().Length, 180)];

    private static async Task<(int ExitCode, string Stdout, string Stderr)> Git(
        string directory, CancellationToken cancellationToken, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Failed to start Git.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, "", "Git operation timed out");
        }
    }
}