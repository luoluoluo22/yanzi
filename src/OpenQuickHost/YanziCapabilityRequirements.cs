using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpenQuickHost;

/// <summary>
/// 小程序对外部能力的声明与解析。
/// manifest 只描述“需要什么”，宿主负责检测、补齐和验证。
/// </summary>
public static class YanziCapabilityRequirementResolver
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex RequirementPattern = new(
        @"^(?<name>[A-Za-z0-9._-]+)(?:\s*>=\s*(?<version>\d+(?:\.\d+){0,3}))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> GetRequirements(CommandItem command)
    {
        if (string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            return Array.Empty<string>();
        }

        var manifestPath = Path.Combine(command.ExtensionDirectoryPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!TryGetProperty(document.RootElement, "requires", out var requires) ||
                requires.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return requires.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            HostAssets.AppendLog($"读取小程序 requires 失败: id={command.ExtensionId}, error={ex.Message}");
            return Array.Empty<string>();
        }
    }

    public static bool TryParse(string raw, out YanziCapabilityRequirement requirement, out string error)
    {
        requirement = default!;
        error = string.Empty;
        var value = (raw ?? string.Empty).Trim();
        var match = RequirementPattern.Match(value);
        if (!match.Success)
        {
            error = $"依赖声明格式无效：{raw}。当前支持 name 或 name>=version，例如 python>=3.12、node>=22、ffmpeg>=8。";
            return false;
        }

        Version? minimumVersion = null;
        var versionText = match.Groups["version"].Value;
        if (!string.IsNullOrWhiteSpace(versionText) &&
            !Version.TryParse(NormalizeVersion(versionText), out minimumVersion))
        {
            error = $"依赖版本格式无效：{raw}";
            return false;
        }

        requirement = new YanziCapabilityRequirement(
            value,
            match.Groups["name"].Value.Trim(),
            minimumVersion);
        return true;
    }

    public static async Task<YanziRequirementResolutionResult> EnsureForCommandAsync(
        CommandItem command,
        CancellationToken cancellationToken = default)
    {
        var rawRequirements = GetRequirements(command);
        if (rawRequirements.Count == 0)
        {
            return YanziRequirementResolutionResult.Ready();
        }

        var resolved = new List<YanziResolvedRequirement>();
        foreach (var raw in rawRequirements)
        {
            if (!TryParse(raw, out var requirement, out var parseError))
            {
                return YanziRequirementResolutionResult.Failed(parseError, resolved);
            }

            // 已有燕子能力提供者优先；否则再解析宿主负责的系统依赖。
            if (YanziCapabilityRegistry.Contains(requirement.Name))
            {
                resolved.Add(new YanziResolvedRequirement(
                    requirement.Raw,
                    requirement.Name,
                    true,
                    "capability-registry",
                    null,
                    null,
                    false));
                continue;
            }

            if (!YanziSystemDependencyProvider.CanHandle(requirement.Name))
            {
                return YanziRequirementResolutionResult.Failed(
                    $"缺少能力“{requirement.Name}”，且当前没有可用 Provider。", resolved);
            }

            var key = YanziSystemDependencyProvider.NormalizeName(requirement.Name);
            var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var ensured = await YanziSystemDependencyProvider
                    .EnsureAsync(requirement, cancellationToken)
                    .ConfigureAwait(false);
                resolved.Add(ensured);
                if (!ensured.Available)
                {
                    return YanziRequirementResolutionResult.Failed(
                        ensured.Error ?? $"能力“{requirement.Raw}”准备失败。", resolved);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        return YanziRequirementResolutionResult.Ready(resolved);
    }

    public static string BuildAuthoringPrompt()
    {
        return """
            【小程序基础能力依赖】
            - 小程序需要宿主或系统基础环境时，只在 manifest 顶层声明 requires，不要在小程序脚本里重复编写下载/安装逻辑。
            - 示例："requires": ["git", "python>=3.12", "node>=22", "ffmpeg>=8"]。
            - requires 表示“小程序需要什么”；provides 表示“小程序向燕子能力网络提供什么”，两者不要混用。
            - 燕子运行小程序前会解析 requires：已经满足则直接跳过；缺失或版本过低时，由宿主 Provider 自动准备并再次验证。
            - 当前可由宿主自动准备的系统依赖：git、python、node、ffmpeg。
            - 需要 Python 时声明 python；需要 Node.js/npm 生态时声明 node；需要音视频编解码时声明 ffmpeg。不要把这些运行时的下载安装逻辑复制进每个小程序。
            """;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeVersion(string value)
    {
        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? value + ".0" : value;
    }
}

public sealed record YanziCapabilityRequirement(string Raw, string Name, Version? MinimumVersion);

public sealed record YanziResolvedRequirement(
    string Raw,
    string Name,
    bool Available,
    string Provider,
    string? Version,
    string? Path,
    bool InstalledNow,
    string? Error = null);

public sealed record YanziRequirementResolutionResult(
    bool Success,
    string? Error,
    IReadOnlyList<YanziResolvedRequirement> Requirements)
{
    public static YanziRequirementResolutionResult Ready(
        IReadOnlyList<YanziResolvedRequirement>? requirements = null)
        => new(true, null, requirements ?? Array.Empty<YanziResolvedRequirement>());

    public static YanziRequirementResolutionResult Failed(
        string error,
        IReadOnlyList<YanziResolvedRequirement>? requirements = null)
        => new(false, error, requirements ?? Array.Empty<YanziResolvedRequirement>());
}

public sealed record YanziDependencyProgressSnapshot(
    string Name,
    string Requirement,
    string State,
    string Message,
    int? Percent,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 宿主级系统依赖 Provider。
/// 小程序声明 git/python/node/ffmpeg，宿主统一负责发现、安装、版本校验和 PATH 刷新。
/// </summary>
public static class YanziSystemDependencyProvider
{
    private static readonly ConcurrentDictionary<string, YanziDependencyProgressSnapshot> Progress =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly DependencyDefinition[] Definitions =
    [
        new(
            Name: "git",
            Description: "Git 命令行基础环境",
            Aliases: ["git"],
            ExecutableName: "git.exe",
            VersionArguments: "--version",
            DefaultInstallerId: "Git.Git"),
        new(
            Name: "python",
            Description: "Python 运行时",
            Aliases: ["python", "python3"],
            ExecutableName: "python.exe",
            VersionArguments: "--version",
            DefaultInstallerId: "Python.Python.3.14"),
        new(
            Name: "node",
            Description: "Node.js 运行时（同时提供 npm/npx）",
            Aliases: ["node", "nodejs"],
            ExecutableName: "node.exe",
            VersionArguments: "--version",
            DefaultInstallerId: "OpenJS.NodeJS.LTS"),
        new(
            Name: "ffmpeg",
            Description: "FFmpeg 音视频工具链（同时提供 ffprobe）",
            Aliases: ["ffmpeg"],
            ExecutableName: "ffmpeg.exe",
            VersionArguments: "-version",
            DefaultInstallerId: "Gyan.FFmpeg")
    ];

    public static bool CanHandle(string name)
        => FindDefinition(name) != null;

    public static string NormalizeName(string name)
        => FindDefinition(name)?.Name ?? (name ?? string.Empty).Trim().ToLowerInvariant();

    public static IReadOnlyList<object> ListKnown()
    {
        return Definitions
            .Select(definition => (object)new
            {
                name = definition.Name,
                description = definition.Description,
                aliases = definition.Aliases,
                installer = "winget:" + definition.DefaultInstallerId,
                supportsMinimumVersion = true
            })
            .ToArray();
    }

    public static YanziDependencyProgressSnapshot GetProgress(string requirementText)
    {
        var name = requirementText;
        if (YanziCapabilityRequirementResolver.TryParse(
                requirementText,
                out var requirement,
                out _))
        {
            name = NormalizeName(requirement.Name);
        }

        return Progress.TryGetValue(name, out var snapshot)
            ? snapshot
            : new YanziDependencyProgressSnapshot(
                name,
                requirementText,
                "idle",
                "尚未开始安装任务。",
                null,
                DateTimeOffset.Now);
    }

    public static async Task<YanziResolvedRequirement> GetStatusAsync(
        string requirementText,
        CancellationToken cancellationToken = default)
    {
        if (!YanziCapabilityRequirementResolver.TryParse(
                requirementText,
                out var requirement,
                out var parseError))
        {
            return new YanziResolvedRequirement(
                requirementText,
                requirementText,
                false,
                "host",
                null,
                null,
                false,
                parseError);
        }

        var definition = FindDefinition(requirement.Name);
        if (definition == null)
        {
            return new YanziResolvedRequirement(
                requirement.Raw,
                requirement.Name,
                false,
                "host",
                null,
                null,
                false,
                $"没有系统依赖 Provider：{requirement.Name}");
        }

        var detected = await FindBestInstalledAsync(
            definition,
            requirement.MinimumVersion,
            cancellationToken).ConfigureAwait(false);

        if (detected == null)
        {
            var installerId = SelectInstallerId(definition, requirement);
            return new YanziResolvedRequirement(
                requirement.Raw,
                definition.Name,
                false,
                "winget:" + installerId,
                null,
                null,
                false,
                $"未检测到满足要求的 {definition.Name}。");
        }

        var versionOk = requirement.MinimumVersion == null ||
                        (detected.ParsedVersion != null &&
                         detected.ParsedVersion >= requirement.MinimumVersion);

        return new YanziResolvedRequirement(
            requirement.Raw,
            definition.Name,
            versionOk,
            "system",
            detected.VersionText,
            detected.Path,
            false,
            versionOk
                ? null
                : $"{definition.Name} 版本 {detected.VersionText ?? "未知"} 低于要求 {requirement.MinimumVersion}。");
    }

    public static async Task<YanziResolvedRequirement> EnsureAsync(
        YanziCapabilityRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        var definition = FindDefinition(requirement.Name);
        if (definition == null)
        {
            return new YanziResolvedRequirement(
                requirement.Raw,
                requirement.Name,
                false,
                "host",
                null,
                null,
                false,
                $"没有系统依赖 Provider：{requirement.Name}");
        }

        SetProgress(definition.Name, requirement.Raw, "checking", "正在检测本机环境与版本…", 5);

        var before = await GetStatusAsync(requirement.Raw, cancellationToken).ConfigureAwait(false);
        if (before.Available)
        {
            EnsureExecutableDirectoryOnPath(before.Path);
            SetProgress(
                definition.Name,
                requirement.Raw,
                "completed",
                $"已满足要求：{definition.Name} {before.Version ?? string.Empty}".Trim(),
                100);
            return before;
        }

        var winget = FindExecutableOnPath("winget.exe");
        var installerId = SelectInstallerId(definition, requirement);
        if (winget == null)
        {
            if (definition.Name == "node")
                return await EnsurePortableNodeAsync(requirement, cancellationToken).ConfigureAwait(false);

            var error = $"电脑缺少 {definition.Name}，同时没有检测到 WinGet，燕子无法自动准备该依赖。";
            SetProgress(definition.Name, requirement.Raw, "failed", error, null);
            return before with
            {
                Provider = "winget:" + installerId,
                Error = error
            };
        }

        HostAssets.AppendLog(
            $"Capability requirement: installing {definition.Name} for {requirement.Raw} via WinGet package {installerId}.");

        SetProgress(
            definition.Name,
            requirement.Raw,
            "installing",
            $"正在通过 WinGet 下载并安装 {installerId}…",
            20);

        ProcessResult install;
        try
        {
            install = await RunProcessAsync(
                winget,
                BuildWingetInstallArguments(installerId),
                cancellationToken,
                timeout: TimeSpan.FromMinutes(15)).ConfigureAwait(false);
        }
        catch (Exception ex) when (definition.Name == "node" &&
            ex is not OperationCanceledException)
        {
            return await EnsurePortableNodeAsync(requirement, cancellationToken,
                "WinGet 启动失败：" + ex.Message).ConfigureAwait(false);
        }

        SetProgress(definition.Name, requirement.Raw, "verifying", "安装进程已结束，正在刷新 PATH 并验证版本…", 90);

        // 安装器会修改用户/机器 PATH，但当前进程不会自动刷新。
        RefreshProcessPath();

        var detected = await FindBestInstalledAsync(
            definition,
            requirement.MinimumVersion,
            cancellationToken).ConfigureAwait(false);

        if (detected != null)
        {
            EnsureExecutableDirectoryOnPath(detected.Path);
            var versionOk = requirement.MinimumVersion == null ||
                            (detected.ParsedVersion != null &&
                             detected.ParsedVersion >= requirement.MinimumVersion);
            if (versionOk)
            {
                SetProgress(
                    definition.Name,
                    requirement.Raw,
                    "completed",
                    $"准备完成：{definition.Name} {detected.VersionText}",
                    100);
                return new YanziResolvedRequirement(
                    requirement.Raw,
                    definition.Name,
                    true,
                    "winget:" + installerId,
                    detected.VersionText,
                    detected.Path,
                    install.ExitCode == 0,
                    null);
            }
        }

        var detail = install.ExitCode == 0
            ? $"WinGet 已结束，但燕子仍未找到满足 {requirement.Raw} 的可执行环境。"
            : $"{definition.Name} 自动安装失败（WinGet exit {install.ExitCode}）：{CompactError(install.StdErr, install.StdOut)}";

        if (definition.Name == "node")
            return await EnsurePortableNodeAsync(requirement, cancellationToken, detail).ConfigureAwait(false);

        SetProgress(definition.Name, requirement.Raw, "failed", detail, null);
        return before with
        {
            Provider = "winget:" + installerId,
            Error = detail
        };
    }

    private static async Task<YanziResolvedRequirement> EnsurePortableNodeAsync(
        YanziCapabilityRequirement requirement,
        CancellationToken cancellationToken,
        string? priorError = null)
    {
        SetProgress("node", requirement.Raw, "installing",
            "正在从 Node.js 官网准备当前用户专用运行时，并验证官方 SHA-256…", 30);
        try
        {
            var nodePath = await YanziPortableNodeInstaller
                .EnsureAsync(requirement.MinimumVersion, cancellationToken)
                .ConfigureAwait(false);
            SetProgress("node", requirement.Raw, "verifying",
                "已准备 Node 便携版，正在检查版本与 npm…", 90);
            var versionText = await GetVersionAsync(nodePath, "--version", cancellationToken)
                .ConfigureAwait(false);
            if (!TryParseLooseVersion(versionText, out var version) ||
                (requirement.MinimumVersion != null && version < requirement.MinimumVersion) ||
                !File.Exists(Path.Combine(Path.GetDirectoryName(nodePath)!, "npm.cmd")))
                throw new InvalidDataException("安装后的 Node/npm 未通过版本与文件校验。");

            EnsureExecutableDirectoryOnPath(nodePath);
            SetProgress("node", requirement.Raw, "completed",
                $"Node 便携版准备完成：{versionText}（无需管理员权限）", 100);
            return new YanziResolvedRequirement(
                requirement.Raw, "node", true, "portable:nodejs.org", versionText,
                nodePath, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var detail = (priorError == null ? "" : priorError + "；") +
                "Node 便携版安装失败：" + ex.Message;
            HostAssets.AppendLog($"Portable Node dependency installation failed: {detail}");
            SetProgress("node", requirement.Raw, "failed", detail, null);
            return new YanziResolvedRequirement(
                requirement.Raw, "node", false, "portable:nodejs.org", null,
                null, false, detail);
        }
    }

    private static void SetProgress(
        string name,
        string requirement,
        string state,
        string message,
        int? percent)
    {
        Progress[name] = new YanziDependencyProgressSnapshot(
            name,
            requirement,
            state,
            message,
            percent,
            DateTimeOffset.Now);
    }

    private static DependencyDefinition? FindDefinition(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        return Definitions.FirstOrDefault(definition =>
            definition.Aliases.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    private static string SelectInstallerId(
        DependencyDefinition definition,
        YanziCapabilityRequirement requirement)
    {
        if (definition.Name == "python")
        {
            // 当前 WinGet 主线为 3.14。最低要求高于主线时尝试对应 major.minor 包，
            // 让未来新增的 Python 分支无需再次修改协议。
            if (requirement.MinimumVersion is { } pythonMin &&
                (pythonMin.Major > 3 || (pythonMin.Major == 3 && pythonMin.Minor > 14)))
            {
                return $"Python.Python.{pythonMin.Major}.{pythonMin.Minor}";
            }

            return "Python.Python.3.14";
        }

        if (definition.Name == "node")
        {
            // 默认安装 LTS；只有最低要求超过当前 LTS major 24 时才切到 Current。
            if (requirement.MinimumVersion is { Major: > 24 })
            {
                return "OpenJS.NodeJS";
            }

            return "OpenJS.NodeJS.LTS";
        }

        return definition.DefaultInstallerId;
    }

    private static string BuildWingetInstallArguments(string installerId)
        => $"install --id {installerId} -e --source winget " +
           "--accept-package-agreements --accept-source-agreements --silent --disable-interactivity";

    private static async Task<DetectedInstallation?> FindBestInstalledAsync(
        DependencyDefinition definition,
        Version? minimumVersion,
        CancellationToken cancellationToken)
    {
        var detected = new List<DetectedInstallation>();
        foreach (var candidate in FindCandidateExecutables(definition))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versionText = await GetVersionAsync(
                candidate,
                definition.VersionArguments,
                cancellationToken).ConfigureAwait(false);

            if (versionText == null)
            {
                continue;
            }

            _ = TryParseLooseVersion(versionText, out var parsed);
            detected.Add(new DetectedInstallation(candidate, versionText, parsed));
        }

        if (detected.Count == 0)
        {
            return null;
        }

        var satisfying = detected
            .Where(item => minimumVersion == null ||
                           (item.ParsedVersion != null && item.ParsedVersion >= minimumVersion))
            .OrderByDescending(item => item.ParsedVersion)
            .FirstOrDefault();

        return satisfying ??
               detected.OrderByDescending(item => item.ParsedVersion).First();
    }

    private static IEnumerable<string> FindCandidateExecutables(DependencyDefinition definition)
    {
        var candidates = new List<string>();

        var fromPath = FindExecutableOnPath(definition.ExecutableName);
        if (fromPath != null)
        {
            candidates.Add(fromPath);
        }

        switch (definition.Name)
        {
            case "git":
                AddIfExists(candidates,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "git.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"));
                break;

            case "python":
                AddPythonCandidates(candidates);
                break;

            case "node":
                AddIfExists(candidates,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"));
                candidates.AddRange(YanziPortableNodeInstaller.GetCachedExecutables());
                break;

            case "ffmpeg":
                AddIfExists(candidates,
                    @"C:\ffmpeg\ffmpeg.exe",
                    @"C:\ffmpeg\bin\ffmpeg.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
                break;
        }

        return candidates
            .Where(File.Exists)
            .Where(path => definition.Name != "node" ||
                File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "npm.cmd")))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void AddPythonCandidates(List<string> candidates)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python")
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                if (root.EndsWith(
                        Path.Combine("Programs", "Python"),
                        StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var directory in Directory.EnumerateDirectories(root, "Python*"))
                    {
                        AddIfExists(candidates, Path.Combine(directory, "python.exe"));
                    }
                }
                else
                {
                    foreach (var directory in Directory.EnumerateDirectories(root, "Python*"))
                    {
                        AddIfExists(candidates, Path.Combine(directory, "python.exe"));
                    }
                }
            }
            catch
            {
                // Program Files 目录可能受 ACL 或并发安装影响；忽略单个枚举失败。
            }
        }
    }

    private static void AddIfExists(List<string> candidates, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                candidates.Add(path);
            }
        }
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
        {
            return fileName;
        }

        var path = Environment.GetEnvironmentVariable(
            "PATH",
            EnvironmentVariableTarget.Process) ?? string.Empty;

        foreach (var raw in path.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = Environment.ExpandEnvironmentVariables(
                raw.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static void EnsureExecutableDirectoryOnPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var current = Environment.GetEnvironmentVariable(
            "PATH",
            EnvironmentVariableTarget.Process) ?? string.Empty;
        var entries = current
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('"'));

        // Put the selected version first, even if an older Node is earlier on PATH.
        // Otherwise PowerShell's Get-Command can launch the wrong executable.
        var ordered = new[] { directory }.Concat(entries.Where(item =>
            !string.Equals(item, directory, StringComparison.OrdinalIgnoreCase)));
        Environment.SetEnvironmentVariable(
            "PATH",
            string.Join(Path.PathSeparator, ordered),
            EnvironmentVariableTarget.Process);
    }

    private static void RefreshProcessPath()
    {
        var machine = Environment.GetEnvironmentVariable(
            "PATH",
            EnvironmentVariableTarget.Machine) ?? string.Empty;
        var user = Environment.GetEnvironmentVariable(
            "PATH",
            EnvironmentVariableTarget.User) ?? string.Empty;
        var current = Environment.GetEnvironmentVariable(
            "PATH",
            EnvironmentVariableTarget.Process) ?? string.Empty;

        var merged = string.Join(
            Path.PathSeparator,
            (machine + Path.PathSeparator + user + Path.PathSeparator + current)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));

        Environment.SetEnvironmentVariable(
            "PATH",
            merged,
            EnvironmentVariableTarget.Process);
    }

    private static async Task<string?> GetVersionAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            executablePath,
            arguments,
            cancellationToken,
            TimeSpan.FromSeconds(15)).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var text = (result.StdOut + " " + result.StdErr).Trim();
        var match = Regex.Match(
            text,
            @"(?<version>\d+\.\d+(?:\.\d+){0,2})",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static bool TryParseLooseVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Regex.Match(
            value,
            @"\d+\.\d+(?:\.\d+){0,2}",
            RegexOptions.CultureInvariant);
        if (!match.Success || !Version.TryParse(match.Value, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
        {
            return new ProcessResult(-1, string.Empty, $"无法启动 {fileName}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new ProcessResult(
                -2,
                await stdoutTask.ConfigureAwait(false),
                "执行超时。");
        }

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    private static string CompactError(string first, string second)
    {
        var value = string.IsNullOrWhiteSpace(first) ? second : first;
        value = (value ?? string.Empty).Trim();
        return value.Length <= 500 ? value : value[..500];
    }

    private sealed record DependencyDefinition(
        string Name,
        string Description,
        string[] Aliases,
        string ExecutableName,
        string VersionArguments,
        string DefaultInstallerId);

    private sealed record DetectedInstallation(
        string Path,
        string VersionText,
        Version? ParsedVersion);

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}

/// <summary>
/// 将系统依赖管理本身暴露为宿主能力，供 Agent/受权小程序发现和调用。
/// </summary>
public static class YanziSystemDependencyCapabilities
{
    public static IReadOnlyList<YanziCapabilityProviderDefinition> Create()
    {
        return
        [
            new YanziCapabilityProviderDefinition
            {
                Name = "dependency.list",
                Description = "列出燕子宿主可自动准备的系统基础依赖",
                Audience = "developer", Category = "development",
                InputSchema = YanziCapabilitySchema.EmptyObject,
                OutputSchema = YanziCapabilitySchema.Parse(
                    "{\"type\":\"array\",\"items\":{\"type\":\"object\"}}"),
                Handler = _ => Task.FromResult<object?>(
                    YanziSystemDependencyProvider.ListKnown())
            },
            new YanziCapabilityProviderDefinition
            {
                Name = "dependency.status",
                Description = "检测一个系统基础依赖及最低版本是否已满足",
                Audience = "developer", Category = "development",
                InputSchema = YanziCapabilitySchema.Parse(
                    "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"name\"],\"additionalProperties\":false}"),
                OutputSchema = YanziCapabilitySchema.Parse(
                    "{\"type\":\"object\"}"),
                Handler = async payload =>
                {
                    var name = ((JsonElement)payload!)
                        .GetProperty("name")
                        .GetString()!;
                    return await YanziSystemDependencyProvider
                        .GetStatusAsync(name)
                        .ConfigureAwait(false);
                }
            },
            new YanziCapabilityProviderDefinition
            {
                Name = "dependency.progress",
                Description = "读取系统基础依赖最近一次准备任务的进度与阶段",
                Audience = "developer", Category = "development",
                InputSchema = YanziCapabilitySchema.Parse(
                    "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"name\"],\"additionalProperties\":false}"),
                OutputSchema = YanziCapabilitySchema.Parse(
                    "{\"type\":\"object\"}"),
                Handler = payload =>
                {
                    var name = ((JsonElement)payload!)
                        .GetProperty("name")
                        .GetString()!;
                    return Task.FromResult<object?>(
                        YanziSystemDependencyProvider.GetProgress(name));
                }
            },
            new YanziCapabilityProviderDefinition
            {
                Name = "dependency.ensure",
                Description = "确保一个系统基础依赖可用；缺失或版本过低时由燕子 Provider 自动安装并验证",
                Audience = "developer", Category = "development", RiskLevel = "medium", RequiresConfirmation = true,
                Permissions = new[] { "system.install" },
                InputSchema = YanziCapabilitySchema.Parse(
                    "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"minLength\":1}},\"required\":[\"name\"],\"additionalProperties\":false}"),
                OutputSchema = YanziCapabilitySchema.Parse(
                    "{\"type\":\"object\"}"),
                Handler = async payload =>
                {
                    var name = ((JsonElement)payload!)
                        .GetProperty("name")
                        .GetString()!;
                    if (!YanziCapabilityRequirementResolver.TryParse(
                            name,
                            out var requirement,
                            out var error))
                    {
                        throw new ArgumentException(error);
                    }

                    return await YanziSystemDependencyProvider
                        .EnsureAsync(requirement)
                        .ConfigureAwait(false);
                }
            }
        ];
    }
}
