using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace OpenQuickHost;

public static class YanziScreenshotCapabilityProvider
{
    public static IEnumerable<YanziCapabilityProviderDefinition> Create()
    {
        yield return new()
        {
            Name="screenshot.capture",
            Description="截取当前 Windows 虚拟桌面并保存为 PNG，返回可继续交给 chat.send 的本地文件路径",
            Permissions=["screen.capture"], Category="screen",
            InputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"outputPath":{"type":"string"}},"additionalProperties":false}"""),
            OutputSchema=YanziCapabilitySchema.Parse("""{"type":"object","properties":{"filePath":{"type":"string"},"width":{"type":"integer"},"height":{"type":"integer"}},"required":["filePath","width","height"]}"""),
            Handler=CaptureAsync
        };
    }

    private static Task<object?> CaptureAsync(object? payload)
    {
        var input=(JsonElement)payload!;
        var path=input.TryGetProperty("outputPath",out var p)?p.GetString():null;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"Yanzi","Screenshots");
            Directory.CreateDirectory(dir);
            path=Path.Combine(dir,$"yanzi-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        }
        else
        {
            path=Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("截图输出路径必须是 .png。");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        var left=(int)Math.Floor(System.Windows.SystemParameters.VirtualScreenLeft);
        var top=(int)Math.Floor(System.Windows.SystemParameters.VirtualScreenTop);
        var width=Math.Max(1,(int)Math.Ceiling(System.Windows.SystemParameters.VirtualScreenWidth));
        var height=Math.Max(1,(int)Math.Ceiling(System.Windows.SystemParameters.VirtualScreenHeight));
        using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(left,top,0,0,new Size(width,height),CopyPixelOperation.SourceCopy);
        bitmap.Save(path,ImageFormat.Png);
        return Task.FromResult<object?>(new {filePath=path,width,height});
    }
}
