using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        // ================= ویجت کوچک =================
        private void MiniRestoreBtn_Click(object sender, RoutedEventArgs e) => ExitMiniMode();

        // مینیمایز کردن پنجره (دکمه استاندارد ویندوز) دیگر به Taskbar نمی‌رود —
        // به‌جایش مستقیم وارد حالت ویجت کوچک می‌شود (مثل پلیرهای موزیک)
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                EnterMiniMode();
                return;
            }
            base.OnStateChanged(e);
        }

        // کلیک راست روی ویجت کوچک — «همیشه بالا» تا وقتی کاربر خودش خاموشش نکند فعال می‌ماند
        private void MiniAlwaysOnTop_Click(object sender, RoutedEventArgs e)
        {
            Topmost = MiniAlwaysOnTopMenuItem.IsChecked;
        }

        private void EnterMiniMode()
        {
            if (_isMiniMode) return;
            _isMiniMode = true;
            _normalWidth = Width; _normalHeight = Height;
            _normalLeft = Left; _normalTop = Top;

            RootGrid.Visibility = Visibility.Collapsed;
            MiniWidget.Visibility = Visibility.Visible;
            TitleBarRow.Height = new GridLength(0);

            // حذف نوار عنوان ویندوز تا فقط خود کارت دیده شود (بدون Always-on-Top)
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Width = Math.Round(300 * _uiScale); Height = Math.Round(86 * _uiScale);
            Left = SystemParameters.WorkArea.Right - Width - 16;
            Top = SystemParameters.WorkArea.Bottom - Height - 16;
        }

        private void ExitMiniMode()
        {
            if (!_isMiniMode) return;
            _isMiniMode = false;

            MiniWidget.Visibility = Visibility.Collapsed;
            RootGrid.Visibility = Visibility.Visible;
            TitleBarRow.Height = new GridLength(31);

            WindowStyle = WindowStyle.None;
            Width = _normalWidth; Height = _normalHeight;
            Left = _normalLeft; Top = _normalTop;
            ResizeMode = ResizeMode.NoResize;
        }


        // پاپ‌آپ کوچک و بی‌صدا گوشه پایین صفحه — جایگزین بالون ویندوز (فوکوس نمی‌دزدد، کلیک = بستن)
        private void Notify(string message)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    var text = new TextBlock
                    {
                        Text = Localization.T(message),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                        FontSize = 12.5,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 250,
                    };
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(14, 9, 14, 9),
                        Child = text,
                    };
                    var toast = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        ShowActivated = false,
                        ShowInTaskbar = false,
                        Topmost = true,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        FlowDirection = FlowDirection.RightToLeft,
                        FontFamily = FontFamily,
                        Content = border,
                    };
                    toast.MouseLeftButtonDown += (_, _) => toast.Close();
                    toast.Loaded += (_, _) =>
                    {
                        var wa = SystemParameters.WorkArea;
                        toast.Left = wa.Right - toast.ActualWidth - 14;
                        toast.Top = wa.Bottom - toast.ActualHeight - 14;
                    };
                    toast.Show();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
                        fade.Completed += (_, _) => toast.Close();
                        toast.BeginAnimation(UIElement.OpacityProperty, fade);
                    };
                    timer.Start();
                });
            }
            catch { }
        }

    }
}
