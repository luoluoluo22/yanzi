using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Yanzi.Avalonia;

public static class Converters
{
    public static readonly IValueConverter EditModeBrushConverter =
        new FuncValueConverter<bool?, IBrush>(b => b == true ? new SolidColorBrush(Color.Parse("#FFF59E0B")) : new SolidColorBrush(Color.Parse("#FFAAAAAA")));

    public static readonly IValueConverter PinBrushConverter =
        new FuncValueConverter<bool?, IBrush>(b => b == true ? new SolidColorBrush(Color.Parse("#FF3B82F6")) : new SolidColorBrush(Color.Parse("#FFAAAAAA")));

    public static readonly IValueConverter EmptyTextBrushConverter =
        new FuncValueConverter<bool?, IBrush>(b =>
        {
            var app = Application.Current;
            if (app != null)
            {
                var theme = app.ActualThemeVariant;
                if (b == true && app.TryGetResource("BrushTextMuted", theme, out var mutedObj) && mutedObj is IBrush mutedBrush)
                    return mutedBrush;
                if (b == false && app.TryGetResource("BrushTextMain", theme, out var mainObj) && mainObj is IBrush mainBrush)
                    return mainBrush;
            }
            return b == true ? new SolidColorBrush(Color.Parse("#888888")) : new SolidColorBrush(Color.Parse("#333333"));
        });
}
