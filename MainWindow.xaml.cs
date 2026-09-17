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
        // ----- متون فارسی متمرکز (بقیه فایل انگلیسی) -----
        private const string TxtReady = "آماده اتصال";
        private const string TxtConnecting = "در حال اتصال...";
        private const string TxtConnected = "متصل";
        private const string TxtReconnecting = "تلاش مجدد برای اتصال...";
        private const string TxtAuthFailed = "احراز هویت ناموفق بود";
        private const string MsgNoConn = "هیچ کانکشنی برای اتصال وجود ندارد.";
        private const string MsgAuthFailed = "نام کاربری یا رمز عبور اشتباه است — یا ممکن است اشتراک شما به پایان رسیده باشد.";
        private const string BtnCheckSubscription = "بررسی اشتراک";
        private const string BtnOkText = "باشه";
        private const string MsgCaInstall = "برای اتصال از طریق SSTP یا IKEv2 لازم است گواهی امنیتی NETFASTVIP یک‌بار روی ویندوز نصب شود. نصب شود؟";
        private const string BtnInstallCa = "نصب گواهی";
        private const string BtnNotNow = "حالا نه";
        private const string MsgCaRequired = "بدون نصب گواهی، اتصال SSTP / IKEv2 ممکن نیست.";
        private const string LogCaInstalled = "گواهی CA با موفقیت نصب شد.";
        private const string LogCaFailed = "نصب گواهی CA انجام نشد — کانکشن‌های SSTP/IKEv2 از این اتصال کنار گذاشته شدند.";
        private const string LogCaSkipped = "نصب گواهی CA رد شد — کانکشن‌های SSTP/IKEv2 از این اتصال کنار گذاشته شدند.";
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
        private const string LogIdleStopped = "قطع خودکار پس از {0} دقیقه بی‌استفادگی.";
        private const string TrayShowApp = "نمایش برنامه";
        private const string TrayExit = "خروج";
        private const string TrayHint = "برنامه کنار ساعت باز است — برای خروج کامل، روی آیکون راست‌کلیک کنید و «خروج» را بزنید.";
        private const string LogExitIpChanged = "IP خروجی تغییر کرد: {0} ← {1}";
        private const string MsgExitConfirm = "اتصال VPN فعال است — با خروج از برنامه قطع می‌شود. خارج شوید؟";

        private readonly AppConfig _config = AppConfig.Load();
        private readonly VpnEngine _engine = new VpnEngine();
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        private readonly List<double> _dlHistory = new List<double>();
        private readonly List<double> _ulHistory = new List<double>();

        private string? _selectedName;
        // نام پروفایلی که واقعا الان وصل است، فقط در OnEngineConnected تنظیم می‌شود -
        // برخلاف _selectedName که با یک تک‌کلیک روی کارت هم بدون اتصال واقعی تغییر می‌کند
        private string? _connectedName;
        private bool _isMiniMode;
        private int _geoGen;
        private bool _ready;          // جلوگیری از شلیک رویدادها هنگام لود اولیه

        // تشخیص تم روشن/تیره فعلی برای رنگ‌بندی مناسب کارت‌های کانکشن
        internal bool IsLightTheme()
        {
            // قبلاً اینجا دوباره رجیستری چک می‌شد که برای حالت "system" می‌توانست با نتیجه‌ی
            // واقعی که App.ApplyTheme روی DynamicResource ها (کارت بالا و بقیه‌ی برنامه) اعمال
            // کرده بود هم‌خوان نباشد (مثلاً بین لحظه‌ی استارت و لحظه‌ی رسم لیست، یا خطای رجیستری)؛
            // برای همین لیست کانکشن‌ها گاهی با رنگ اشتباه (مثلاً کارت روشن روی تم تیره) می‌ماند.
            // حالا از همان پرچم نهایی و قطعی App.IsDark استفاده می‌شود تا همیشه یکسان باشند.
            return !App.IsDark;
        }
        private bool _suppressTheme;  // جلوگیری از حلقه رویداد تم
        private bool _everConnected;   // اعلان «قطع شد» فقط بعد از یک اتصال واقعی
        private bool _wasReconnecting; // تشخیص «اتصال مجدد برقرار شد»
        private bool _manualStop;      // قطع دستی توسط کاربر (قطع ناخواسته = رنگ قرمز)
        private string _currentPowerState = "off"; // آخرین وضعیت واقعی که SetPowerState اعمال کرده (off/connecting/connected/error) — منبع مرجع برای هم‌خوانی بج لیست کانکشن‌ها با کارت بالا

        private DispatcherTimer? _statsTimer;
        private DateTime _connectStart;
        private long _lastDownBytes = -1;
        private long _lastUpBytes = -1;
        private long _sessionStartTotalBytes;
        private string? _tunnelLocalIp; // IP تانل — برای شناسایی آداپتور تانل
        private string? _tunnelNicId;   // آداپتور تانل شناسایی‌شده — منبع دقیق شمارش حجم
        private int _nicFindTries;      // شمارنده تلاش برای شناسایی آداپتور تانل (برای لاگ هشدار)

        private string? _pingHost;   // هدف پینگ زنده (سرور کانکشن فعال)
        private string? _tunnelPeerIp; // IP واقعی سرور از لاگ خود تونل (OpenVPN) — دقیق‌تر از DNS
        private HashSet<string> _routesBeforeConnect = new(); // عکس از routeهای /32 قبل از اتصال — route جدید = سرور واقعی
        private int _pingTick;
        private int _pingFails;      // شکست‌های پیاپی پینگ — برای نشانگر «ناپایدار»
        private bool _pingInFlight;  // جلوگیری از هم‌پوشانی دو پینگ زنده وقتی فاصله کوتاه شده

        private int _idleTick;         // شمارنده ثانیه برای پنجره یک‌دقیقه‌ای بی‌استفادگی
        private long _idleWindowBytes; // ترافیک ردوبدل‌شده در دقیقه جاری
        private int _idleMinutes;      // دقیقه‌های پیاپی بی‌استفادگی
        private bool _activeIsWg;      // پروتکل فعلی WireGuard یا AmneziaWG است — برای نمایش هندشیک
        private bool _reallyExit;      // خروج واقعی از منوی تری (به‌جای مخفی‌شدن)
        private bool _trayHintShown;   // اعلان راهنمای تری فقط یک بار

        private double _normalWidth, _normalHeight, _normalLeft, _normalTop;
        private string? _nicBase;

        private System.Diagnostics.Process? _pingProc;
        private System.Diagnostics.Process? _tracertProc;

        private System.Windows.Forms.NotifyIcon? _tray;

        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "NETFASTVIP";

        // ریسپانسیو: روی مانیتورهای کوچیک (مثل لپ‌تاپ HD) کل UI با حفظ نسبت کوچیک می‌شه تا از صفحه بیرون نزنه
        private double _uiScale = 1.0;

        private void FitToScreen()
        {
            var wa = SystemParameters.WorkArea;
            var scale = Math.Min(1.0, Math.Min(wa.Width / 420.0, (wa.Height - 6) / 790.0));
            if (scale >= 0.999) return;
            _uiScale = scale;
            RootScale.ScaleX = scale;
            RootScale.ScaleY = scale;
            Width = Math.Round(420 * scale);
            Height = Math.Round(790 * scale);
        }

        public MainWindow()
        {
            InitializeComponent();
            // برندینگ از config.json — عنوان پنجره و نام هدر
            Title = AppConfig.BrandName;
            BrandTitleText.Text = AppConfig.BrandName;
            FitToScreen();

            // بررسی نسخه جدید از روی هاست — بی‌صدا؛ فقط اگر نسخه جدید بود می‌پرسد (UpdateChecker.cs)
            Loaded += async (_, _) => await UpdateChecker.CheckAsync(this);

            // بررسی و اعمال پکیج کانکشن‌ها (سرورها/base.ovpn/CA) — اگر چیزی تغییر کند خلاصه‌اش با یک پیام به کاربر نشان داده می‌شود (ConnectionsUpdateChecker.cs)
            Loaded += async (_, _) => await ConnectionsUpdateChecker.CheckAsync(this, _config);

            _engine.Log += AppendConnLog;
            _engine.Connected += OnEngineConnected;
            _engine.Reconnecting += OnEngineReconnecting;
            _engine.Stopped += OnEngineStopped;
            _engine.AuthFailed += OnEngineAuthFailed;

            SplitTunnel.Log += AppendConnLog;
            KillSwitch.Log += AppendConnLog;
            RasProvider.Log += AppendConnLog;
            OpenVpnProvider.Log += AppendConnLog; // خروجی openvpn.exe — منبع IP واقعی سرور برای sniff
            CredentialsDialog.PanelUrl = _config.PanelUrl; // لینک خرید/تمدید در دیالوگ یوزر/پس
            DnsManager.Log += AppendConnLog;

            // ثبت کرش‌های غیرمنتظره در app.log برای عیب��یابی
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
                exArgs.Handled = true; // جلوگیری از بسته‌شدن کامل برنامه
            };

            // در حالت مینیمال، با گرفتن کارت می‌توان پنجره را جابه‌جا کرد
            MiniWidget.MouseLeftButtonDown += (_, __) => { try { DragMove(); } catch { } };
        }

        // ================= DWM دارک تایتل =================
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        private void ApplyDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int caption = 0x00171717;
                int text = 0x00F8FAFC;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch { }
        }

        // ================= Lifecycle =================
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyDarkTitleBar();
            ApplyThemeChoice(_config.Theme);
            InitStartupToggle();

            // درگ‌اند‌دراپ فایل در حالت ادمین — بدون این، ویندوز دراپ روی پنجره ادمین را بلاک می‌کند
            EnableElevatedDragDrop();

            // نوار اسکرول تنظیمات مخفی — اسکرول با غلتک ماوس همچنان کار می‌کند
            // (صفحه ابزارها دیگر اسکرول ندارد — فقط کادر گزارش اسکرول می‌شود)
            SettingsPanel.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

            // اسکرول‌بار لیست کانکشن‌ها هم مخفی — اسکرول با غلتک ماوس کار می‌کند
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
            StListBox.Text = string.Join(Environment.NewLine, _config.SplitTunnelList);

            switch (_config.DnsMode)
            {
                case "cloudflare": DnsCfRb.IsChecked = true; break;
                case "google": DnsGoogleRb.IsChecked = true; break;
                case "custom": DnsCustomRb.IsChecked = true; break;
                default: DnsAutoRb.IsChecked = true; break;
            }
            DnsPrimaryBox.Text = _config.DnsPrimary;
            DnsSecondaryBox.Text = _config.DnsSecondary;

            // پاک‌سازی روت‌های به‌جامانده از اجرای قبلی (کرش/بسته‌شدن ناگهانی)
            // نکته: چون KillSwitch از FWPM_SESSION_FLAG_DYNAMIC استفاده می‌کند، اگر برنامه قبلاً
            // کرش کرده باشد، خود ویندوز فیلترهای آن Session را حذف کرده — نیازی به پاک‌سازی دس����������ی در استارتاپ نیست.
            _ = Task.Run(async () => { try { await SplitTunnel.ClearAsync(); } catch { } });

            TryFirstRunImport();
            InitTray();

            // بازیابی رفتار سیو‌شدهی لیست کانکشن‌ها از اجرای قبلی برنامه: وضعیت باز/بسته «نمایش بیشتر» و اینکه آخرین کانکشنی که واقعاً وصل بوده
            // به‌طور انتخاب‌شده/هایلایت در بالای لیست بماند (ردیف لیست هم خودش به‌دلیل MoveToTop از قبل بر اساس استفاده‌ی واقعی مرتب مانده)
            _connListExpanded = _config.ConnListExpanded;
            if (!string.IsNullOrEmpty(_config.LastConnectedName) &&
                _config.Connections.Any(c => c.Name == _config.LastConnectedName))
                _selectedName = _config.LastConnectedName;

            RefreshList();
            TryAutoCheckSubscriptionSummaryOnce(); // استعلام خودکار یک‌باره وضعیت اشتراک اگر یوزر/پس از قبل ذخیره شده

            ShowPanel("home");
            SetStatusText(TxtReady);
            SetPowerState("off");

            _ready = true;
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // مخفی‌شدن کنار ساعت به‌جای بستن (اگر در تنظیمات فعال باشد)
            if (_config.MinimizeToTray && !_reallyExit)
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

            // اگر VPN وصل است، قبل از خروج تأیید بگیر — خروج از منوی تری آگاهانه است و بدون سؤال
            if (_engine.IsRunning && !_reallyExit && !AskDialog.Confirm(this, MsgExitConfirm))
            {
                e.Cancel = true;
                return;
            }

            _geoGen++;
            try { _tray?.Dispose(); } catch { }
            try { SplitTunnel.ClearAsync().Wait(2000); } catch { }
            try { KillSwitch.DisableAsync().Wait(2000); } catch { }
            try { _engine.StopAsync().Wait(4000); } catch { }
        }

        private void TryFirstRunImport()
        {
            try
            {
                if (_config.Connections.Count > 0) return;
                // فرمت رسمی: JSON — نام قدیمی و pkg هم برای سازگاری پذیرفته می‌شوند
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
                _tray = new System.Windows.Forms.NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Shield,
                    Visible = true,
                    Text = AppConfig.BrandName
                };
                _tray.DoubleClick += (_, __) => { Show(); WindowState = WindowState.Normal; Activate(); };

                // منوی راست‌کلیک آیکون تری — نمایش / خروج کامل
                var trayMenu = new System.Windows.Forms.ContextMenuStrip();
                trayMenu.Items.Add(TrayShowApp, null, (_, __) =>
                    Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
                trayMenu.Items.Add(TrayExit, null, (_, __) =>
                    Dispatcher.Invoke(() => { _reallyExit = true; Close(); }));
                _tray.ContextMenuStrip = trayMenu;
            }
            catch { }
        }



        // لاگ جعبهٔ ابزار — فقط پینگ/تریس و اقدامات جعبهٔابزار (از MainWindow.Tools.cs)
        private void AppendLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine);
                LogBox.CaretIndex = LogBox.Text.Length;
                LogBox.ScrollToEnd();
            });
            LogWriter.Write(line);
        }

        // لاگ مجزای کانکشن‌ها — رویدادهای اتصال/قطع/killswitch/dns/split-tunnel/openvpn و غیره — مستقل از لاگ جعبهٔ ابزار
        private void AppendConnLog(string line)
        {
            Dispatcher.Invoke(() =>
            {
                ConnLogBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine);
                ConnLogBox.ScrollToEnd();

                // IP واقعی سرور از لاگ خود OpenVPN — دقیق‌تر از DNS که با چند رکورد ممکن است سرور دیگری را نشان دهد
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
                            // فرمت: Connecting to [host]:port (5.10.249.9) via UDP
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
                                ServerIpText.Text = ipStr;
                                ConnLogBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] [ip] server from openvpn log: " + ipStr + Environment.NewLine);
                            }
                        }
                        break;
                    }
                }
                catch { }

                // اسکرول لاگ همیشه روی آخرین داده‌ها بماند — حتی بعد از افزودن خط داخلی
                ConnLogBox.CaretIndex = ConnLogBox.Text.Length;
                ConnLogBox.ScrollToEnd();
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

    }
}
