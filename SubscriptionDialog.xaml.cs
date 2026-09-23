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
    private const int DWMWA_TEXT_COLOR    = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const string TitleSuffix       = "وضعیت اشتراک";
    private const string MsgMissingCreds   = "نام کاربری و پسورد را وارد کنید.";
    private const string MsgChecking       = "در حال استعلام از پورتال...";
    private const string MsgNetworkError   = "ارتباط با سرور برقرار نشد. اینترنت خود را بررسی کنید و دوباره تلاش کنید.";
    private const string MsgBadCreds       = "نام کاربری یا پسورد اشتباه است.";
    private const string MsgServerErrorFmt = "خطای سرور: {0}";
    private const string StActive          = "فعال ✅";
    private const string StExpired         = "منقضی ❌";
    private const string OnlineText  = "آنلاین ✅";
    private const string OfflineText = "آفلاین ⚫";
    private const string BtnCheck          = "📊 استعلام وضعیت";
    private const string BtnChecking       = "⏳ در حال استعلام...";

    private readonly string _portalBaseUrl;
    public Action<string, string>? OnSaveCredentials;

    public SubscriptionDialog(string portalBaseUrl, string username, string password)
    {
        InitializeComponent();
        AsciiInputFilter.ApplyTo(UserBox);
        AsciiInputFilter.ApplyTo(PassBox);
        _portalBaseUrl = portalBaseUrl;
        UserBox.Text = username;
        PassBox.Password = password;
        SourceInitialized += (_, __) => ApplyDarkTitleBar();
        Loaded += (_, __) =>
        {
            // اعمال ترجمه بعد از Load کامل پنجره
            Localization.Watch(this);
            Title = AppConfig.BrandName + " — " + Localization.T(TitleSuffix);
            CheckBtn.Content = Localization.T(BtnCheck);

            if (UserBox.Text.Trim().Length > 0 && PassBox.Password.Length > 0)
                Check_Click(this, new RoutedEventArgs());
        };
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd    = new WindowInteropHelper(this).Handle;
            int caption = 0x00171717;
            int text    = 0x00F8FAFC;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR,    ref text,    sizeof(int));
        }
        catch { }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        var user = UserBox.Text.Trim();
        var pass = PassBox.Password;
        if (user.Length == 0 || pass.Length == 0) { ShowStatus(Localization.T(MsgMissingCreds), true); return; }

        CheckBtn.IsEnabled = false;
        CheckBtn.Content   = Localization.T(BtnChecking);
        ShowStatus(Localization.T(MsgChecking), false);
        ResultPanel.Visibility = Visibility.Collapsed;

        try
        {
            var client     = new PortalApiClient(_portalBaseUrl);
            var loginUser  = await client.LoginAsync(user, pass);
            var dashboard  = await client.GetDashboardAsync();

            Populate(dashboard, loginUser.CanConnect);
            StatusText.Visibility  = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;

            // پروفایل‌ها از هاست/پروفایل‌های موجود مدیریت می‌شوند؛ این استعلام فقط وضعیت اشتراک را می‌خواند.
            if (SaveCheck.IsChecked == true) OnSaveCredentials?.Invoke(user, pass);
        }
        catch (PortalApiException ex)
        {
            if (ex.StatusCode is 400 or 401 or 403 or 404) ShowStatus(Localization.T(MsgBadCreds), true);
            else ShowStatus(string.Format(Localization.T(MsgServerErrorFmt), ex.Message), true);
        }
        catch { ShowStatus(Localization.T(MsgNetworkError), true); }
        finally
        {
            CheckBtn.IsEnabled = true;
            CheckBtn.Content   = Localization.T(BtnCheck);
        }
    }

    private void Populate(PortalDashboard d, bool canConnect)
    {
        var expired = string.Equals(d.Account.Status, "expired", StringComparison.OrdinalIgnoreCase)
                      || d.Account.RemainingDays <= 0;

        // وضعیت اشتراک
        StatusValue.Text = expired ? Localization.T(StExpired) : Localization.T(StActive);
        StatusValue.Foreground = new SolidColorBrush(
            expired ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0x22, 0xC5, 0x5E));

        // سرویس / پکیج
        GroupValue.Text = d.Package.Name.Length > 0 ? d.Package.Name : "—";

        // تاریخ انقضا (شمسی)
        ExpireValue.Text    = FormatExpire(d.Account.ExpireAt);
        ExpireValue.ToolTip = d.Account.ExpireAt?.ToString("yyyy-MM-dd") ?? "";

        // زمان باقیمانده (از expireAt محاسبه میشه)
        RemainValue.Text = FormatRemaining(d.Account.ExpireAt, expired);

        // وضعیت اتصال
        OnlineValue.Text = canConnect ? Localization.T(OnlineText) : Localization.T(OfflineText);
        OnlineValue.Foreground = new SolidColorBrush(
            canConnect ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0x9C, 0xA3, 0xAF));

        // اطلاعات اکانت
        UsernameValue.Text  = d.Account.Username.Length > 0 ? d.Account.Username : "—";
        FirstLoginValue.Text = "—";

        // ترافیک
        var usedGb    = d.Traffic.UsedMb      / 1024.0;
        var totalGb   = d.Traffic.TotalMb     / 1024.0;
        var remainGb  = d.Traffic.RemainingMb / 1024.0;

        if (totalGb > 0)
        {
            var pct = Math.Min(usedGb / totalGb, 1.0);

            // خلاصه بالای بار
            TrafficSummaryText.Text =
                $"{usedGb:0.0} / {totalGb:0.#} " + Localization.T("گیگ");

            // بار progress — عرض والد ≈ ۳۰۴ پیکسل (360 - 2×14 padding - 2×10 card padding)
            const double barWidth = 304;
            TrafficBar.Width      = barWidth * pct;
            TrafficBar.Background = new SolidColorBrush(
                pct >= 0.9 ? Color.FromRgb(0xEF, 0x44, 0x44)
                           : Color.FromRgb(0x22, 0xC5, 0x5E));

            // پایین بار
            UsageText.Text         = $"Used: {usedGb:0.0}GB";
            RemainTrafficText.Text  = $"Left: {remainGb:0.0}GB";
            RemainTrafficText.Foreground = new SolidColorBrush(
                remainGb <= totalGb * 0.1 ? Color.FromRgb(0xEF, 0x44, 0x44)
                                          : Color.FromRgb(0x22, 0xC5, 0x5E));
        }
        else
        {
            TrafficSummaryText.Text = Localization.T("نامحدود");
            TrafficBar.Width        = 0;
            UsageText.Text          = $"Used: {usedGb:0.0}GB";
            RemainTrafficText.Text  = "—";
        }
    }

    private static string FormatExpire(DateTime? dt)
    {
        if (dt == null) return "—";
        try
        {
            var pc    = new PersianCalendar();
            var local = dt.Value.ToLocalTime();
            return $"{pc.GetYear(local)}/{pc.GetMonth(local):00}/{pc.GetDayOfMonth(local):00}";
        }
        catch { return dt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }

    private static string FormatRemaining(DateTime? expireAt, bool expired)
    {
        if (expired) return StExpired;
        if (expireAt == null) return "—";
        var rem = expireAt.Value.ToLocalTime() - DateTime.Now;
        if (rem.TotalSeconds <= 0) return StExpired;
        var days  = (int)rem.TotalDays;
        var hours = rem.Hours;
        if (days == 0 && hours == 0) return StExpired;
        if (days == 0) return $"{hours}H";
        if (hours == 0) return $"{days}D";
        return $"{days}D-{hours}H";
    }

    private void ShowStatus(string msg, bool error)
    {
        StatusText.Text       = msg;
        StatusText.Foreground = error
            ? new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44))
            : (TryFindResource("SubTextBrush") as Brush ?? Brushes.Gray);
        StatusText.Visibility = Visibility.Visible;
    }

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