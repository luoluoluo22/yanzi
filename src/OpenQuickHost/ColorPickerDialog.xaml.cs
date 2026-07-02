using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenQuickHost
{
    public partial class ColorPickerDialog : Window
    {
        private double _h = 0; // 0 - 360
        private double _s = 1; // 0 - 1
        private double _b = 1; // 0 - 1
        private double _a = 1; // 0 - 1

        private bool _isUpdatingUi = false;
        private bool _isDraggingSb = false;
        private bool _isDraggingHue = false;
        private bool _isDraggingAlpha = false;

        public System.Windows.Media.Color SelectedColor { get; private set; } = System.Windows.Media.Colors.Blue;

        public ColorPickerDialog(Window owner, System.Windows.Media.Color initialColor)
        {
            InitializeComponent();
            Owner = owner;

            ColorToHsb(initialColor, out _h, out _s, out _b);
            _a = initialColor.A / 255.0;

            this.Loaded += ColorPickerDialog_Loaded;

            InitSwatches();
        }

        private void ColorPickerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateColor(notifyText: true);
        }

        private void InitSwatches()
        {
            var colors = new[]
            {
                "#FF3B82F6", "#FF10B981", "#FFEC4899", "#FFF59E0B", "#FF06B6D4", "#FFF87171", "#FF6366F1",
                "#FFFFFFFF", "#FFD1D5DB", "#FF9CA3AF", "#FF4B5563", "#FF1F2937", "#FF111827", "#FF000000"
            };

            foreach (var hex in colors)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = hex
                };

                var border = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(34, 255, 255, 255))
                };
                btn.Content = border;

                btn.Template = new ControlTemplate(typeof(System.Windows.Controls.Button))
                {
                    VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
                };

                btn.Click += (s, e) =>
                {
                    if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color c)
                    {
                        ColorToHsb(c, out _h, out _s, out _b);
                        _a = c.A / 255.0;
                        UpdateColor(notifyText: true);
                    }
                };

                SwatchGrid.Children.Add(btn);
            }
        }

        private void UpdateColor(bool notifyText = false)
        {
            if (_isUpdatingUi) return;
            _isUpdatingUi = true;

            try
            {
                var baseColor = ColorFromAhsb(1.0, _h, 1.0, 1.0);
                SbCanvas.Background = new SolidColorBrush(baseColor);

                SelectedColor = ColorFromAhsb(_a, _h, _s, _b);

                if (SbCanvas.ActualWidth > 0 && SbCanvas.ActualHeight > 0)
                {
                    Canvas.SetLeft(SbCursor, _s * SbCanvas.ActualWidth);
                    Canvas.SetTop(SbCursor, (1.0 - _b) * SbCanvas.ActualHeight);
                }

                if (HueTrack.ActualWidth > 0)
                {
                    Canvas.SetLeft(HueCursor, (_h / 360.0) * HueTrack.ActualWidth);
                }

                var solidColorOpaque = ColorFromAhsb(1.0, _h, _s, _b);
                var solidColorTransparent = ColorFromAhsb(0.0, _h, _s, _b);
                AlphaBrush.GradientStops[0].Color = solidColorTransparent;
                AlphaBrush.GradientStops[1].Color = solidColorOpaque;

                if (AlphaTrack.ActualWidth > 0)
                {
                    Canvas.SetLeft(AlphaCursor, _a * AlphaTrack.ActualWidth);
                }

                if (notifyText)
                {
                    HexInput.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
                    AlphaInput.Text = $"{Math.Round(_a * 100)}%";
                }
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void SbCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingSb = true;
                SbCanvas.CaptureMouse();
                UpdateSbFromMouse(e.GetPosition(SbCanvas));
            }
        }

        private void SbCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingSb)
            {
                UpdateSbFromMouse(e.GetPosition(SbCanvas));
            }
        }

        private void SbCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingSb)
            {
                _isDraggingSb = false;
                SbCanvas.ReleaseMouseCapture();
            }
        }

        private void UpdateSbFromMouse(System.Windows.Point p)
        {
            var w = SbCanvas.ActualWidth;
            var h = SbCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var x = Math.Max(0, Math.Min(p.X, w));
            var y = Math.Max(0, Math.Min(p.Y, h));

            _s = x / w;
            _b = 1.0 - (y / h);

            UpdateColor(notifyText: true);
        }

        private void HueTrack_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingHue = true;
                HueTrack.CaptureMouse();
                UpdateHueFromMouse(e.GetPosition(HueTrack));
            }
        }

        private void HueTrack_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingHue)
            {
                UpdateHueFromMouse(e.GetPosition(HueTrack));
            }
        }

        private void HueTrack_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingHue)
            {
                _isDraggingHue = false;
                HueTrack.ReleaseMouseCapture();
            }
        }

        private void UpdateHueFromMouse(System.Windows.Point p)
        {
            var w = HueTrack.ActualWidth;
            if (w <= 0) return;

            var x = Math.Max(0, Math.Min(p.X, w));
            _h = (x / w) * 360.0;
            if (_h >= 360.0) _h = 359.9;

            UpdateColor(notifyText: true);
        }

        private void AlphaTrack_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingAlpha = true;
                AlphaTrack.CaptureMouse();
                UpdateAlphaFromMouse(e.GetPosition(AlphaTrack));
            }
        }

        private void AlphaTrack_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingAlpha)
            {
                UpdateAlphaFromMouse(e.GetPosition(AlphaTrack));
            }
        }

        private void AlphaTrack_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingAlpha)
            {
                _isDraggingAlpha = false;
                AlphaTrack.ReleaseMouseCapture();
            }
        }

        private void UpdateAlphaFromMouse(System.Windows.Point p)
        {
            var w = AlphaTrack.ActualWidth;
            if (w <= 0) return;

            var x = Math.Max(0, Math.Min(p.X, w));
            _a = x / w;

            UpdateColor(notifyText: true);
        }

        private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi) return;

            var text = HexInput.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                if (!text.StartsWith("#")) text = "#" + text;
                if (System.Windows.Media.ColorConverter.ConvertFromString(text) is System.Windows.Media.Color c)
                {
                    ColorToHsb(c, out _h, out _s, out _b);
                    if (text.Length == 9)
                    {
                        _a = c.A / 255.0;
                    }
                    UpdateColor(notifyText: false);
                    AlphaInput.Text = $"{Math.Round(_a * 100)}%";
                }
            }
            catch
            {
            }
        }

        private void AlphaInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi) return;

            var text = AlphaInput.Text?.Trim().Replace("%", "");
            if (string.IsNullOrEmpty(text)) return;

            if (double.TryParse(text, out var val))
            {
                _a = Math.Max(0, Math.Min(val / 100.0, 1.0));
                UpdateColor(notifyText: false);
            }
        }

        private void DropperBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new DropperOverlayWindow(color =>
            {
                ColorToHsb(color, out _h, out _s, out _b);
                UpdateColor(notifyText: true);
            });
            picker.ShowDialog();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static System.Windows.Media.Color ColorFromAhsb(double a, double h, double s, double b)
        {
            double r = 0, g = 0, bl = 0;
            if (s == 0)
            {
                r = g = bl = b;
            }
            else
            {
                double sectorPosition = h / 60.0;
                int sectorNumber = (int)Math.Floor(sectorPosition);
                double fractionalSector = sectorPosition - sectorNumber;

                double p = b * (1 - s);
                double q = b * (1 - (s * fractionalSector));
                double t = b * (1 - (s * (1 - fractionalSector)));

                switch (sectorNumber)
                {
                    case 0:
                        r = b; g = t; bl = p; break;
                    case 1:
                        r = q; g = b; bl = p; break;
                    case 2:
                        r = p; g = b; bl = t; break;
                    case 3:
                        r = p; g = q; bl = b; break;
                    case 4:
                        r = t; g = p; bl = b; break;
                    case 5:
                        r = b; g = p; bl = q; break;
                }
            }
            return System.Windows.Media.Color.FromArgb((byte)(a * 255), (byte)(r * 255), (byte)(g * 255), (byte)(bl * 255));
        }

        private static void ColorToHsb(System.Windows.Media.Color color, out double h, out double s, out double b)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double bl = color.B / 255.0;

            double min = Math.Min(r, Math.Min(g, bl));
            double max = Math.Max(r, Math.Max(g, bl));
            double delta = max - min;

            b = max;
            if (max == 0)
            {
                s = 0;
                h = 0;
                return;
            }

            s = delta / max;

            if (r == max)
            {
                h = (g - bl) / delta;
            }
            else if (g == max)
            {
                h = 2 + (bl - r) / delta;
            }
            else
            {
                h = 4 + (r - g) / delta;
            }

            h *= 60.0;
            if (h < 0)
            {
                h += 360.0;
            }
            if (double.IsNaN(h))
            {
                h = 0;
            }
        }
    }

    public class DropperOverlayWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        private readonly Action<System.Windows.Media.Color> _onPicked;

        public DropperOverlayWindow(Action<System.Windows.Media.Color> onPicked)
        {
            _onPicked = onPicked;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            Cursor = System.Windows.Input.Cursors.Pen;

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            MouseDown += DropperOverlayWindow_MouseDown;
            KeyDown += DropperOverlayWindow_KeyDown;
        }

        private void DropperOverlayWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private void DropperOverlayWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                var screenPoint = PointToScreen(e.GetPosition(this));
                var color = GetScreenPixelColor((int)screenPoint.X, (int)screenPoint.Y);
                _onPicked?.Invoke(color);
                Close();
            }
            else
            {
                Close();
            }
        }

        private System.Windows.Media.Color GetScreenPixelColor(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);

            byte r = (byte)(pixel & 0x000000FF);
            byte g = (byte)((pixel & 0x0000FF00) >> 8);
            byte b = (byte)((pixel & 0x00FF0000) >> 16);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }
    }
}
