using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using OpenQuickHost.Sync;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private async Task HandleFilesRoute11Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var command = GetString(payload, "command");

        if (string.IsNullOrEmpty(command))
        {
            await WriteJsonAsync(response, 400, new { error = "Command is required" });
            return;
        }

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell.exe";
            // Prepend progress preference silencer to avoid CLIXML progress streams in stderr
            var prependedCommand = "$ProgressPreference = 'SilentlyContinue';\r\n" + command;
            var bytes = Encoding.Unicode.GetBytes(prependedCommand);
            var base64 = Convert.ToBase64String(bytes);

            process.StartInfo.Arguments = $"-NoProfile -NonInteractive -EncodedCommand {base64}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                await WriteJsonAsync(response, 200, new
                {
                    ok = false,
                    output = outputBuilder.ToString() + "\r\n[API 错误] 命令执行超时 (15秒)",
                    exitCode = -1
                });
                return;
            }

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            // If error output contains only CLIXML progress data on a successful exit, discard it to avoid confusing the user
            if (!string.IsNullOrEmpty(error) && process.ExitCode == 0 && error.Contains("CLIXML") && error.Contains("progress"))
            {
                error = "";
            }
            var combinedOutput = string.IsNullOrEmpty(error) ? output : $"{output}\r\n[错误输出]\r\n{error}";

            await WriteJsonAsync(response, 200, new
            {
                ok = true,
                output = combinedOutput,
                exitCode = process.ExitCode
            });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, 500, new { error = ex.Message });
        }
        return;
    }

    private async Task HandleFilesRoute12Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var targetPath = GetString(payload, "path");

        try
        {
            var items = new List<object>();
            string currentPath = "";

            if (string.IsNullOrEmpty(targetPath) || string.Equals(targetPath, "Desktop", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        items.Add(new
                        {
                            name = drive.Name,
                            isDir = true,
                            size = 0L,
                            lastModified = 0L
                        });
                    }
                }

                var specialDirs = new[]
                {
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.UserProfile
                };
                foreach (var dir in specialDirs)
                {
                    var dirPath = Environment.GetFolderPath(dir);
                    if (!string.IsNullOrEmpty(dirPath))
                    {
                        items.Add(new
                        {
                            name = dirPath,
                            isDir = true,
                            size = 0L,
                            lastModified = 0L
                        });
                    }
                }
                currentPath = "";
            }
            else
            {
                currentPath = Path.GetFullPath(targetPath);
                if (Directory.Exists(currentPath))
                {
                    foreach (var dir in Directory.GetDirectories(currentPath))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(dir);
                            if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 || (dirInfo.Attributes & FileAttributes.System) != 0)
                            {
                                continue;
                            }
                            items.Add(new
                            {
                                name = Path.GetFileName(dir),
                                isDir = true,
                                size = 0L,
                                lastModified = new DateTimeOffset(dirInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds()
                            });
                        }
                        catch {}
                    }

                    foreach (var file in Directory.GetFiles(currentPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 || (fileInfo.Attributes & FileAttributes.System) != 0)
                            {
                                continue;
                            }
                            items.Add(new
                            {
                                name = Path.GetFileName(file),
                                isDir = false,
                                size = fileInfo.Length,
                                lastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds()
                            });
                        }
                        catch {}
                    }
                }
                else
                {
                    await WriteJsonAsync(response, 404, new { error = "Directory not found" });
                    return;
                }
            }

            await WriteJsonAsync(response, 200, new
            {
                ok = true,
                path = currentPath,
                items
            });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, 500, new { error = ex.Message });
        }
        return;
    }

    private async Task HandleFilesRoute13Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var targetPath = ResolveFsPath(GetString(payload, "path"));

        if (string.IsNullOrEmpty(targetPath))
        {
            await WriteJsonAsync(response, 400, new { error = "Path is required" });
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(targetPath);
            if (!File.Exists(fullPath))
            {
                await WriteJsonAsync(response, 404, new { error = "File not found" });
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > 10 * 1024 * 1024)
            {
                await WriteJsonAsync(response, 400, new { error = "File is too large to read (max 10MB)" });
                return;
            }

            var ext = fileInfo.Extension.ToLowerInvariant();
            bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp" || ext == ".bmp" || ext == ".ico";
            if (isImage)
            {
                var bytes = File.ReadAllBytes(fullPath);
                var base64 = Convert.ToBase64String(bytes);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    path = fullPath,
                    content = base64,
                    isBase64 = true,
                    ext = ext
                });
                return;
            }

            string content = File.ReadAllText(fullPath, Encoding.UTF8);
            await WriteJsonAsync(response, 200, new
            {
                ok = true,
                path = fullPath,
                content = content,
                isBase64 = false,
                ext = ext
            });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, 500, new { error = ex.Message });
        }
        return;
    }

    private async Task HandleFilesRoute14Async(HttpListenerRequest request, HttpListenerResponse response, string path)
    {

        var payload = await ReadJsonBodyAsync(request);
        var targetPath = GetString(payload, "path");
        var content = GetString(payload, "content");

        if (string.IsNullOrEmpty(targetPath))
        {
            await WriteJsonAsync(response, 400, new { error = "Path is required" });
            return;
        }

        var isBase64 = false;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("base64", out var base64Val))
        {
            if (base64Val.ValueKind == JsonValueKind.True)
            {
                isBase64 = true;
            }
            else if (base64Val.ValueKind == JsonValueKind.False)
            {
                isBase64 = false;
            }
            else if (base64Val.ValueKind == JsonValueKind.String)
            {
                bool.TryParse(base64Val.GetString(), out isBase64);
            }
        }

        try
        {
            var fullPath = Path.GetFullPath(targetPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (isBase64)
            {
                var bytes = Convert.FromBase64String(content ?? string.Empty);
                File.WriteAllBytes(fullPath, bytes);
            }
            else
            {
                File.WriteAllText(fullPath, content ?? string.Empty, Encoding.UTF8);
            }
            await WriteJsonAsync(response, 200, new
            {
                ok = true,
                path = fullPath
            });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, 500, new { error = ex.Message });
        }
        return;
    }
}
