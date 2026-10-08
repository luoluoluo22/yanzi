using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Yanzi.UI.Wpf;

/// <summary>Reusable single-character OTP entry. Controls are bound only to this view's state.</summary>
public sealed class YanziOtpInput : UserControl
{
    private readonly List<TextBox> _cells = new();
    public event EventHandler? ValueChanged;

    public int Length { get; }
    public string Value => string.Concat(_cells.Select(c => c.Text));
    public bool IsComplete => Value.Length == Length;

    public YanziOtpInput(int length = 6)
    {
        if (length < 2 || length > 12) throw new ArgumentOutOfRangeException(nameof(length));
        Length = length;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < length; i++)
        {
            var index = i;
            var cell = YanziUi.WithStyle(new TextBox
            {
                MaxLength = 1, Width = 38, Height = 39, Margin = new Thickness(0, 0, 7, 0),
                TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas")
            }, YanziUi.Styles.Input);
            cell.PreviewTextInput += (_, args) =>
            {
                if (args.Text.Any(character => !char.IsDigit(character))) args.Handled = true;
            };
            cell.TextChanged += (_, _) =>
            {
                ValueChanged?.Invoke(this, EventArgs.Empty);
                if (cell.Text.Length == 1 && index + 1 < _cells.Count)
                    _cells[index + 1].Focus();
            };
            cell.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Back && cell.Text.Length == 0 && index > 0)
                {
                    _cells[index - 1].Focus();
                    _cells[index - 1].Clear();
                    args.Handled = true;
                }
            };
            row.Children.Add(cell);
            _cells.Add(cell);
        }
        Content = row;
    }

    public void Clear()
    {
        foreach (var cell in _cells) cell.Clear();
        if (_cells.Count > 0) _cells[0].Focus();
    }

    public void SetValue(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length > Length || digits.Any(c => !char.IsDigit(c)))
            throw new ArgumentException("OTP must consist of digits within the configured length.", nameof(digits));
        for (var i = 0; i < _cells.Count; i++) _cells[i].Text = i < digits.Length ? digits[i].ToString() : "";
    }
}
