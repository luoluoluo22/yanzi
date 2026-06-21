using System;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace OpenQuickHost
{
    public partial class MobileMessageNotificationCard : Window
    {
        public MobileMessageNotificationCard(string deviceModel, string text)
        {
            InitializeComponent();
            TitleText.Text = string.IsNullOrWhiteSpace(deviceModel) ? "手机发来消息" : deviceModel;
            MessageText.Text = text;
            TimeText.Text = DateTime.Now.ToString("HH:mm:ss");

            Loaded += (_, _) =>
            {
                PositionTopRight();
                StartAutoCloseTimer();
            };
        }

        private void PositionTopRight()
        {
            var area = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            Left = area.Right / GetDpiScaleX() - Width - 18;
            Top = area.Top / GetDpiScaleY() + 18;
        }

        private void StartAutoCloseTimer()
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Close();
            };
            timer.Start();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
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
