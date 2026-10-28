using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace OpenQuickHost;

public class QuickPanelDragGhostWindow : Window
{
    private static QuickPanelDragGhostWindow? _instance;
    public static QuickPanelDragGhostWindow Instance => _instance ??= CreateInstance();

    private static QuickPanelDragGhostWindow CreateInstance()
    {
        var win = new QuickPanelDragGhostWindow();
        var helper = new WindowInteropHelper(win);
        helper.EnsureHandle();
        win.Show();
        win.MoveOffscreen();
        HostAssets.AppendLog("[DragGhost] Initialized and positioned offscreen.");
        return win;
    }

    private Point _anchorOffset = new(29, 32);
    private IntPtr _hwnd;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    private readonly Grid _iconContainer;
    private readonly TextBlock _titleBlock;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private QuickPanelDragGhostWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        IsHitTestVisible = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 58;
        Height = 64;
        Left = -10000;
        Top = -10000;

        var rootBorder = new Border
        {
            Width = 58,
            Height = 64,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xDC, 0x1E, 0x29, 0x3B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x60, 0xA5, 0xFA)),
            BorderThickness = new Thickness(1.2),
            Padding = new Thickness(4, 4, 4, 3),
            Opacity = 0.90,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.55
            }
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _iconContainer = new Grid
        {
            Width = 40,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2)
        };
        stack.Children.Add(_iconContainer);

        _titleBlock = new TextBlock
        {
            Foreground = Brushes.WhiteSmoke,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 52,
            TextAlignment = TextAlignment.Center
        };
        stack.Children.Add(_titleBlock);

        rootBorder.Child = stack;
        Content = rootBorder;

        SourceInitialized += (_, _) =>
        {
            try
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
                HostAssets.AppendLog($"[DragGhost] SourceInitialized: HWND={_hwnd}, DpiScale=({_dpiScaleX},{_dpiScaleY})");
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"[DragGhost] SourceInitialized error: {ex.Message}");
            }
        };
    }

    public void ShowGhost(SlotViewModel slot, Point anchorOffset, double screenPixelX, double screenPixelY)
    {
        _anchorOffset = (anchorOffset.X >= 0 && anchorOffset.X <= 80 && anchorOffset.Y >= 0 && anchorOffset.Y <= 80)
            ? anchorOffset
            : new Point(29, 32);

        _iconContainer.Children.Clear();
        try
        {
            if (slot.IsFolder)
            {
                var folderIcon = new Path
                {
                    Data = Geometry.Parse("M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z"),
                    Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
                    Stretch = Stretch.Uniform,
                    Width = 32,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _iconContainer.Children.Add(folderIcon);
            }
            else if (slot.HasImageIcon && slot.Icon != null)
            {
                var img = new Image
                {
                    Source = slot.Icon,
                    Width = 36,
                    Height = 36,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                _iconContainer.Children.Add(img);
            }
            else if (slot.HasVectorIcon && slot.VectorIcon != null)
            {
                var path = new Path
                {
                    Data = slot.VectorIcon,
                    Fill = Brushes.White,
                    Stretch = Stretch.Uniform,
                    Width = 26,
                    Height = 26,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _iconContainer.Children.Add(path);
            }
            else if (slot.UseGlyphIcon && !string.IsNullOrEmpty(slot.DisplayGlyph))
            {
                var glyph = new TextBlock
                {
                    Text = slot.DisplayGlyph,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _iconContainer.Children.Add(glyph);
            }
            else
            {
                var defaultIcon = new Path
                {
                    Data = Geometry.Parse("M4,2A2,2 0 0,0 2,4V20A2,2 0 0,0 4,22H20A2,2 0 0,0 22,20V8L14,2H4M4,4H13V9H18V20H4V4Z"),
                    Fill = Brushes.WhiteSmoke,
                    Stretch = Stretch.Uniform,
                    Width = 28,
                    Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _iconContainer.Children.Add(defaultIcon);
            }
        }
        catch { }

        _titleBlock.Text = slot.Title;

        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
        }

        UpdatePositionPixels(screenPixelX, screenPixelY);
        HostAssets.AppendLog($"[DragGhost] ShowGhost: slot='{slot.Title}', Pos=({screenPixelX},{screenPixelY}), HWND={_hwnd}");
    }

    public void HideGhost()
    {
        MoveOffscreen();
        HostAssets.AppendLog("[DragGhost] HideGhost (moved offscreen).");
    }

    private void MoveOffscreen()
    {
        try
        {
            if (_hwnd == IntPtr.Zero)
            {
                _hwnd = new WindowInteropHelper(this).EnsureHandle();
            }

            if (_hwnd != IntPtr.Zero)
            {
                int widthPixels = (int)Math.Round(58 * _dpiScaleX);
                int heightPixels = (int)Math.Round(64 * _dpiScaleY);
                SetWindowPos(_hwnd, IntPtr.Zero, -10000, -10000, widthPixels, heightPixels, SwpNoZOrder | SwpNoActivate | SwpShowWindow);
            }
        }
        catch { }
    }

    public void UpdatePositionPixels(double screenPixelX, double screenPixelY)
    {
        try
        {
            if (_hwnd == IntPtr.Zero)
            {
                _hwnd = new WindowInteropHelper(this).EnsureHandle();
            }

            if (_hwnd != IntPtr.Zero)
            {
                int targetPixelX = (int)Math.Round(screenPixelX - _anchorOffset.X * _dpiScaleX);
                int targetPixelY = (int)Math.Round(screenPixelY - _anchorOffset.Y * _dpiScaleY);
                int widthPixels = (int)Math.Round(58 * _dpiScaleX);
                int heightPixels = (int)Math.Round(64 * _dpiScaleY);
                SetWindowPos(_hwnd, IntPtr.Zero, targetPixelX, targetPixelY, widthPixels, heightPixels, SwpNoZOrder | SwpNoActivate | SwpShowWindow);
            }
        }
        catch { }
    }
}
