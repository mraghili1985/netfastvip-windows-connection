using QRCoder;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SmartVpn
{
    public partial class HotspotWindow : Window
    {
        private readonly MainWindow _mainWin;
        private readonly global::SmartVpn.HotspotService _hotspot = new();
        private DispatcherTimer? _stabilizationTimer;
        private int _secondsRemaining = 0;
        private bool _isVpnConnected = false;

        public HotspotWindow(MainWindow mainWin)
        {
            InitializeComponent();
            _mainWin = mainWin;
            _hotspot.Log += msg => Dispatcher.Invoke(() => _mainWin.AppendLog(msg));
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // اعمال ترجمه روی دکمه‌های ثابت
            BtnStart.Content = Localization.T("🚀 روشن کردن هات‌اسپات");
            BtnStop.Content  = Localization.T("⏹ توقف و قطع");

            CheckVpnState();
            await RefreshAdaptersAsync(searchForTarget: false);

            if (_isVpnConnected)
            {
                BtnStart.IsEnabled = true;
                StatusText.Text = Localization.T("آماده راه‌اندازی هات‌اسپات.");
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
        }

        private void CheckVpnState()
        {
            bool isXray = _mainWin.XrayIsConnected;
            var activeConn = _mainWin.ActiveConnText?.Text?.Trim();
            bool isTrad = !string.IsNullOrEmpty(activeConn) && activeConn != "—" && activeConn != Localization.T("قطع شده");

            _isVpnConnected = isXray || isTrad;

            if (isXray)
            {
                string pName = _mainWin.XrayActiveProfile?.Alias ?? "sing-box";
                VpnStatusBadge.Text = Localization.T("وضعیت اتصال: متصل (Xray)") + $" ({pName})";
                VpnStatusBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else if (isTrad)
            {
                VpnStatusBadge.Text = Localization.T("وضعیت VPN: متصل") + $" ({activeConn})";
                VpnStatusBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else
            {
                VpnStatusBadge.Text = Localization.T("وضعیت VPN: متصل نیست (غیرفعال)");
                VpnStatusBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                ResetAllState(Localization.T("برای استفاده از هات‌اسپات ابتدا به VPN یا Xray متصل شوید."));
            }
        }

        public void OnVpnConnected()
        {
            _isVpnConnected = true;
            CheckVpnState();
            StartStabilizationCountdown();
        }

        private void StartStabilizationCountdown()
        {
            _stabilizationTimer?.Stop();
            // تایمر کاهش یافته به ۵ ثانیه
            _secondsRemaining = 5;

            BtnStart.IsEnabled = false;
            SourceCombo.IsEnabled = false;
            TargetCombo.IsEnabled = false;

            _stabilizationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _stabilizationTimer.Tick += async (s, e) =>
            {
                _secondsRemaining--;
                if (_secondsRemaining > 0)
                {
                    StatusText.Text = Localization.T("⏳ در حال تثبیت شبکه و درایورهای مجازی... ") + $"({_secondsRemaining} " + Localization.T("ثانیه") + ")";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                }
                else
                {
                    _stabilizationTimer.Stop();
                    StatusText.Text = Localization.T("✅ شبکه تثبیت شد. در حال شناسایی خودکار کارت‌ها...");
                    await RefreshAdaptersAsync(searchForTarget: false);
                    BtnStart.IsEnabled = true;
                    StatusText.Text = Localization.T("آماده راه‌اندازی هات‌اسپات.");
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                }
            };
            _stabilizationTimer.Start();
        }

        private async Task RefreshAdaptersAsync(bool searchForTarget)
        {
            var adapters = await _hotspot.GetAllAdaptersAsync();
            bool isXray = _mainWin.XrayIsConnected;
            var activeConn = isXray ? "sing-box" : (_mainWin.ActiveConnText?.Text?.Trim() ?? "");
            string? tunnelIp = isXray ? "172.19.0.1" : _mainWin._tunnelLocalIp;

            SourceCombo.Items.Clear();
            TargetCombo.Items.Clear();

            string? bestSrc = HotspotService.FindBestVpnSourceAdapter(activeConn, tunnelIp, adapters);
            string? bestTgt = searchForTarget ? HotspotService.FindBestTargetAdapter(adapters) : null;

            if (bestSrc != null)
            {
                SourceCombo.Items.Add(bestSrc);
                SourceCombo.SelectedIndex = 0;
                
                string lowerSrc = bestSrc.ToLowerInvariant();
                if (lowerSrc.Contains("ikev2") || lowerSrc.Contains("sstp") || lowerSrc.Contains("l2tp") || lowerSrc.Contains("pptp"))
                {
                    RasWarningBorder.Visibility = Visibility.Visible;
                }
                else
                {
                    RasWarningBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                SourceCombo.Items.Add(Localization.T("کارت شبکه VPN متصل یافت نشد"));
                SourceCombo.SelectedIndex = 0;
            }
            SourceCombo.IsEnabled = false; 

            if (searchForTarget && bestTgt != null)
            {
                TargetCombo.Items.Add(bestTgt);
                TargetCombo.SelectedIndex = 0;
            }
            else if (searchForTarget && bestTgt == null)
            {
                TargetCombo.Items.Add(Localization.T("کارت شبکه Wi-Fi Direct ساخته نشد"));
                TargetCombo.SelectedIndex = 0;
            }
            else
            {
                TargetCombo.Items.Add(Localization.T("در انتظار روشن شدن هات‌اسپات..."));
                TargetCombo.SelectedIndex = 0;
            }
            TargetCombo.IsEnabled = false; 
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!_isVpnConnected)
            {
                MessageBox.Show(Localization.T("ابتدا به VPN متصل شوید."), Localization.T("اخطار"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SourceCombo.SelectedItem == null || SourceCombo.SelectedItem.ToString()!.Contains(Localization.T("یافت نشد")))
            {
                await RefreshAdaptersAsync(searchForTarget: false);
            }

            if (SourceCombo.SelectedItem == null || SourceCombo.SelectedItem.ToString()!.Contains(Localization.T("یافت نشد")))
            {
                MessageBox.Show(Localization.T("کارت شبکه اینترنت (VPN) یافت نشد."), Localization.T("اخطار"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string sourceName = SourceCombo.SelectedItem.ToString()!;

            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = false;

            TargetCombo.Items.Clear();
            TargetCombo.Items.Add(Localization.T("در حال ایجاد کارت شبکه..."));
            TargetCombo.SelectedIndex = 0;

            StatusText.Text = Localization.T("مرحله ۱: راه‌اندازی هات‌اسپات...");
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));

            var startResult = await _hotspot.StartHotspotOnlyAsync(sourceName);
            if (!startResult.ok)
            {
                StatusText.Text = Localization.T("خطا در روشن شدن هات‌اسپات!");
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                BtnStart.IsEnabled = true;
                MessageBox.Show(startResult.err, Localization.T("خطا"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // تایمر کاهش یافته به ۵ ثانیه
            for (int i = 5; i > 0; i--)
            {
                StatusText.Text = Localization.T("مرحله ۲: تثبیت شبکه وای‌فای دایرکت... ") + $"({i} " + Localization.T("ثانیه") + ")";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                await Task.Delay(1000);
            }

            await RefreshAdaptersAsync(searchForTarget: true);

            string targetName = TargetCombo.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrEmpty(targetName) || targetName.Contains(Localization.T("ساخته نشد")) || targetName.Contains(Localization.T("ایجاد")))
            {
                StatusText.Text = Localization.T("کارت شبکه Wi-Fi Direct یافت نشد.");
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                await _hotspot.StopAsync();
                BtnStart.IsEnabled = true;
                return;
            }

            StatusText.Text = Localization.T("مرحله ۳: برقراری پل ارتباطی با ") + $"({sourceName} ➔ {targetName})...";
            var shareResult = await _hotspot.ApplySharingOnlyAsync(sourceName, targetName);

                        if (!shareResult.ok)
            {
                StatusText.Text = Localization.T("هات‌اسپات روشن شد! (نیاز به اشتراک‌گذاری دستی در صورت قطعی اینترنت)");
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                _mainWin.AppendLog("[Hotspot] شیرینگ خودکار انجام نشد. آموزش دستی در دسترس قرار گرفت.");
                
                BtnTutorial.Visibility = Visibility.Visible;
            }
            else
            {
                StatusText.Text = Localization.T("✅ هات‌اسپات با موفقیت فعال شد.");
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }

            DisplaySsidText.Text = startResult.ssid;
            DisplayPassText.Text = startResult.pass;
            InfoContainer.Visibility = Visibility.Visible;
            GenerateQrCode(startResult.ssid, startResult.pass);
            QrContainer.Visibility = Visibility.Visible;
            BtnStop.IsEnabled = true;
        }

        private void GenerateQrCode(string ssid, string password)
        {
            string wifiPayload = $"WIFI:T:WPA;S:{ssid};P:{password};;";
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(wifiPayload, QRCodeGenerator.ECCLevel.M);
            using var qrCode = new PngByteQRCode(qrData);
            byte[] qrBytes = qrCode.GetGraphic(20);

            using var ms = new MemoryStream(qrBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            QrImage.Source = bitmap;
        }

                private void BtnTutorial_Click(object sender, RoutedEventArgs e)
        {
            string src = SourceCombo.SelectedItem?.ToString() ?? "NETFASTVIP-ikev2";
            string tgt = TargetCombo.SelectedItem?.ToString() ?? "Local Area Connection* X";
            
            var msg = "ویندوز شما اجازه اشتراک‌گذاری خودکار (شیرینگ) را روی این پروتکل نمی‌دهد، اما هات‌اسپات الان روشن است!\n\n" +
                      "برای اینترنت‌دار کردنِ گوشی متصل به هات‌اسپات، این ۳ قدم ساده را انجام دهید:\n\n" +
                      "۱. در پنجره باز شده (ncpa.cpl)، روی کارت شبکه با نام «" + src + "» کلیک راست کرده و Properties را بزنید.\n" +
                      "۲. به تب Sharing بروید و تیک اول (Allow other network users...) را فعال کنید.\n" +
                      "۳. از منوی کشویی زیر آن، نام کارت شبکه «" + tgt + "» را انتخاب کرده و OK بزنید.\n\n" +
                      "تمام! اینترنت هات‌اسپات وصل شد.";
                      
            MessageBox.Show(msg, "راهنمای قدم به قدم اشتراک‌گذاری دستی", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "ncpa.cpl") { UseShellExecute = true }); } catch { }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            await ForceStopAsync();
        }

        private void ResetAllState(string message)
        {
            _stabilizationTimer?.Stop();
            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = false;
            
            InfoContainer.Visibility = Visibility.Collapsed;
            if (BtnTutorial != null) BtnTutorial.Visibility = Visibility.Collapsed;
            QrContainer.Visibility = Visibility.Collapsed;
            
            TargetCombo.Items.Clear();
            TargetCombo.Items.Add(Localization.T("در انتظار روشن شدن هات‌اسپات..."));
            TargetCombo.SelectedIndex = 0;
            
            StatusText.Text = Localization.T(message);
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
        }

        public void HandleVpnDisconnect()
        {
            _isVpnConnected = false;
            Dispatcher.Invoke(() =>
            {
                CheckVpnState();
                ResetAllState(Localization.T("اتصال VPN قطع شد. هات‌اسپات متوقف گردید."));
            });

            _ = Task.Run(async () =>
            {
                try { await _hotspot.StopAsync(); } catch { }
            });
        }

        public async Task ForceStopAsync()
        {
            BtnStop.IsEnabled = false;
            StatusText.Text = Localization.T("در حال آزادسازی کارت‌ها و توقف هات‌اسپات...");
            await _hotspot.StopAsync();
            ResetAllState(Localization.T("هات‌اسپات متوقف شد."));
            
            if (_isVpnConnected)
            {
                BtnStart.IsEnabled = true;
                StatusText.Text = Localization.T("آماده راه‌اندازی مجدد هات‌اسپات.");
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            await RefreshAdaptersAsync(searchForTarget: false);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}












