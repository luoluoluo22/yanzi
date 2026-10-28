using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Yanzi.Avalonia;

public static class Converters
{
    public static readonly IValueConverter EditModeBrushConverter =
        new FuncValueConverter<bool?, IBrush>(b => b == true ? new SolidColorBrush(Color.Parse("#FFF59E0B")) : new SolidColorBrush(Color.Parse("#FFAAAAAA")));

    public static readonly IValueConverter PinBrushConverter =
        new FuncValueConverter<bool?, IBrush>(b => b == true ? new SolidColorBrush(Color.Parse("#FF3B82F6")) : new SolidColorBrush(Color.Parse("#FFAAAAAA")));

    public static readonly IValueConverter EmptyTextBrushConverter =
        new FuncValueConverter<bool?, IBrush>(b => b == true ? new SolidColorBrush(Color.Parse("#66FFFFFF")) : new SolidColorBrush(Color.Parse("#E0FFFFFF")));
}
