using OpenQuickHost.CSharpRuntime;
using Yanzi.Capture;

public static class YanziAction
{
    public static async Task<string> RunAsync(YanziActionContext context)
    {
        var existing = context.GetRegisteredObject?.Invoke(
            context.ExtensionId + "-window");

        if (existing != null)
        {
            return HandleExisting(existing, context.InputText);
        }

        var service = new CaptureExtensionService();
        await service.InitializeAsync(context);

        context.RegisterObject?.Invoke(
            context.ExtensionId + "-window",
            service);

        var isStartup =
            string.Equals(
                context.LaunchSource,
                "app-startup",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                context.LaunchSource,
                "startup",
                StringComparison.OrdinalIgnoreCase);

        if (!isStartup)
            service.Run(context.InputText ?? string.Empty);

        context.Log(
            isStartup
                ? "【燕子截图】已在宿主进程内完成预热并常驻。"
                : "【燕子截图】已在宿主进程内启动。");

        await service.WaitForStopAsync();

        try
        {
            context.RegisterObject?.Invoke(
                context.ExtensionId + "-window",
                null!);
        }
        catch
        {
        }

        return "燕子截图服务已停止。";
    }

    private static string HandleExisting(
        object service,
        string input)
    {
        var type = service.GetType();
        var text = input?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(text))
        {
            var run = type.GetMethod(
                "Run",
                [typeof(string)]);

            if (run != null)
            {
                run.Invoke(service, [text]);
                return "已发送到正在运行的燕子截图。";
            }
        }

        var toggle =
            type.GetMethod("Toggle") ??
            type.GetMethod("Activate") ??
            type.GetMethod("Show");

        toggle?.Invoke(service, null);

        return "已呼出燕子截图。";
    }
}
