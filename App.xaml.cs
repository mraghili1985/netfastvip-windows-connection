using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SmartVpn.XrayCore;

namespace SmartVpn;

public partial class App : Application
{
    private static string _currentTheme = "dark";
    private static bool _isDark = true;

    // تک‌نمونه‌ای بودن برنامه: اگر کاربر چند بار روی exe بزند، فقط یک نمونه اجرا می‌ماند و
    // نمونه‌های بعدی فقط پنجره‌ی همان نمونه‌ی قبلی را جلو می‌آورند و بلافاصله خارج می‌شوند.
    // نگه‌داشتن رفرنس لازم است وگرنه GC ممکن است Mutex را زودتر آزاد کند.
    private static Mutex? _singleInstanceMutex;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        // این چک باید همه‌چیز دیگر (تم، FirstRunSetup و...) را جلو بزند تا نمونه‌ی تکراری
        // هیچ کار اضافه‌ای نکند و در سریع‌ترین حالت ممکن بسته شود.
        _singleInstanceMutex = new Mutex(true, "NETFASTVIP_SingleInstance_9F2E7B31-4C4A-4C7E-9A2B-6D5E1F0C8A3D", out var isNewInstance);
        if (!isNewInstance)
        {
            BringExistingInstanceToFront();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        try
        {
            var cfg = AppConfig.Load();
            Localization.SetLanguage(cfg.Language);
            ApplyTheme(cfg.Theme);
        }
        catch { Localization.SetLanguage("fa"); ApplyTheme("dark"); }

        // اعمال خودکار روی همه پنجره‌ها (پاپ‌آپ‌ها هم) بعد از ساخته‌شدن هندل:
        // ۱) پس‌زمینه کمی متمایز برای پاپ‌آپ‌ها تا روی برنامه مشخص باشند
        // ۲) نوار عنوان هماهنگ با تم (در تم تیره، نوار سفید پیش‌فرض ویندوز زشت است)
        EventManager.RegisterClassHandler(typeof(Window), Window.LoadedEvent,
            new RoutedEventHandler((s, _) =>
            {
                if (s is Window w)
                {
                    Localization.Watch(w);
                    ApplyPopupTint(w);
                    DarkTitleBar.Apply(w);
                }
            }));

        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((s, e) =>
            {
                if (e.Key != Key.Escape || s is not Window window || window is MainWindow)
                    return;

                e.Handled = true;
                window.Close();
            }));

        // نسخه پورتابل: بررسی‌های اولین اجرا (هشدار اجرا از ZIP، مجوز نوشتن، میان‌بر دسکتاپ)
        try { FirstRunSetup.Run(); } catch { }
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        AppDomain.CurrentDomain.ProcessExit += (_, __) =>
        {
            try { XrayEngine.Stop(); } catch { }
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        try { XrayEngine.Stop(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    // پنجره‌ی نمونه‌ی قبلی را از بین فرآیندها پیدا کرده و جلو می‌آورد (Restore اگر Minimize بود + Focus)
    private static void BringExistingInstanceToFront()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id) continue;
                var handle = p.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;
                if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);
                SetForegroundWindow(handle);
                break;
            }
        }
        catch { }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentTheme == "system")
            Current.Dispatcher.Invoke(() => ApplyTheme("system"));
    }

    public static string CurrentTheme => _currentTheme;

    // نتیجه‌ی نهایی و قطعی روشن/تیره بودن (بعد از حل حالت "system" روی رجیستری) —
    // منبع مرجع واحد؛ IsLightTheme() توی MainWindow دیگر رجیستری را دوباره و جدا چک نمی‌کند
    // تا با رنگ‌های واقعی که روی DynamicResource ها اعمال شده (کارت بالا و غیره) همیشه هم‌خوان بماند
    public static bool IsDark => _isDark;

    // پس‌زمینه پاپ‌آپ‌ها کمی متفاوت از پنجره اصلی — تا لبه پاپ‌آپ روی برنامه دیده شود
    private static void ApplyPopupTint(Window w)
    {
        if (w is MainWindow) return;
        var hex = _isDark ? "#0B1B33" : "#D9E1EB";
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (w.Resources["BgBrush"] is SolidColorBrush b && !b.IsFrozen) b.Color = color;
        else w.Resources["BgBrush"] = new SolidColorBrush(color);
    }

    // Radius panel palette (shadcn slate, blue primary)
    public static void ApplyTheme(string theme)
    {
        if (theme != "light" && theme != "system") theme = "dark";
        _currentTheme = theme;

        var dark = theme switch
        {
            "dark" => true,
            "light" => false,
            _ => !SystemUsesLightTheme(),
        };
        _isDark = dark;

        if (dark)
        {
            SetBrush("BgBrush", "#80090E1A");
            SetBrush("SidebarBrush", "#8D080D18");
            SetBrush("CardBrush", "#45111B2E");
            SetBrush("CardHoverBrush", "#6018263F");
            SetBrush("FieldBrush", "#40142136");
            SetBrush("LineBrush", "#253554");
            SetBrush("TextBrush", "#FFFFFF");
            SetBrush("SubTextBrush", "#94A3B8");
            SetBrush("AccentBrush", "#38BDF8");
            SetBrush("Accent2Brush", "#22C55E");
            SetBrush("DangerBrush", "#EF4444");
            SetBrush("HoverBrush", "#3020304D");
            SetBrush("RowCardBrush", "#40162339");
            SetBrush("RowLineBrush", "#152033");
            SetBrush("GlassBrush", "#38101A2D");
            SetBrush("GlassBorderBrush", "#304A70");
        }
        else
        {
            // پالت روشن مایل به خاکستری — هماهنگ با پنل ردیوس (slate + آبی 2563EB)،
            // بدون سفید خالص تا چشم را نزند
            SetBrush("BgBrush", "#E8ECF1");
            SetBrush("CardBrush", "#F6F8FA");
            SetBrush("FieldBrush", "#E2E8F0");
            SetBrush("LineBrush", "#CBD5E1");
            SetBrush("TextBrush", "#0F172A");
            SetBrush("SubTextBrush", "#5B6B80");
            SetBrush("AccentBrush", "#2563EB");
            SetBrush("Accent2Brush", "#16A34A");
            SetBrush("DangerBrush", "#EF4444");
            SetBrush("HoverBrush", "#DAE1E9");
            SetBrush("RowCardBrush", "#F0FFFFFF");
            SetBrush("RowLineBrush", "#280F172A");
        }

        // پنجره‌های باز: رنگ پاپ‌آپ‌ها و نوار عنوان با تم جدید همگام شوند
        DarkTitleBar.IsDark = dark;
        if (Current != null)
        {
            foreach (Window w in Current.Windows) ApplyPopupTint(w);
            DarkTitleBar.ApplyToAll();
        }
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return true; }
    }

    private static void SetBrush(string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = color;
        else
            Current.Resources[key] = new SolidColorBrush(color);
    }
}
