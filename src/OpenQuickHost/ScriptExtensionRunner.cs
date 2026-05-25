using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Collections.Concurrent;
using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace OpenQuickHost;

public static class ScriptExtensionRunner
{
    private const string CSharpCacheVersion = "v11";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CSharpBuildLocks = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<ScriptExecutionResult> PreparePortableAssetsAsync(
        CommandItem command,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(command.Runtime, "csharp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.Runtime, "cs", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command.Runtime, "c#", StringComparison.OrdinalIgnoreCase))
        {
            return new ScriptExecutionResult(true, string.Empty, string.Empty, 0);
        }

        var isInline = string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase);
        var source = isInline
            ? command.InlineScriptSource
            : await ReadEntrySourceAsync(command, cancellationToken);
        if (string.IsNullOrWhiteSpace(source))
        {
            return new ScriptExecutionResult(false, string.Empty, "C# 扩展缺少源码入口。", -1);
        }

        return await EnsureCSharpBuildAsync(command, source, ShouldUseNativeWindowMode(command, source), cancellationToken);
    }

    public static bool CanExecute(CommandItem command)
    {
        if (string.IsNullOrWhiteSpace(command.Runtime) || string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            return false;
        }

        if (string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(command.InlineScriptSource);
        }

        return !string.IsNullOrWhiteSpace(command.EntryPoint);
    }

    public static async Task<ScriptExecutionResult> ExecuteAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(command, inputText, launchSource, null, cancellationToken);
    }

    public static async Task<ScriptExecutionResult> ExecuteAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        CancellationToken cancellationToken = default)
    {
        var executionStopwatch = Stopwatch.StartNew();
        if (!CanExecute(command))
        {
            return new ScriptExecutionResult(false, string.Empty, "扩展没有可执行脚本入口。", -1);
        }

        HostAssets.AppendLog(
            $"ScriptRunner execute start: id={command.ExtensionId}, title={command.Title}, runtime={command.Runtime}, uiMode={command.UiMode ?? "none"}, launchSource={launchSource}, inputLength={(inputText ?? string.Empty).Length}");

        var isInline = string.Equals(command.EntryMode, "inline", StringComparison.OrdinalIgnoreCase);
        var result = command.Runtime?.ToLowerInvariant() switch
        {
            "powershell" or "ps1" => await ExecutePowerShellEntryAsync(command, inputText, launchSource, state, isInline, cancellationToken),

            "csharp" or "cs" or "c#" => await ExecuteCSharpEntryAsync(command, inputText, launchSource, state, isInline, cancellationToken),

            _ => new ScriptExecutionResult(false, string.Empty, $"当前还不支持脚本运行时：{command.Runtime}", -1)
        };

        HostAssets.AppendLog(
            $"ScriptRunner execute done: id={command.ExtensionId}, title={command.Title}, success={result.Success}, exitCode={result.ExitCode}, elapsedMs={executionStopwatch.ElapsedMilliseconds}, outputLength={result.Output.Length}, errorLength={result.Error.Length}");
        return result;
    }

    private static async Task<ScriptExecutionResult> ExecutePowerShellEntryAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        bool isInline,
        CancellationToken cancellationToken)
    {
        var entryPath = isInline
            ? await MaterializeInlineScriptAsync(command, ".ps1", cancellationToken)
            : Path.Combine(command.ExtensionDirectoryPath!, command.EntryPoint!);
        if (!File.Exists(entryPath))
        {
            return new ScriptExecutionResult(false, string.Empty, $"没有找到脚本入口：{entryPath}", -1);
        }

        try
        {
            return await ExecutePowerShellAsync(command, entryPath, inputText, launchSource, state, cancellationToken);
        }
        finally
        {
            if (isInline)
            {
                TryDeleteTempFile(entryPath);
            }
        }
    }

    private static async Task<ScriptExecutionResult> ExecuteCSharpEntryAsync(
        CommandItem command,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        bool isInline,
        CancellationToken cancellationToken)
    {
        var source = isInline
            ? command.InlineScriptSource
            : await ReadEntrySourceAsync(command, cancellationToken);
        return string.IsNullOrWhiteSpace(source)
            ? new ScriptExecutionResult(false, string.Empty, "C# 扩展缺少源码入口。", -1)
            : await ExecuteCSharpAsync(command, source, inputText, launchSource, state, cancellationToken);
    }

    private static async Task<string> MaterializeInlineScriptAsync(CommandItem command, string extension, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            throw new InvalidOperationException("内联脚本缺少扩展目录。");
        }

        if (string.IsNullOrWhiteSpace(command.InlineScriptSource))
        {
            throw new InvalidOperationException("内联脚本缺少 script.source。");
        }

        Directory.CreateDirectory(command.ExtensionDirectoryPath);
        var tempScriptPath = Path.Combine(command.ExtensionDirectoryPath, $".yanzi-inline-{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(
            tempScriptPath,
            command.InlineScriptSource,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);
        return tempScriptPath;
    }

    private static async Task<ScriptExecutionResult> ExecutePowerShellAsync(
        CommandItem command,
        string entryPath,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        CancellationToken cancellationToken)
    {
        var context = CreateContext(command, inputText, launchSource, state);
        var contextPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}.json");
        var stateUpdatePath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-state.json");
        var wrapperPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-wrapper.ps1");

        try
        {
            await File.WriteAllTextAsync(
                contextPath,
                JsonSerializer.Serialize(context, JsonOptions),
                Encoding.UTF8,
                cancellationToken);
            await File.WriteAllTextAsync(
                wrapperPath,
                BuildPowerShellWrapperScript(entryPath, inputText ?? string.Empty, contextPath),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {Quote(wrapperPath)}",
                WorkingDirectory = command.ExtensionDirectoryPath!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ApplyRuntimeEnvironment(startInfo, command, inputText, contextPath, stateUpdatePath, null, launchSource);

            return await RunProcessAsync(startInfo, "脚本", stateUpdatePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
        finally
        {
            TryDeleteTempFile(contextPath);
            TryDeleteTempFile(stateUpdatePath);
            TryDeleteTempFile(wrapperPath);
        }
    }

    private static async Task<ScriptExecutionResult> ExecuteCSharpAsync(
        CommandItem command,
        string source,
        string? inputText,
        string launchSource,
        IReadOnlyDictionary<string, string>? state,
        CancellationToken cancellationToken)
    {
        var compileStopwatch = Stopwatch.StartNew();
        var useNativeWindowMode = ShouldUseNativeWindowMode(command, source);
        var context = CreateContext(command, inputText, launchSource, state);
        var contextPath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}.json");
        var stateUpdatePath = Path.Combine(Path.GetTempPath(), $"yanzi-{command.ExtensionId}-{Guid.NewGuid():N}-state.json");

        try
        {
            await File.WriteAllTextAsync(
                contextPath,
                JsonSerializer.Serialize(context, JsonOptions),
                Encoding.UTF8,
                cancellationToken);

            var build = await EnsureCSharpBuildAsync(command, source, useNativeWindowMode, cancellationToken);
            HostAssets.AppendLog(
                $"ScriptRunner csharp build done: id={command.ExtensionId}, title={command.Title}, success={build.Success}, nativeWindowMode={useNativeWindowMode}, elapsedMs={compileStopwatch.ElapsedMilliseconds}, output={build.Output.Trim()}");
            if (!build.Success)
            {
                return build;
            }
            var assemblyPath = build.Output.Trim();
            if (File.Exists(assemblyPath))
            {
                return await ExecuteManagedAssemblyAsync(
                    command,
                    assemblyPath,
                    context,
                    contextPath,
                    stateUpdatePath,
                    useNativeWindowMode,
                    launchSource,
                    cancellationToken);
            }

            return new ScriptExecutionResult(false, string.Empty, $"没有找到已编译的 C# 扩展输出：{assemblyPath}", -1);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.Message, -1);
        }
        finally
        {
            TryDeleteTempFile(contextPath);
            TryDeleteTempFile(stateUpdatePath);
        }
    }

    private static async Task<ScriptExecutionResult> EnsureCSharpBuildAsync(
        CommandItem command,
        string source,
        bool useNativeWindowMode,
        CancellationToken cancellationToken)
    {
        var cacheFingerprint = string.Join(
            "\n---\n",
            CSharpCacheVersion,
            command.ExtensionId ?? string.Empty,
            source,
            CSharpGlobalUsingsSource,
            CSharpRuntimeSource);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheFingerprint)))[..16].ToLowerInvariant();
        var buildRoot = CSharpExtensionCacheService.GetBuildRoot(command.ExtensionDirectoryPath!, sourceHash);
        var dllPath = Path.Combine(buildRoot, "bin", "Release", "net9.0", "YanziExtension.dll");
        var buildLock = CSharpBuildLocks.GetOrAdd(buildRoot, static _ => new SemaphoreSlim(1, 1));
        await buildLock.WaitAsync(cancellationToken);
        try
        {
            return await EnsureCSharpBuildCoreAsync(command, source, useNativeWindowMode, buildRoot, dllPath, cancellationToken);
        }
        finally
        {
            buildLock.Release();
        }
    }

    private static async Task<ScriptExecutionResult> EnsureCSharpBuildCoreAsync(
        CommandItem command,
        string source,
        bool useNativeWindowMode,
        string buildRoot,
        string dllPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(dllPath))
        {
            if (IsLoadableManagedAssembly(dllPath))
            {
                CSharpExtensionCacheService.TouchBuildRoot(buildRoot);
                CSharpExtensionCacheService.QueueCleanup(buildRoot);
                return new ScriptExecutionResult(true, dllPath, string.Empty, 0);
            }

            HostAssets.AppendLog($"ScriptRunner csharp cache invalidated: id={command.ExtensionId}, path={dllPath}");
            TryDeleteTempFile(dllPath);
            TryDeleteTempFile(Path.ChangeExtension(dllPath, ".pdb"));
        }

        Directory.CreateDirectory(buildRoot);
        var outputDirectory = Path.GetDirectoryName(dllPath)!;
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "Action.cs"), source, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "YanziGlobalUsings.cs"), CSharpGlobalUsingsSource, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(buildRoot, "YanziRuntime.cs"), CSharpRuntimeSource, Encoding.UTF8, cancellationToken);
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(SourceText.From(CSharpGlobalUsingsSource, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Latest), path: "YanziGlobalUsings.cs", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Latest), path: "Action.cs", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText(SourceText.From(CSharpRuntimeSource, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.Latest), path: "YanziRuntime.cs", cancellationToken: cancellationToken)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "YanziExtension",
            syntaxTrees: syntaxTrees,
            references: BuildCSharpMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        var tempSuffix = $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var tempDllPath = Path.Combine(outputDirectory, $"YanziExtension{tempSuffix}.dll");
        var tempPdbPath = Path.Combine(outputDirectory, $"YanziExtension{tempSuffix}.pdb");
        var pdbPath = Path.Combine(outputDirectory, "YanziExtension.pdb");

        EmitResult emitResult;
        await using (var peStream = new FileStream(tempDllPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var pdbStream = new FileStream(
                         tempPdbPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            emitResult = compilation.Emit(peStream, pdbStream: pdbStream, cancellationToken: cancellationToken);
            await peStream.FlushAsync(cancellationToken);
            await pdbStream.FlushAsync(cancellationToken);
        }

        if (emitResult.Success && File.Exists(tempDllPath))
        {
            if (!IsLoadableManagedAssembly(tempDllPath))
            {
                TryDeleteTempFile(tempDllPath);
                TryDeleteTempFile(tempPdbPath);
                return new ScriptExecutionResult(false, string.Empty, "C# 扩展编译输出无效，请重试。", -1);
            }

            try
            {
                File.Move(tempDllPath, dllPath, overwrite: true);
                File.Move(tempPdbPath, pdbPath, overwrite: true);
            }
            catch (IOException ex) when (File.Exists(dllPath) && IsLoadableManagedAssembly(dllPath))
            {
                HostAssets.AppendLog($"ScriptRunner csharp cache publish skipped because another build completed: id={command.ExtensionId}, error={ex.Message}");
                TryDeleteTempFile(tempDllPath);
                TryDeleteTempFile(tempPdbPath);
            }

            CSharpExtensionCacheService.TouchBuildRoot(buildRoot);
            CSharpExtensionCacheService.QueueCleanup(buildRoot);
            return new ScriptExecutionResult(true, dllPath, string.Empty, 0);
        }

        TryDeleteTempFile(tempDllPath);
        TryDeleteTempFile(tempPdbPath);

        var diagnostics = emitResult.Diagnostics
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();
        var error = diagnostics.Length == 0
            ? "C# 扩展编译失败。"
            : string.Join(Environment.NewLine, diagnostics);
        if (useNativeWindowMode)
        {
            HostAssets.AppendLog("ScriptRunner native-window reference diagnostics:" + Environment.NewLine + BuildNativeWindowReferenceDebugInfo());
        }

        return new ScriptExecutionResult(false, string.Empty, error, -1);
    }

    private static bool IsLoadableManagedAssembly(string path)
    {
        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<MetadataReference> BuildCSharpMetadataReferences()
    {
        var references = new List<MetadataReference>(
            global::Basic.Reference.Assemblies.Net90.ReferenceInfos.All
                .Select(static info => (MetadataReference)info.Reference));

        var bundledDirectory = GetBundledNativeWindowReferenceDirectory();
        if (!string.IsNullOrWhiteSpace(bundledDirectory) && Directory.Exists(bundledDirectory))
        {
            references.AddRange(Directory.EnumerateFiles(bundledDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)));
        }

        var referenceDirectories = new[]
        {
            GetWindowsDesktopReferenceDirectory()
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        if (referenceDirectories.Length > 0)
        {
            references.AddRange(referenceDirectories
                .SelectMany(static directory => Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
                .ToArray());
        }

        var runtimeReferences = BuildNativeWindowRuntimeReferences();
        if (runtimeReferences.Count > 0)
        {
            references.AddRange(runtimeReferences);
        }

        return references
            .GroupBy(static reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static string GetBundledNativeWindowReferenceDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "NativeWindowRefs");
    }

    private static string? GetWindowsDesktopReferenceDirectory()
    {
        return GetReferencePackDirectory("Microsoft.WindowsDesktop.App.Ref", "net9.0", "net8.0", "net6.0");
    }

    private static string? GetNetCoreReferenceDirectory()
    {
        return GetReferencePackDirectory("Microsoft.NETCore.App.Ref", "net9.0", "net8.0", "net6.0");
    }

    private static string? GetReferencePackDirectory(string packName, params string[] tfmCandidates)
    {
        var packsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "packs", packName);
        if (!Directory.Exists(packsRoot))
        {
            return null;
        }

        var candidate = Directory.EnumerateDirectories(packsRoot)
            .Select(path => new DirectoryInfo(path))
            .Where(info => Version.TryParse(info.Name, out var version) && version.Major == 9)
            .OrderByDescending(info => Version.Parse(info.Name))
            .SelectMany(info => tfmCandidates.Select(tfm => Path.Combine(info.FullName, "ref", tfm)))
            .FirstOrDefault(Directory.Exists);

        if (candidate != null)
        {
            return candidate;
        }

        return Directory.EnumerateDirectories(packsRoot)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info =>
            {
                return Version.TryParse(info.Name, out var parsed) ? parsed : new Version(0, 0);
            })
            .SelectMany(info => tfmCandidates.Select(tfm => Path.Combine(info.FullName, "ref", tfm)))
            .FirstOrDefault(Directory.Exists);
    }

    private static string? GetSharedRuntimeDirectory(string sharedName)
    {
        var sharedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", sharedName);
        if (!Directory.Exists(sharedRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(sharedRoot)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info =>
            {
                return Version.TryParse(info.Name, out var parsed) ? parsed : new Version(0, 0);
            })
            .Select(static info => info.FullName)
            .FirstOrDefault(Directory.Exists);
    }

    private static IReadOnlyList<MetadataReference> BuildNativeWindowRuntimeReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static void AddAssemblyPath(HashSet<string> set, Assembly? assembly)
        {
            if (assembly == null || assembly.IsDynamic)
            {
                return;
            }

            var location = assembly.Location;
            var assemblyName = assembly.GetName().Name;
            if (string.Equals(assemblyName, "YanziExtension", StringComparison.OrdinalIgnoreCase) ||
                IsGeneratedExtensionAssemblyPath(location))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            {
                set.Add(location);
            }
        }

        static bool IsGeneratedExtensionAssemblyPath(string? location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            var normalized = location.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return normalized.Contains($"{Path.DirectorySeparatorChar}.yanzi-csharp-cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        }

        static void AddCandidatePath(HashSet<string> set, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                IsGeneratedExtensionAssemblyPath(path) ||
                !File.Exists(path) ||
                !IsLoadableManagedAssembly(path))
            {
                return;
            }

            set.Add(path);
        }

        static void AddCandidateFile(HashSet<string> set, string? directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var fullPath = Path.Combine(directory, fileName);
            AddCandidatePath(set, fullPath);
        }

        static void AddCandidateDirectoryAssemblies(HashSet<string> set, string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                AddCandidatePath(set, path);
            }
        }

        static void AddTrustedPlatformAssembly(HashSet<string> set, string? tpaValue, string fileName)
        {
            if (string.IsNullOrWhiteSpace(tpaValue))
            {
                return;
            }

            var match = tpaValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match) && File.Exists(match))
            {
                set.Add(match);
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            AddAssemblyPath(paths, assembly);
        }

        var knownAssemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Task).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Uri).Assembly,
            typeof(System.Windows.Window).Assembly,
            typeof(System.Windows.Controls.Button).Assembly,
            typeof(System.Windows.Media.Brush).Assembly,
            typeof(System.Windows.Markup.XmlLanguage).Assembly
        };

        foreach (var assembly in knownAssemblies)
        {
            AddAssemblyPath(paths, assembly);
        }

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var coreSharedDirectory = GetSharedRuntimeDirectory("Microsoft.NETCore.App");
        var windowsDesktopSharedDirectory = GetSharedRuntimeDirectory("Microsoft.WindowsDesktop.App");
        var appDirectory = AppContext.BaseDirectory;
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        AddCandidateDirectoryAssemblies(paths, appDirectory);

        foreach (var fileName in new[]
                 {
                     "System.Private.CoreLib.dll",
                     "System.Runtime.dll",
                     "System.Console.dll",
                     "System.Collections.dll",
                     "System.ObjectModel.dll",
                     "System.Linq.dll",
                     "System.Runtime.Extensions.dll",
                     "System.Text.RegularExpressions.dll",
                     "System.Threading.dll",
                     "System.Threading.Tasks.dll",
                     "System.Drawing.Common.dll",
                     "System.Management.dll",
                     "netstandard.dll",
                     "WindowsBase.dll",
                     "PresentationCore.dll",
                     "PresentationFramework.dll",
                     "System.Xaml.dll"
                 })
        {
            AddTrustedPlatformAssembly(paths, trustedPlatformAssemblies, fileName);
        }

        foreach (var directory in new[] { runtimeDirectory, coreSharedDirectory, windowsDesktopSharedDirectory, appDirectory }.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            AddCandidateFile(paths, directory, "System.Private.CoreLib.dll");
            AddCandidateFile(paths, directory, "System.Runtime.dll");
            AddCandidateFile(paths, directory, "System.Console.dll");
            AddCandidateFile(paths, directory, "System.Collections.dll");
            AddCandidateFile(paths, directory, "System.ObjectModel.dll");
            AddCandidateFile(paths, directory, "System.Linq.dll");
            AddCandidateFile(paths, directory, "System.Runtime.Extensions.dll");
            AddCandidateFile(paths, directory, "System.Text.RegularExpressions.dll");
            AddCandidateFile(paths, directory, "System.Threading.dll");
            AddCandidateFile(paths, directory, "System.Threading.Tasks.dll");
            AddCandidateFile(paths, directory, "System.Drawing.Common.dll");
            AddCandidateFile(paths, directory, "System.Management.dll");
            AddCandidateFile(paths, directory, "netstandard.dll");
            AddCandidateFile(paths, directory, "WindowsBase.dll");
            AddCandidateFile(paths, directory, "PresentationCore.dll");
            AddCandidateFile(paths, directory, "PresentationFramework.dll");
            AddCandidateFile(paths, directory, "System.Xaml.dll");
        }

        return paths
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string BuildNativeWindowReferenceDebugInfo()
    {
        var bundledDirectory = GetBundledNativeWindowReferenceDirectory();
        var sharedCore = GetSharedRuntimeDirectory("Microsoft.NETCore.App") ?? "(missing)";
        var sharedDesktop = GetSharedRuntimeDirectory("Microsoft.WindowsDesktop.App") ?? "(missing)";
        var packCore = GetNetCoreReferenceDirectory() ?? "(missing)";
        var packDesktop = GetWindowsDesktopReferenceDirectory() ?? "(missing)";
        var tpaValue = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var tpaFiles = new[]
        {
            "PresentationFramework.dll",
            "PresentationCore.dll",
            "WindowsBase.dll",
            "System.Xaml.dll"
        }
        .Select(fileName =>
        {
            var match = string.IsNullOrWhiteSpace(tpaValue)
                ? null
                : tpaValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
            return $"{fileName}={(string.IsNullOrWhiteSpace(match) ? "(missing)" : match)}";
        });

        return string.Join(
            Environment.NewLine,
            [
                $"NativeWindow refs: bundled={(Directory.Exists(bundledDirectory) ? bundledDirectory : "(missing)")}",
                $"NativeWindow refs: corePack={packCore}",
                $"NativeWindow refs: desktopPack={packDesktop}",
                $"NativeWindow refs: coreShared={sharedCore}",
                $"NativeWindow refs: desktopShared={sharedDesktop}",
                .. tpaFiles.Select(static line => $"NativeWindow refs: {line}")
            ]);
    }

    private static async Task<ScriptExecutionResult> ExecuteManagedAssemblyAsync(
        CommandItem command,
        string assemblyPath,
        ScriptExecutionContext context,
        string contextPath,
        string stateUpdatePath,
        bool useNativeWindowMode,
        string launchSource,
        CancellationToken cancellationToken)
    {
        return await ExecuteManagedAssemblyInProcessAsync(
            command,
            assemblyPath,
            context,
            contextPath,
            stateUpdatePath,
            useNativeWindowMode,
            launchSource,
            cancellationToken);
    }

    private static async Task<ScriptExecutionResult> ExecuteManagedAssemblyInProcessAsync(
        CommandItem command,
        string assemblyPath,
        ScriptExecutionContext context,
        string contextPath,
        string stateUpdatePath,
        bool useNativeWindowMode,
        string launchSource,
        CancellationToken cancellationToken)
    {
        var executionStopwatch = Stopwatch.StartNew();
        var ready = new TaskCompletionSource<ScriptExecutionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<ScriptExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = ExecuteManagedAssemblyInCurrentThread(
                    command,
                    assemblyPath,
                    context,
                    contextPath,
                    stateUpdatePath,
                    launchSource,
                    ready,
                    signalEarlyReady: useNativeWindowMode);
                completed.TrySetResult(result);
            }
            catch (Exception ex)
            {
                var result = new ScriptExecutionResult(false, string.Empty, ex.ToString(), -1);
                ready.TrySetResult(result);
                completed.TrySetResult(result);
            }
        })
        {
            IsBackground = useNativeWindowMode,
            Name = $"Yanzi C# {command.ExtensionId}"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        HostAssets.AppendLog(
            $"ScriptRunner in-process csharp started: id={command.ExtensionId}, title={command.Title}, nativeWindowMode={useNativeWindowMode}, threadId={thread.ManagedThreadId}");

        if (useNativeWindowMode)
        {
            var startupTask = await Task.WhenAny(
                ready.Task,
                completed.Task,
                Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));

            if (startupTask == completed.Task)
            {
                var completedResult = await completed.Task.ConfigureAwait(false);
                HostAssets.AppendLog(
                    $"ScriptRunner in-process csharp completed before early return: id={command.ExtensionId}, success={completedResult.Success}, elapsedMs={executionStopwatch.ElapsedMilliseconds}");
                return completedResult;
            }

            if (startupTask == ready.Task)
            {
                var readyResult = await ready.Task.ConfigureAwait(false);
                if (readyResult != null)
                {
                    return readyResult;
                }
            }

            _ = ObserveInProcessNativeWindowAsync(command, completed.Task, stateUpdatePath, executionStopwatch);
            return new ScriptExecutionResult(true, "native-window-started", "原生窗口已启动。", 0);
        }

        return await completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ScriptExecutionResult ExecuteManagedAssemblyInCurrentThread(
        CommandItem command,
        string assemblyPath,
        ScriptExecutionContext context,
        string contextPath,
        string stateUpdatePath,
        string launchSource,
        TaskCompletionSource<ScriptExecutionResult?> ready,
        bool signalEarlyReady)
    {
        if (!File.Exists(assemblyPath))
        {
            return new ScriptExecutionResult(false, string.Empty, $"没有找到扩展程序集：{assemblyPath}", 2);
        }

        var environmentSnapshot = CaptureRuntimeEnvironmentSnapshot();
        var originalDirectory = Directory.GetCurrentDirectory();
        var loadContext = new AssemblyLoadContext($"yanzi-inprocess-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            ApplyRuntimeEnvironment(command, contextPath, stateUpdatePath, launchSource);
            if (!string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath) && Directory.Exists(command.ExtensionDirectoryPath))
            {
                Directory.SetCurrentDirectory(command.ExtensionDirectoryPath);
            }

            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var runtimeContext = CreateInProcessRuntimeContext(assembly, context, stateUpdatePath);
            var runMethod = FindYanziActionRunMethod(assembly, runtimeContext.GetType());
            ready.TrySetResult(null);

            var invocationResult = runMethod.Invoke(null, [runtimeContext]);
            var output = ResolveInProcessInvocationOutput(invocationResult);
            var stateUpdates = TryReadStateUpdatesAsync(stateUpdatePath, CancellationToken.None).GetAwaiter().GetResult();
            return new ScriptExecutionResult(true, output.Trim(), string.Empty, 0, stateUpdates);
        }
        catch (TargetInvocationException ex)
        {
            return new ScriptExecutionResult(false, string.Empty, (ex.InnerException ?? ex).ToString(), 1);
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult(false, string.Empty, ex.ToString(), 1);
        }
        finally
        {
            if (!signalEarlyReady)
            {
                ready.TrySetResult(null);
            }

            Directory.SetCurrentDirectory(originalDirectory);
            RestoreRuntimeEnvironmentSnapshot(environmentSnapshot);
            loadContext.Unload();
        }
    }

    private static async Task ObserveInProcessNativeWindowAsync(
        CommandItem command,
        Task<ScriptExecutionResult> completionTask,
        string stateUpdatePath,
        Stopwatch executionStopwatch)
    {
        try
        {
            var result = await completionTask.ConfigureAwait(false);
            HostAssets.AppendLog(
                $"ScriptRunner in-process native-window completed: id={command.ExtensionId}, title={command.Title}, success={result.Success}, exitCode={result.ExitCode}, elapsedMs={executionStopwatch.ElapsedMilliseconds}, outputLength={result.Output.Length}, errorLength={result.Error.Length}");
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"ScriptRunner in-process native-window observe failed: id={command.ExtensionId}, error={ex}");
        }
        finally
        {
            TryDeleteTempFile(stateUpdatePath);
        }
    }

    private static object CreateInProcessRuntimeContext(
        Assembly assembly,
        ScriptExecutionContext context,
        string stateUpdatePath)
    {
        var contextType = assembly.GetType(
            "OpenQuickHost.CSharpRuntime.YanziActionContext",
            throwOnError: true) ?? throw new InvalidOperationException("C# 运行时上下文类型缺失。");
        var runtimeContext = Activator.CreateInstance(
            contextType,
            context.ExtensionId,
            context.Title,
            context.ExtensionDirectory,
            context.ExtensionDataDirectory,
            context.InputText,
            context.LaunchSource,
            context.Now,
            context.Permissions,
            context.State,
            context.AgentApiBaseUrl,
            context.AgentApiToken) ?? throw new InvalidOperationException("C# 运行时上下文创建失败。");
        var stateUpdateProperty = contextType.GetProperty("StateUpdatePath", BindingFlags.Instance | BindingFlags.Public);
        if (stateUpdateProperty?.CanWrite == true)
        {
            stateUpdateProperty.SetValue(runtimeContext, stateUpdatePath);
        }

        return runtimeContext;
    }

    private static MethodInfo FindYanziActionRunMethod(Assembly assembly, Type runtimeContextType)
    {
        var actionType = GetLoadableTypes(assembly)
            .FirstOrDefault(type => string.Equals(type.Name, "YanziAction", StringComparison.Ordinal));
        var runMethod = actionType?.GetMethod(
            "RunAsync",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [runtimeContextType],
            modifiers: null);
        return runMethod ?? throw new InvalidOperationException("C# 扩展缺少 public static RunAsync(YanziActionContext context) 入口。");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type != null).Cast<Type>();
        }
    }

    private static string ResolveInProcessInvocationOutput(object? invocationResult)
    {
        if (invocationResult is Task task)
        {
            task.GetAwaiter().GetResult();
            var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
            return resultProperty?.GetValue(task)?.ToString() ?? string.Empty;
        }

        return invocationResult?.ToString() ?? string.Empty;
    }

    private static Dictionary<string, string?> CaptureRuntimeEnvironmentSnapshot()
    {
        return RuntimeEnvironmentKeys.ToDictionary(
            static key => key,
            static key => Environment.GetEnvironmentVariable(key),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void RestoreRuntimeEnvironmentSnapshot(IReadOnlyDictionary<string, string?> snapshot)
    {
        foreach (var pair in snapshot)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private static async Task<string?> ReadEntrySourceAsync(CommandItem command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.EntryPoint) || string.IsNullOrWhiteSpace(command.ExtensionDirectoryPath))
        {
            return null;
        }

        var entryPath = Path.Combine(command.ExtensionDirectoryPath, command.EntryPoint);
        return File.Exists(entryPath)
            ? await File.ReadAllTextAsync(entryPath, Encoding.UTF8, cancellationToken)
            : null;
    }

    private static async Task<ScriptExecutionResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string label,
        string? stateUpdatePath,
        CancellationToken cancellationToken)
    {
        var processStopwatch = Stopwatch.StartNew();
        var process = new Process { StartInfo = startInfo };
        process.Start();
        var argumentText = startInfo.ArgumentList.Count > 0
            ? string.Join(" ", startInfo.ArgumentList)
            : startInfo.Arguments;
        HostAssets.AppendLog(
            $"ScriptRunner process started: label={label}, file={startInfo.FileName}, args={argumentText}, pid={process.Id}, workingDir={startInfo.WorkingDirectory}");
        Task<string>? outputTask = startInfo.RedirectStandardOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : null;
        Task<string>? errorTask = startInfo.RedirectStandardError
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : null;

        await process.WaitForExitAsync(cancellationToken);

        var output = outputTask == null ? string.Empty : (await outputTask).Trim();
        var error = errorTask == null ? string.Empty : (await errorTask).Trim();
        var stateUpdates = await TryReadStateUpdatesAsync(stateUpdatePath, cancellationToken);
        HostAssets.AppendLog(
            $"ScriptRunner process exited: label={label}, pid={process.Id}, exitCode={process.ExitCode}, elapsedMs={processStopwatch.ElapsedMilliseconds}, outputLength={output.Length}, errorLength={error.Length}");
        var hasErrorOutput = !string.IsNullOrWhiteSpace(error);
        var result = process.ExitCode == 0 && !hasErrorOutput
            ? new ScriptExecutionResult(true, output, error, process.ExitCode, stateUpdates)
            : new ScriptExecutionResult(
                false,
                output,
                hasErrorOutput ? error : $"{label}退出码：{process.ExitCode}",
                process.ExitCode == 0 && hasErrorOutput ? -1 : process.ExitCode,
                stateUpdates);
        process.Dispose();
        return result;
    }

    private static bool ShouldUseNativeWindowMode(CommandItem command, string source)
    {
        if (command.UsesNativeWindowUi)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source.Contains("new Window", StringComparison.Ordinal) ||
               source.Contains("new System.Windows.Window", StringComparison.Ordinal) ||
               source.Contains("ShowDialog()", StringComparison.Ordinal) ||
               source.Contains(".ShowDialog(", StringComparison.Ordinal) ||
               source.Contains("WindowStartupLocation", StringComparison.Ordinal) ||
               source.Contains("WindowStyle", StringComparison.Ordinal);
    }

    private static ScriptExecutionContext CreateContext(CommandItem command, string? inputText, string launchSource, IReadOnlyDictionary<string, string>? state)
    {
        var settings = AppSettingsStore.Load();
        var agentApiBaseUrl = settings.EnableAgentApi
            ? $"http://127.0.0.1:{settings.AgentApiPort}"
            : string.Empty;
        return new ScriptExecutionContext(
            command.ExtensionId,
            command.Title,
            command.ExtensionDirectoryPath!,
            ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId),
            inputText ?? string.Empty,
            launchSource,
            DateTimeOffset.Now,
            command.Permissions,
            new Dictionary<string, string>(state ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            agentApiBaseUrl,
            settings.AgentApiToken);
    }

    private static void ApplyRuntimeEnvironment(
        CommandItem command,
        string contextPath,
        string stateUpdatePath,
        string launchSource)
    {
        var settings = AppSettingsStore.Load();
        Environment.SetEnvironmentVariable("YANZI_INPUT", string.Empty);
        Environment.SetEnvironmentVariable("YANZI_CONTEXT_PATH", contextPath);
        Environment.SetEnvironmentVariable("YANZI_STATE_UPDATES_PATH", stateUpdatePath);
        Environment.SetEnvironmentVariable("YANZI_EXTENSION_ID", command.ExtensionId);
        Environment.SetEnvironmentVariable("YANZI_EXTENSION_DIR", command.ExtensionDirectoryPath!);
        Environment.SetEnvironmentVariable("YANZI_EXTENSION_DATA_DIR", ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId));
        Environment.SetEnvironmentVariable("YANZI_LAUNCH_SOURCE", launchSource);
        Environment.SetEnvironmentVariable("YANZI_AGENT_API_BASE_URL", settings.EnableAgentApi
            ? $"http://127.0.0.1:{settings.AgentApiPort}"
            : string.Empty);
        Environment.SetEnvironmentVariable("YANZI_AGENT_API_TOKEN", settings.AgentApiToken ?? string.Empty);
        Environment.SetEnvironmentVariable("YANZI_HOST_LOG_PATH", HostAssets.HostLogPath);
    }

    private static void ApplyRuntimeEnvironment(
        ProcessStartInfo startInfo,
        CommandItem command,
        string? inputText,
        string contextPath,
        string stateUpdatePath,
        string? readyPath,
        string launchSource)
    {
        var settings = AppSettingsStore.Load();
        startInfo.Environment["YANZI_INPUT"] = inputText ?? string.Empty;
        startInfo.Environment["YANZI_CONTEXT_PATH"] = contextPath;
        startInfo.Environment["YANZI_STATE_UPDATES_PATH"] = stateUpdatePath;
        if (!string.IsNullOrWhiteSpace(readyPath))
        {
            startInfo.Environment["YANZI_READY_PATH"] = readyPath;
        }
        startInfo.Environment["YANZI_EXTENSION_ID"] = command.ExtensionId;
        startInfo.Environment["YANZI_EXTENSION_DIR"] = command.ExtensionDirectoryPath!;
        startInfo.Environment["YANZI_EXTENSION_DATA_DIR"] = ExtensionStorageService.GetExtensionStorageDirectoryPath(command.ExtensionId);
        startInfo.Environment["YANZI_LAUNCH_SOURCE"] = launchSource;
        startInfo.Environment["YANZI_AGENT_API_BASE_URL"] = settings.EnableAgentApi
            ? $"http://127.0.0.1:{settings.AgentApiPort}"
            : string.Empty;
        startInfo.Environment["YANZI_AGENT_API_TOKEN"] = settings.AgentApiToken ?? string.Empty;
        startInfo.Environment["YANZI_HOST_LOG_PATH"] = HostAssets.HostLogPath;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string BuildPowerShellWrapperScript(string entryPath, string inputText, string contextPath)
    {
        var escapedEntryPath = EscapePowerShellSingleQuoted(entryPath);
        var escapedInputText = EscapePowerShellSingleQuoted(inputText);
        var escapedContextPath = EscapePowerShellSingleQuoted(contextPath);

        return
            "$utf8 = [System.Text.UTF8Encoding]::new($false)\r\n" +
            "[Console]::InputEncoding = $utf8\r\n" +
            "[Console]::OutputEncoding = $utf8\r\n" +
            "$OutputEncoding = $utf8\r\n" +
            "$yanziAssemblies = @('System.Drawing','System.Windows.Forms','System.Management','Microsoft.VisualBasic','System.ServiceProcess')\r\n" +
            "foreach ($yanziAssembly in $yanziAssemblies) { try { Add-Type -AssemblyName $yanziAssembly -ErrorAction Stop } catch { } }\r\n" +
            $"& '{escapedEntryPath}' -InputText '{escapedInputText}' -ContextPath '{escapedContextPath}'\r\n";
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }

    private sealed record ScriptExecutionContext(
        string ExtensionId,
        string Title,
        string ExtensionDirectory,
        string ExtensionDataDirectory,
        string InputText,
        string LaunchSource,
        DateTimeOffset Now,
        IReadOnlyList<string> Permissions,
        IReadOnlyDictionary<string, string> State,
        string AgentApiBaseUrl,
        string AgentApiToken);

    private static async Task<IReadOnlyDictionary<string, string>> TryReadStateUpdatesAsync(string? stateUpdatePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stateUpdatePath) || !File.Exists(stateUpdatePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = await File.ReadAllTextAsync(stateUpdatePath, cancellationToken);
            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return payload != null
                ? new Dictionary<string, string>(payload, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] RuntimeEnvironmentKeys =
    [
        "YANZI_INPUT",
        "YANZI_CONTEXT_PATH",
        "YANZI_STATE_UPDATES_PATH",
        "YANZI_READY_PATH",
        "YANZI_EXTENSION_ID",
        "YANZI_EXTENSION_DIR",
        "YANZI_EXTENSION_DATA_DIR",
        "YANZI_LAUNCH_SOURCE",
        "YANZI_AGENT_API_BASE_URL",
        "YANZI_AGENT_API_TOKEN",
        "YANZI_HOST_LOG_PATH"
    ];

    private const string CSharpRuntimeSource =
        """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using System.Net.Http;
        using System.Net.Http.Json;
        using System.Text;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;

        namespace OpenQuickHost.CSharpRuntime;

        public sealed record YanziActionContext(
            string ExtensionId,
            string Title,
            string ExtensionDirectory,
            string ExtensionDataDirectory,
            string InputText,
            string LaunchSource,
            DateTimeOffset Now,
            IReadOnlyList<string> Permissions,
            IReadOnlyDictionary<string, string> State,
            string AgentApiBaseUrl,
            string AgentApiToken)
        {
            private YanziStorageClient? _storage;
            private readonly Dictionary<string, string> _pendingStateUpdates = new(StringComparer.OrdinalIgnoreCase);
            private HostedViewStateProxy? _viewState;

            public YanziStorageClient Storage => _storage ??= new YanziStorageClient(this);
            public HostedViewStateProxy ViewState => _viewState ??= new HostedViewStateProxy(this);
            public string StateUpdatePath { get; set; } = Environment.GetEnvironmentVariable("YANZI_STATE_UPDATES_PATH") ?? string.Empty;

            public async Task SetStateAsync(object values)
            {
                if (values == null)
                {
                    return;
                }

                foreach (var property in values.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                {
                    _pendingStateUpdates[property.Name] = property.GetValue(values)?.ToString() ?? string.Empty;
                }

                await FlushStateUpdatesAsync();
            }

            public async Task SetStateAsync(IReadOnlyDictionary<string, string> values)
            {
                if (values == null)
                {
                    return;
                }

                foreach (var pair in values)
                {
                    _pendingStateUpdates[pair.Key] = pair.Value ?? string.Empty;
                }

                await FlushStateUpdatesAsync();
            }

            public static async Task<YanziActionContext> LoadFromEnvironmentAsync()
            {
                var contextPath = Environment.GetEnvironmentVariable("YANZI_CONTEXT_PATH");
                if (string.IsNullOrWhiteSpace(contextPath) || !File.Exists(contextPath))
                {
                    throw new InvalidOperationException("YANZI_CONTEXT_PATH is missing.");
                }

                var json = await File.ReadAllTextAsync(contextPath);
                return JsonSerializer.Deserialize<YanziActionContext>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidOperationException("Failed to read Yanzi context.");
            }

            private async Task FlushStateUpdatesAsync()
            {
                var stateUpdatePath = string.IsNullOrWhiteSpace(StateUpdatePath)
                    ? Environment.GetEnvironmentVariable("YANZI_STATE_UPDATES_PATH")
                    : StateUpdatePath;
                if (string.IsNullOrWhiteSpace(stateUpdatePath))
                {
                    return;
                }

                await File.WriteAllTextAsync(stateUpdatePath, JsonSerializer.Serialize(_pendingStateUpdates));
            }

            public Task UpdateView()
            {
                return FlushStateUpdatesAsync();
            }

            public sealed class HostedViewStateProxy
            {
                private readonly YanziActionContext _context;

                public HostedViewStateProxy(YanziActionContext context)
                {
                    _context = context;
                }

                public object? this[string key]
                {
                    get
                    {
                        if (_context._pendingStateUpdates.TryGetValue(key, out var pending))
                        {
                            return pending;
                        }

                        return _context.State.TryGetValue(key, out var value) ? value : null;
                    }
                    set
                    {
                        _context._pendingStateUpdates[key] = value?.ToString() ?? string.Empty;
                    }
                }

                public bool TryGetValue(string key, out object? value)
                {
                    if (_context._pendingStateUpdates.TryGetValue(key, out var pending))
                    {
                        value = pending;
                        return true;
                    }

                    if (_context.State.TryGetValue(key, out var existing))
                    {
                        value = existing;
                        return true;
                    }

                    value = null;
                    return false;
                }
            }
        }

        public sealed class YanziStorageClient
        {
            private readonly YanziActionContext _context;
            private readonly SemaphoreSlim _cloudWriteGate = new(1, 1);

            public YanziStorageClient(YanziActionContext context)
            {
                _context = context;
            }

            public async Task<string?> ReadTextAsync(string key, string scope = "local")
            {
                var normalizedScope = NormalizeScope(scope);
                if (string.Equals(normalizedScope, "local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_context.AgentApiBaseUrl))
                {
                    return await ReadLocalTextAsync(key);
                }

                if (string.Equals(normalizedScope, "both", StringComparison.OrdinalIgnoreCase))
                {
                    var localText = await ReadLocalTextAsync(key);
                    _ = RefreshLocalFromCloudAsync(key);
                    return localText;
                }

                return await ReadCloudTextAsync(key, normalizedScope);
            }

            private async Task<string?> ReadCloudTextAsync(string key, string scope)
            {
                using var client = CreateClient();
                var response = await client.GetAsync($"/v1/storage/{Uri.EscapeDataString(_context.ExtensionId)}?key={Uri.EscapeDataString(key)}&scope={Uri.EscapeDataString(scope)}");
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<StorageReadResponse>();
                return payload?.Content;
            }

            public async Task WriteTextAsync(string key, string content, string scope = "local")
            {
                var normalizedScope = NormalizeScope(scope);
                if (string.Equals(normalizedScope, "local", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(_context.AgentApiBaseUrl))
                {
                    await WriteLocalTextAsync(key, content);
                    return;
                }

                if (string.Equals(normalizedScope, "both", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteLocalTextAsync(key, content);
                    _ = TryWriteCloudTextAsync(key, content ?? string.Empty);
                    return;
                }

                await WriteCloudTextAsync(key, content ?? string.Empty);
            }

            private async Task WriteCloudTextAsync(string key, string content)
            {
                using var client = CreateClient();
                using var response = await client.PutAsJsonAsync(
                    $"/v1/storage/{Uri.EscapeDataString(_context.ExtensionId)}",
                    new StorageWriteRequest(key, content ?? string.Empty, "cloud"));
                response.EnsureSuccessStatusCode();
            }

            private async Task<string?> ReadLocalTextAsync(string key)
            {
                var path = ResolveLocalPath(key);
                return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
            }

            private async Task WriteLocalTextAsync(string key, string? content)
            {
                var path = ResolveLocalPath(key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content ?? string.Empty, Encoding.UTF8);
            }

            private async Task RefreshLocalFromCloudAsync(string key)
            {
                try
                {
                    var cloudText = await ReadCloudTextAsync(key, "cloud");
                    if (cloudText != null)
                    {
                        await WriteLocalTextAsync(key, cloudText);
                    }
                }
                catch
                {
                    // Cloud refresh is opportunistic; local-first reads must stay fast.
                }
            }

            private async Task TryWriteCloudTextAsync(string key, string content)
            {
                await _cloudWriteGate.WaitAsync();
                try
                {
                    await WriteCloudTextAsync(key, content);
                }
                catch
                {
                    // Cloud writes are queued behind the local save for UI responsiveness.
                }
                finally
                {
                    _cloudWriteGate.Release();
                }
            }

            public async Task<T?> ReadJsonAsync<T>(string key, string scope = "local")
            {
                var text = await ReadTextAsync(key, scope);
                return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, SerializerOptions);
            }

            public Task WriteJsonAsync<T>(string key, T value, string scope = "local")
            {
                var json = JsonSerializer.Serialize(value, SerializerOptions);
                return WriteTextAsync(key, json, scope);
            }

            private string ResolveLocalPath(string key)
            {
                var normalized = NormalizeKey(key);
                return Path.Combine(_context.ExtensionDataDirectory, normalized.Replace('/', Path.DirectorySeparatorChar));
            }

            private HttpClient CreateClient()
            {
                var client = new HttpClient
                {
                    BaseAddress = new Uri(_context.AgentApiBaseUrl, UriKind.Absolute),
                    Timeout = TimeSpan.FromSeconds(8)
                };

                if (!string.IsNullOrWhiteSpace(_context.AgentApiToken))
                {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _context.AgentApiToken);
                }

                return client;
            }

            private static string NormalizeScope(string? scope)
            {
                return string.Equals(scope, "cloud", StringComparison.OrdinalIgnoreCase)
                    ? "cloud"
                    : string.Equals(scope, "both", StringComparison.OrdinalIgnoreCase)
                        ? "both"
                        : "local";
            }

            private static string NormalizeKey(string key)
            {
                var normalized = (key ?? string.Empty).Replace('\\', '/').Trim('/');
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    throw new InvalidOperationException("Storage key is required.");
                }

                var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (segments.Any(static segment => segment is "." or ".."))
                {
                    throw new InvalidOperationException("Storage key cannot contain . or .. segments.");
                }

                return string.Join("/", segments);
            }

            private static readonly JsonSerializerOptions SerializerOptions = new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

            private sealed record StorageReadResponse(bool Found, string? Content, string Source, string LocalPath);

            private sealed record StorageWriteRequest(string Key, string Content, string Scope);
        }
        """;

    private const string CSharpGlobalUsingsSource =
        """
        global using System;
        global using System.Collections.Generic;
        global using System.Diagnostics;
        global using System.IO;
        global using System.Linq;
        global using System.Text;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using OpenQuickHost.CSharpRuntime;
        """;
}

public sealed record ScriptExecutionResult(
    bool Success,
    string Output,
    string Error,
    int ExitCode,
    IReadOnlyDictionary<string, string>? StateUpdates = null);
