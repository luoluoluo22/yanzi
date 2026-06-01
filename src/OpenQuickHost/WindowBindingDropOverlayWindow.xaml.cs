using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace OpenQuickHost;

public partial class WindowBindingDropOverlayWindow : Window
{
    private readonly CommandItem _command;
    private readonly int _marginPixels;
    private IntPtr _targetWindow;
    private string _targetCorner = WindowBindingCorners.TopLeft;
    private int _targetOffsetX;
    private int _targetOffsetY;
    private bool _hasDropTarget;
    private bool _dropCommitted;

    public WindowBindingDropOverlayWindow(CommandItem command, int marginPixels = 14)
    {
        InitializeComponent();
        _command = command;
        _marginPixels = Math.Max(0, marginPixels);
        Loaded += (_, _) => EnsureToolWindowStyle();
    }

    public event Action<IntPtr, string, int, int>? BindingDropped;

    public void ShowFullDesktop()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Show();
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(CommandItem)))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            HideMarker();
            HideDragLabel();
            e.Handled = true;
            return;
        }

        // Update drag label position (follows cursor)
        if (!GetCursorPos(out var cursorPos))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            HideMarker();
            HideDragLabel();
            e.Handled = true;
            return;
        }

        UpdateDragLabel(cursorPos);

        if (!TryGetCursorTarget(cursorPos, out var hwnd, out var rect, out var corner))
        {
            e.Effects = System.Windows.DragDropEffects.None;
            HideMarker();
            HidePreviewIcon();
            e.Handled = true;
            return;
        }

        _targetWindow = hwnd;
        _targetCorner = corner;
        e.Effects = System.Windows.DragDropEffects.Copy;
        ShowMarker(rect, corner);
        ShowPreviewIcon(rect, Math.Max(96u, GetDpiForWindow(_targetWindow)), corner, cursorPos, out _targetOffsetX, out _targetOffsetY);
        _hasDropTarget = true;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!_dropCommitted && _hasDropTarget && _targetWindow != IntPtr.Zero && e.Data.GetDataPresent(typeof(CommandItem)))
        {
            _dropCommitted = true;
            BindingDropped?.Invoke(_targetWindow, _targetCorner, _targetOffsetX, _targetOffsetY);
        }

        Close();
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
    }

    private void ShowMarker(RECT rect, string corner)
    {
        var normalizedCorner = WindowBindingCorners.Normalize(corner);
        var isInterior = WindowBindingCorners.IsInterior(normalizedCorner);

        var text = normalizedCorner switch
        {
            WindowBindingCorners.TopRight => "右上",
            WindowBindingCorners.BottomLeft => "左下",
            WindowBindingCorners.BottomRight => "右下",
            WindowBindingCorners.InsideTopLeft => "内左上",
            WindowBindingCorners.InsideTopRight => "内右上",
            WindowBindingCorners.InsideBottomLeft => "内左下",
            WindowBindingCorners.InsideBottomRight => "内右下",
            _ => "左上"
        };
        CornerText.Text = $"{_command.Title}\n{text}";

        // Visual distinction: interior zones use a different color scheme
        if (isInterior)
        {
            CornerMarker.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x0B, 0x20, 0x12));
            CornerMarker.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80)); // Green for interior
        }
        else
        {
            CornerMarker.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x0B, 0x12, 0x20));
            CornerMarker.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x67, 0xE8, 0xF9)); // Cyan for exterior
        }

        var dpi = Math.Max(96, GetDpiForWindow(_targetWindow));
        var scale = dpi / 96.0;
        var leftDip = rect.Left / scale;
        var topDip = rect.Top / scale;
        var rightDip = rect.Right / scale;
        var bottomDip = rect.Bottom / scale;
        var windowWidth = Math.Max(1, rightDip - leftDip);
        var windowHeight = Math.Max(1, bottomDip - topDip);

        double markerLeft;
        double markerTop;

        if (isInterior)
        {
            // Interior: show marker inside the window at the target quadrant
            CornerMarker.Width = Math.Min(260, Math.Max(80, windowWidth / 3));
            CornerMarker.Height = 42;
            markerLeft = normalizedCorner switch
            {
                WindowBindingCorners.InsideTopRight or WindowBindingCorners.InsideBottomRight
                    => rightDip - CornerMarker.Width - 16,
                _ => leftDip + 16
            };
            markerTop = normalizedCorner switch
            {
                WindowBindingCorners.InsideBottomLeft or WindowBindingCorners.InsideBottomRight
                    => bottomDip - CornerMarker.Height - 16,
                _ => topDip + 16
            };
        }
        else
        {
            var isLeftOrRight = corner is WindowBindingCorners.TopLeft or WindowBindingCorners.BottomLeft
                ? IsPointerNearVerticalSide(leftDip, rightDip, topDip, bottomDip, leftSide: true)
                : IsPointerNearVerticalSide(leftDip, rightDip, topDip, bottomDip, leftSide: false);

            if (isLeftOrRight)
            {
                CornerMarker.Width = 42;
                CornerMarker.Height = Math.Min(520, Math.Max(120, windowHeight / 2));
                markerLeft = corner is WindowBindingCorners.TopLeft or WindowBindingCorners.BottomLeft
                    ? leftDip - CornerMarker.Width - 8
                    : rightDip + 8;
                markerTop = corner is WindowBindingCorners.BottomLeft or WindowBindingCorners.BottomRight
                    ? bottomDip - CornerMarker.Height
                    : topDip;
            }
            else
            {
                CornerMarker.Width = Math.Min(520, Math.Max(120, windowWidth / 2));
                CornerMarker.Height = 42;
                markerLeft = corner switch
                {
                    WindowBindingCorners.TopRight or WindowBindingCorners.BottomRight => rightDip - CornerMarker.Width,
                    _ => leftDip
                };
                markerTop = corner switch
                {
                    WindowBindingCorners.BottomLeft or WindowBindingCorners.BottomRight => bottomDip + 8,
                    _ => topDip - CornerMarker.Height - 8
                };
            }
        }

        Canvas.SetLeft(CornerMarker, Math.Clamp(markerLeft - Left, 0, Math.Max(0, Width - CornerMarker.Width)));
        Canvas.SetTop(CornerMarker, Math.Clamp(markerTop - Top, 0, Math.Max(0, Height - CornerMarker.Height)));
        CornerMarker.Visibility = Visibility.Visible;
    }

    private void HideMarker()
    {
        _targetWindow = IntPtr.Zero;
        _hasDropTarget = false;
        _targetOffsetX = 0;
        _targetOffsetY = 0;
        CornerMarker.Visibility = Visibility.Collapsed;
        HidePreviewIcon();
    }

    private static string TruncateLabel(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= 20 ? text : text[..20] + "…";
    }

    private void UpdateDragLabel(POINT cursorPos)
    {
        var dpi = Math.Max(96u, GetDpiForWindow(new WindowInteropHelper(this).Handle));
        var scale = dpi / 96.0;
        var x = cursorPos.X / scale - Left;
        var y = cursorPos.Y / scale - Top;

        DragLabel.Text = TruncateLabel(_command.Title);
        Canvas.SetLeft(DragLabelContainer, x - 40);
        Canvas.SetTop(DragLabelContainer, y + 24);
        DragLabelContainer.Visibility = Visibility.Visible;
    }

    private void HideDragLabel()
    {
        DragLabelContainer.Visibility = Visibility.Collapsed;
    }

    private void ShowPreviewIcon(RECT rect, uint dpi, string corner, POINT cursorPoint, out int offsetX, out int offsetY)
    {
        var placement = ComputePreviewPlacement(rect, dpi, corner, cursorPoint);
        offsetX = placement.OffsetX;
        offsetY = placement.OffsetY;

        // Set preview icon content from command
        if (_command.IconSource != null)
        {
            PreviewIconImage.Source = _command.IconSource;
            PreviewIconImage.Visibility = Visibility.Visible;
            PreviewIconVector.Visibility = Visibility.Collapsed;
            PreviewIconGlyph.Visibility = Visibility.Collapsed;
        }
        else if (_command.VectorIcon != null)
        {
            PreviewIconPath.Data = _command.VectorIcon;
            PreviewIconVector.Visibility = Visibility.Visible;
            PreviewIconImage.Visibility = Visibility.Collapsed;
            PreviewIconGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewIconGlyph.Text = _command.DisplayGlyph;
            PreviewIconGlyph.Visibility = Visibility.Visible;
            PreviewIconImage.Visibility = Visibility.Collapsed;
            PreviewIconVector.Visibility = Visibility.Collapsed;
        }

        PreviewIcon.Background = _command.IconSource != null
            ? System.Windows.Media.Brushes.Transparent
            : _command.AccentBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0x3B, 0x82, 0xF6));

        Canvas.SetLeft(PreviewIcon, Math.Clamp(placement.LeftDip - Left, 0, Math.Max(0, Width - 34)));
        Canvas.SetTop(PreviewIcon, Math.Clamp(placement.TopDip - Top, 0, Math.Max(0, Height - 34)));
        PreviewIcon.Visibility = Visibility.Visible;
    }

    private PreviewPlacement ComputePreviewPlacement(RECT rect, uint dpi, string corner, POINT cursorPoint)
    {
        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var leftDip = rect.Left / scale;
        var rightDip = rect.Right / scale;
        var topDip = rect.Top / scale;
        var bottomDip = rect.Bottom / scale;
        var normalizedCorner = WindowBindingCorners.Normalize(corner);
        const double contentSize = 34;
        var marginDip = _marginPixels / scale;

        double previewLeft;
        double previewTop;

        if (WindowBindingCorners.IsInterior(normalizedCorner))
        {
            previewLeft = cursorPoint.X / scale - contentSize / 2;
            previewTop = cursorPoint.Y / scale - contentSize / 2;
            previewLeft = Math.Clamp(previewLeft, leftDip + marginDip, Math.Max(leftDip + marginDip, rightDip - contentSize - marginDip));
            previewTop = Math.Clamp(previewTop, topDip + marginDip, Math.Max(topDip + marginDip, bottomDip - contentSize - marginDip));
        }
        else
        {
            var edge = ResolvePrimaryEdge(cursorPoint, rect);
            switch (edge)
            {
                case BindingEdge.Top:
                    previewLeft = Math.Clamp(cursorPoint.X / scale - contentSize / 2, leftDip, Math.Max(leftDip, rightDip - contentSize));
                    previewTop = topDip - contentSize - marginDip;
                    break;
                case BindingEdge.Bottom:
                    previewLeft = Math.Clamp(cursorPoint.X / scale - contentSize / 2, leftDip, Math.Max(leftDip, rightDip - contentSize));
                    previewTop = bottomDip + marginDip;
                    break;
                case BindingEdge.Right:
                    previewLeft = rightDip + marginDip;
                    previewTop = Math.Clamp(cursorPoint.Y / scale - contentSize / 2, topDip, Math.Max(topDip, bottomDip - contentSize));
                    break;
                default:
                    previewLeft = leftDip - contentSize - marginDip;
                    previewTop = Math.Clamp(cursorPoint.Y / scale - contentSize / 2, topDip, Math.Max(topDip, bottomDip - contentSize));
                    break;
            }
        }

        var baseLeft = GetBaseContentLeft(rect, dpi, contentSize, normalizedCorner, marginDip);
        var baseTop = GetBaseContentTop(rect, dpi, contentSize, normalizedCorner, marginDip);
        return new PreviewPlacement(
            previewLeft,
            previewTop,
            (int)Math.Round(previewLeft - baseLeft, MidpointRounding.AwayFromZero),
            (int)Math.Round(previewTop - baseTop, MidpointRounding.AwayFromZero));
    }

    private static double GetBaseContentLeft(RECT rect, uint dpi, double widthDip, string corner, double marginDip)
    {
        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var leftDip = rect.Left / scale;
        var rightDip = rect.Right / scale;
        var normalizedCorner = WindowBindingCorners.Normalize(corner);

        if (WindowBindingCorners.IsInterior(normalizedCorner))
        {
            return normalizedCorner switch
            {
                WindowBindingCorners.InsideTopRight or WindowBindingCorners.InsideBottomRight
                    => rightDip - widthDip - marginDip,
                _ => leftDip + marginDip
            };
        }

        return normalizedCorner switch
        {
            WindowBindingCorners.TopRight or WindowBindingCorners.BottomRight => rightDip + marginDip,
            _ => leftDip - widthDip - marginDip
        };
    }

    private static double GetBaseContentTop(RECT rect, uint dpi, double heightDip, string corner, double marginDip)
    {
        var scale = dpi <= 0 ? 1 : dpi / 96.0;
        var topDip = rect.Top / scale;
        var bottomDip = rect.Bottom / scale;
        var normalizedCorner = WindowBindingCorners.Normalize(corner);

        if (WindowBindingCorners.IsInterior(normalizedCorner))
        {
            return normalizedCorner switch
            {
                WindowBindingCorners.InsideBottomLeft or WindowBindingCorners.InsideBottomRight
                    => bottomDip - heightDip - marginDip,
                _ => topDip + marginDip
            };
        }

        return normalizedCorner switch
        {
            WindowBindingCorners.BottomLeft or WindowBindingCorners.BottomRight => bottomDip - heightDip,
            _ => topDip
        };
    }

    private void HidePreviewIcon()
    {
        PreviewIcon.Visibility = Visibility.Collapsed;
    }

    private static bool TryGetCursorTarget(POINT point, out IntPtr hwnd, out RECT rect, out string corner)
    {
        hwnd = IntPtr.Zero;
        rect = default;
        corner = WindowBindingCorners.TopLeft;

        var pointWindow = WindowFromPoint(point);
        var rootWindow = pointWindow == IntPtr.Zero ? IntPtr.Zero : GetAncestor(pointWindow, GaRoot);
        if (rootWindow != IntPtr.Zero &&
            IsWindowCandidate(rootWindow) &&
            GetWindowRect(rootWindow, out var rootRect) &&
            TryResolveBindingArea(point, rootRect, out var rootCorner))
        {
            hwnd = rootWindow;
            rect = rootRect;
            corner = rootCorner;
            return true;
        }

        foreach (var candidate in EnumerateTopLevelWindows())
        {
            if (!IsWindowCandidate(candidate))
            {
                continue;
            }

            if (!GetWindowRect(candidate, out var candidateRect) ||
                !TryResolveBindingArea(point, candidateRect, out var candidateCorner))
            {
                continue;
            }

            hwnd = candidate;
            rect = candidateRect;
            corner = candidateCorner;
            return true;
        }

        return false;
    }

    private static BindingEdge ResolvePrimaryEdge(POINT point, RECT rect)
    {
        var distances = new[]
        {
            (Edge: BindingEdge.Left, Distance: Math.Abs(point.X - rect.Left)),
            (Edge: BindingEdge.Right, Distance: Math.Abs(point.X - rect.Right)),
            (Edge: BindingEdge.Top, Distance: Math.Abs(point.Y - rect.Top)),
            (Edge: BindingEdge.Bottom, Distance: Math.Abs(point.Y - rect.Bottom))
        };

        return distances.OrderBy(item => item.Distance).First().Edge;
    }

    private static bool TryResolveBindingArea(POINT point, RECT rect, out string corner)
    {
        corner = WindowBindingCorners.TopLeft;
        const int bandPixels = 96;

        // Calculate distances to each edge
        var distToLeft = Math.Abs(point.X - rect.Left);
        var distToRight = Math.Abs(point.X - rect.Right);
        var distToTop = Math.Abs(point.Y - rect.Top);
        var distToBottom = Math.Abs(point.Y - rect.Bottom);
        var minEdgeDist = Math.Min(Math.Min(distToLeft, distToRight), Math.Min(distToTop, distToBottom));

        var isInsideHorizontally = point.X >= rect.Left && point.X <= rect.Right;
        var isInsideVertically = point.Y >= rect.Top && point.Y <= rect.Bottom;
        var isInsideWindow = isInsideHorizontally && isInsideVertically;

        // Determine horizontal half
        var windowCenterX = rect.Left + (rect.Right - rect.Left) / 2;
        var windowCenterY = rect.Top + (rect.Bottom - rect.Top) / 2;
        var isLeftHalf = point.X < windowCenterX;
        var isTopHalf = point.Y < windowCenterY;

        if (minEdgeDist <= bandPixels)
        {
            // Within edge band — external binding
            // Check if point is within reach of the window (in band or inside)
            var inHorizontalRange = point.X >= rect.Left - bandPixels && point.X <= rect.Right + bandPixels;
            var inVerticalRange = point.Y >= rect.Top - bandPixels && point.Y <= rect.Bottom + bandPixels;
            if (!inHorizontalRange || !inVerticalRange)
            {
                return false;
            }

            // Determine vertical component: nearest of top/bottom edges
            var vertical = distToTop <= distToBottom ? "top" : "bottom";
            // Determine horizontal component: nearest of left/right edges
            var horizontal = distToLeft <= distToRight ? "left" : "right";

            corner = (vertical, horizontal) switch
            {
                ("top", "right") => WindowBindingCorners.TopRight,
                ("bottom", "left") => WindowBindingCorners.BottomLeft,
                ("bottom", "right") => WindowBindingCorners.BottomRight,
                _ => WindowBindingCorners.TopLeft
            };
            return true;
        }

        if (isInsideWindow)
        {
            // Inside window, beyond edge band — interior binding
            corner = (isTopHalf, isLeftHalf) switch
            {
                (true, true) => WindowBindingCorners.InsideTopLeft,
                (true, false) => WindowBindingCorners.InsideTopRight,
                (false, true) => WindowBindingCorners.InsideBottomLeft,
                (false, false) => WindowBindingCorners.InsideBottomRight,
            };
            return true;
        }

        // Outside window and beyond edge band — no binding
        return false;
    }

    private static bool IsPointerNearVerticalSide(double leftDip, double rightDip, double topDip, double bottomDip, bool leftSide)
    {
        if (!GetCursorPos(out var point))
        {
            return leftSide;
        }

        const double band = 96;
        var dpi = Math.Max(96, GetDpiForWindow(WindowFromPoint(point)));
        var scale = dpi / 96.0;
        var x = point.X / scale;
        var y = point.Y / scale;
        return y >= topDip && y <= bottomDip && (leftSide
            ? x >= leftDip - band && x <= leftDip + band
            : x >= rightDip - band && x <= rightDip + band);
    }

    private static bool IsWindowCandidate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd))
        {
            return false;
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return rect.Right - rect.Left >= 80 && rect.Bottom - rect.Top >= 80;
    }

    private static IntPtr[] EnumerateTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            windows.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return windows.ToArray();
    }

    private void EnsureToolWindowStyle()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var style = GetWindowLongPtr(handle, GwlExstyle);
            SetWindowLongPtr(handle, GwlExstyle, new IntPtr(style.ToInt64() | WsExToolwindow | WsExNoactivate));
        }
        catch
        {
            // Best effort; drag binding still works without the tool window style.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private enum BindingEdge
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private readonly record struct PreviewPlacement(double LeftDip, double TopDip, int OffsetX, int OffsetY);

    private const int GwlExstyle = -20;
    private const uint GaRoot = 2;
    private const long WsExToolwindow = 0x00000080L;
    private const long WsExNoactivate = 0x08000000L;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
