using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.ModelProvider;

namespace OpenQuickHost;

/// <summary>
/// 燕子通用 OCR 能力。
/// 运行时只随宿主携带 SimdPaddleOCR 核心；PP-OCRv6 Small 模型在首次使用时按需下载。
/// </summary>
public static class YanziOcrCapabilityProvider
{
    private const string EngineId = "simd-ppocr-v6-small";
    private const string ModelVersion = "ppocr-v6-small-sdcb-1.0.0";
    private const string ChinesePackageVersion = "1.0.0";
    private const string OrientationPackageVersion = "1.0.0";
    private static readonly TimeSpan IdleUnloadAfter = TimeSpan.FromMinutes(5);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static readonly SemaphoreSlim RuntimeGate = new(1, 1);
    private static readonly object RuntimeSync = new();
    private static readonly System.Threading.Timer IdleTimer = new(
        _ => TryUnloadIdleRuntime(),
        null,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(1));

    private static PaddleOcrAll? _runtime;
    private static DateTimeOffset _lastUseUtc = DateTimeOffset.MinValue;
    private static Assembly? _orientationModelAssembly;
    private static Assembly? _chineseModelAssembly;
    private static bool _modelsVerifiedThisProcess;

    private static readonly ModelPackage OrientationPackage = new(
        Id: "Sdcb.SimdPaddleOCR.Models.TextLineOrientation",
        Version: OrientationPackageVersion,
        DllName: "Sdcb.SimdPaddleOCR.Models.TextLineOrientation.dll",
        DllSha256: "c08ccb09b99e137989c6abfbae3dbf01c5b87709d350b745402a836e35cfab20",
        DllBytes: 1_026_048);

    private static readonly ModelPackage ChinesePackage = new(
        Id: "Sdcb.SimdPaddleOCR.Models.ChineseV6Small",
        Version: ChinesePackageVersion,
        DllName: "Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll",
        DllSha256: "96e3fcfb4b6d3bd7365bc000003e1534abfe86605631e69e0f3793f33d8f0755",
        DllBytes: 31_142_400);

    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name = "ocr.status",
            Description = "查询燕子 OCR 模型是否已安装、推理引擎是否已加载以及模型缓存位置",
            Category = "vision",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = _ => Task.FromResult<object?>(BuildStatus())
        };

        yield return new()
        {
            Name = "ocr.ensure",
            Description = "按需下载并校验 PP-OCRv6 Small 中文 OCR 模型；仅安装模型，不加载推理引擎",
            Category = "vision",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = _ => EnsureCapabilityAsync()
        };

        yield return new()
        {
            Name = "ocr.recognize",
            Description = "使用 PP-OCRv6 Small 识别本地图片中的文字，返回文本、置信度和坐标；首次调用会自动下载模型",
            Category = "vision",
            Permissions = ["file.read"],
            InputSchema = YanziCapabilitySchema.Parse(
                """{"type":"object","properties":{"imagePath":{"type":"string","minLength":1},"includeLines":{"type":"boolean"}},"required":["imagePath"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = payload => RecognizeCapabilityAsync((JsonElement)payload!, null)
        };

        yield return new()
        {
            Name = "ocr.recognizeRegion",
            Description = "识别本地图片的指定矩形区域，坐标结果仍以原始图片为基准；首次调用会自动下载模型",
            Category = "vision",
            Permissions = ["file.read"],
            InputSchema = YanziCapabilitySchema.Parse(
                """{"type":"object","properties":{"imagePath":{"type":"string","minLength":1},"x":{"type":"integer","minimum":0},"y":{"type":"integer","minimum":0},"width":{"type":"integer","minimum":1},"height":{"type":"integer","minimum":1},"includeLines":{"type":"boolean"}},"required":["imagePath","x","y","width","height"],"additionalProperties":false}"""),
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = payload =>
            {
                var input = (JsonElement)payload!;
                var region = new Rectangle(
                    input.GetProperty("x").GetInt32(),
                    input.GetProperty("y").GetInt32(),
                    input.GetProperty("width").GetInt32(),
                    input.GetProperty("height").GetInt32());
                return RecognizeCapabilityAsync(input, region);
            }
        };

        yield return new()
        {
            Name = "ocr.unload",
            Description = "释放当前已加载的 OCR 推理引擎和推理内存；已下载模型保留在本地缓存",
            Category = "vision",
            InputSchema = YanziCapabilitySchema.EmptyObject,
            OutputSchema = YanziCapabilitySchema.Parse("""{"type":"object"}"""),
            Handler = _ => UnloadCapabilityAsync()
        };
    }

    private static string ModelRoot =>
        Path.Combine(HostAssets.ResolveDataDirectoryPath("Models"), "ocr", "ppocr-v6-small");

    private static string ReadyMarkerPath => Path.Combine(ModelRoot, "ready.json");

    private static object BuildStatus()
    {
        var installed = IsInstalledFast();
        var loaded = Volatile.Read(ref _runtime) != null;
        return new
        {
            engine = EngineId,
            model = "PP-OCRv6 Small",
            modelVersion = ModelVersion,
            installed,
            loaded,
            modelDirectory = ModelRoot,
            idleUnloadMinutes = (int)IdleUnloadAfter.TotalMinutes,
            lastUseUtc = _lastUseUtc == DateTimeOffset.MinValue ? (DateTimeOffset?)null : _lastUseUtc
        };
    }

    private static async Task<object?> EnsureCapabilityAsync()
    {
        var sw = Stopwatch.StartNew();
        var result = await EnsureModelsAsync();
        sw.Stop();
        return new
        {
            engine = EngineId,
            model = "PP-OCRv6 Small",
            installed = true,
            loaded = Volatile.Read(ref _runtime) != null,
            downloaded = result.Downloaded,
            downloadedBytes = result.DownloadedBytes,
            modelDirectory = ModelRoot,
            elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1)
        };
    }

    private static async Task<object?> RecognizeCapabilityAsync(JsonElement input, Rectangle? requestedRegion)
    {
        var imagePath = Path.GetFullPath(input.GetProperty("imagePath").GetString()
            ?? throw new ArgumentException("imagePath 不能为空。"));
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("OCR 图片不存在。", imagePath);

        var info = new FileInfo(imagePath);
        if (info.Length > 100L * 1024 * 1024)
            throw new ArgumentException("OCR 图片不能超过 100 MB。");

        var includeLines = !input.TryGetProperty("includeLines", out var linesProperty)
            || linesProperty.ValueKind != JsonValueKind.False;

        await EnsureModelsAsync();

        await RuntimeGate.WaitAsync();
        try
        {
            var runtime = await EnsureRuntimeLoadedUnsafeAsync();
            _lastUseUtc = DateTimeOffset.UtcNow;

            return await Task.Run<object?>(() =>
            {
                var sw = Stopwatch.StartNew();
                using var source = new Bitmap(imagePath);
                if (source.Width <= 0 || source.Height <= 0)
                    throw new ArgumentException("OCR 图片尺寸无效。");
                if ((long)source.Width * source.Height > 100_000_000L)
                    throw new ArgumentException("OCR 图片像素过大，最大支持约 1 亿像素。");

                var region = requestedRegion ?? new Rectangle(0, 0, source.Width, source.Height);
                if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
                    || region.Right > source.Width || region.Bottom > source.Height)
                {
                    throw new ArgumentOutOfRangeException(nameof(requestedRegion), "OCR 区域超出图片范围。");
                }

                using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        region,
                        GraphicsUnit.Pixel);
                }

                var result = RunOcr(runtime, bitmap);
                sw.Stop();

                var lineResults = includeLines
                    ? result.Lines.Select(line => MapLine(line, region.X, region.Y)).ToArray()
                    : Array.Empty<object>();

                var averageRecognitionScore = result.Lines.Length == 0
                    ? 0
                    : result.Lines.Average(line => (double)line.RecognitionScore);

                return new
                {
                    engine = EngineId,
                    model = "PP-OCRv6 Small",
                    imagePath,
                    image = new { width = source.Width, height = source.Height },
                    region = new { x = region.X, y = region.Y, width = region.Width, height = region.Height },
                    text = result.Text,
                    detectedCount = result.DetectedCount,
                    averageRecognitionScore = Math.Round(averageRecognitionScore, 5),
                    recognitionScoreScale = "engine-native",
                    elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
                    lines = lineResults
                };
            });
        }
        finally
        {
            _lastUseUtc = DateTimeOffset.UtcNow;
            RuntimeGate.Release();
        }
    }

    private static unsafe PaddleOcrResult RunOcr(PaddleOcrAll runtime, Bitmap bitmap)
    {
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var strideBytes = Math.Abs(data.Stride) * bitmap.Height;
            return runtime.Run(
                new ReadOnlySpan<byte>((byte*)data.Scan0, strideBytes),
                bitmap.Width,
                bitmap.Height,
                data.Stride,
                ImagePixelFormat.Bgra32);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static object MapLine(PaddleOcrLine line, int offsetX, int offsetY)
    {
        var box = line.Box;
        var minX = Math.Min(Math.Min(box.X1, box.X2), Math.Min(box.X3, box.X4));
        var minY = Math.Min(Math.Min(box.Y1, box.Y2), Math.Min(box.Y3, box.Y4));
        var maxX = Math.Max(Math.Max(box.X1, box.X2), Math.Max(box.X3, box.X4));
        var maxY = Math.Max(Math.Max(box.Y1, box.Y2), Math.Max(box.Y3, box.Y4));

        object Point(double x, double y) => new
        {
            x = Math.Round(x + offsetX, 1),
            y = Math.Round(y + offsetY, 1)
        };

        return new
        {
            text = line.Text,
            recognitionScore = Math.Round(line.RecognitionScore, 5),
            recognitionScoreScale = "engine-native",
            emittedCount = line.EmittedCount,
            classificationConfidence = Math.Round(line.ClassificationScore, 5),
            rotationDegrees = line.AppliedRotationDegrees,
            box = new
            {
                x = Math.Round(minX + offsetX, 1),
                y = Math.Round(minY + offsetY, 1),
                width = Math.Round(maxX - minX, 1),
                height = Math.Round(maxY - minY, 1)
            },
            polygon = new[]
            {
                Point(box.X1, box.Y1),
                Point(box.X2, box.Y2),
                Point(box.X3, box.Y3),
                Point(box.X4, box.Y4)
            }
        };
    }

    private static async Task<EnsureResult> EnsureModelsAsync()
    {
        if (IsInstalledFast() && _modelsVerifiedThisProcess)
            return new EnsureResult(false, 0);

        await InstallGate.WaitAsync();
        try
        {
            if (IsInstalledFast() && await AreInstalledModelHashesValidAsync())
            {
                _modelsVerifiedThisProcess = true;
                return new EnsureResult(false, 0);
            }

            _modelsVerifiedThisProcess = false;
            Directory.CreateDirectory(ModelRoot);
            long downloadedBytes = 0;

            downloadedBytes += await EnsurePackageDllAsync(OrientationPackage);
            downloadedBytes += await EnsurePackageDllAsync(ChinesePackage);

            var marker = JsonSerializer.Serialize(new
            {
                version = ModelVersion,
                createdAtUtc = DateTimeOffset.UtcNow,
                packages = new[]
                {
                    new { id = OrientationPackage.Id, version = OrientationPackage.Version, sha256 = OrientationPackage.DllSha256 },
                    new { id = ChinesePackage.Id, version = ChinesePackage.Version, sha256 = ChinesePackage.DllSha256 }
                }
            }, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(ReadyMarkerPath, marker);
            _modelsVerifiedThisProcess = true;
            HostAssets.AppendLog($"[OCR] PP-OCRv6 Small models ready: {ModelRoot}");
            return new EnsureResult(downloadedBytes > 0, downloadedBytes);
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private static bool IsInstalledFast()
    {
        if (!File.Exists(ReadyMarkerPath))
            return false;

        return ModelFileLooksValid(OrientationPackage) && ModelFileLooksValid(ChinesePackage);
    }

    private static bool ModelFileLooksValid(ModelPackage package)
    {
        var path = Path.Combine(ModelRoot, package.DllName);
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == package.DllBytes;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> AreInstalledModelHashesValidAsync()
    {
        return await HasExpectedHashAsync(
                   Path.Combine(ModelRoot, OrientationPackage.DllName),
                   OrientationPackage.DllSha256)
               && await HasExpectedHashAsync(
                   Path.Combine(ModelRoot, ChinesePackage.DllName),
                   ChinesePackage.DllSha256);
    }

    private static async Task<long> EnsurePackageDllAsync(ModelPackage package)
    {
        var targetPath = Path.Combine(ModelRoot, package.DllName);
        if (await HasExpectedHashAsync(targetPath, package.DllSha256))
            return 0;

        var packageIdLower = package.Id.ToLowerInvariant();
        var nupkgUrl =
            $"https://api.nuget.org/v3-flatcontainer/{packageIdLower}/{package.Version}/{packageIdLower}.{package.Version}.nupkg";

        var tempRoot = Path.Combine(ModelRoot, ".download");
        Directory.CreateDirectory(tempRoot);
        var nupkgPath = Path.Combine(tempRoot, packageIdLower + "." + package.Version + ".nupkg");
        var extractedPath = Path.Combine(tempRoot, package.DllName + ".tmp");

        try
        {
            using (var response = await Http.GetAsync(nupkgUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(
                    nupkgPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    useAsync: true);
                await input.CopyToAsync(output);
            }

            using (var archive = ZipFile.OpenRead(nupkgPath))
            {
                var entry = archive.Entries.FirstOrDefault(item =>
                    item.FullName.EndsWith("/" + package.DllName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.FullName, package.DllName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"NuGet 模型包中缺少 {package.DllName}。");

                await using var entryStream = entry.Open();
                await using var output = new FileStream(
                    extractedPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    useAsync: true);
                await entryStream.CopyToAsync(output);
            }

            if (!await HasExpectedHashAsync(extractedPath, package.DllSha256))
                throw new InvalidDataException($"OCR 模型校验失败：{package.Id}。");

            File.Move(extractedPath, targetPath, overwrite: true);
            return new FileInfo(nupkgPath).Length;
        }
        finally
        {
            TryDeleteFile(extractedPath);
            TryDeleteFile(nupkgPath);
        }
    }

    private static async Task<bool> HasExpectedHashAsync(string path, string expectedSha256)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                useAsync: true);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            return string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<PaddleOcrAll> EnsureRuntimeLoadedUnsafeAsync()
    {
        lock (RuntimeSync)
        {
            if (_runtime != null)
                return _runtime;
        }

        var orientationPath = Path.Combine(ModelRoot, OrientationPackage.DllName);
        var chinesePath = Path.Combine(ModelRoot, ChinesePackage.DllName);

        _orientationModelAssembly ??= FindLoadedAssembly("Sdcb.SimdPaddleOCR.Models.TextLineOrientation")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(orientationPath));
        _chineseModelAssembly ??= FindLoadedAssembly("Sdcb.SimdPaddleOCR.Models.ChineseV6Small")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(chinesePath));

        var modelsType = _chineseModelAssembly.GetType(
            "Sdcb.SimdPaddleOCR.Models.ChineseV6Small.ChineseV6SmallModels",
            throwOnError: true)!;
        var defaultProperty = modelsType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMemberException(modelsType.FullName, "Default");
        var bundle = defaultProperty.GetValue(null) as PaddleOcrModelBundle
            ?? throw new InvalidDataException("OCR 模型包与当前推理核心不兼容。");

        var runtime = await PaddleOcrAll.LoadAsync(bundle);
        lock (RuntimeSync)
        {
            _runtime ??= runtime;
            if (!ReferenceEquals(_runtime, runtime))
                runtime.Dispose();
            _lastUseUtc = DateTimeOffset.UtcNow;
            return _runtime;
        }
    }

    private static Assembly? FindLoadedAssembly(string simpleName)
        => AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

    private static async Task<object?> UnloadCapabilityAsync()
    {
        await RuntimeGate.WaitAsync();
        try
        {
            var unloaded = DisposeRuntimeUnsafe();
            return new
            {
                engine = EngineId,
                unloaded,
                installed = IsInstalledFast(),
                message = unloaded ? "OCR 推理引擎已释放，模型缓存保留。" : "OCR 推理引擎当前未加载。"
            };
        }
        finally
        {
            RuntimeGate.Release();
        }
    }

    private static void TryUnloadIdleRuntime()
    {
        if (Volatile.Read(ref _runtime) == null)
            return;
        if (DateTimeOffset.UtcNow - _lastUseUtc < IdleUnloadAfter)
            return;
        if (!RuntimeGate.Wait(0))
            return;

        try
        {
            if (DateTimeOffset.UtcNow - _lastUseUtc >= IdleUnloadAfter)
            {
                if (DisposeRuntimeUnsafe())
                    HostAssets.AppendLog("[OCR] Runtime unloaded after idle timeout.");
            }
        }
        finally
        {
            RuntimeGate.Release();
        }
    }

    private static bool DisposeRuntimeUnsafe()
    {
        lock (RuntimeSync)
        {
            var runtime = _runtime;
            _runtime = null;
            if (runtime == null)
                return false;
            runtime.Dispose();
            return true;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record ModelPackage(
        string Id,
        string Version,
        string DllName,
        string DllSha256,
        long DllBytes);

    private sealed record EnsureResult(bool Downloaded, long DownloadedBytes);
}
