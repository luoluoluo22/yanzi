using System;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Yanzi.Shared;

namespace Yanzi.Avalonia;

public sealed class VectorIconExtension : MarkupExtension
{
    public string? Key { get; set; }

    public VectorIconExtension() { }

    public VectorIconExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return Geometry.Parse("M0,0");

        var pathData = VectorIconLibrary.GetPathData(Key);
        try
        {
            return Geometry.Parse(pathData);
        }
        catch
        {
            return Geometry.Parse("M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2Z");
        }
    }
}
