using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziGitCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return CreateDefinition("git.status", "读取 Git 仓库分支、ahead/behind 和工作区状态", StatusAsync);
        yield return CreateDefinition("git.log", "读取 Git 提交历史", LogAsync,
            """{"type":"object","properties":{"path":{"type":"string","minLength":1},"count":{"type":"integer","minimum":1,"maximum":100}},"required":["path"],"additionalProperties":false}""");
        yield return CreateDefinition("git.diff", "读取 Git 工作区或暂存区差异", DiffAsync,
            """{"type":"object","properties":{"path":{"type":"string","minLength":1},"staged":{"type":"boolean"},"maxChars":{"type":"integer","minimum":1000,"maximum":200000}},"required":["path"],"additionalProperties":false}""");
        yield return CreateDefinition("git.branch.list", "列出 Git 本地/远程分支及当前分支", BranchListAsync,
            """{"type":"object","properties":{"path":{"type":"string","minLength":1},"includeRemote":{"type":"boolean"}},"required":["path"],"additionalProperties":false}""");
        yield return CreateDefinition("git.remote.list", "列出 Git remotes，并移除 URL 中的认证信息", RemoteListAsync);

        yield return new()
        {
            Name = "git.fetch",
            Description = "执行 git fetch --prune，不修改工作区文件",
            Permissions = ["file.read", "network.write"],
            Category = "git",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"path":{"type":"string","minLength":1},"remote":{"type":"string"}},"required":["path"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = FetchAsync
        };

        yield return new()
        {
            Name = "git.pull",
            Description = "执行 git pull --ff-only，禁止自动产生 merge commit",
            Permissions = ["file.read", "file.write", "network.write"],
            Category = "git",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"path":{"type":"string","minLength":1},"remote":{"type":"string"},"branch":{"type":"string"}},"required":["path"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = PullAsync
        };

        yield return new()
        {
            Name = "git.commit",
            Description = "创建 Git 提交；stageAll=true 时先执行 git add -A，否则只提交已暂存内容",
            Permissions = ["file.read", "file.write"],
            Category = "git",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"path":{"type":"string","minLength":1},"message":{"type":"string","minLength":1},"stageAll":{"type":"boolean"}},"required":["path","message"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = CommitAsync
        };

        yield return new()
        {
            Name = "git.push",
            Description = "执行普通 git push；不支持 force、force-with-lease 等强制推送",
            Permissions = ["file.read", "network.write"],
            Category = "git",
            RiskLevel = "medium",
            InputSchema = YanziCapabilitySchema.Parse("""{"type":"object","properties":{"path":{"type":"string","minLength":1},"remote":{"type":"string"},"branch":{"type":"string"},"setUpstream":{"type":"boolean"}},"required":["path"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = PushAsync
        };
    }

    private static YanziCapabilityProviderDefinition CreateDefinition(
        string name,
        string description,
        Func<object?, Task<object?>> handler,
        string? schema = null)
        => new()
        {
            Name = name,
            Description = description,
            Permissions = ["file.read"],
            Category = "git",
            InputSchema = YanziCapabilitySchema.Parse(schema ??
                """{"type":"object","properties":{"path":{"type":"string","minLength":1}},"required":["path"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = handler
        };

    private static async Task<object?> StatusAsync(object? payload)
    {
        var repo = await ResolveRepoAsync(GetPath(payload));
        var result = await RunGitAsync(repo, ["status", "--porcelain=v2", "--branch"]);
        EnsureSuccess(result, "git status");

        string? branch = null;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var changes = new List<string>();

        foreach (var line in result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                branch = line["# branch.head ".Length..].Trim();
            else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
                upstream = line["# branch.upstream ".Length..].Trim();
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                foreach (var part in line["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.StartsWith('+') && int.TryParse(part[1..], out var a)) ahead = a;
                    if (part.StartsWith('-') && int.TryParse(part[1..], out var b)) behind = b;
                }
            }
            else if (!line.StartsWith('#'))
                changes.Add(line);
        }

        return new
        {
            repository = repo,
            branch,
            upstream,
            ahead,
            behind,
            clean = changes.Count == 0,
            changeCount = changes.Count,
            changes = changes.Take(500).ToArray()
        };
    }

    private static async Task<object?> LogAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var repo = await ResolveRepoAsync(input.GetProperty("path").GetString()!);
        var count = input.TryGetProperty("count", out var c) && c.TryGetInt32(out var n)
            ? Math.Clamp(n, 1, 100) : 20;

        const string sep = "\u001f";
        const string end = "\u001e";
        var format = $"%H{sep}%h{sep}%an{sep}%ae{sep}%aI{sep}%s{end}";
        var result = await RunGitAsync(repo, ["log", $"-n{count}", $"--pretty=format:{format}"]);
        EnsureSuccess(result, "git log");

        var items = result.Stdout.Split(end, StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.Trim('\r', '\n'))
            .Where(record => record.Length > 0)
            .Select(record =>
            {
                var parts = record.Split(sep);
                return new
                {
                    hash = parts.ElementAtOrDefault(0) ?? "",
                    shortHash = parts.ElementAtOrDefault(1) ?? "",
                    authorName = parts.ElementAtOrDefault(2) ?? "",
                    authorEmail = parts.ElementAtOrDefault(3) ?? "",
                    authoredAt = parts.ElementAtOrDefault(4) ?? "",
                    subject = parts.ElementAtOrDefault(5) ?? ""
                };
            }).ToArray();

        return new { repository = repo, count = items.Length, items };
    }

    private static async Task<object?> DiffAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var repo = await ResolveRepoAsync(input.GetProperty("path").GetString()!);
        var staged = input.TryGetProperty("staged", out var s) && s.ValueKind == JsonValueKind.True;
        var maxChars = input.TryGetProperty("maxChars", out var m) && m.TryGetInt32(out var n)
            ? Math.Clamp(n, 1000, 200000) : 50000;

        var args = new List<string> { "diff", "--no-ext-diff", "--no-color" };
        if (staged) args.Add("--cached");
        var result = await RunGitAsync(repo, args);
        EnsureSuccess(result, "git diff");

        var clipped = result.Stdout.Length > maxChars;
        return new { repository = repo, staged, clipped, diff = clipped ? result.Stdout[..maxChars] : result.Stdout };
    }

    private static async Task<object?> BranchListAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var repo = await ResolveRepoAsync(input.GetProperty("path").GetString()!);
        var includeRemote = input.TryGetProperty("includeRemote", out var r) && r.ValueKind == JsonValueKind.True;

        var args = new List<string> { "for-each-ref", "--format=%(refname)|%(refname:short)|%(HEAD)|%(upstream:short)|%(objectname:short)", "refs/heads" };
        if (includeRemote) args.Add("refs/remotes");
        var result = await RunGitAsync(repo, args);
        EnsureSuccess(result, "git branch list");

        var items = result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var parts = line.Split('|');
                var full = parts.ElementAtOrDefault(0) ?? "";
                return new
                {
                    name = parts.ElementAtOrDefault(1) ?? "",
                    current = (parts.ElementAtOrDefault(2) ?? "").Trim() == "*",
                    upstream = parts.ElementAtOrDefault(3) ?? "",
                    shortHash = parts.ElementAtOrDefault(4) ?? "",
                    remote = full.StartsWith("refs/remotes/", StringComparison.Ordinal)
                };
            }).ToArray();

        return new { repository = repo, count = items.Length, items };
    }

    private static async Task<object?> RemoteListAsync(object? payload)
    {
        var repo = await ResolveRepoAsync(GetPath(payload));
        var names = await RunGitAsync(repo, ["remote"]);
        EnsureSuccess(names, "git remote");

        var items = new List<object>();
        foreach (var name in names.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                     .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal))
        {
            var fetch = await RunGitAsync(repo, ["remote", "get-url", name]);
            var push = await RunGitAsync(repo, ["remote", "get-url", "--push", name]);
            items.Add(new
            {
                name,
                fetchUrl = fetch.ExitCode == 0 ? SanitizeUrl(fetch.Stdout.Trim()) : null,
                pushUrl = push.ExitCode == 0 ? SanitizeUrl(push.Stdout.Trim()) : null
            });
        }

        return new { repository = repo, count = items.Count, items };
    }

    private static async Task<object?> FetchAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var repo = await ResolveRepoAsync(input.GetProperty("path").GetString()!);
        var remote = OptionalToken(input, "remote");
        var args = new List<string> { "fetch", "--prune" };
        if (remote != null) args.Add(remote);

        var result = await RunGitAsync(repo, args, 300);
        EnsureSuccess(result, "git fetch");
        return CommandResult(repo, result);
    }

    private static async Task<object?> PullAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var repo = await ResolveRepoAsync(input.GetProperty("path").GetString()!);
        var remote = OptionalToken(input, "remote");
        var branch = OptionalToken(input, "branch");
        if (branch != null && remote == null)
            throw new ArgumentException("指定 branch 时必须同时指定 remote。");

        var args = new List<string> { "pull", "--ff-only" };
        if (remote != null)
        {
            args.Add(remote);
            if (branch != null) args.Add(branch);
        }

        var result = await RunGitAsync(repo, args, 300);
        EnsureSuccess(result, "git pull --ff-only");
        return CommandResult(repo, result);
    }

    private static async Task<object?> CommitAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var repo = await ResolveRepoAsync(input.GetProperty("path").GetString()!);
        var message = input.GetProperty("message").GetString()?.Trim() ?? "";
        if (message.Length == 0 || message.Length > 5000 || message.IndexOf('\0') >= 0)
            throw new ArgumentException("commit message 无效。");

        var stageAll = input.TryGetProperty("stageAll", out var stage) &&
                       stage.ValueKind == JsonValueKind.True;
        if (stageAll)
        {
            var add = await RunGitAsync(repo, ["add", "-A"]);
            EnsureSuccess(add, "git add -A");
        }

        var result = await RunGitAsync(repo, ["commit", "-m", message], 120);
        EnsureSuccess(result, "git commit");

        var head = await RunGitAsync(repo, ["rev-parse", "HEAD"]);
        return new
        {
            repository = repo,
            committed = true,
            hash = head.ExitCode == 0 ? head.Stdout.Trim() : null,
            output = Tail((result.Stdout + result.Stderr).Trim(), 12000)
        };
    }

    private static async Task<object?> PushAsync(object? payload)
    {
        var input = (JsonElement)payload!;
        var repo = await ResolveRepoAsync(input.GetProperty("path").GetString()!);
        var remote = OptionalToken(input, "remote");
        var branch = OptionalToken(input, "branch");
        var setUpstream = input.TryGetProperty("setUpstream", out var upstream) &&
                          upstream.ValueKind == JsonValueKind.True;

        if (branch != null && remote == null)
            throw new ArgumentException("指定 branch 时必须同时指定 remote。");
        if (setUpstream && (remote == null || branch == null))
            throw new ArgumentException("setUpstream=true 时必须同时提供 remote 和 branch。");

        var args = new List<string> { "push" };
        if (setUpstream) args.Add("-u");
        if (remote != null)
        {
            args.Add(remote);
            if (branch != null) args.Add(branch);
        }

        var result = await RunGitAsync(repo, args, 300);
        EnsureSuccess(result, "git push");
        return CommandResult(repo, result);
    }

    private static string? OptionalToken(JsonElement input, string property)
    {
        if (!input.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            return null;

        var text = value.GetString()!.Trim();
        if (text.Length > 250 ||
            text.StartsWith('-') ||
            text.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch)))
            throw new ArgumentException($"{property} 无效。");
        return text;
    }

    private static object CommandResult(string repo, GitResult result) => new
    {
        repository = repo,
        success = result.ExitCode == 0,
        exitCode = result.ExitCode,
        output = Tail((result.Stdout + result.Stderr).Trim(), 16000)
    };

    private static async Task<string> ResolveRepoAsync(string raw)
    {
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"')));
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("路径不存在：" + path);

        var result = await RunGitAsync(path, ["rev-parse", "--show-toplevel"]);
        EnsureSuccess(result, "验证 Git 仓库");
        return Path.GetFullPath(result.Stdout.Trim().Replace('/', Path.DirectorySeparatorChar));
    }

    private static async Task<GitResult> RunGitAsync(string cwd, IEnumerable<string> args, int timeoutSeconds = 60)
    {
        var info = new ProcessStartInfo
        {
            FileName = ResolveGit(),
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args) info.ArgumentList.Add(arg);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 git。");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"Git 命令超过 {timeoutSeconds} 秒未完成。");
        }

        return new(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string ResolveGit()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        return File.Exists(path) ? path : "git.exe";
    }

    private static string GetPath(object? payload)
    {
        var input = (JsonElement)payload!;
        return input.GetProperty("path").GetString()!;
    }

    private static string SanitizeUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            var builder = new UriBuilder(uri) { UserName = "", Password = "" };
            return builder.Uri.AbsoluteUri;
        }
        return value;
    }

    private static void EnsureSuccess(GitResult result, string operation)
    {
        if (result.ExitCode == 0) return;
        throw new InvalidOperationException($"{operation} 失败：{Tail((result.Stderr + result.Stdout).Trim(), 10000)}");
    }

    private static string Tail(string text, int max) => text.Length <= max ? text : text[^max..];
    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
