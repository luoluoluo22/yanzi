using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;

namespace OpenQuickHost;

/// <summary>
/// 专有名词统一管理中心 (Terminology / Brand Terms)
/// 负责管理小程序、背包、仓库、燕环、燕语、燕幕、鼠标手势等核心业务名词，
/// 并提供模板占位符解析、强类型访问和运行时动态更新能力。
/// </summary>
public sealed class BrandTerms : INotifyPropertyChanged
{
    public static BrandTerms Current { get; } = new();

    // 默认专有名词定义
    public const string DefaultAppName = "燕子";
    public const string DefaultMiniApp = "小程序";
    public const string DefaultBackpack = "背包";
    public const string DefaultWarehouse = "仓库";
    public const string DefaultYanRing = "燕环";
    public const string DefaultYanVoice = "燕语";
    public const string DefaultYanScreen = "燕幕";
    public const string DefaultMouseGesture = "鼠标手势";
    public const string DefaultYanSelect = "燕选";
    public const string DefaultYanNest = "燕窝";

    private string _appName = DefaultAppName;
    private string _miniApp = DefaultMiniApp;
    private string _backpack = DefaultBackpack;
    private string _warehouse = DefaultWarehouse;
    private string _yanRing = DefaultYanRing;
    private string _yanVoice = DefaultYanVoice;
    private string _yanScreen = DefaultYanScreen;
    private string _mouseGesture = DefaultMouseGesture;
    private string _yanSelect = DefaultYanSelect;
    private string _yanNest = DefaultYanNest;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>主程序 / 品牌名称（默认：燕子）</summary>
    public string AppName
    {
        get => _appName;
        set => SetProperty(ref _appName, value, nameof(AppName), "Term.AppName");
    }

    /// <summary>小程序 / 轻应用 / 扩展（默认：小程序）</summary>
    public string MiniApp
    {
        get => _miniApp;
        set => SetProperty(ref _miniApp, value, nameof(MiniApp), "Term.MiniApp");
    }

    /// <summary>随身背包 / 快捷面板（默认：背包）</summary>
    public string Backpack
    {
        get => _backpack;
        set => SetProperty(ref _backpack, value, nameof(Backpack), "Term.Backpack");
    }

    /// <summary>主搜索窗口 / 小程序聚合中心（默认：仓库）</summary>
    public string Warehouse
    {
        get => _warehouse;
        set => SetProperty(ref _warehouse, value, nameof(Warehouse), "Term.Warehouse");
    }

    /// <summary>圆环快捷轮盘（默认：燕环）</summary>
    public string YanRing
    {
        get => _yanRing;
        set => SetProperty(ref _yanRing, value, nameof(YanRing), "Term.YanRing");
    }

    /// <summary>快捷文本/热字符串/扩展触发词指令（默认：燕语）</summary>
    public string YanVoice
    {
        get => _yanVoice;
        set => SetProperty(ref _yanVoice, value, nameof(YanVoice), "Term.YanVoice");
    }

    /// <summary>屏幕信息/小组件悬浮层（默认：燕幕）</summary>
    public string YanScreen
    {
        get => _yanScreen;
        set => SetProperty(ref _yanScreen, value, nameof(YanScreen), "Term.YanScreen");
    }

    /// <summary>鼠标手势轨迹功能（默认：鼠标手势）</summary>
    public string MouseGesture
    {
        get => _mouseGesture;
        set => SetProperty(ref _mouseGesture, value, nameof(MouseGesture), "Term.MouseGesture");
    }

    /// <summary>划词选中文本快捷菜单（默认：燕选）</summary>
    public string YanSelect
    {
        get => _yanSelect;
        set => SetProperty(ref _yanSelect, value, nameof(YanSelect), "Term.YanSelect");
    }

    /// <summary>工作区 / 多端协同集散地（默认：燕窝）</summary>
    public string YanNest
    {
        get => _yanNest;
        set => SetProperty(ref _yanNest, value, nameof(YanNest), "Term.YanNest");
    }

    // 静态便捷访问器
    public static string TermAppName => Current.AppName;
    public static string TermMiniApp => Current.MiniApp;
    public static string TermBackpack => Current.Backpack;
    public static string TermWarehouse => Current.Warehouse;
    public static string TermYanRing => Current.YanRing;
    public static string TermYanVoice => Current.YanVoice;
    public static string TermYanScreen => Current.YanScreen;
    public static string TermMouseGesture => Current.MouseGesture;
    public static string TermYanSelect => Current.YanSelect;
    public static string TermYanNest => Current.YanNest;

    /// <summary>
    /// 根据术语标识获取专有名词
    /// 支持 "MiniApp"、"Backpack"、"Term.MiniApp" 等
    /// </summary>
    public string GetTerm(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var cleanKey = key.Trim().Replace("Term.", string.Empty, StringComparison.OrdinalIgnoreCase);

        return cleanKey.ToLowerInvariant() switch
        {
            "appname" or "app" or "yanzi" => AppName,
            "miniapp" or "extension" or "applet" => MiniApp,
            "backpack" or "quickpanel" or "bag" => Backpack,
            "warehouse" or "store" or "searchbox" or "hub" => Warehouse,
            "yanring" or "radial" or "ring" or "wheel" => YanRing,
            "yanvoice" or "yanyu" or "voice" => YanVoice,
            "yanscreen" or "yanm" or "screen" or "overlay" => YanScreen,
            "mousegesture" or "gesture" or "gestures" => MouseGesture,
            "yanselect" or "select" => YanSelect,
            "yannest" or "nest" or "yanwo" => YanNest,
            _ => key
        };
    }

    private static readonly Regex PlaceholderRegex = new(@"(?:\{([A-Za-z0-9_\.]+)\}|\[([A-Za-z0-9_\.]+)\]|%([A-Za-z0-9_\.]+)%)", RegexOptions.Compiled);

    /// <summary>
    /// 将文本模板中的专有名词占位符进行替换
    /// 支持 {MiniApp}、[MiniApp] 或 %MiniApp% 占位符语法
    /// 例如：Format("在主界面[Warehouse]搜索您需要的[MiniApp]。") => "在主界面仓库搜索您需要的小程序。"
    /// </summary>
    public static string Format(string? template)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Success ? match.Groups[1].Value :
                      match.Groups[2].Success ? match.Groups[2].Value :
                      match.Groups[3].Value;
            var term = Current.GetTerm(key);
            return !string.Equals(term, key, StringComparison.OrdinalIgnoreCase) ? term : match.Value;
        });
    }

    /// <summary>
    /// 批量更新自定义专有名词字典（支持白标定制、租户配置或运行期热更新）
    /// </summary>
    public void UpdateCustomTerms(IDictionary<string, string> customTerms)
    {
        if (customTerms == null || customTerms.Count == 0) return;

        foreach (var (key, value) in customTerms)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;

            var cleanKey = key.Trim().Replace("Term.", string.Empty, StringComparison.OrdinalIgnoreCase);
            switch (cleanKey.ToLowerInvariant())
            {
                case "appname" or "app" or "yanzi":
                    AppName = value.Trim();
                    break;
                case "miniapp" or "extension" or "applet":
                    MiniApp = value.Trim();
                    break;
                case "backpack" or "quickpanel" or "bag":
                    Backpack = value.Trim();
                    break;
                case "warehouse" or "store" or "searchbox" or "hub":
                    Warehouse = value.Trim();
                    break;
                case "yanring" or "radial" or "ring" or "wheel":
                    YanRing = value.Trim();
                    break;
                case "yanvoice" or "yanyu" or "voice":
                    YanVoice = value.Trim();
                    break;
                case "yanscreen" or "yanm" or "screen" or "overlay":
                    YanScreen = value.Trim();
                    break;
                case "mousegesture" or "gesture" or "gestures":
                    MouseGesture = value.Trim();
                    break;
                case "yanselect" or "select":
                    YanSelect = value.Trim();
                    break;
                case "yannest" or "nest" or "yanwo":
                    YanNest = value.Trim();
                    break;
            }
        }
    }

    private void SetProperty(ref string field, string value, string propName, string resourceKey)
    {
        if (string.Equals(field, value, StringComparison.Ordinal)) return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        // 同步更新全局 XAML 资源，使 DynamicResource 即时生效
        if (System.Windows.Application.Current != null)
        {
            try
            {
                if (System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Resources[resourceKey] = value;
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        System.Windows.Application.Current.Resources[resourceKey] = value;
                    });
                }
            }
            catch
            {
                // 容错处理
            }
        }
    }
}
