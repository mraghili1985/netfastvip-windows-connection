using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartVpn;

/// <summary>
/// هماهنگ‌سازی نوار عنوان ویندوز (Title Bar) با تم برنامه.
/// بدون این، پنجره‌های پاپ‌اپ در تم تیره نوار/حاشیه سفید پیش‌فرض ویندوز را می‌گیرند.
/// از App.xaml.cs به‌صورت خودکار روی همه پنجره‌ها اعمال می‌شود — نیازی نیست در هر دیالوگ کدی اضافه شود.
/// </summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;     // ویندوز 10 نسخه 2004 به بعد
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19; // بیلدهای 1809 تا 1909
    private const int DWMWA_CAPTION_COLOR = 35;               // فقط ویندوز 11 — رنگ دقیق نوار عنوان
    private const int DWMWA_TEXT_COLOR = 36;                  // فقط ویندوز 11 — رنگ متن نوار عنوان

    /// <summary>تم فعلی تیره است؟ توسط App.ApplyTheme تنظیم می‌شود.</summary>
    public static bool IsDark { get; set; } = true;

    /// <summary>اعمال روی یک پنجره (بعد از ساخته‌شدن هندل — رویداد Loaded).</summary>
    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // حالت تیره نوار عنوان (روی ویندوز 10 هم کار می‌کند)
            int dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));

            // ویندوز 11: رنگ نوار عنوان و متنش دقیقا از پالت تم گرفته می‌شود
            // (روی ویندوز 10 این دو صدا بی‌اثر و بی‌خطرند)
            int caption = ToColorRef(window, "BgBrush");
            int text = ToColorRef(window, "TextBrush");
            if (caption >= 0) DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            if (text >= 0) DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));

            // ویندوز 11 (بیلد 22621 به بعد): افکت Mica روی پنجره
            const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
            int backdrop = 2; // Mica
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
        catch { /* ویندوزهای خیلی قدیمی — نادیده گرفتن بی‌خطر است */ }
    }

    /// <summary>اعمال روی همه پنجره‌های باز (هنگام عوض‌شدن تم در تنظیمات).</summary>
    public static void ApplyToAll()
    {
        var app = Application.Current;
        if (app == null) return;
        foreach (Window w in app.Windows) Apply(w);
    }

    // تبدیل براش تم به COLORREF ویندوز (0x00BBGGRR)؛ اگر براش پیدا نشد ‎-1
    // جست‌وجو از منابع خود پنجره شروع می‌شود تا رنگ متمایز پاپ‌آپ‌ها روی نوار عنوان هم بنشیند
    private static int ToColorRef(Window window, string brushKey)
    {
        if (window.TryFindResource(brushKey) is System.Windows.Media.SolidColorBrush b)
            return b.Color.R | (b.Color.G << 8) | (b.Color.B << 16);
        return -1;
    }
}
