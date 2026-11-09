using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace OpenQuickHost;

public sealed partial class LocalAgentApiServer
{
    private static readonly string PersonalSyncStorageRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenQuickHost",
        "PersonalSyncStorage");

    private async Task<bool> TryHandlePersonalSyncApiAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string path)
    {
        if (path != "/v1/sync/personal/file")
        {
            return false;
        }

        if (!IsAuthorized(request))
        {
            await WriteJsonAsync(response, 401, new { error = "unauthorized" });
            return true;
        }

        var query = request.Url?.Query ?? string.Empty;
        var queryParams = System.Web.HttpUtility.ParseQueryString(query);
        var relPath = queryParams["path"];

        if (string.IsNullOrWhiteSpace(relPath))
        {
            await WriteJsonAsync(response, 400, new { error = "missing_path_parameter" });
            return true;
        }

        relPath = relPath.Replace('\\', '/').TrimStart('/');
        var targetFullPath = Path.GetFullPath(Path.Combine(PersonalSyncStorageRoot, relPath.Replace('/', Path.DirectorySeparatorChar)));
        var safeRoot = Path.GetFullPath(PersonalSyncStorageRoot);

        if (!targetFullPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(response, 403, new { error = "invalid_path_traversal" });
            return true;
        }

        try
        {
            if (request.HttpMethod == "GET")
            {
                if (!File.Exists(targetFullPath))
                {
                    await WriteJsonAsync(response, 404, new { error = "file_not_found" });
                    return true;
                }

                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = GetPersonalSyncContentType(targetFullPath);
                var fileBytes = await File.ReadAllBytesAsync(targetFullPath);
                response.ContentLength64 = fileBytes.Length;
                await response.OutputStream.WriteAsync(fileBytes);
                return true;
            }

            if (request.HttpMethod == "PUT")
            {
                var dir = Path.GetDirectoryName(targetFullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var fs = new FileStream(targetFullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await request.InputStream.CopyToAsync(fs);
                }

                await WriteJsonAsync(response, 200, new { ok = true });
                return true;
            }

            if (request.HttpMethod == "DELETE")
            {
                if (File.Exists(targetFullPath))
                {
                    File.Delete(targetFullPath);
                }

                await WriteJsonAsync(response, 200, new { ok = true });
                return true;
            }

            await WriteJsonAsync(response, 405, new { error = "method_not_allowed" });
            return true;
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, 500, new { error = ex.Message });
            return true;
        }
    }

    private static string GetPersonalSyncContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".json" => "application/json; charset=utf-8",
            ".zip" => "application/zip",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }
}
