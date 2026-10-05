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
        if (_currentTheme == "system" && Current != null)
        {
            Current.Dispatcher.Invoke(() =>
            {
                ApplyTheme("system");
                if (Current.MainWindow is MainWindow mw)
                {
                    mw.ApplyThemeChoice("system");
                }
            });
        }
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
        var hex = _isDark ? "#242E3C" : "#D9E1EB";
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (w.Resources["BgBrush"] is SolidColorBrush b && !b.IsFrozen) b.Color = color;
        else w.Resources["BgBrush"] = new SolidColorBrush(color);
    }

    public static Color GetWindowsAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int acc)
            {
                byte r = (byte)(acc & 0xFF);
                byte g = (byte)((acc >> 8) & 0xFF);
                byte b = (byte)((acc >> 16) & 0xFF);
                if (r != 0 || g != 0 || b != 0) return Color.FromRgb(r, g, b);
            }
            if (key?.GetValue("ColorizationColor") is int col)
            {
                byte r = (byte)((col >> 16) & 0xFF);
                byte g = (byte)((col >> 8) & 0xFF);
                byte b = (byte)(col & 0xFF);
                if (r != 0 || g != 0 || b != 0) return Color.FromRgb(r, g, b);
            }
            if (SystemParameters.WindowGlassBrush is SolidColorBrush sb)
            {
                return sb.Color;
            }
        }
        catch { }
        return Color.FromRgb(0x38, 0xBD, 0xF8);
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

        // رنگ اکسنت Royal Blue هماهنگ با طراحی وب‌سایت مدرن Tailwind / MikroTik
        string accentHex = dark ? "#3B82F6" : "#2563EB";

        if (dark)
        {
            SetBrush("WindowBackdropTintBrush", "#E00B1120");
            SetBrush("BgBrush", "#0B1120");              // Slate 950 deep canvas
            SetBrush("SidebarBrush", "#070B14");         // Deep obsidian drawer
            SetBrush("SidebarActiveBrush", "#1E293B");   // Slate 800 active
            SetBrush("CardBrush", "#151F32");            // Slate 850 card surface
            SetBrush("CardHoverBrush", "#1E293B");       // Elevated hover
            SetBrush("FieldBrush", "#0F172A");           // Code / terminal / inner box
            SetBrush("LineBrush", "#243248");            // Subtle borders
            SetBrush("TextBrush", "#F8FAFC");            // Crisp White (Slate 50)
            SetBrush("SubTextBrush", "#CBD5E1");         // Bright Crisp Slate (Slate 300) for maximum dark mode readability
            SetBrush("AccentBrush", accentHex);          // #3B82F6 Royal Blue
            SetBrush("Accent2Brush", "#10B981");         // Emerald Green
            SetBrush("DangerBrush", "#EF4444");          // Red
            SetBrush("HoverBrush", "#1A2538");           // Subtle hover
            SetBrush("RowCardBrush", "#151F32");         // Row cards
            SetBrush("RowLineBrush", "#243248");         // Row divider
            SetBrush("GlassBrush", "#151F32");           // Glass surface
            SetBrush("GlassBorderBrush", "#243248");     // Glass border

            // 3D Elevated Card Gradient Surfaces (React / Tailwind UI Style)
            SetLinearGradientBrush("ElevatedCardBrush", new[] {
                ((Color)ColorConverter.ConvertFromString("#18253B"), 0.0),
                ((Color)ColorConverter.ConvertFromString("#111827"), 1.0)
            }, new Point(0, 0), new Point(0, 1));

            SetLinearGradientBrush("ElevatedCardBorderBrush", new[] {
                ((Color)ColorConverter.ConvertFromString("#466088"), 0.0),
                ((Color)ColorConverter.ConvertFromString("#28394E"), 0.35),
                ((Color)ColorConverter.ConvertFromString("#162232"), 1.0)
            }, new Point(0, 0), new Point(0, 1));
        }
        else
        {
            // پالت روشن روان (Fluent Light) با کنتراست بالا و تفکیک لایه‌ای المان‌ها
            SetBrush("WindowBackdropTintBrush", "#D0F1F5F9");
            SetBrush("BgBrush", "#F1F5F9");
            SetBrush("SidebarBrush", "#E2E8F0");
            SetBrush("SidebarActiveBrush", "#CBD5E1");
            SetBrush("CardBrush", "#FFFFFF");
            SetBrush("CardHoverBrush", "#F8FAFC");
            SetBrush("FieldBrush", "#F1F5F9");
            SetBrush("LineBrush", "#E2E8F0");
            SetBrush("TextBrush", "#0F172A");
            SetBrush("SubTextBrush", "#334155");         // Deep Slate 700 with high contrast against white card surface
            SetBrush("AccentBrush", accentHex);
            SetBrush("Accent2Brush", "#10B981");
            SetBrush("DangerBrush", "#EF4444");
            SetBrush("HoverBrush", "#E2E8F0");
            SetBrush("RowCardBrush", "#FFFFFF");
            SetBrush("RowLineBrush", "#CBD5E1");
            SetBrush("GlassBrush", "#FFFFFF");
            SetBrush("GlassBorderBrush", "#CBD5E1");

            SetLinearGradientBrush("ElevatedCardBrush", new[] {
                ((Color)ColorConverter.ConvertFromString("#FFFFFF"), 0.0),
                ((Color)ColorConverter.ConvertFromString("#F8FAFC"), 1.0)
            }, new Point(0, 0), new Point(0, 1));

            SetLinearGradientBrush("ElevatedCardBorderBrush", new[] {
                ((Color)ColorConverter.ConvertFromString("#FFFFFF"), 0.0),
                ((Color)ColorConverter.ConvertFromString("#E2E8F0"), 0.35),
                ((Color)ColorConverter.ConvertFromString("#CBD5E1"), 1.0)
            }, new Point(0, 0), new Point(0, 1));
        }

        if (Current?.Resources["PrimaryGlowBrush"] is LinearGradientBrush glow && !glow.IsFrozen)
        {
            var c1 = (Color)ColorConverter.ConvertFromString(accentHex);
            var c2 = dark ? Color.FromRgb(0x1D, 0x4E, 0xD8) : Color.FromRgb(0x1E, 0x40, 0xAF);
            if (glow.GradientStops.Count >= 2)
            {
                glow.GradientStops[0].Color = c1;
                glow.GradientStops[1].Color = c2;
            }
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

    private static void SetLinearGradientBrush(string key, (Color color, double offset)[] stops, Point startPoint, Point endPoint)
    {
        var gradStops = new GradientStopCollection();
        foreach (var stop in stops)
            gradStops.Add(new GradientStop(stop.color, stop.offset));

        if (Current?.Resources[key] is LinearGradientBrush lgb && !lgb.IsFrozen)
        {
            lgb.StartPoint = startPoint;
            lgb.EndPoint = endPoint;
            lgb.GradientStops = gradStops;
        }
        else if (Current != null)
        {
            Current.Resources[key] = new LinearGradientBrush(gradStops, startPoint, endPoint);
        }
    }
}
