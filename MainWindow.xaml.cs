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
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartVpn.XrayCore;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        private const string TxtReady = "آماده اتصال";
        private const string TxtConnecting = "در حال اتصال...";
        private const string TxtConnected = "متصل";
        private const string TxtReconnecting = "تلاش مجدد برای اتصال...";
        private const string TxtAuthFailed = "احراز هویت ناموفق بود";
        private const string MsgNoConn = "هیچ کانکشنی برای اتصال وجود ندارد.";
        private const string MsgAuthFailed = "نام کاربری یا رمز عبور اشتباه است — یا ممکن است اشتراک شما به پایان رسیده باشد.";
        private const string BtnCheckSubscription = "بررسی اشتراک";
        private const string BtnOkText = "باشه";
        private static string MsgCaInstall => $"برای اتصال از طریق SSTP یا IKEv2 لازم است گواهی امنیتی {AppConfig.BrandName} یک‌بار روی ویندوز نصب شود. نصب شود؟";
        private const string BtnInstallCa = "نصب گواهی";
        private const string BtnNotNow = "حالا نه";
        private const string MsgCaRequired = "بدون نصب گواهی، اتصال SSTP / IKEv2 ممکن نیست.";
        private const string LogCaInstalled = "CA certificate installed successfully.";
        private const string LogCaFailed = "CA certificate installation failed — SSTP/IKEv2 profiles excluded.";
        private const string LogCaSkipped = "CA certificate installation skipped — SSTP/IKEv2 profiles excluded.";
        private const string MsgBaseMissing = "فایل base.ovpn موجود نیست.";
        private const string MsgDeleteConfirm = "این کانکشن حذف شود؟";
        private const string MsgImported = "ایمپورت انجام شد.";
        private const string MsgExported = "پکیج اکسپورت شد.";
        private const string CredPromptSuffix = "ورود اطلاعات کاربری";
        private const string NotifyConnected = "اتصال برقرار شد";
        private const string NotifyReconnected = "اتصال مجدد برقرار شد";
        private const string NotifyReconnecting = "اتصال قطع شد — تلاش مجدد...";
        private const string NotifyStopped = "اتصال قطع شد.";
        private const string MsgSwitchConfirm = "کانکشن «{0}» قطع و «{1}» وصل شود؟";
        private const string MsgConnectConfirm = "به «{0}» وصل شوی؟";
        private const string MsgLockedWhileConnected = "هنگام اتصال نمی‌توان کانکشن‌ها را ویرایش یا حذف کرد. ابتدا اتصال را قطع کنید.";
        private const string MsgExportChoice = "بک‌اپ کانکشن‌ها چطور ساخته شود؟";
        private const string BtnExportWithCreds = "همراه یوزر/پسورد";
        private const string BtnExportNoCreds = "بدون یوزر/پسورد";
        private const string NotifyIdleStopped = "به‌دلیل بی‌استفادگی، اتصال قطع شد.";
        private const string LogIdleStopped = "Auto-disconnected after {0} minute(s) of inactivity.";
        private const string TrayShowApp = "نمایش برنامه";
        private const string TrayExit = "خروج";
        private const string TrayHint = "برنامه کنار ساعت باز است — برای خروج کامل، روی آیکون راست‌کلیک کنید و «خروج» را بزنید.";
        private const string LogExitIpChanged = "Exit IP changed: {0} -> {1}";
        private const string MsgExitConfirm = "اتصال VPN فعال است — با خروج از برنامه قطع می‌شود. خارج شوید؟";

        private readonly AppConfig _config = AppConfig.Load();
        private readonly VpnEngine _engine = new VpnEngine();
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        private readonly List<double> _dlHistory = new List<double>();
        private readonly List<double> _ulHistory = new List<double>();

        private string? _selectedName;
        private string? _connectedName;
        private string _currentStatusKey = "آماده اتصال"; // کلید فارسی وضعیت جاری برای ترجمه مجدد هنگام تغییر زبان
        private bool _isMiniMode;
        private int _geoGen;
        private bool _ready;

        internal bool IsLightTheme()
        {
            return !App.IsDark;
        }
        
        private bool _everConnected;
        private bool _wasReconnecting;
        private bool _manualStop;
        private string _currentPowerState = "off";

        private DispatcherTimer? _statsTimer;
        private DateTime _connectStart;
        private long _lastDownBytes = -1;
        private long _lastUpBytes = -1;
        private long _sessionStartTotalBytes;
        internal string? _tunnelLocalIp;
        private string? _tunnelNicId;
        private int _nicFindTries;

        private string? _pingHost;
        private string? _tunnelPeerIp;
        private HashSet<string> _routesBeforeConnect = new();
        private int _pingTick;
        private int _pingFails;
        private bool _pingInFlight;

        private int _idleTick;
        private long _idleWindowBytes;
        private int _idleMinutes;
        private bool _activeIsWg;
        private bool _reallyExit;
        private bool _trayHintShown;

        private double _normalWidth, _normalHeight, _normalLeft, _normalTop;
        private string? _nicBase;

        private System.Diagnostics.Process? _pingProc;
        private System.Diagnostics.Process? _tracertProc;

        private System.Windows.Forms.NotifyIcon? _tray;
        private System.Drawing.Icon? _trayIcon;

        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private static string RunValueName => AppConfig.BrandName;

        private double _uiScale = 1.0;

        private void FitToScreen()
        {
            var wa = SystemParameters.WorkArea;
            var targetBaseWidth = _config.SidebarExpanded ? 880.0 : 670.0;
            var scale = Math.Min(1.0, Math.Min(wa.Width / 880.0, (wa.Height - 6) / 580.0));
            _uiScale = (scale < 0.999) ? scale : 1.0;
            if (RootScale != null)
            {
                RootScale.ScaleX = _uiScale;
                RootScale.ScaleY = _uiScale;
            }
            Width = Math.Round(targetBaseWidth * _uiScale);
            Height = Math.Round(580 * _uiScale);
        }

        public MainWindow()
        {
            InitializeComponent();
            Title = AppConfig.BrandName;
            if (BrandTitleText != null) BrandTitleText.Text = AppConfig.BrandName;
            if (TitleBrandText != null) TitleBrandText.Text = AppConfig.BrandName;
            if (SidebarBrandText != null) SidebarBrandText.Text = AppConfig.BrandName;
            if (ExitModalTitle != null) ExitModalTitle.Text = string.Format(Localization.T("خروج از برنامه {0}"), AppConfig.BrandName);
            FitToScreen();
            InitXrayPanel();
            Loaded += async (_, _) => await UpdateChecker.CheckAsync(this);
            Loaded += (_, _) =>
            {
                UpdateSidebarAccountCard();
                InitializeSubscriptionSummaryCardFromCache();
            };
            Loaded += async (_, _) =>
            {
                // اگر از روش پنل و API استفاده می‌شود (آدرس پورتال تنظیم است)، هاست استاتیک قدیمی را لود نکن
                if (!string.IsNullOrWhiteSpace(_config.PortalApiUrl))
                {
                    if (!string.IsNullOrWhiteSpace(_config.CustomerRefreshToken) ||
                        !string.IsNullOrWhiteSpace(_config.CustomerUsername))
                    {
                        _ = RefreshSubscriptionSummaryAsync();
                    }
                    return;
                }
                await ConnectionsUpdateChecker.CheckAsync(this, _config);
            };
            Loaded += (_, _) =>
            {
                Task.Run(async () =>
                {
                    while (true)
                    {
                        await UptimeKumaClient.UpdateStatusAsync();
                        await Dispatcher.InvokeAsync(() => { try { RefreshList(); } catch { } });
                        await Task.Delay(15000);
                    }
                });
            };

            _engine.Log += AppendConnLog;
            _hotspot.Log += AppendConnLog;
            _engine.Connected += OnEngineConnected;
            
            // اتصال رویدادهای هات‌اسپات داخلی
            _engine.Connected += delegate { Dispatcher.Invoke(() => { try { HsOnVpnConnected(); } catch {} }); };
            _engine.Reconnecting += OnEngineReconnecting;
            _engine.Reconnecting += delegate { Dispatcher.Invoke(() => { try { HsHandleVpnDisconnect(); } catch {} }); };
            _engine.Stopped += OnEngineStopped;
            _engine.Stopped += delegate { Dispatcher.Invoke(() => { try { HsHandleVpnDisconnect(); } catch {} }); };

            _engine.AuthFailed += OnEngineAuthFailed;

            SplitTunnel.Log += AppendConnLog;
            KillSwitch.Log += AppendConnLog;
            RasProvider.Log += AppendConnLog;
            OpenVpnProvider.Log += AppendConnLog;
            CredentialsDialog.PanelUrl = _config.PanelUrl;
            DnsManager.Log += AppendConnLog;

            AppDomain.CurrentDomain.UnhandledException += (_, exArgs) =>
            {
                try { LogWriter.Write("CRASH: " + exArgs.ExceptionObject); } catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, exArgs) =>
            {
                try { LogWriter.Write("TASK CRASH: " + exArgs.Exception); } catch { }
                exArgs.SetObserved();
            };
            System.Windows.Application.Current.DispatcherUnhandledException += (_, exArgs) =>
            {
                try { LogWriter.Write("UI CRASH: " + exArgs.Exception); } catch { }
                exArgs.Handled = true;
            };

            MiniWidget.MouseLeftButtonDown += (_, __) => { try { DragMove(); } catch { } };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            EnableElevatedDragDrop();
            ApplyDarkTitleBar();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_DONOTROUND = 1;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private static void EnableWindows10Acrylic(IntPtr hwnd)
        {
            try
            {
                var policy = new AccentPolicy
                {
                    AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
                    AccentFlags = 2,
                    GradientColor = unchecked((int)0x991E232E),
                    AnimationId = 0
                };
                int size = Marshal.SizeOf(policy);
                var ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = ptr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(ptr);
            }
            catch { }
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                // عنوان ویندوز 11 باید با تم داخلی برنامه هماهنگ بماند.
                int darkMode = App.IsDark ? 1 : 0;
                int caption = App.IsDark ? 0x0020110B : 0x00F9F5F1;
                int text = App.IsDark ? 0x00F8FAFC : 0x000F172A;

                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));

                // ویندوز 11 (بیلد 22621 به بعد): افکت سیستم اکریلیک/میکا روی کل کلاینت اریا
                const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
                int backdrop = 3; // 3 = Acrylic blur
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

                // با داشتن WindowChrome نیتیو، کرنل DWM ویندوز ۱۱ لبه‌ها را ۱۲ پیکسل گرد می‌کند.
                int cornerPreference = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch { }
        }

        private void AppTitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
            if (e.ClickCount == 2) return;
            try { DragMove(); } catch { }
        }

        private void TitleMinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void TitleCloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private Rect GetCurrentWorkArea()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
                    if (screen != null)
                    {
                        var source = PresentationSource.FromVisual(this);
                        if (source?.CompositionTarget != null)
                        {
                            var m = source.CompositionTarget.TransformFromDevice;
                            var pt = m.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
                            var sz = m.Transform(new Vector(screen.WorkingArea.Width, screen.WorkingArea.Height));
                            return new Rect(pt.X, pt.Y, sz.X, sz.Y);
                        }
                        return new Rect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height);
                    }
                }
            }
            catch { }
            return SystemParameters.WorkArea;
        }

        private bool _isUpdatingSidebar = false;

        public void SetSidebarExpanded(bool expand)
        {
            if (_isUpdatingSidebar) return;
            _isUpdatingSidebar = true;
            try
            {
                var wa = GetCurrentWorkArea();
                var curLeft = Left;
                var curWidth = ActualWidth > 0 ? ActualWidth : Width;
                var curRight = curLeft + curWidth;
                var newWidth = Math.Round((expand ? 880.0 : 670.0) * _uiScale);

                const double edgeTolerance = 45.0;
                bool isAnchoredToRight = (curRight >= wa.Right - edgeTolerance);
                bool isAnchoredToLeft = (curLeft <= wa.Left + edgeTolerance);

                SidebarColumn.Width = expand ? new GridLength(210) : new GridLength(0);
                _config.SidebarExpanded = expand;
                Width = newWidth;

                if (isAnchoredToRight)
                {
                    Left = Math.Max(wa.Left, wa.Right - newWidth);
                }
                else if (isAnchoredToLeft)
                {
                    Left = wa.Left;
                }
                else if (curLeft + newWidth > wa.Right)
                {
                    Left = Math.Max(wa.Left, wa.Right - newWidth);
                }

                if (SidebarDefaultOpenToggle != null && SidebarDefaultOpenToggle.IsChecked != expand)
                {
                    SidebarDefaultOpenToggle.IsChecked = expand;
                }

                _config.Save();
            }
            finally
            {
                _isUpdatingSidebar = false;
            }
        }

        private void SidebarToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            var isExpanded = SidebarColumn.Width.Value > 0;
            SetSidebarExpanded(!isExpanded);
        }

        private void TitleAccountBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Subscription_Click(sender, e);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // تغییر نام خودکار کارت شبکه‌های خام به اسم حرفه‌ای اپلیکیشن
            _ = Task.Run(async () =>
            {
                try
                {
                    string brand = AppConfig.BrandName;
                    string psCmd = $@"
                        $i = 1
                        $brand = '{brand} VPN'
                        Get-NetAdapter | Where-Object {{ $_.Name -like 'Local Area Connection*' -and ($_.InterfaceDescription -match 'TAP-Windows|Wintun|OpenVPN|WireGuard|Amnezia') }} | ForEach-Object {{
                            $newName = if ($i -eq 1) {{ $brand }} else {{ $brand + ' ' + $i }}
                            Rename-NetAdapter -Name $_.Name -NewName $newName -ErrorAction SilentlyContinue
                            $i++
                        }}
                    ";
                    var psFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nfv_rename_nic.ps1");
                    await System.IO.File.WriteAllTextAsync(psFilePath, psCmd, new System.Text.UTF8Encoding(false));
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -NoProfile -NonInteractive -File \"{psFilePath}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using (var proc = System.Diagnostics.Process.Start(psi))
                    {
                        if (proc != null) await proc.WaitForExitAsync();
                    }
                    try { System.IO.File.Delete(psFilePath); } catch { }
                }
                catch { }
            });

            ApplyDarkTitleBar();
            Localization.SetLanguage(_config.Language);
            if (_config.Language == "en")
            {
                LangEnRb.IsChecked = true;
                FlowDirection = FlowDirection.LeftToRight;
            }
            else
            {
                LangFaRb.IsChecked = true;
                FlowDirection = FlowDirection.RightToLeft;
            }
            ApplyLocalizationToNamedElements();
            ApplyThemeChoice(_config.Theme);
            ApplyFontChoice(_config.Font ?? "segoe");
            InitStartupToggle();

            EnableElevatedDragDrop();

            SettingsPanel.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HideConnListScrollBar();

            SmartToggle.IsChecked = _config.SmartSwitch;
            TrayToggle.IsChecked = _config.MinimizeToTray;
            KillSwitchToggle.IsChecked = _config.KillSwitchEnabled;
            KillSwitch.Enabled = _config.KillSwitchEnabled;
            if (_config.IdleDisconnectMinutes > 0)
            {
                IdleOnRb.IsChecked = true;
                IdleMinutesBox.Text = _config.IdleDisconnectMinutes.ToString();
            }
            else
            {
                IdleOffRb.IsChecked = true;
                IdleMinutesBox.Text = "30";
            }
            switch (_config.SplitTunnelMode)
            {
                case "deny": StDenyRb.IsChecked = true; break;
                case "allow": StAllowRb.IsChecked = true; break;
                default: StOffRb.IsChecked = true; break;
            }
            

            switch (_config.DnsMode)
            {
                case "cloudflare": DnsCfRb.IsChecked = true; break;
                case "google": DnsGoogleRb.IsChecked = true; break;
                case "adguard": DnsAdguardRb.IsChecked = true; break;
                case "adguard_family": DnsAdguardFamilyRb.IsChecked = true; break;
                case "custom": DnsCustomRb.IsChecked = true; break;
                default: DnsAutoRb.IsChecked = true; break;
            }
            DnsPrimaryBox.Text = _config.DnsPrimary;
            DnsSecondaryBox.Text = _config.DnsSecondary;

            _ = Task.Run(async () => { try { await SplitTunnel.ClearAsync(); } catch { } });

            TryFirstRunImport();
            InitTray();

            _connListExpanded = _config.ConnListExpanded;
            if (!string.IsNullOrEmpty(_config.LastConnectedName) &&
                _config.Connections.Any(c => c.Name == _config.LastConnectedName))
            {
                _selectedName = _config.LastConnectedName;
            }
            else if (_config.Connections.Count > 0)
            {
                _selectedName = _config.Connections[0].Name;
            }

            if (!string.IsNullOrEmpty(_selectedName))
            {
                var selConn = _config.Connections.FirstOrDefault(c => c.Name == _selectedName);
                if (selConn != null)
                {
                    MainWindow.LastConnectionType = "vpn";
                    ActiveConnText.Text = $"{CategoryLabel(selConn.Type)} {selConn.Name}";
                    ServerSubText.Text = selConn.ServerLine;
                    UpdateActiveBadge(selConn.Name, selConn.Type);
                }
            }
            else if (_xraySelectedProfile != null)
            {
                MainWindow.LastConnectionType = "xray";
                ActiveConnText.Text = _xraySelectedProfile.Alias;
                ServerSubText.Text = $"{_xraySelectedProfile.Protocol.ToUpper()} {_xraySelectedProfile.Network.ToUpper()}";
                UpdateActiveBadge(_xraySelectedProfile.Alias, _xraySelectedProfile.Protocol);
            }

            FlagHelper.FlagDownloaded += (_, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateActiveBadge(ActiveConnText.Text, ServerSubText.Text, GeoIpText?.Text);
                });
            };

            RefreshList();
            RenderRecentServersList();
            UpdateXraySubscriptionCard();
            UpdateCoreSwitcherCards();
            InitializeSubscriptionSummaryCardFromCache();
            TryAutoCheckSubscriptionSummaryOnce();

            // سایدبار و پنل جزئیات بر اساس تنظیمات ذخیره‌شده کاربر
            SidebarColumn.Width = _config.SidebarExpanded ? new GridLength(210) : new GridLength(0);
            Width = Math.Round((_config.SidebarExpanded ? 880.0 : 670.0) * _uiScale);
            if (MoreInfoPanel != null)
            {
                MoreInfoPanel.Visibility = _config.HomeDetailsExpanded ? Visibility.Visible : Visibility.Collapsed;
                MoreInfoArrow.Text = _config.HomeDetailsExpanded ? " ▴" : " ▾";
                MoreInfoText.Text = _config.HomeDetailsExpanded ? Localization.T("بستن جزئیات") : Localization.T("جزئیات بیشتر");
            }
            if (RecentServersCard != null)
            {
                RecentServersCard.Visibility = _config.ShowRecentServersOnHome ? Visibility.Visible : Visibility.Collapsed;
                if (SecurityStatusCard != null)
                    SecurityStatusCard.Visibility = _config.ShowRecentServersOnHome ? Visibility.Collapsed : Visibility.Visible;
            }
            if (ShowRecentServersToggle != null) ShowRecentServersToggle.IsChecked = _config.ShowRecentServersOnHome;
            if (SidebarDefaultOpenToggle != null) SidebarDefaultOpenToggle.IsChecked = _config.SidebarExpanded;
            if (DashWidgetRecentRb != null) DashWidgetRecentRb.IsChecked = _config.DashboardBottomWidgetMode == 0;
            if (DashWidgetSecurityRb != null) DashWidgetSecurityRb.IsChecked = _config.DashboardBottomWidgetMode == 1;
            if (DashWidgetToolsRb != null) DashWidgetToolsRb.IsChecked = _config.DashboardBottomWidgetMode == 2;

            WindowState = WindowState.Normal;
            MaxWidth = 920;
            MaxHeight = 650;

            ShowPanel("home");
            SetStatusText(TxtReady);
            SetPowerState("off");
            ApplyDashboardBottomWidgetMode();

            _ready = true;
        }

        public void UpdateActiveBadge(string? name, string? protocol, string? countryText = null)
        {
            Dispatcher.Invoke(() =>
            {
                FlagHelper.ApplyFlagOrIcon(ActiveServerBadgeImg, ActiveServerBadgeText, name, protocol, countryText);
                FlagHelper.ApplyFlagOrIcon(TargetConnBadgeImg, TargetConnBadgeText, name, protocol, countryText);
            });
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_reallyExit)
            {
                _geoGen++;
                _xrayGeoGen++;
                try { _tray?.Dispose(); } catch { }
                try { _trayIcon?.Dispose(); } catch { }
                try { HsForceStopAsync().Wait(1500); } catch { }
                try { TerminateAllVpnProcessesAndResetAdaptersAsync().Wait(2500); } catch { }
                try { SplitTunnel.ClearAsync().Wait(1500); } catch { }
                try { KillSwitch.DisableAsync().Wait(1500); } catch { }
                try { _engine.StopAsync().Wait(2000); } catch { }
                try { XrayEngine.Stop(); } catch { }
                return;
            }

            if (_config.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    Notify(TrayHint);
                }
                return;
            }

            e.Cancel = true;
            if (AppExitOverlay != null)
            {
                AppExitOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                ExitModalCleanup_Click(this, new RoutedEventArgs());
            }
        }

        private async void ExitModalCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (AppExitOverlay != null) AppExitOverlay.Visibility = Visibility.Collapsed;
            try
            {
                await TerminateAllVpnProcessesAndResetAdaptersAsync();
            }
            catch { }
            _reallyExit = true;
            Close();
        }

        private void ExitModalQuick_Click(object sender, RoutedEventArgs e)
        {
            if (AppExitOverlay != null) AppExitOverlay.Visibility = Visibility.Collapsed;
            _reallyExit = true;
            Close();
        }

        private void ExitModalCancel_Click(object sender, RoutedEventArgs e)
        {
            if (AppExitOverlay != null) AppExitOverlay.Visibility = Visibility.Collapsed;
        }

        private void AppExitOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == AppExitOverlay)
            {
                AppExitOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenHotspotWindow_Click(object sender, RoutedEventArgs e)
        {
            SetPanelFromHotspotNavigation("hotspot");

            HsCheckVpnState();
            _ = HsRefreshAdaptersAsync(searchForTarget: false);
        }

        private void SidebarNavDashboard_Click(object sender, RoutedEventArgs e)
        {
            try { CloseSpeedTestView(); } catch { }
            ShowPanel("home");
            UpdateSidebarState("dashboard");
        }

        private void SidebarNavVpnCore_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel("vpn");
            UpdateSidebarState("vpn");
            if (_vpnPingCache.Count == 0 && _config.Connections.Count > 0)
            {
                _ = PingAllVpnProfilesAsync();
            }
        }

        private void SidebarNavXrayCore_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel("xray");
            UpdateSidebarState("xray");
        }

        private void SidebarNavServers_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel("xray");
            UpdateSidebarState("xray");
        }

        private void SidebarNavHotspot_Click(object sender, RoutedEventArgs e)
        {
            SetPanelFromHotspotNavigation("hotspot");
            UpdateSidebarState("hotspot");
        }

        private void SidebarNavSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel("settings");
            UpdateSidebarState("settings");
        }

        private void SidebarNavTools_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel("tools");
            UpdateSidebarState("tools");
        }

        private void SidebarNavLogs_Click(object sender, RoutedEventArgs e)
        {
            ShowPanel("connlog");
            UpdateSidebarState("connlog");
        }

        public void UpdateSidebarState(string current)
        {
            try
            {
                var activeBg = (Brush)FindResource("SidebarActiveBrush");
                var transparent = Brushes.Transparent;

                void SetBtn(Button? btn, Border? indicator, bool isActive)
                {
                    if (btn == null) return;
                    btn.Background = isActive ? activeBg : transparent;
                    if (indicator != null) indicator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                }

                SetBtn(NavDashboardBtn, NavDashboardIndicator, current == "dashboard" || current == "home");
                SetBtn(NavVpnCoreBtn, NavVpnCoreIndicator, current == "vpn");
                SetBtn(NavXrayCoreBtn, NavXrayCoreIndicator, current == "xray");
                SetBtn(NavServersBtn, NavServersIndicator, current == "servers");
                SetBtn(NavHotspotBtn, NavHotspotIndicator, current == "hotspot");
                SetBtn(NavSettingsBtn, NavSettingsIndicator, current == "settings");
                SetBtn(NavToolsBtn, NavToolsIndicator, current == "tools");
                SetBtn(NavLogsBtn, NavLogsIndicator, current == "logs" || current == "connlog");
            }
            catch { }
        }

        public void RenderRecentServersList()
        {
            try
            {
                if (RecentServersList == null) return;
                RecentServersList.Children.Clear();

                var recentNames = (_config.RecentConnections ?? new List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var itemsToRender = new List<(string name, string type, string subtitle, string icon, bool isXray, object? rawObj)>();

                foreach (var name in recentNames)
                {
                    if (itemsToRender.Count >= 3) break;

                    var vpnConn = _config.Connections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (vpnConn != null)
                    {
                        var flag = ExtractFlagOrIcon(vpnConn.Name, vpnConn.Type);
                        itemsToRender.Add((vpnConn.Name, CategoryLabel(vpnConn.Type), vpnConn.ServerLine ?? vpnConn.Type, flag, false, vpnConn));
                        continue;
                    }

                    var xrayProfile = XrayGroups.SelectMany(g => g.Profiles).FirstOrDefault(p => string.Equals(p.Alias, name, StringComparison.OrdinalIgnoreCase));
                    if (xrayProfile != null)
                    {
                        var flag = ExtractFlagOrIcon(xrayProfile.Alias, xrayProfile.Protocol);
                        itemsToRender.Add((xrayProfile.Alias, xrayProfile.Protocol.ToUpperInvariant(), $"{xrayProfile.Address}:{xrayProfile.Port}", flag, true, xrayProfile));
                        continue;
                    }
                }

                if (itemsToRender.Count < 3)
                {
                    foreach (var vpnConn in _config.Connections)
                    {
                        if (itemsToRender.Count >= 3) break;
                        if (itemsToRender.Any(x => string.Equals(x.name, vpnConn.Name, StringComparison.OrdinalIgnoreCase))) continue;
                        var flag = ExtractFlagOrIcon(vpnConn.Name, vpnConn.Type);
                        itemsToRender.Add((vpnConn.Name, CategoryLabel(vpnConn.Type), vpnConn.ServerLine ?? vpnConn.Type, flag, false, vpnConn));
                    }
                }

                if (itemsToRender.Count < 3)
                {
                    foreach (var p in XrayGroups.SelectMany(g => g.Profiles))
                    {
                        if (itemsToRender.Count >= 3) break;
                        if (itemsToRender.Any(x => string.Equals(x.name, p.Alias, StringComparison.OrdinalIgnoreCase))) continue;
                        var flag = ExtractFlagOrIcon(p.Alias, p.Protocol);
                        itemsToRender.Add((p.Alias, p.Protocol.ToUpperInvariant(), $"{p.Address}:{p.Port}", flag, true, p));
                    }
                }

                if (itemsToRender.Count == 0)
                {
                    var emptyText = new TextBlock
                    {
                        Text = Localization.T("هنوز سروری اضافه نشده است"),
                        FontSize = 10.5,
                        Foreground = (Brush)FindResource("SubTextBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(4, 8, 4, 8),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    RecentServersList.Children.Add(emptyText);
                    return;
                }

                var rowBg = (Brush)FindResource("FieldBrush");
                var hoverBg = (Brush)FindResource("CardHoverBrush");
                var accentBrush = (Brush)FindResource("AccentBrush");

                foreach (var item in itemsToRender)
                {
                    bool isCurrentActive = (!item.isXray && _selectedName == item.name) ||
                                           (item.isXray && _xraySelectedProfile != null && _xraySelectedProfile.Alias == item.name);

                    var rowBorder = new Border
                    {
                        Background = isCurrentActive ? hoverBg : rowBg,
                        BorderThickness = new Thickness(0),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 0, 0, 3),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var iconBlock = new TextBlock
                    {
                        Text = item.icon,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    Grid.SetColumn(iconBlock, 0);
                    rowGrid.Children.Add(iconBlock);

                    var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
                    var nameBlock = new TextBlock
                    {
                        Text = item.name,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 180
                    };
                    var subBlock = new TextBlock
                    {
                        Text = $"{item.type} • {item.subtitle}",
                        FontSize = 9,
                        Foreground = (Brush)FindResource("SubTextBrush"),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 180
                    };
                    textStack.Children.Add(nameBlock);
                    textStack.Children.Add(subBlock);
                    Grid.SetColumn(textStack, 1);
                    rowGrid.Children.Add(textStack);

                    var quickBadge = new Border
                    {
                        Background = isCurrentActive ? accentBrush : (Brush)FindResource("CardHoverBrush"),
                        BorderThickness = new Thickness(0),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(7, 2, 7, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = isCurrentActive ? Localization.T("فعال") : Localization.T("انتخاب"),
                            FontSize = 9.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = isCurrentActive ? (App.IsDark ? Brushes.Black : Brushes.White) : (Brush)FindResource("SubTextBrush")
                        }
                    };
                    Grid.SetColumn(quickBadge, 2);
                    rowGrid.Children.Add(quickBadge);

                    rowBorder.Child = rowGrid;

                    rowBorder.MouseEnter += (_, __) => { if (!isCurrentActive) rowBorder.Background = hoverBg; };
                    rowBorder.MouseLeave += (_, __) => { if (!isCurrentActive) rowBorder.Background = rowBg; };

                    rowBorder.MouseLeftButtonUp += (_, __) =>
                    {
                        if (item.isXray && item.rawObj is ProxyProfile p)
                        {
                            MainWindow.LastConnectionType = "xray";
                            _xraySelectedProfile = p;
                            if (XrayProxyList != null) XrayProxyList.SelectedItem = p;
                            UpdateActiveProfileInfo(p);
                            RenderXrayConnList();
                            _config.AddRecentConnection(p.Alias);
                            RenderRecentServersList();
                        }
                        else if (!item.isXray && item.rawObj is ConnectionProfile cp)
                        {
                            MainWindow.LastConnectionType = "vpn";
                            _selectedName = cp.Name;
                            ActiveConnText.Text = $"{CategoryLabel(cp.Type)} {cp.Name}";
                            ServerSubText.Text = cp.ServerLine;
                            UpdateActiveBadge(cp.Name, cp.Type);
                            RefreshList();
                            _config.AddRecentConnection(cp.Name);
                            RenderRecentServersList();
                        }
                    };

                    RecentServersList.Children.Add(rowBorder);
                }
            }
            catch { }
        }

        // این handlerها نام‌های اختصاصی دارند تا با handlerهای قبلی پروژه
        // مثل ShowPanel، Nav_Click یا HeaderLogBtn_Click تداخل نداشته باشند.
        private void HotspotNavHome_Click(object sender, RoutedEventArgs e)
        {
            SetPanelFromHotspotNavigation("home");
        }

        private void HotspotNavXray_Click(object sender, RoutedEventArgs e)
        {
            SetPanelFromHotspotNavigation("xray");
        }

        private void HotspotNavTools_Click(object sender, RoutedEventArgs e)
        {
            SetPanelFromHotspotNavigation("tools");
        }

        private void HotspotNavSettings_Click(object sender, RoutedEventArgs e)
        {
            SetPanelFromHotspotNavigation("settings");
        }

        private void HotspotNavLogs_Click(object sender, RoutedEventArgs e)
        {
            SetPanelFromHotspotNavigation("logs");
        }

        private void SetPanelFromHotspotNavigation(string page)
        {
            UpdateSidebarState(page);
            if (DrawerOverlay != null) DrawerOverlay.Visibility = Visibility.Collapsed;
            // ابتدا همه پنل‌ها را مخفی کن؛ این خط مانع باقی‌ماندن هات‌اسپات می‌شود.
            HomePanel.Visibility = Visibility.Collapsed;
            if (XrayPanel != null) XrayPanel.Visibility = Visibility.Collapsed;
            ToolboxPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            ConnLogPanel.Visibility = Visibility.Collapsed;
            HotspotPanel.Visibility = Visibility.Collapsed;

            // توقف انیمیشن و بازگرداندن موقعیت پنل‌ها
            ResetNavigationTransform(HomeTransform);
            ResetNavigationTransform(ToolsTransform);
            ResetNavigationTransform(SettingsTransform);
            ResetNavigationTransform(ConnLogTransform);
            ResetNavigationTransform(HotspotTransform);
            if (XrayTransform != null) ResetNavigationTransform(XrayTransform);

            switch (page)
            {
                case "home":
                    HomePanel.Visibility = Visibility.Visible;
                    HeaderBrandPanel.Visibility = Visibility.Visible;
                    HeaderPagePanel.Visibility = Visibility.Collapsed;
                    break;

                case "xray":
                    if (XrayPanel != null) XrayPanel.Visibility = Visibility.Visible;
                    HeaderBrandPanel.Visibility = Visibility.Visible;
                    HeaderPagePanel.Visibility = Visibility.Collapsed;
                    break;

                case "tools":
                    ToolboxPanel.Visibility = Visibility.Visible;
                    HeaderTitleText.Text = Localization.T("ابزارها و گزارش");
                    HeaderBrandPanel.Visibility = Visibility.Collapsed;
                    HeaderPagePanel.Visibility = Visibility.Visible;
                    break;

                case "settings":
                    SettingsPanel.Visibility = Visibility.Visible;
                    HeaderTitleText.Text = Localization.T("تنظیمات");
                    HeaderBrandPanel.Visibility = Visibility.Collapsed;
                    HeaderPagePanel.Visibility = Visibility.Visible;
                    break;

                case "logs":
                    ConnLogPanel.Visibility = Visibility.Visible;
                    HeaderTitleText.Text = Localization.T("گزارش کانکشن‌ها");
                    HeaderBrandPanel.Visibility = Visibility.Collapsed;
                    HeaderPagePanel.Visibility = Visibility.Visible;
                    break;

                case "hotspot":
                    HotspotPanel.Visibility = Visibility.Visible;
                    HeaderTitleText.Text = Localization.T("VPN WIFI-Direct");
                    HeaderBrandPanel.Visibility = Visibility.Collapsed;
                    HeaderPagePanel.Visibility = Visibility.Visible;
                    break;

                default:
                    HomePanel.Visibility = Visibility.Visible;
                    HeaderBrandPanel.Visibility = Visibility.Visible;
                    HeaderPagePanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private static void ResetNavigationTransform(TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.X = 0;
            transform.Y = 0;
        }

        private void TryFirstRunImport()
        {
            try
            {
                if (_config.Connections.Count > 0) return;
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "netfastvip-package.json"),
                    Path.Combine(AppContext.BaseDirectory, "netfastvip.json"),
                    Path.Combine(AppContext.BaseDirectory, "netfastvip.pkg"),
                };
                var path = candidates.FirstOrDefault(File.Exists);
                if (path != null)
                {
                    ImportPackage(path);
                }
            }
            catch { }
        }

        private void InitTray()
        {
            try
            {
                _trayIcon = LoadTrayIcon();
                _tray = new System.Windows.Forms.NotifyIcon
                {
                    Icon = _trayIcon,
                    Visible = true,
                    Text = AppConfig.BrandName
                };
                _tray.DoubleClick += (_, __) => { Show(); WindowState = WindowState.Normal; Activate(); };

                var trayMenu = new System.Windows.Forms.ContextMenuStrip();
                trayMenu.Items.Add(TrayShowApp, null, (_, __) =>
                    Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
                trayMenu.Items.Add(TrayExit, null, (_, __) =>
                    Dispatcher.Invoke(() => { _reallyExit = true; Close(); }));
                _tray.ContextMenuStrip = trayMenu;
            }
            catch { }
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                var resource = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/app.ico", UriKind.Absolute));
                if (resource?.Stream is not null)
                {
                    using var stream = resource.Stream;
                    using var source = new System.Drawing.Icon(stream);
                    return new System.Drawing.Icon(source, source.Width, source.Height);
                }
            }
            catch { }

            return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }
		
		internal void AppendLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                var entry = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine;
                if (LogBox != null)
                {
                    LogBox.AppendText(entry);
                    LogBox.CaretIndex = LogBox.Text.Length;
                    LogBox.ScrollToEnd();
                }
            });
            LogWriter.Write(line);
        }

        private void AppendConnLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                var entry = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine;
                if (ConnLogBox != null)
                {
                    ConnLogBox.AppendText(entry);
                    ConnLogBox.CaretIndex = ConnLogBox.Text.Length;
                    ConnLogBox.ScrollToEnd();
                }

                try
                {
                    string[] markers = { "Peer Connection Initiated with [AF_INET]", "link remote: [AF_INET]", "Preserving recently used remote address: [AF_INET]", "Connecting to [" };
                    foreach (var marker in markers)
                    {
                        var idx = line.IndexOf(marker, StringComparison.Ordinal);
                        if (idx < 0) continue;
                        var rest = line.Substring(idx + marker.Length);
                        if (marker == "Connecting to [")
                        {
                            var p1 = rest.IndexOf('(');
                            var p2 = rest.IndexOf(')');
                            if (p1 >= 0 && p2 > p1 + 1) rest = rest.Substring(p1 + 1, p2 - p1 - 1) + ":";
                            else continue;
                        }
                        var colon = rest.IndexOf(':');
                        if (colon > 0)
                        {
                            var ipStr = rest.Substring(0, colon).Trim();
                            if (System.Net.IPAddress.TryParse(ipStr, out _))
                            {
                                _tunnelPeerIp = ipStr;
                                if (ServerIpText != null) ServerIpText.Text = ipStr;
                                var sniffEntry = "[" + DateTime.Now.ToString("HH:mm:ss") + "] [ip] server from openvpn log: " + ipStr + Environment.NewLine;
                                if (ConnLogBox != null)
                                {
                                    ConnLogBox.AppendText(sniffEntry);
                                    ConnLogBox.CaretIndex = ConnLogBox.Text.Length;
                                    ConnLogBox.ScrollToEnd();
                                }
                            }
                        }
                        break;
                    }
                }
                catch { }
            });
            LogWriter.Write(line);
        }

        private static string Fmt(double bytesPerSecOrTotal)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytesPerSecOrTotal;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return v.ToString(v < 10 ? "0.0" : "0") + " " + units[u];
        }

        private static class LogWriter
        {
            private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "Data", "app.log");
            private static readonly object Lock = new object();

            public static void Write(string line)
            {
                try
                {
                    lock (Lock)
                    {
                        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
                        File.AppendAllText(LogPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch { }
            }
        }

        #region Hotspot Internal Logic
        private readonly global::SmartVpn.HotspotService _hotspot = new();
        private DispatcherTimer? _hsStabilizationTimer;
        private int _hsSecondsRemaining = 0;
        private bool _isHsVpnConnected = false;
        private string? _hsBestSrc = null;
        private DispatcherTimer? _hsClientsTimer;
        private bool _hsClientsRefreshInFlight;

        private void HsCheckVpnState()
        {
            bool isXray = _xrayIsConnected && XrayEngine.IsRunning;
            var activeConn = ActiveConnText?.Text?.Trim();
            bool isTrad = _engine.IsRunning && _currentPowerState == "connected" && !string.IsNullOrEmpty(activeConn) && activeConn != "—" && activeConn != Localization.T("قطع شده");

            _isHsVpnConnected = isXray || isTrad;

            if (isXray)
            {
                string pName = _xrayActiveProfile?.Alias ?? "sing-box";
                HsVpnConnectionText.Text = $"Xray ({pName})";
            }
            else if (isTrad)
            {
                HsVpnConnectionText.Text = activeConn!;
            }
            else
            {
                HsVpnConnectionText.Text = Localization.T("عدم اتصال");
                HsResetAllState(Localization.T("برای استفاده از هات‌اسپات ابتدا به VPN یا Xray متصل شوید."));
                HsSourceText.Text = Localization.T("عدم اتصال");
            }
        }

        public void HsOnVpnConnected()
        {
            _isHsVpnConnected = true;
            SetHotspotHeaderState("busy");
            bool isXray = _xrayIsConnected && XrayEngine.IsRunning;
            if (isXray)
            {
                string pName = _xrayActiveProfile?.Alias ?? "sing-box";
                HsVpnConnectionText.Text = $"Xray ({pName})";
            }
            else
            {
                HsVpnConnectionText.Text = ActiveConnText?.Text?.Trim() ?? Localization.T("اتصال VPN");
            }
            HsCheckVpnState();
            _hsStabilizationTimer?.Stop();
            _hsSecondsRemaining = 5;

            BtnHsStart.IsEnabled = false;

            _hsStabilizationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _hsStabilizationTimer.Tick += async (s, e) =>
            {
                _hsSecondsRemaining--;
                if (_hsSecondsRemaining > 0)
                {
                    HsStatusText.Text = Localization.T("⏳ در حال تثبیت شبکه و درایورهای مجازی... ") + $"({_hsSecondsRemaining} ثانیه)";
                    HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                }
                else
                {
                    _hsStabilizationTimer.Stop();
                    HsStatusText.Text = Localization.T("✅ شبکه تثبیت شد. آماده راه‌اندازی هات‌اسپات.");
                    HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    await HsRefreshAdaptersAsync(searchForTarget: false);
                    BtnHsStart.IsEnabled = true;
                }
            };
            _hsStabilizationTimer.Start();
        }

        private async Task<string?> HsRefreshAdaptersAsync(bool searchForTarget)
        {
            var adapters = await _hotspot.GetAllAdaptersAsync();
            bool isXray = _xrayIsConnected && XrayEngine.IsRunning;
            var activeConn = isXray ? "sing-box" : (ActiveConnText?.Text?.Trim() ?? "");
            string? tunnelIp = isXray ? "172.19.0.1" : _tunnelLocalIp;

            string? bestTgt = null;
            if (searchForTarget)
            {
                bestTgt = HotspotService.FindBestTargetAdapter(adapters);
            }

            _hsBestSrc = HotspotService.FindBestVpnSourceAdapter(activeConn, tunnelIp, adapters);

            HsSourceText.Text = _hsBestSrc ?? Localization.T("پیدا نشد");
            return bestTgt;
        }

        private async void BtnHsStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_hsBestSrc))
            {
                await HsRefreshAdaptersAsync(searchForTarget: false);
            }

            if (!_isHsVpnConnected || string.IsNullOrEmpty(_hsBestSrc))
            {
                ShowInAppMessage(Localization.T("کارت شبکه اینترنت (VPN) یافت نشد."), Localization.T("اخطار"));
                return;
            }

            BtnHsStart.IsEnabled = false;
            BtnHsStop.IsEnabled = false;
            BtnHsRestart.IsEnabled = false;
            SetHotspotHeaderState("busy");
            HsStatusText.Text = Localization.T("مرحله ۱: راه‌اندازی هات‌اسپات...");
            HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));

            var startResult = await _hotspot.StartHotspotOnlyAsync(_hsBestSrc);
            if (!startResult.ok)
            {
                HsStatusText.Text = Localization.T("خطا در روشن شدن هات‌اسپات!");
                HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                BtnHsStart.IsEnabled = true;
                BtnHsRestart.IsEnabled = false;
                return;
            }

            for (int i = 5; i > 0; i--)
            {
                HsStatusText.Text = Localization.T("مرحله ۲: تثبیت شبکه وای‌فای دایرکت... ") + $"({i} ثانیه)";
                HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                await Task.Delay(1000);
            }

            string? targetName = await HsRefreshAdaptersAsync(searchForTarget: true);
            if (string.IsNullOrEmpty(targetName))
            {
                HsStatusText.Text = Localization.T("کارت شبکه Wi-Fi Direct یافت نشد.");
                HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                await _hotspot.StopAsync();
                BtnHsStart.IsEnabled = true;
                BtnHsRestart.IsEnabled = false;
                return;
            }

            HsStatusText.Text = Localization.T("مرحله ۳: برقراری پل ارتباطی با ") + targetName + "...";
            var shareResult = await _hotspot.ApplySharingOnlyAsync(_hsBestSrc, targetName);

            if (!shareResult.ok)
            {
                HsStatusText.Text = Localization.T("خطا در شیرینگ خودکار. لطفاً دستی انجام دهید.");
                HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                HsManualHint.Visibility = Visibility.Visible; BtnOpenNcpa.Visibility = Visibility.Visible;
                
            }
            else
            {
                HsStatusText.Text = Localization.T("✅ هات‌اسپات فعال شد و ترافیک در جریان است.");
                HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }

            HsSsidText.Text = startResult.ssid;
            HsPassText.Text = startResult.pass;
            HsInfoContainer.Visibility = Visibility.Visible;
            GenerateHsQrCode(startResult.ssid, startResult.pass);
            HsQrContainer.Visibility = Visibility.Visible;
            SetHotspotHeaderState("active");
            BtnHsStop.IsEnabled = true;
            BtnHsRestart.IsEnabled = true;
            StartHsClientsPolling();
        }

        private void GenerateHsQrCode(string ssid, string password)
        {
            string wifiPayload = $"WIFI:T:WPA;S:{ssid};P:{password};;";
            using var qrGen = new QRCoder.QRCodeGenerator();
            using var qrData = qrGen.CreateQrCode(wifiPayload, QRCoder.QRCodeGenerator.ECCLevel.M);
            using var qrCode = new QRCoder.PngByteQRCode(qrData);
            
            using var ms = new MemoryStream(qrCode.GetGraphic(20));
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            HsQrImage.Source = bitmap;
        }

        private async void BtnHsStop_Click(object sender, RoutedEventArgs e)
        {
            BtnHsStop.IsEnabled = false;
            BtnHsRestart.IsEnabled = false;
            HsStatusText.Text = Localization.T("در حال توقف هات‌اسپات...");
            await _hotspot.StopAsync();
            HsResetAllState(Localization.T("هات‌اسپات متوقف شد."));
            if (_isHsVpnConnected) BtnHsStart.IsEnabled = true;
        }

        private void BtnOpenNcpa_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "ncpa.cpl") { UseShellExecute = true }); } catch { }
        }

        private async void BtnHsRestart_Click(object sender, RoutedEventArgs e)
        {
            if (!_isHsVpnConnected || string.IsNullOrEmpty(_hsBestSrc)) return;

            BtnHsStart.IsEnabled = false;
            BtnHsStop.IsEnabled = false;
            BtnHsRestart.IsEnabled = false;
            HsStatusText.Text = Localization.T("در حال راه‌اندازی مجدد هات‌اسپات...");
            HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));

            try
            {
                await _hotspot.StopAsync();
                HsResetAllState(Localization.T("در حال راه‌اندازی مجدد..."));
                BtnHsStart.IsEnabled = true;
                BtnHsStart_Click(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                HsResetAllState(Localization.T("راه‌اندازی مجدد ناموفق بود."));
                HsStatusText.Text = Localization.T("خطا: ") + ex.Message;
                if (_isHsVpnConnected) BtnHsStart.IsEnabled = true;
            }
        }

        private void StartHsClientsPolling()
        {
            StopHsClientsPolling();
            // شمارنده حتی وقتی هیچ دستگاهی وصل نیست هم دیده شود.
            HsClientsContainer.Visibility = Visibility.Visible;
            HsClientsCountText.Text = Localization.T("دستگاه‌های متصل: ") + "۰";
            HsClientsTextBox.Text = Localization.T("در حال بررسی دستگاه‌های متصل...");
            _hsClientsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hsClientsTimer.Tick += async (_, __) => await RefreshHsClientsAsync();
            _hsClientsTimer.Start();
            _ = RefreshHsClientsAsync();
        }

        private void StopHsClientsPolling()
        {
            _hsClientsTimer?.Stop();
            _hsClientsTimer = null;
            _hsClientsRefreshInFlight = false;
        }

        private async Task RefreshHsClientsAsync()
        {
            if (_hsClientsRefreshInFlight || HotspotPanel.Visibility != Visibility.Visible) return;
            _hsClientsRefreshInFlight = true;
            try
            {
                HsClientsContainer.Visibility = Visibility.Visible;
                var clients = await _hotspot.GetConnectedClientsAsync();
                HsClientsCountText.Text = Localization.T("دستگاه‌های متصل: ") + clients.Count;
                HsClientsTextBox.Text = clients.Count == 0
                    ? Localization.T("هیچ دستگاهی متصل نیست.")
                    : string.Join(Environment.NewLine + Environment.NewLine, clients.Select(c =>
                        $"{c.Name}\r\nIP: {c.IpAddress}\r\nMAC: {c.MacAddress}"));
            }
            catch
            {
                HsClientsCountText.Text = Localization.T("دستگاه‌های متصل: ") + Localization.T("نامشخص");
                HsClientsTextBox.Text = Localization.T("امکان دریافت فهرست دستگاه‌ها وجود ندارد.");
            }
            finally
            {
                _hsClientsRefreshInFlight = false;
            }
        }

        private void HsClientsContainer_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            HsClientsPopup.IsOpen = true;
        }

        private void HsClientsContainer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            HsClientsPopup.IsOpen = false;
        }

        private void HsResetAllState(string message)
        {
            SetHotspotHeaderState("off");
            _hsStabilizationTimer?.Stop();
            StopHsClientsPolling();
            BtnHsStart.IsEnabled = false;
            BtnHsStop.IsEnabled = false;
            BtnHsRestart.IsEnabled = false;
            HsInfoContainer.Visibility = Visibility.Collapsed;
            if (HsManualHint != null) HsManualHint.Visibility = Visibility.Collapsed;
            if (BtnOpenNcpa != null) BtnOpenNcpa.Visibility = Visibility.Collapsed;
            HsQrContainer.Visibility = Visibility.Collapsed;
            HsClientsContainer.Visibility = Visibility.Collapsed;
            HsClientsPopup.IsOpen = false;
            HsClientsTextBox.Clear();
            HsClientsCountText.Text = Localization.T("دستگاه‌های متصل: ") + "۰";
            HsStatusText.Text = message;
            HsStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
        }

        public void HsHandleVpnDisconnect()
        {
            _isHsVpnConnected = false;
            Dispatcher.Invoke(() =>
            {
                HsVpnConnectionText.Text = Localization.T("عدم اتصال");
            });
            Dispatcher.Invoke(() =>
            {
                HsCheckVpnState();
                HsResetAllState(Localization.T("اتصال VPN قطع شد. هات‌اسپات متوقف گردید."));
            });
            _ = Task.Run(async () => { try { await _hotspot.StopAsync(); } catch { } });
        }

        public async Task HsForceStopAsync()
        {
            await _hotspot.StopAsync();
        }
        #endregion
    
        public static string LastConnectionType = "home";

        private void HamburgerBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DrawerOverlay != null) DrawerOverlay.Visibility = System.Windows.Visibility.Visible;
        }

        private void DrawerClose_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DrawerOverlay != null) DrawerOverlay.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void DrawerOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DrawerOverlay != null) DrawerOverlay.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void DrawerMenuPanel_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
        }
}
}






