using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OpenQuickHost
{
    public partial class MobileMessageNotificationCard : Window
    {
        private static readonly List<MobileMessageNotificationCard> ActiveCards = new();
        private static System.Drawing.Rectangle? _stackArea;

        private readonly DispatcherTimer _autoCloseTimer;
        private readonly string? _sourceDeviceId;
        private bool _registered;

        public MobileMessageNotificationCard(
            string deviceModel,
            string text,
            string? sourceDeviceId = null,
            string? imageDataUrl = null,
            string? imageFilePath = null)
        {
            InitializeComponent();

            _sourceDeviceId = sourceDeviceId;
            TitleText.Text = string.IsNullOrWhiteSpace(deviceModel) ? "手机发来消息" : deviceModel;
            MessageText.Text = text;
            TimeText.Text = DateTime.Now.ToString("HH:mm:ss");

            var preview = TryLoadPreview(imageDataUrl, imageFilePath);
            if (preview != null)
            {
                PreviewImage.Source = preview;
                PreviewContainer.Visibility = Visibility.Visible;
            }

            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                Close();
            };

            Loaded += (_, _) =>
            {
                RegisterAndPosition();
                _autoCloseTimer.Start();
            };

            SizeChanged += (_, _) =>
            {
                if (_registered)
                {
                    RepositionAll();
                }
            };

            Closed += (_, _) =>
            {
                _autoCloseTimer.Stop();
                UnregisterAndReposition();
            };
        }

        private void RegisterAndPosition()
        {
            CleanupClosedCards();

            if (ActiveCards.Count == 0)
            {
                _stackArea = System.Windows.Forms.Screen
                    .FromPoint(System.Windows.Forms.Cursor.Position)
                    .WorkingArea;
            }

            if (!ActiveCards.Contains(this))
            {
                ActiveCards.Add(this);
            }

            _registered = true;
            RepositionAll();
        }

        private void UnregisterAndReposition()
        {
            _registered = false;
            ActiveCards.Remove(this);
            CleanupClosedCards();

            if (ActiveCards.Count == 0)
            {
                _stackArea = null;
                return;
            }

            RepositionAll();
        }

        private static void CleanupClosedCards()
        {
            ActiveCards.RemoveAll(card => !card.IsLoaded || !card.IsVisible);
        }

        private static void RepositionAll()
        {
            CleanupClosedCards();
            if (ActiveCards.Count == 0)
            {
                _stackArea = null;
                return;
            }

            var area = _stackArea ?? System.Windows.Forms.Screen
                .FromPoint(System.Windows.Forms.Cursor.Position)
                .WorkingArea;

            double offsetY = 18;
            foreach (var card in ActiveCards.ToArray())
            {
                if (!card.IsLoaded || !card.IsVisible)
                {
                    continue;
                }

                var scaleX = card.GetDpiScaleX();
                var scaleY = card.GetDpiScaleY();
                var width = card.ActualWidth > 0 ? card.ActualWidth : card.Width;
                var height = card.ActualHeight > 0 ? card.ActualHeight : 100;

                card.Left = area.Right / scaleX - width - 18;
                card.Top = area.Top / scaleY + offsetY;
                offsetY += height + 10;
            }
        }

        private static BitmapImage? TryLoadPreview(string? dataUrl, string? filePath)
        {
            try
            {
                byte[]? bytes = null;

                if (!string.IsNullOrWhiteSpace(filePath) &&
                    File.Exists(filePath) &&
                    IsImagePath(filePath))
                {
                    bytes = File.ReadAllBytes(filePath);
                }
                else if (!string.IsNullOrWhiteSpace(dataUrl))
                {
                    const string marker = "base64,";
                    var markerIndex = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (markerIndex >= 0)
                    {
                        bytes = Convert.FromBase64String(dataUrl[(markerIndex + marker.Length)..]);
                    }
                }

                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 420;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                HostAssets.AppendLog($"Mobile notification image preview failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static bool IsImagePath(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".ico";
        }

        private void NotificationCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            _autoCloseTimer.Stop();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowMobileInboxWindow(_sourceDeviceId);
            }

            Close();
            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer.Stop();
            Close();
            e.Handled = true;
        }

        private double GetDpiScaleX()
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        }

        private double GetDpiScaleY()
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        }
    }
}
