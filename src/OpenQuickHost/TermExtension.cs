using System.Windows.Markup;

namespace OpenQuickHost;

/// <summary>
/// XAML 专有名词读取标记扩展
/// 用法：
///   Text="{local:Term MiniApp}"
///   Header="{local:Term Backpack}"
///   Content="{local:Term YanRing}"
///   Text="{local:Term YanVoice, Suffix='编辑'}"
///   Header="{local:Term MiniApp, Prefix='打开', Suffix='目录'}"
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TermExtension : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public string? Prefix { get; set; }
    public string? Suffix { get; set; }

    public TermExtension()
    {
    }

    public TermExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var val = BrandTerms.Current.GetTerm(Key);
        if (string.IsNullOrEmpty(Prefix) && string.IsNullOrEmpty(Suffix))
        {
            return val;
        }

        return $"{Prefix}{val}{Suffix}";
    }
}

/// <summary>
/// XAML 专有名词模板格式化标记扩展
/// 用法：
///   Text="{local:TermFormat '在主界面{Warehouse}搜索您需要的{MiniApp}。'}"
///   Text="{local:TermFormat '按住触发键临时显示信息层，或双击触发键固定进入{YanScreen}。'}"
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TermFormatExtension : MarkupExtension
{
    [ConstructorArgument("template")]
    public string Template { get; set; } = string.Empty;

    public TermFormatExtension()
    {
    }

    public TermFormatExtension(string template)
    {
        Template = template;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return BrandTerms.Format(Template);
    }
}
