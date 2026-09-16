using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmartVpn;

public partial class SubscriptionDialog : Window
{
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // ---------- متن‌های فارسی ----------
    private const string TitleSuffix = "وضعیت اشتراک";
    private const string MsgMissingCreds = "نام کاربری و پسورد را وارد کنید.";
    private const string MsgChecking = "در حال استعلام از پورتال...";
    private const string MsgNetworkError = "ارتباط با سرور برقرار نشد. اینترنت خود را بررسی کنید و دوباره تلاش کنید.";
    private const string MsgBadCreds = "نام کاربری یا پسورد اشتباه است.";
    private const string MsgServerErrorFmt = "خطای سرور: {0}";
    private const string MsgExpired = "منقضی شده ❌";
    private const string MsgDaysFmt = "{0} روز";
    private const string StActive = "فعال ✅";
    private const string StExpired = "منقضی ❌";
    private const string OnlineText = "آنلاین ✅";
    private const string OfflineText = "آفلاین ⚫";
    private const string BtnCheck = "📊 استعلام وضعیت";
    private const string BtnChecking = "⏳ در حال استعلام...";

    private readonly string _portalBaseUrl;

    // با تیک «ذخیره»، بعد از استعلام‌ موفق صدا زده می‌شود تا یوزر/پس روی کانکشن‌های رسمی ست شود
    public Action<string, string>? OnSaveCredentials;

    public SubscriptionDialog(string portalBaseUrl, string username, string password)
    {
        InitializeComponent();
        Title = AppConfig.BrandName + " — " + TitleSuffix;
        _portalBaseUrl = portalBaseUrl;
        UserBox.Text = username;
        PassBox.Password = password;
        SourceInitialized += (_, __) => ApplyDarkTitleBar();
        // اگر یوزر/پس از پروفایل پر شده بود، استعلام خودکار هنگام باز شدن
        Loaded += (_, __) =>
        {
            if (UserBox.Text.Trim().Length > 0 && PassBox.Password.Length > 0)
                Check_Click(this, new RoutedEventArgs());
        };
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

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        var pass = PassBox.Password;
        if (user.Length == 0 || pass.Length == 0) { ShowStatus(MsgMissingCreds, true); return; }

        CheckBtn.IsEnabled = false;
        CheckBtn.Content = BtnChecking;
        ShowStatus(MsgChecking, false);
        ResultPanel.Visibility = Visibility.Collapsed;
        try
        {
            var client = new PortalApiClient(_portalBaseUrl);
            var loginUser = await client.LoginAsync(user, pass);
            var dashboard = await client.GetDashboardAsync();

            Populate(dashboard, loginUser.CanConnect);
            StatusText.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;

            // فقط وقتی استعلام موفق بود ذخیره می‌کنیم — یعنی یوزر/پس معتبر است
            if (SaveCheck.IsChecked == true) OnSaveCredentials?.Invoke(user, pass);
        }
        catch (PortalApiException ex)
        {
            if (ex.StatusCode is 400 or 401 or 403 or 404) ShowStatus(MsgBadCreds, true);
            else ShowStatus(string.Format(MsgServerErrorFmt, ex.Message), true);
        }
        catch { ShowStatus(MsgNetworkError, true); }
        finally
        {
            CheckBtn.IsEnabled = true;
            CheckBtn.Content = BtnCheck;
        }
    }

    private void Populate(PortalDashboard d, bool canConnect)
    {
        var expired = string.Equals(d.Account.Status, "expired", StringComparison.OrdinalIgnoreCase)
                      || d.Account.RemainingDays <= 0;

        StatusValue.Text = expired ? StExpired : StActive;
        StatusValue.Foreground = new SolidColorBrush(
            expired ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x22, 0xC5, 0x5E));

        GroupValue.Text = d.Package.Name.Length > 0 ? d.Package.Name : "—";

        ExpireValue.Text = FormatExpire(d.Account.ExpireAt);
        ExpireValue.ToolTip = d.Account.ExpireAt?.ToString("yyyy-MM-dd") ?? "";

        RemainValue.Text = FormatRemaining(d.Account.RemainingDays, expired);

        // وضعیت اتصال فعلی اکانت — براساس فیلد canConnect پاسخ لاگین پورتال (فقط آنلاین/آفلاین، بدون تعداد دستگاه)
        OnlineValue.Text = canConnect ? OnlineText : OfflineText;
        OnlineValue.Foreground = new SolidColorBrush(
            canConnect ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0x9C, 0xA3, 0xAF));

        var usageGb = d.Traffic.UsedMb / 1024.0;
        UsageValue.Text = usageGb.ToString("0.00", CultureInfo.InvariantCulture) + " GB";

        var quotaGb = d.Traffic.TotalMb / 1024.0;
        if (quotaGb > 0)
        {
            TotalValue.Text = quotaGb.ToString("0.#", CultureInfo.InvariantCulture) + " GB";
            var leftGb = d.Traffic.RemainingMb / 1024.0;
            LeftValue.Text = leftGb.ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            LeftValue.Foreground = new SolidColorBrush(
                leftGb <= quotaGb * 0.1 ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x22, 0xC5, 0x5E));
        }
        else
        {
            TotalValue.Text = "—";
            LeftValue.Text = "—";
        }
    }

    // تاریخ میلادی سرور -> شمسی برای نمایش (میلادی در ToolTip می‌ماند)
    private static string FormatExpire(DateTime? dt)
    {
        if (dt == null) return "—";
        try
        {
            var pc = new PersianCalendar();
            var local = dt.Value.ToLocalTime();
            return $"{pc.GetYear(local)}/{pc.GetMonth(local):00}/{pc.GetDayOfMonth(local):00}";
        }
        catch { return dt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }

    private static string FormatRemaining(int remainingDays, bool expired)
    {
        if (expired || remainingDays <= 0) return MsgExpired;
        return string.Format(MsgDaysFmt, remainingDays);
    }

    private void ShowStatus(string msg, bool error)
    {
        StatusText.Text = msg;
        StatusText.Foreground = error
            ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
            : (TryFindResource("SubTextBrush") as Brush ?? Brushes.Gray);
        StatusText.Visibility = Visibility.Visible;
    }

    // کلیک روی «خرید / تمدید اشتراک»
    private void Panel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var url = AppConfig.Load().PanelUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}
