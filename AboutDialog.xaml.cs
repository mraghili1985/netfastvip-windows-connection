using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartVpn;

// پنجره «درباره برنامه» — هم‌تم با بقیه دیالوگ‌ها (تیره/روشن، راست‌به‌چپ)
public partial class AboutDialog : Window
{
    // ---- متن‌های فارسی ----
    private const string VersionPrefix = "نسخه ";

    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly string _telegramUrl;
    private readonly string _panelUrl;
    private readonly string _supportUrl;

    public AboutDialog(string telegramUrl, string panelUrl, string supportUrl)
    {
        InitializeComponent();
        _telegramUrl = telegramUrl;
        _panelUrl = panelUrl;
        _supportUrl = supportUrl;
        // برندینگ از config.json — نام و نسخه (نسخه خالی = خودکار از EXE)
        Title = AppConfig.BrandName;
        BrandNameText.Text = AppConfig.BrandName;
        VersionText.Text = VersionPrefix + AppConfig.BrandVersion;
        SourceInitialized += (_, __) => ApplyDarkTitleBar();
    }

    // نوار عنوان تیره — هماهنگ با پنجره اصلی
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

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void Telegram_Click(object sender, RoutedEventArgs e) => OpenUrl(_telegramUrl);
    private void Panel_Click(object sender, RoutedEventArgs e) => OpenUrl(_panelUrl);
    private void Support_Click(object sender, RoutedEventArgs e) => OpenUrl(_supportUrl);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
