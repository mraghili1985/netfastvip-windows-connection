using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
    private readonly AppConfig _appConfig;
    public Action<string, string>? OnSaveCredentials;
    public Action? OnLoggedOut;
    public Action? OnSyncCompleted;

    public AppConfig Config => _appConfig;

    public SubscriptionDialog(string portalBaseUrl, string username, string password, AppConfig? appConfig = null)
    {
        InitializeComponent();
        _appConfig = appConfig ?? AppConfig.Load();
        AsciiInputFilter.ApplyTo(UserBox);
        AsciiInputFilter.ApplyTo(PassBox);
        _portalBaseUrl = portalBaseUrl;

        if (string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(_appConfig.CustomerUsername))
            username = _appConfig.CustomerUsername;
        if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(_appConfig.CustomerPassword))
            password = _appConfig.CustomerPassword;

        UserBox.Text = username;
        PassBox.Password = password;
        SourceInitialized += (_, __) => ApplyDarkTitleBar();
        Loaded += (_, __) =>
        {
            // اعمال ترجمه بعد از Load کامل پنجره
            Localization.Watch(this);
            Title = AppConfig.BrandName + " — " + Localization.T(TitleSuffix);
            CheckBtn.Content = Localization.T(BtnCheck);

            var isFa = Localization.CurrentLanguage == "fa";
            RenewPromptBlock.FlowDirection = isFa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            RenewPromptText.Text = Localization.T("نیاز به تمدید دارید؟") + " ";
            RenewActionText.Text = Localization.T("خرید / تمدید اشتراک");

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

        // اگر نام کاربری با کاربر قبلی فرق می‌کند، توکن‌های کش‌شده قبلی را پاک کن تا تداخل نکنند
        if (!string.Equals(user, _appConfig.CustomerUsername, StringComparison.OrdinalIgnoreCase))
        {
            _appConfig.CustomerAccessToken = null;
            _appConfig.CustomerRefreshToken = null;
            _appConfig.CustomerTokenExpiresAt = null;
        }

        try
        {
            bool handled = false;
            // ۱. تلاش با کلاینت جدید اپ مشتری (25-Customer-App-API)
            try
            {
                var appClient = new CustomerAppApiClient(_portalBaseUrl);
                CustomerLoginResponse loginRes;
                if (user.StartsWith("09") || user.StartsWith("+") || user.Contains("@"))
                    loginRes = await appClient.LoginCustomerAsync(user, pass);
                else
                    loginRes = await appClient.LoginRadiusAsync(user, pass);

                var services = await appClient.GetServicesAsync();
                var primarySvc = services.FirstOrDefault();
                if (primarySvc != null)
                {
                    var dashboard = CustomerAppApiClient.ConvertToPortalDashboard(primarySvc);
                    Populate(dashboard, primarySvc.CanConnect);
                    StatusText.Visibility  = Visibility.Collapsed;
                    ResultPanel.Visibility = Visibility.Visible;

                    // ثبت توکن‌ها و اطلاعات کاربر روی کانفیگ
                    _appConfig.CustomerAccessToken = appClient.AccessToken;
                    _appConfig.CustomerRefreshToken = appClient.RefreshToken;
                    _appConfig.CustomerTokenExpiresAt = appClient.TokenExpiresAt;
                    _appConfig.CustomerDeviceId = appClient.CurrentDevice?.Id;
                    _appConfig.CustomerLoginMode = appClient.CurrentAccount?.Kind ?? "radius";
                    _appConfig.CustomerUsername = user;
                    _appConfig.CustomerPassword = pass;
                    _appConfig.CustomerPackageName = primarySvc.PackageName ?? primarySvc.Name;
                    _appConfig.CustomerStatus = primarySvc.Status;

                    // سینک خودکار کانکشن‌ها و سرورها روی نمونه فعال کانفیگ برنامه
                    await PortalSyncService.SyncFromCustomerAppAsync(appClient, _appConfig);
                    _appConfig.Save();

                    OnSyncCompleted?.Invoke();
                    if (SaveCheck.IsChecked == true) OnSaveCredentials?.Invoke(user, pass);
                    handled = true;
                }
            }
            catch (CustomerDeviceLimitException devEx)
            {
                var oldest = devEx.Devices.OrderBy(d => d.LastSeenAt ?? d.CreatedAt).FirstOrDefault();
                if (oldest != null)
                {
                    bool replace = AskDialog.Confirm(this,
                        $"{devEx.Message}\n\nآیا مایلید دستگاه قدیمی «{oldest.Name}» ({oldest.Platform}) خارج شده و این سیستم جایگزین آن شود؟",
                        "بله، جایگزین شود", "خیر");
                    if (replace)
                    {
                        try
                        {
                            var appClient = new CustomerAppApiClient(_portalBaseUrl);
                            CustomerLoginResponse loginRes;
                            if (user.StartsWith("09") || user.StartsWith("+") || user.Contains("@"))
                                loginRes = await appClient.LoginCustomerAsync(user, pass, replaceDeviceId: oldest.Id);
                            else
                                loginRes = await appClient.LoginRadiusAsync(user, pass, replaceDeviceId: oldest.Id);

                            var services = await appClient.GetServicesAsync();
                            var primarySvc = services.FirstOrDefault();
                            if (primarySvc != null)
                            {
                                var dashboard = CustomerAppApiClient.ConvertToPortalDashboard(primarySvc);
                                Populate(dashboard, primarySvc.CanConnect);
                                StatusText.Visibility  = Visibility.Collapsed;
                                ResultPanel.Visibility = Visibility.Visible;

                                // ثبت توکن‌ها و اطلاعات کاربر روی کانفیگ
                                _appConfig.CustomerAccessToken = appClient.AccessToken;
                                _appConfig.CustomerRefreshToken = appClient.RefreshToken;
                                _appConfig.CustomerTokenExpiresAt = appClient.TokenExpiresAt;
                                _appConfig.CustomerDeviceId = appClient.CurrentDevice?.Id;
                                _appConfig.CustomerLoginMode = appClient.CurrentAccount?.Kind ?? "radius";
                                _appConfig.CustomerUsername = user;
                                _appConfig.CustomerPassword = pass;
                                _appConfig.CustomerPackageName = primarySvc.PackageName ?? primarySvc.Name;
                                _appConfig.CustomerStatus = primarySvc.Status;

                                await PortalSyncService.SyncFromCustomerAppAsync(appClient, _appConfig);
                                _appConfig.Save();

                                OnSyncCompleted?.Invoke();
                                if (SaveCheck.IsChecked == true) OnSaveCredentials?.Invoke(user, pass);
                                handled = true;
                                return;
                            }
                        }
                        catch (Exception repEx)
                        {
                            ShowStatus($"خطا در جایگزینی دستگاه: {repEx.Message}", true);
                            return;
                        }
                    }
                }
                ShowStatus(devEx.Message, true);
                return;
            }
            catch (CustomerAppException cEx) when (cEx.StatusCode == 404 || cEx.ErrorCode == "NOT_FOUND")
            {
                // سرور مسیر /api/app ندارد؛ به پورتال قدیمی فال‌بک کن
                handled = false;
            }
            catch (CustomerAppException cEx)
            {
                if (cEx.StatusCode is 401 || cEx.ErrorCode == "UNAUTHORIZED")
                    ShowStatus(Localization.T(MsgBadCreds), true);
                else
                    ShowStatus(cEx.Message, true);
                return;
            }

            // ۲. در صورت عدم تطابق با API جدید، استفاده از کلاینت پورتال قدیمی (Fallback)
            if (!handled)
            {
                var client     = new PortalApiClient(_portalBaseUrl);
                var loginUser  = await client.LoginAsync(user, pass);
                var dashboard  = await client.GetDashboardAsync();

                Populate(dashboard, loginUser.CanConnect);
                StatusText.Visibility  = Visibility.Collapsed;
                ResultPanel.Visibility = Visibility.Visible;

                // ثبت مشخصات کاربر در کانفیگ برنامه
                _appConfig.CustomerUsername = user;
                _appConfig.CustomerPassword = pass;
                _appConfig.CustomerPackageName = dashboard.Package?.Name ?? "";
                _appConfig.CustomerStatus = dashboard.Account?.Status ?? "active";

                // دریافت و همگام‌سازی خودکار سرورها از پورتال
                await PortalSyncService.SyncFromPortalAsync(client, _appConfig, user, pass);
                _appConfig.Save();

                OnSyncCompleted?.Invoke();
                if (SaveCheck.IsChecked == true) OnSaveCredentials?.Invoke(user, pass);
                handled = true;
            }
        }
        catch (PortalApiException ex)
        {
            if (ex.StatusCode is 400 or 401 or 403 or 404) ShowStatus(Localization.T(MsgBadCreds), true);
            else ShowStatus(string.Format(Localization.T(MsgServerErrorFmt), ex.Message), true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SubscriptionDialog] Error: {ex}");
            var isNet = ex is HttpRequestException or TaskCanceledException;
            ShowStatus(isNet ? Localization.T(MsgNetworkError) : $"{Localization.T(MsgNetworkError)} ({ex.Message})", true);
        }
        finally
        {
            CheckBtn.IsEnabled = true;
            CheckBtn.Content   = Localization.T(BtnCheck);
        }
    }

    private void Populate(PortalDashboard d, bool canConnect)
    {
        bool notStarted = d.Account.ExpireAt == null && d.Account.FirstLoginAt == null;
        bool unlimitedDuration = d.Account.ExpireAt == null && d.Account.FirstLoginAt != null;

        var expired = string.Equals(d.Account.Status, "expired", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(d.Account.Status, "suspended", StringComparison.OrdinalIgnoreCase)
                      || (d.Account.ExpireAt.HasValue && d.Account.ExpireAt.Value.ToLocalTime() <= DateTime.Now)
                      || (!notStarted && !unlimitedDuration && (d.Account.RemainingDays ?? -1) == 0);

        // وضعیت اشتراک و تاریخ انقضا
        if (expired)
        {
            StatusValue.Text = Localization.T(StExpired);
            StatusValue.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            ExpireValue.Text = FormatExpire(d.Account.ExpireAt);
            ExpireValue.ToolTip = d.Account.ExpireAt?.ToString("yyyy-MM-dd") ?? "";
            RemainValue.Text = Localization.T(StExpired);
        }
        else if (notStarted)
        {
            StatusValue.Text = "شروع پس از اتصال ⏳";
            StatusValue.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)); // Sky Blue
            ExpireValue.Text = "پس از اولین اتصال";
            ExpireValue.ToolTip = "مدت اشتراک از لحظه اولین اتصال موفق آغاز می‌گردد.";
            RemainValue.Text = "شروع نشده ⏳";
        }
        else if (unlimitedDuration)
        {
            StatusValue.Text = Localization.T(StActive);
            StatusValue.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            ExpireValue.Text = "بدون تاریخ انقضا (نامحدود)";
            ExpireValue.ToolTip = "";
            RemainValue.Text = "نامحدود";
        }
        else
        {
            StatusValue.Text = Localization.T(StActive);
            StatusValue.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            ExpireValue.Text = FormatExpire(d.Account.ExpireAt);
            ExpireValue.ToolTip = d.Account.ExpireAt?.ToString("yyyy-MM-dd") ?? "";
            RemainValue.Text = FormatRemaining(d.Account.ExpireAt, false);
        }

        // سرویس / پکیج
        GroupValue.Text = !string.IsNullOrWhiteSpace(d.Package?.Name) ? d.Package.Name : "—";

        // وضعیت اتصال
        OnlineValue.Text = canConnect ? Localization.T(OnlineText) : Localization.T(OfflineText);
        OnlineValue.Foreground = new SolidColorBrush(
            canConnect ? Color.FromRgb(0x22, 0xC5, 0x5E) : Color.FromRgb(0x9C, 0xA3, 0xAF));

        // اطلاعات اکانت
        UsernameValue.Text = !string.IsNullOrWhiteSpace(d.Account?.Username) ? d.Account.Username : "—";
        FirstLoginValue.Text = d.Account?.FirstLoginAt.HasValue == true
            ? FormatExpire(d.Account.FirstLoginAt)
            : "ثبت نشده (استارت نخورده)";

        // ترافیک
        var usedGb   = (d.Traffic?.UsedMb ?? 0)      / 1024.0;
        var totalGb  = (d.Traffic?.TotalMb ?? 0)     / 1024.0;
        var remainGb = (d.Traffic?.RemainingMb ?? 0) / 1024.0;

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
            RemainTrafficText.Text  = "نامحدود";
        }

        _appConfig.CustomerPackageName = !string.IsNullOrWhiteSpace(d.Package?.Name) ? d.Package!.Name : "—";
        _appConfig.CustomerRemainingTime = RemainValue.Text;
        _appConfig.CustomerRemainingTraffic = totalGb > 0 ? $"{remainGb:0.#} " + Localization.T("گیگ") : Localization.T("نامحدود");
        _appConfig.CustomerStatus = StatusValue.Text;
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
        if (expireAt == null) return "نامحدود";
        var rem = expireAt.Value.ToLocalTime() - DateTime.Now;
        if (rem.TotalSeconds <= 0) return StExpired;
        var days  = (int)rem.TotalDays;
        var hours = rem.Hours;
        if (days == 0 && hours == 0) return "< 1H";
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

    private async void LogoutBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var appClient = new CustomerAppApiClient(_portalBaseUrl);
            await appClient.LogoutAsync();
        }
        catch { }

        _appConfig.CustomerAccessToken = null;
        _appConfig.CustomerRefreshToken = null;
        _appConfig.CustomerTokenExpiresAt = null;
        _appConfig.CustomerUsername = null;
        _appConfig.CustomerPassword = null;
        _appConfig.CustomerPackageName = null;
        _appConfig.CustomerRemainingTime = null;
        _appConfig.CustomerRemainingTraffic = null;
        _appConfig.CustomerStatus = null;
        // حذف کانکشن‌های پورتال هنگام خروج
        _appConfig.Connections.RemoveAll(c => string.Equals(c.Source, "portal", StringComparison.OrdinalIgnoreCase));
        _appConfig.Save();

        OnLoggedOut?.Invoke();
        Close();
    }
}