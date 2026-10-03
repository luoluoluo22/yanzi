using System.Net;

namespace OpenQuickHost;

/// <summary>Ordered route dispatch. The server must authenticate before invoking it.</summary>
internal sealed class LocalApiRouter
{
    private readonly List<Func<HttpListenerRequest, HttpListenerResponse, string, Task<bool>>> routes = new();

    public void Add(Func<HttpListenerRequest, string, bool> matches,
        Func<HttpListenerRequest, HttpListenerResponse, string, Task> handle) => routes.Add(async (request, response, path) =>
        {
            if (!matches(request, path)) return false;
            await handle(request, response, path);
            return true;
        });

    public void AddHandled(Func<HttpListenerRequest, HttpListenerResponse, string, Task<bool>> handle) => routes.Add(handle);

    public async Task<bool> TryHandleAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        foreach (var route in routes)
        {
            if (await route(request, response, path)) return true;
        }
        return false;
    }
}
