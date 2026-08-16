using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace OpenQuickHost;

internal static class ExtensionIconLibrary
{
    private static readonly HttpClient IconHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly IReadOnlyDictionary<string, string> MdiIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["arrow-left"] = "M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z",
        ["arrow-right"] = "M4,11V13H16L10.5,18.5L11.92,19.92L19.84,12L11.92,4.08L10.5,5.5L16,11H4Z",
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
        ["pin"] = "M14,3L21,10L18,11L15,18L13,18L13,12L8,17L7,16L12,11L6,11L6,9L13,8L14,3Z",
        ["plus"] = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z",
        ["circle-outline"] = "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 1,1 4,12A8,8 0 0,1 12,4Z",
        ["refresh"] = "M21,12C21,16.97 16.97,21 12,21C7.03,21 3,16.97 3,12C3,7.03 7.03,3 12,3C14.76,3 17.22,4.25 18.86,6.21 M21,3V8H16",
        ["sync"] = "M12,6V9L16,5L12,1V4A8,8 0 0,0 4,12C4,13.43 4.37,14.77 5.03,15.94L6.47,14.5C6.17,13.73 6,12.89 6,12A6,6 0 0,1 12,6M18.97,8.06L17.53,9.5C17.83,10.27 18,11.11 18,12A6,6 0 0,1 12,18V15L8,19L12,23V20A8,8 0 0,0 20,12C20,10.57 19.63,9.23 18.97,8.06Z",
        ["pause"] = "M14,19H18V5H14M6,19H10V5H6V19Z",
        ["logout"] = "M19,3H5C3.89,3 3,3.89 3,5V9H5V5H19V19H5V15H3V19C3,20.11 3.89,21 5,21H19C20.11,21 21,20.11 21,19V5C21,3.89 20.11,3 19,3M10.08,15.58L11.5,17L16.5,12L11.5,7L10.08,8.41L12.67,11H3V13H12.67L10.08,15.58Z",
        ["shortcut"] = "M19,10H17V8H19M19,13H17V11H19M16,10H14V8H16M16,13H14V11H16M16,17H8V15H16M7,10H5V8H7M7,13H5V11H7M8,11H10V13H8M8,8H10V10H8M11,11H13V13H11M11,8H13V10H11M20,5H4C2.89,5 2,5.89 2,7V17A2,2 0 0,0 4,19H20A2,2 0 0,0 22,17V7C22,5.89 21.1,5 20,5Z",
        ["keyboard"] = "M19 10H17V8H19M19 13H17V11H19M16 10H14V8H16M16 13H14V11H16M16 17H8V15H16M7 10H5V8H7M7 13H5V11H7M8 11H10V13H8M8 8H10V10H8M11 11H13V13H11M11 8H13V10H11M20 5H4C2.89 5 2 5.89 2 7V17C2 18.11 2.89 19 4 19H20C21.11 19 22 18.11 22 17V7C22 5.89 21.11 5 20 5Z",
        ["keyboard-outline"] = "M19 10H17V8H19M19 13H17V11H19M16 10H14V8H16M16 13H14V11H16M16 17H8V15H16M7 10H5V8H7M7 13H5V11H7M8 11H10V13H8M8 8H10V10H8M11 11H13V13H11M11 8H13V10H11M20 5H4C2.89 5 2 5.89 2 7V17C2 18.11 2.89 19 4 19H20C21.11 19 22 18.11 22 17V7C22 5.89 21.11 5 20 5Z",
        ["mouse"] = "M13 1.07C16.39 1.56 19 4.47 19 8V16C19 19.87 15.87 23 12 23C8.13 23 5 19.87 5 16V8C5 4.47 7.61 1.56 11 1.07V9H13V1.07M11 3.09C8.76 3.5 7 5.54 7 8H11V3.09M13 8H17C17 5.54 15.24 3.5 13 3.09V8Z",
        ["desktop-shortcut"] = "M4,2H20A2,2 0 0,1 22,4V16A2,2 0 0,1 20,18H16L12,22L8,18H4A2,2 0 0,1 2,16V4A2,2 0 0,1 4,2M4,4V16H8.83L12,19.17L15.17,16H20V4H4M13,14V10H15V14H18L14,18L10,14H13Z",
        ["cut"] = "M9.64,7.64C11.19,6.09 13.7,6.09 15.24,7.64L16.66,9.05L18.07,7.64L16.66,6.22C14.34,3.91 10.55,3.91 8.22,6.22C6.23,8.21 5.95,11.32 7.38,13.62L4,17V15H2V21H8V19H6L8.79,16.21L12.38,19.79C10.08,21.23 6.96,20.95 4.97,18.96L3.56,17.54L2.14,18.96L3.56,20.37C6.67,23.49 11.72,23.49 14.83,20.37L16.24,18.96L20.5,23.22L21.91,21.81L17.66,17.56L19.07,16.15C22.18,13.03 22.18,7.98 19.07,4.86L17.66,3.45L16.24,4.86L17.66,6.27C19.98,8.59 19.98,12.38 17.66,14.7L16.24,16.12L9.64,9.52C8.48,8.36 8.48,6.48 9.64,5.31",
        ["paste"] = "M19,20H5V4H7V2H17V6H19M19,8H5C3.89,8 3,8.89 3,10V20A2,2 0 0,0 5,22H19A2,2 0 0,0 21,20V10C21,8.89 20.11,8 19,8Z",
        ["skill-export"] = "M12,3 L20,7 V17 L12,21 L4,17 V7 Z M12,3 V21 M4,7 L12,11 L20,7",
        ["trash"] = "M9,3V4H4V6H5V19A2,2 0 0,0 7,21H17A2,2 0 0,0 19,19V6H20V4H15V3H9M7,6H17V19H7V6M9,8V17H11V8H9M13,8V17H15V8H13Z",
        ["edit"] = "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z",
        ["store"] = "M12,18H6V14H12M21,14V12L20,7H4L3,12V14H4V20H14V14H18V20H20V14M20,4H4V6H20V4Z",
        ["stop"] = "M6,6H18V18H6V6Z",
        ["broom"] = "M19.36,2.72L20.78,4.14L15.06,9.85C16.13,11.39 16.28,13.47 15.38,15.17L12.06,11.85L10.65,13.26L13.97,16.58C12.27,17.48 10.19,17.33 8.65,16.26L2.94,21.97L1.5,20.55L7.24,14.83C6.17,13.29 6.02,11.21 6.92,9.51L10.24,12.83L11.65,11.42L8.33,8.1C10.03,7.2 12.11,7.35 13.65,8.42L19.36,2.72Z"
    };

    private static readonly IReadOnlyDictionary<string, string> SvgAssetIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["folder"] = "folder.svg",
        ["clipboard"] = "copy-success.svg",
        ["copy"] = "copy-success.svg",
        ["note"] = "comment.svg",
        ["file"] = "form.svg",
        ["window"] = "dashboard.svg",
        ["search"] = "search.svg",
        ["clock"] = "clock.svg",
        ["timer"] = "timer.svg",
        ["chat"] = "comment.svg",
        ["ai"] = "ai.svg",
        ["settings"] = "settings.svg",
        ["star"] = "star.svg",
        ["pin"] = "pin.svg",
        ["pin-off"] = "pin-off.svg",
        ["plus"] = "add.svg",
        ["delete"] = "circle-delete.svg",
        ["check"] = "circle-check.svg",
        ["close"] = "close.svg",
        ["arrow-left-solid"] = "arrow-left-solid.svg",
        ["warning"] = "circle-warning.svg",
        ["user"] = "user.svg",
        ["users"] = "users.svg",
        ["calendar"] = "calendar.svg",
        ["lock"] = "lock.svg",
        ["info"] = "info.svg",
        ["more"] = "circle-more.svg",
        ["dashboard"] = "dashboard.svg",
        ["pen"] = "pen.svg",
        ["form"] = "form.svg",
        ["task"] = "task-done.svg",
        ["flag"] = "flag.svg",
        ["refresh"] = "refresh.svg",
        ["sync"] = "sync.svg",
        ["pause"] = "pause.svg",
        ["logout"] = "exit.svg",
        ["shortcut"] = "shortcut.svg",
        ["desktop-shortcut"] = "desktop-shortcut.svg",
        ["cut"] = "cut.svg",
        ["paste"] = "paste.svg",
        ["publish"] = "circle-arrow-up.svg",
        ["open-folder"] = "folder.svg",
        ["menu"] = "drawer.svg",
        ["about"] = "info.svg",
        ["show-main"] = "dashboard.svg",
        ["mouse-panel"] = "mouse-panel.svg",
        ["recycle"] = "recycle.svg",
        ["running"] = "timer.svg",
        ["help-docs"] = "form.svg",
        ["skill-export"] = "skill-export.svg",
        ["minimize"] = "minimize.svg",
        ["phone"] = "phone.svg",
        ["mobile"] = "phone-mobile.svg",
        ["briefcase"] = "briefcase.svg",
        ["location"] = "location.svg",
        ["trash"] = "circle-delete.svg",
        ["edit"] = "pen.svg"
    };

    private static readonly IReadOnlyDictionary<string, string> IconAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["notebook-outline"] = "note",
        ["notebook-edit-outline"] = "pen",
        ["note-text-outline"] = "note",
        ["clipboard-outline"] = "clipboard",
        ["clipboard-text"] = "clipboard",
        ["clipboard-text-outline"] = "clipboard",
        ["clipboard-check"] = "clipboard",
        ["clipboard-check-outline"] = "clipboard",
        ["clipboard-edit"] = "clipboard",
        ["clipboard-edit-outline"] = "clipboard",
        ["content-copy"] = "clipboard",
        ["text-box-edit-outline"] = "pen",
        ["text-box-search-outline"] = "note",
        ["monitor-dashboard"] = "dashboard",
        ["view-dashboard-outline"] = "dashboard",
        ["application-outline"] = "dashboard",
        ["cog-outline"] = "settings",
        ["folder-search-outline"] = "folder",
        ["folder-cog-outline"] = "folder",
        ["file-search-outline"] = "file",
        ["code-json"] = "form",
        ["code-tags"] = "code",
        ["console"] = "terminal",
        ["counter"] = "timer",
        ["compass-outline"] = "location",
        ["notebook"] = "note",
        ["magnify"] = "search"
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
        ["window"] = "window",
        ["user"] = "user",
        ["users"] = "users",
        ["calendar"] = "calendar",
        ["lock"] = "lock",
        ["dashboard"] = "dashboard"
    };

    private static readonly Dictionary<string, Geometry> GeometryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> ImageCache = new(StringComparer.OrdinalIgnoreCase);
    public static event Action<string, ImageSource?>? RemoteIconDownloaded;
    private static readonly HashSet<string> DownloadingUrls = [];
    private static readonly Lazy<IReadOnlyDictionary<string, string>> FullMdiIcons = new(LoadFullMdiIcons);

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
            CreateOption("mdi:pin", "固定"),
            CreateOption("mdi:plus", "新增"),
            CreateOption("mdi:calendar", "日历"),
            CreateOption("mdi:lock", "锁定"),
            CreateOption("mdi:user", "用户"),
            CreateOption("mdi:users", "多人"),
            CreateOption("mdi:dashboard", "工作台"),
            CreateOption("mdi:pen", "编辑"),
            CreateOption("mdi:task", "完成事项"),
            CreateOption("mdi:flag", "旗帜")
        ];
    }

    private static IReadOnlyList<ExtensionIconOption>? _allMdiOptionsCache;

    public static IReadOnlyList<ExtensionIconOption> GetAllMdiOptions()
    {
        if (_allMdiOptionsCache != null)
        {
            return _allMdiOptionsCache;
        }

        var list = new List<ExtensionIconOption>();
        var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var opt in GetBuiltInOptions())
        {
            list.Add(opt);
            addedKeys.Add(opt.Reference);
        }

        try
        {
            foreach (var kvp in FullMdiIcons.Value)
            {
                var reference = $"mdi:{kvp.Key}";
                if (addedKeys.Contains(reference))
                {
                    continue;
                }

                var geometry = ResolveVectorIcon(reference);
                if (geometry != null)
                {
                    list.Add(new ExtensionIconOption(reference, kvp.Key, geometry));
                    addedKeys.Add(reference);
                }
            }
        }
        catch
        {
            // Ignore
        }

        _allMdiOptionsCache = list;
        return list;
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

        var geometry = SvgAssetIcons.TryGetValue(iconKey, out var fileName)
            ? LoadSvgAssetGeometry(fileName)
            : Geometry.Parse(GetMdiPathData(iconKey));
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        GeometryCache[iconKey] = geometry;
        return geometry;
    }

    public static ImageSource? ResolveImageSource(string? iconReference, string? extensionDirectoryPath)
    {
        if (iconReference != null && iconReference.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
        {
            if (ImageCache.TryGetValue(iconReference, out var cachedUwpImage))
            {
                return cachedUwpImage;
            }
            var uwpIcon = NativeFileIconService.GetIcon(iconReference, isFolder: false);
            if (uwpIcon != null)
            {
                ImageCache[iconReference] = uwpIcon;
                return uwpIcon;
            }
        }

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
            var localPath = resolvedPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(resolvedPath, UriKind.Absolute).LocalPath
                : (File.Exists(resolvedPath) || Directory.Exists(resolvedPath) ? resolvedPath : null);

            HostAssets.AppendLog($"[IconLog] ResolveImageSource: iconReference='{iconReference}', resolvedPath='{resolvedPath}', localPath='{localPath}', exists={(!string.IsNullOrWhiteSpace(localPath) && (File.Exists(localPath) || Directory.Exists(localPath)))}.");

            if (!string.IsNullOrWhiteSpace(localPath) && (File.Exists(localPath) || Directory.Exists(localPath)))
            {
                if (ShouldPreferBitmapContent(localPath))
                {
                    var bitmapImage = LoadBitmapImage(resolvedPath);
                    ImageCache[resolvedPath] = bitmapImage;
                    return bitmapImage;
                }

                var systemIcon = NativeFileIconService.GetIcon(localPath, Directory.Exists(localPath));
                HostAssets.AppendLog($"[IconLog] NativeFileIconService.GetIcon for '{localPath}' returned {(systemIcon != null ? "SUCCESS" : "NULL")}.");
                if (systemIcon != null)
                {
                    ImageCache[resolvedPath] = systemIcon;
                    return systemIcon;
                }

                if (CanExtractAssociatedIcon(localPath))
                {
                    var extracted = TryExtractAssociatedIcon(localPath);
                    HostAssets.AppendLog($"[IconLog] TryExtractAssociatedIcon for '{localPath}' returned {(extracted != null ? "SUCCESS" : "NULL")}.");
                    if (extracted != null)
                    {
                        ImageCache[resolvedPath] = extracted;
                        return extracted;
                    }
                }
            }

            var bitmap = LoadBitmapImage(resolvedPath);
            ImageCache[resolvedPath] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"[IconLog] ResolveImageSource EXCEPTION for '{iconReference}': {ex}");
            ImageCache[resolvedPath] = null;
            return null;
        }
    }

    public static void InvalidateImageCache(string? iconReference, string? extensionDirectoryPath)
    {
        var resolvedPath = ResolveImagePath(iconReference, extensionDirectoryPath);
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return;
        }

        ImageCache.Remove(resolvedPath);
    }

    public static string? ResolveLocalIconFilePath(string? iconReference, string? extensionDirectoryPath)
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
                return Path.GetFullPath(absoluteUri.LocalPath);
            }

            return null;
        }

        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        if (!string.IsNullOrWhiteSpace(extensionDirectoryPath))
        {
            var combined = Path.GetFullPath(Path.Combine(extensionDirectoryPath, trimmed));
            if (File.Exists(combined))
            {
                return combined;
            }
        }

        return null;
    }

    private static bool ShouldPreferBitmapContent(string localPath)
    {
        var extension = Path.GetExtension(localPath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".img", StringComparison.OrdinalIgnoreCase);
    }

    private static BitmapImage LoadBitmapImage(string resolvedPath)
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

        return bitmap;
    }

    public static bool IsBuiltInReference(string? iconReference) => TryResolveVectorKey(iconReference, out _);

    private static bool TryResolveVectorKey(string? iconReference, out string iconKey)
    {
        iconKey = string.Empty;
        if (string.IsNullOrWhiteSpace(iconReference))
        {
            return false;
        }

        var trimmed = iconReference.Trim();
        if (trimmed.LastIndexOf('#') is var hashIdx && hashIdx > 0)
        {
            trimmed = trimmed[..hashIdx].TrimEnd(':');
        }

        if (!TryParseBuiltinReference(trimmed, out var library, out var name))
        {
            return false;
        }

        if (string.Equals(library, "mdi", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryResolveCanonicalIconKey(name, out iconKey))
            {
                return false;
            }

            return true;
        }

        if (string.Equals(library, "app", StringComparison.OrdinalIgnoreCase))
        {
            if (!AppIcons.TryGetValue(name, out var mappedIcon))
            {
                return false;
            }

            return TryResolveCanonicalIconKey(mappedIcon, out iconKey);
        }

        if (TryResolveCanonicalIconKey(name, out iconKey))
        {
            return true;
        }

        if (AppIcons.TryGetValue(name, out var fallbackIcon))
        {
            return TryResolveCanonicalIconKey(fallbackIcon, out iconKey);
        }

        return false;
    }

    private static bool TryResolveCanonicalIconKey(string name, out string iconKey)
    {
        if (IconAliases.TryGetValue(name, out var alias))
        {
            iconKey = alias;
            return HasVectorIconKey(iconKey);
        }

        if (HasVectorIconKey(name))
        {
            iconKey = name;
            return true;
        }

        iconKey = string.Empty;
        return false;
    }

    private static bool HasVectorIconKey(string iconKey)
    {
        return MdiIcons.ContainsKey(iconKey) ||
               SvgAssetIcons.ContainsKey(iconKey) ||
               FullMdiIcons.Value.ContainsKey(iconKey);
    }

    private static string GetMdiPathData(string iconKey)
    {
        if (MdiIcons.TryGetValue(iconKey, out var builtInPath))
        {
            return builtInPath;
        }

        if (FullMdiIcons.Value.TryGetValue(iconKey, out var mdiPath))
        {
            return mdiPath;
        }

        throw new KeyNotFoundException($"MDI icon not found: {iconKey}");
    }

    private static IReadOnlyDictionary<string, string> LoadFullMdiIcons()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mdi-icons.json", UriKind.Absolute));
            if (resource == null)
            {
                HostAssets.AppendLog("Full MDI icon resource not found: Assets/mdi-icons.json");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            using var stream = resource.Stream;
            var icons = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            return new Dictionary<string, string>(icons, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            HostAssets.AppendLog($"Full MDI icon resource load failed: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Geometry LoadSvgAssetGeometry(string fileName)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Icons/{fileName}", UriKind.Absolute))
            ?? throw new InvalidOperationException($"Icon resource not found: {fileName}");

        using var stream = resource.Stream;
        var document = XDocument.Load(stream);
        var pathData = document
            .Descendants()
            .FirstOrDefault(static element => string.Equals(element.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("d")
            ?.Value;

        if (string.IsNullOrWhiteSpace(pathData))
        {
            throw new InvalidOperationException($"Icon resource has no path data: {fileName}");
        }

        var geometry = Geometry.Parse(pathData);
        var bounds = geometry.Bounds;
        if (!bounds.IsEmpty && (Math.Abs(bounds.X) > double.Epsilon || Math.Abs(bounds.Y) > double.Epsilon))
        {
            var normalizedGeometry = geometry.CloneCurrentValue();
            normalizedGeometry.Transform = new TranslateTransform(-bounds.X, -bounds.Y);
            geometry = normalizedGeometry;
        }

        return geometry;
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

    private static bool CanExtractAssociatedIcon(string localPath)
    {
        var extension = Path.GetExtension(localPath);
        return string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".appref-ms", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> AssociatedIconCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? TryExtractAssociatedIcon(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        if (AssociatedIconCache.TryGetValue(localPath, out var cached))
        {
            return cached;
        }

        System.Drawing.Icon? icon = null;

        try
        {
            icon = System.Drawing.Icon.ExtractAssociatedIcon(localPath);

            if (icon == null)
            {
                AssociatedIconCache[localPath] = null;
                return null;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            AssociatedIconCache[localPath] = bitmap;
            return bitmap;
        }
        catch
        {
            AssociatedIconCache[localPath] = null;
            return null;
        }
        finally
        {
            icon?.Dispose();
        }
    }

    private static string? ResolveCachedRemoteImage(Uri uri)
    {
        var cacheDirectory = Path.Combine(HostAssets.RootPath, "icon-cache");
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, ComputeCacheName(uri.AbsoluteUri));
        if (File.Exists(cachePath))
        {
            return new Uri(cachePath).AbsoluteUri;
        }

        var url = uri.AbsoluteUri;
        lock (DownloadingUrls)
        {
            if (DownloadingUrls.Contains(url))
            {
                return null;
            }
            DownloadingUrls.Add(url);
        }

        Task.Run(async () =>
        {
            try
            {
                var bytes = await IconHttpClient.GetByteArrayAsync(uri);
                if (bytes.Length > 0)
                {
                    File.WriteAllBytes(cachePath, bytes);

                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null)
                    {
                        dispatcher.Invoke(() =>
                        {
                            var resolvedPath = new Uri(cachePath).AbsoluteUri;
                            var bitmap = LoadBitmapImage(resolvedPath);
                            ImageCache[resolvedPath] = bitmap;
                            RemoteIconDownloaded?.Invoke(url, bitmap);
                        });
                    }
                    else
                    {
                        var resolvedPath = new Uri(cachePath).AbsoluteUri;
                        var bitmap = LoadBitmapImage(resolvedPath);
                        ImageCache[resolvedPath] = bitmap;
                        RemoteIconDownloaded?.Invoke(url, bitmap);
                    }
                }
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Failed to download remote icon {url}: {ex.Message}");
            }
            finally
            {
                lock (DownloadingUrls)
                {
                    DownloadingUrls.Remove(url);
                }
            }
        });

        return null;
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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static System.Drawing.Icon? GetIconFromShellExtension(string extension)
    {
        try
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES;
            IntPtr hImg = SHGetFileInfo(extension, 0x80, ref shinfo, (uint)System.Runtime.InteropServices.Marshal.SizeOf(shinfo), flags);
            if (shinfo.hIcon != IntPtr.Zero)
            {
                System.Drawing.Icon icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(shinfo.hIcon).Clone();
                DestroyIcon(shinfo.hIcon);
                return icon;
            }
        }
        catch
        {
        }
        return null;
    }
}

internal sealed record ExtensionIconOption(string Reference, string Label, Geometry? Geometry);
