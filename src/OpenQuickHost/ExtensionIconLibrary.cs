using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenQuickHost;

internal static class ExtensionIconLibrary
{
    private static readonly HttpClient IconHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    private static readonly IReadOnlyDictionary<string, string> MdiIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["search"] = "M15.5,14H14.71L14.43,13.73C15.41,12.59 16,11.11 16,9.5A6.5,6.5 0 1,0 9.5,16C11.11,16 12.59,15.41 13.73,14.43L14,14.71V15.5L19,20.5L20.5,19L15.5,14M9.5,14C7.01,14 5,11.99 5,9.5C5,7.01 7.01,5 9.5,5C11.99,5 14,7.01 14,9.5C14,11.99 11.99,14 9.5,14Z",
        ["translate"] = "M12.87,15.07L11,13.2L11.05,13.15C12.32,11.74 13.22,10.13 13.75,8.43H15.82V6.43H10.43V5H8.43V6.43H3V8.43H11.84C11.35,9.85 10.57,11.19 9.5,12.39C8.81,11.62 8.24,10.76 7.75,9.85H5.75C6.33,11.19 7.13,12.44 8.15,13.56L4.4,17.32L5.81,18.73L9.5,15.04L11.8,17.34L12.87,15.07M17.5,10H15.5L11,22H13L14,19H19L20,22H22L17.5,10M14.75,17L16.5,12.33L18.25,17H14.75Z",
        ["folder"] = "M10,4H2C0.89,4 0,4.89 0,6V18A2,2 0 0,0 2,20H22A2,2 0 0,0 24,18V8C24,6.89 23.1,6 22,6H12L10,4Z",
        ["clipboard"] = "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3M12,3A1,1 0 0,1 13,4A1,1 0 0,1 12,5A1,1 0 0,1 11,4A1,1 0 0,1 12,3M19,19H5V5H19V19Z",
        ["note"] = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z",
        ["file"] = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2Z",
        ["window"] = "M4,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M4,8V18H20V8H4Z",
        ["clock"] = "M12,20A8,8 0 1,1 20,12A8,8 0 0,1 12,20M12,7V12.25L15.5,14.33L14.78,15.55L10.5,13V7H12Z",
        ["code"] = "M8.59,16.59L4,12L8.59,7.41L10,8.83L6.83,12L10,15.17L8.59,16.59M15.41,16.59L14,15.17L17.17,12L14,8.83L15.41,7.41L20,12L15.41,16.59Z",
        ["globe"] = "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M4,12A8,8 0 0,1 12,4C10.44,6.22 9.5,8.97 9.5,12C9.5,15.03 10.44,17.78 12,20A8,8 0 0,1 4,12M12,20C13.56,17.78 14.5,15.03 14.5,12C14.5,8.97 13.56,6.22 12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20M11.5,6.05C10.54,7.85 10,9.86 10,12C10,14.14 10.54,16.15 11.5,17.95C12.46,16.15 13,14.14 13,12C13,9.86 12.46,7.85 11.5,6.05Z",
        ["browser"] = "M4,5H20A2,2 0 0,1 22,7V17A2,2 0 0,1 20,19H4A2,2 0 0,1 2,17V7A2,2 0 0,1 4,5M4,8V17H20V8H4Z",
        ["terminal"] = "M4,5H20A2,2 0 0,1 22,7V17A2,2 0 0,1 20,19H4A2,2 0 0,1 2,17V7A2,2 0 0,1 4,5M7.5,10L10.5,12L7.5,14L6.5,13L8.5,12L6.5,11L7.5,10M11,14H14V13H11V14Z",
        ["chat"] = "M4,4H20A2,2 0 0,1 22,6V15A2,2 0 0,1 20,17H7L3,21V6A2,2 0 0,1 4,4Z",
        ["image"] = "M21,19V5A2,2 0 0,0 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19M8.5,11A1.5,1.5 0 1,1 10,9.5A1.5,1.5 0 0,1 8.5,11M5,19L9,14L12,17L16,12L19,16V19H5Z",
        ["settings"] = "M12,8A4,4 0 0,1 16,12A4,4 0 0,1 12,16A4,4 0 0,1 8,12A4,4 0 0,1 12,8M10,22C9.75,22 9.54,21.82 9.5,21.58L9.13,18.93C8.5,18.68 7.96,18.34 7.44,17.94L4.95,18.95C4.73,19.03 4.46,18.95 4.34,18.73L2.34,15.27C2.21,15.05 2.27,14.78 2.46,14.63L4.57,12.97L4.5,12L4.57,11L2.46,9.37C2.27,9.22 2.21,8.95 2.34,8.73L4.34,5.27C4.46,5.05 4.73,4.96 4.95,5.05L7.44,6.05C7.96,5.66 8.5,5.32 9.13,5.07L9.5,2.42C9.54,2.18 9.75,2 10,2H14C14.25,2 14.46,2.18 14.5,2.42L14.87,5.07C15.5,5.32 16.04,5.66 16.56,6.05L19.05,5.05C19.27,4.96 19.54,5.05 19.66,5.27L21.66,8.73C21.79,8.95 21.73,9.22 21.54,9.37L19.43,11L19.5,12L19.43,13L21.54,14.63C21.73,14.78 21.79,15.05 21.66,15.27L19.66,18.73C19.54,18.95 19.27,19.04 19.05,18.95L16.56,17.95C16.04,18.34 15.5,18.68 14.87,18.93L14.5,21.58C14.46,21.82 14.25,22 14,22H10Z",
        ["star"] = "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z",
        ["link"] = "M10.59,13.41L9.17,12L13.41,7.76L14.83,9.17L10.59,13.41M13.41,16.24L9.17,20.5L7.76,19.08L12,14.83L13.41,16.24M16.24,13.41L20.5,9.17L19.08,7.76L14.83,12L16.24,13.41M7.76,16.24L3.5,12L4.92,10.59L9.17,14.83L7.76,16.24Z",
        ["pin"] = "M14,3L21,10L18,11L15,18L13,18L13,12L8,17L7,16L12,11L6,11L6,9L13,8L14,3Z"
    };

    private static readonly IReadOnlyDictionary<string, string> AppIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["google"] = "globe",
        ["browser"] = "browser",
        ["wechat"] = "chat",
        ["qq"] = "chat",
        ["clipboard"] = "clipboard",
        ["selection"] = "clipboard",
        ["translate"] = "translate",
        ["notes"] = "note",
        ["timestamp"] = "clock",
        ["code"] = "code",
        ["script"] = "terminal",
        ["folder"] = "folder",
        ["file"] = "file",
        ["settings"] = "settings",
        ["image"] = "image",
        ["window"] = "window"
    };

    private static readonly Dictionary<string, Geometry> GeometryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ExtensionIconOption> GetBuiltInOptions()
    {
        return
        [
            CreateOption("mdi:search", "搜索"),
            CreateOption("mdi:translate", "翻译"),
            CreateOption("mdi:globe", "网页"),
            CreateOption("mdi:browser", "浏览器"),
            CreateOption("mdi:folder", "文件夹"),
            CreateOption("mdi:file", "文件"),
            CreateOption("mdi:clipboard", "剪贴板"),
            CreateOption("mdi:note", "便签"),
            CreateOption("mdi:clock", "时间"),
            CreateOption("mdi:code", "代码"),
            CreateOption("mdi:terminal", "脚本"),
            CreateOption("mdi:window", "窗口"),
            CreateOption("mdi:chat", "聊天"),
            CreateOption("mdi:image", "图片"),
            CreateOption("mdi:settings", "设置"),
            CreateOption("app:wechat", "微信"),
            CreateOption("app:qq", "QQ"),
            CreateOption("app:google", "谷歌"),
            CreateOption("app:selection", "选中内容"),
            CreateOption("mdi:star", "收藏"),
            CreateOption("mdi:link", "链接"),
            CreateOption("mdi:pin", "固定")
        ];
    }

    public static Geometry? ResolveVectorIcon(string? iconReference)
    {
        if (!TryResolveVectorKey(iconReference, out var iconKey))
        {
            return null;
        }

        if (GeometryCache.TryGetValue(iconKey, out var cachedGeometry))
        {
            return cachedGeometry;
        }

        var geometry = Geometry.Parse(MdiIcons[iconKey]);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        GeometryCache[iconKey] = geometry;
        return geometry;
    }

    public static ImageSource? ResolveImageSource(string? iconReference, string? extensionDirectoryPath)
    {
        var resolvedPath = ResolveImagePath(iconReference, extensionDirectoryPath);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return null;
        }

        if (ImageCache.TryGetValue(resolvedPath, out var cachedImage))
        {
            return cachedImage;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(resolvedPath, UriKind.Absolute);
            bitmap.EndInit();
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            ImageCache[resolvedPath] = bitmap;
            return bitmap;
        }
        catch
        {
            ImageCache[resolvedPath] = null;
            return null;
        }
    }

    public static bool IsBuiltInReference(string? iconReference) => TryResolveVectorKey(iconReference, out _);

    private static bool TryResolveVectorKey(string? iconReference, out string iconKey)
    {
        iconKey = string.Empty;
        if (!TryParseBuiltinReference(iconReference, out var library, out var name))
        {
            return false;
        }

        if (string.Equals(library, "mdi", StringComparison.OrdinalIgnoreCase))
        {
            if (!MdiIcons.ContainsKey(name))
            {
                return false;
            }

            iconKey = name;
            return true;
        }

        if (string.Equals(library, "app", StringComparison.OrdinalIgnoreCase))
        {
            if (!AppIcons.TryGetValue(name, out var mappedIcon))
            {
                return false;
            }

            iconKey = mappedIcon;
            return true;
        }

        if (MdiIcons.ContainsKey(name))
        {
            iconKey = name;
            return true;
        }

        if (AppIcons.TryGetValue(name, out var fallbackIcon))
        {
            iconKey = fallbackIcon;
            return true;
        }

        return false;
    }

    private static ExtensionIconOption CreateOption(string reference, string label)
    {
        return new ExtensionIconOption(reference, label, ResolveVectorIcon(reference));
    }

    private static string? ResolveImagePath(string? iconReference, string? extensionDirectoryPath)
    {
        var trimmed = iconReference?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || IsBuiltInReference(trimmed))
        {
            return null;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            if (absoluteUri.IsFile && File.Exists(absoluteUri.LocalPath))
            {
                return absoluteUri.AbsoluteUri;
            }

            if (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return ResolveCachedRemoteImage(absoluteUri);
            }

            if (string.Equals(absoluteUri.Scheme, "pack", StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUri.AbsoluteUri;
            }
        }

        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            return new Uri(Path.GetFullPath(trimmed)).AbsoluteUri;
        }

        if (!string.IsNullOrWhiteSpace(extensionDirectoryPath))
        {
            var combined = Path.GetFullPath(Path.Combine(extensionDirectoryPath, trimmed));
            if (File.Exists(combined))
            {
                return new Uri(combined).AbsoluteUri;
            }
        }

        return null;
    }

    private static string ResolveCachedRemoteImage(Uri uri)
    {
        var cacheDirectory = Path.Combine(HostAssets.RootPath, "icon-cache");
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, ComputeCacheName(uri.AbsoluteUri));
        if (File.Exists(cachePath))
        {
            return new Uri(cachePath).AbsoluteUri;
        }

        try
        {
            var bytes = IconHttpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
            if (bytes.Length > 0)
            {
                File.WriteAllBytes(cachePath, bytes);
                return new Uri(cachePath).AbsoluteUri;
            }
        }
        catch
        {
            // Fall back to direct URL so WPF can still attempt to load the icon.
        }

        return uri.AbsoluteUri;
    }

    private static string ComputeCacheName(string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        return hash + ".img";
    }

    private static bool TryParseBuiltinReference(string? iconReference, out string library, out string name)
    {
        library = string.Empty;
        name = string.Empty;
        var trimmed = iconReference?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (trimmed.Contains("://", StringComparison.OrdinalIgnoreCase) ||
            Path.IsPathRooted(trimmed) ||
            trimmed.StartsWith(".\\", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("..\\", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1)
        {
            return false;
        }

        library = trimmed[..separatorIndex].Trim();
        name = trimmed[(separatorIndex + 1)..].Trim().ToLowerInvariant();
        return string.Equals(library, "icon", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(library, "mdi", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(library, "app", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record ExtensionIconOption(string Reference, string Label, Geometry? Geometry);
