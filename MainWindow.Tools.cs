using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows.Input;
using SmartVpn.XrayCore;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        private async void FlushDns_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("> ipconfig /flushdns");
            await RunToolAsync("ipconfig", "/flushdns");
        }

        private async void RenewIp_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("> ipconfig /release");
            await RunToolAsync("ipconfig", "/release");
            AppendLog("> ipconfig /renew");
            await RunToolAsync("ipconfig", "/renew");
        }

        private void SpeedTest_Click(object sender, RoutedEventArgs e)
        {
            OpenSpeedTestView();
        }

        private const string MsgSubNotConfigured = "استعلام اشتراک در این نسخه پیکربندی نشده است (PortalApiUrl در config.json).";

        private ConnectionProfile? ResolveOfficialProfileForSubscription()
        {
            var official = _config.Connections
                .Where(c => c.IsOfficial
                    || string.Equals(c.Source, "official", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Source, "portal", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (official.Count == 0)
                return null;

            var activeName = _connectedName;
            if (string.IsNullOrWhiteSpace(activeName) && ActiveConnText is not null)
            {
                var activeText = ActiveConnText.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(activeText))
                {
                    foreach (var c in official)
                    {
                        if (activeText.EndsWith(c.Name, StringComparison.OrdinalIgnoreCase)
                            || activeText.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            activeName = c.Name;
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(activeName))
            {
                var byActive = official.FirstOrDefault(c =>
                    string.Equals(c.Name, activeName, StringComparison.OrdinalIgnoreCase)
                    || activeName.EndsWith(c.Name, StringComparison.OrdinalIgnoreCase)
                    || activeName.Contains(c.Name, StringComparison.OrdinalIgnoreCase));
                if (byActive is not null && byActive.Username.Length > 0)
                    return byActive;
            }

            return official.FirstOrDefault(c => c.Username.Length > 0)
                ?? official.FirstOrDefault();
        }

        private void SidebarAccountCard_Click(object sender, MouseButtonEventArgs e)
        {
            Subscription_Click(sender, e);
        }

        private void VpnSyncPanel_Click(object sender, RoutedEventArgs e)
        {
            Subscription_Click(sender, e);
        }

        private void SidebarTelegramBtn_Click(object sender, RoutedEventArgs e)
        {
            var url = !string.IsNullOrWhiteSpace(_config?.TelegramUrl) ? _config.TelegramUrl : AppConfig.DefaultProductionTelegramUrl;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private void SidebarGithubBtn_Click(object sender, RoutedEventArgs e)
        {
            var url = !string.IsNullOrWhiteSpace(_config?.GithubUrl) ? _config.GithubUrl : AppConfig.DefaultProductionGithubUrl;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        public void UpdateSidebarAccountCard()
        {
            if (SidebarVersionText != null)
            {
                SidebarVersionText.Text = "v" + AppConfig.BrandVersion;
            }

            var user = _config.CustomerUsername;
            bool loggedIn = !string.IsNullOrWhiteSpace(_config.CustomerRefreshToken) || !string.IsNullOrWhiteSpace(user);

            string hexColor = "#64748B";
            if (loggedIn)
            {
                bool notStarted = _config.CustomerRemainingTime?.Contains("شروع") == true;
                bool expired = string.Equals(_config.CustomerStatus, "expired", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(_config.CustomerStatus, "suspended", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(_config.CustomerStatus, "منقضی", StringComparison.OrdinalIgnoreCase);
                bool low = !expired && !notStarted && _config.CustomerRemainingTime != null &&
                           (_config.CustomerRemainingTime.Contains("ساعت") && !_config.CustomerRemainingTime.Contains("روز"));

                hexColor = notStarted ? "#38BDF8" : (expired ? "#EF4444" : (low ? "#F59E0B" : "#22C55E"));
            }

            var statusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));

            // به‌روزرسانی بج نشانگر وضعیت در نوار عنوان (TitleBar) - مثلا 🟢 s22
            if (TitleAccountBadge != null)
            {
                TitleAccountBadge.Visibility = Visibility.Visible;
                if (TitleStatusDot != null) TitleStatusDot.Fill = statusBrush;
                if (TitleAccountText != null)
                {
                    TitleAccountText.Text = loggedIn ? (!string.IsNullOrWhiteSpace(user) ? user : "User") : Localization.T("ورود به حساب");
                }
                TitleAccountBadge.ToolTip = loggedIn
                    ? $"{Localization.T("حساب کاربری")}: {user}\n{Localization.T("وضعیت")}: {_config.CustomerPackageName ?? Localization.T("فعال")}\n{Localization.T("زمان")}: {_config.CustomerRemainingTime ?? "—"}\n{Localization.T("حجم")}: {_config.CustomerRemainingTraffic ?? "—"}"
                    : Localization.T("برای ورود به پنل و دریافت خودکار سرورها کلیک کنید");
            }

            if (SidebarAccountTitle != null)
            {
                if (loggedIn)
                {
                    SidebarAccountBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)) { Opacity = 0.2 };
                    SidebarAccountIcon.Text = "👤";
                    SidebarAccountTitle.Text = !string.IsNullOrWhiteSpace(user) ? $"{Localization.T("کاربر")}: {user}" : Localization.T("کاربر متصل");
                    SidebarAccountSub.Text = !string.IsNullOrWhiteSpace(_config.CustomerPackageName) ? _config.CustomerPackageName : Localization.T("اشتراک فعال ✅");
                    SidebarAccountCard.ToolTip = Localization.T("مشاهده وضعیت حساب و اشتراک");
                }
                else
                {
                    SidebarAccountBadge.Background = (Brush)FindResource("CardHoverBrush");
                    SidebarAccountIcon.Text = "🔑";
                    SidebarAccountTitle.Text = Localization.T("ورود به حساب");
                    SidebarAccountSub.Text = Localization.T("دریافت سرورها از پنل");
                    SidebarAccountCard.ToolTip = Localization.T("ورود به حساب");
                }
            }
        }

        public void InitializeSubscriptionSummaryCardFromCache()
        {
            if (SubSummaryCard == null) return;
            UpdateSidebarAccountCard();

            bool hasSavedInfo = !string.IsNullOrWhiteSpace(_config.CustomerPackageName) ||
                                !string.IsNullOrWhiteSpace(_config.CustomerRemainingTime) ||
                                !string.IsNullOrWhiteSpace(_config.CustomerUsername);

            if (hasSavedInfo)
            {
                if (SubSectionLabel != null && !string.IsNullOrWhiteSpace(_config.CustomerPackageName))
                    SubSectionLabel.Text = _config.CustomerPackageName;
                if (!string.IsNullOrWhiteSpace(_config.CustomerRemainingTime))
                    SubTimeLeftText.Text = _config.CustomerRemainingTime;
                if (!string.IsNullOrWhiteSpace(_config.CustomerRemainingTraffic))
                    SubDataLeftText.Text = _config.CustomerRemainingTraffic;

                if (!string.IsNullOrWhiteSpace(_config.CustomerStatus))
                {
                    bool notStarted = _config.CustomerRemainingTime?.Contains("شروع") == true;
                    bool expired = string.Equals(_config.CustomerStatus, "expired", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(_config.CustomerStatus, "suspended", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(_config.CustomerStatus, "منقضی", StringComparison.OrdinalIgnoreCase);
                    string hex = notStarted ? "#38BDF8" : (expired ? "#EF4444" : "#22C55E");
                    string txt = notStarted ? Localization.T("شروع نشده ⏳") : (expired ? Localization.T("منقضی") : Localization.T("فعال"));
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    var brush = new SolidColorBrush(color);
                    SubActiveText.Text = txt;
                    SubActiveText.Foreground = brush;
                    SubActiveDot.Fill = brush;
                    SubActiveBadge.Background = new SolidColorBrush(color) { Opacity = 0.12 };
                }

                ShowSubCardFilled();
            }
            else
            {
                ShowSubCardEmpty();
            }
        }

        private void Subscription_Click(object sender, RoutedEventArgs e)
        {
            var url = _config.PortalApiUrl.Trim();
            if (url.Length == 0)
            {
                AskDialog.Info(this, MsgSubNotConfigured);
                return;
            }

            var prof = ResolveOfficialProfileForSubscription();
            var username = !string.IsNullOrWhiteSpace(_config.CustomerUsername)
                ? _config.CustomerUsername
                : (prof?.Username ?? "");
            var password = !string.IsNullOrWhiteSpace(_config.CustomerPassword)
                ? _config.CustomerPassword
                : (prof?.Password ?? "");

            var dlg = new SubscriptionDialog(url, username, password, _config) { Owner = this };
            dlg.OnSyncCompleted = () =>
            {
                // همگام‌سازی توکن‌ها و مشخصات کاربر از دیالوگ به کانفیگ اصلی پنجره
                _config.CustomerAccessToken = dlg.Config.CustomerAccessToken;
                _config.CustomerRefreshToken = dlg.Config.CustomerRefreshToken;
                _config.CustomerTokenExpiresAt = dlg.Config.CustomerTokenExpiresAt;
                _config.CustomerDeviceId = dlg.Config.CustomerDeviceId;
                _config.CustomerLoginMode = dlg.Config.CustomerLoginMode;
                _config.CustomerUsername = dlg.Config.CustomerUsername;
                _config.CustomerPassword = dlg.Config.CustomerPassword;
                _config.CustomerPackageName = dlg.Config.CustomerPackageName;
                _config.CustomerRemainingTime = dlg.Config.CustomerRemainingTime;
                _config.CustomerRemainingTraffic = dlg.Config.CustomerRemainingTraffic;
                _config.CustomerStatus = dlg.Config.CustomerStatus;
                _config.Save();

                Dispatcher.Invoke(() =>
                {
                    RefreshList();
                    UpdateSidebarAccountCard();
                    InitializeSubscriptionSummaryCardFromCache();
                    XrayLoadData();
                    _subAutoCheckDone = false;
                    _ = RefreshSubscriptionSummaryAsync();
                });
            };
            dlg.OnSaveCredentials = (u, p) =>
            {
                _config.CustomerUsername = u;
                _config.CustomerPassword = p;
                _config.CustomerAccessToken = dlg.Config.CustomerAccessToken;
                _config.CustomerRefreshToken = dlg.Config.CustomerRefreshToken;
                _config.CustomerTokenExpiresAt = dlg.Config.CustomerTokenExpiresAt;
                _config.CustomerDeviceId = dlg.Config.CustomerDeviceId;
                _config.CustomerLoginMode = dlg.Config.CustomerLoginMode;
                _config.CustomerPackageName = dlg.Config.CustomerPackageName;
                _config.CustomerRemainingTime = dlg.Config.CustomerRemainingTime;
                _config.CustomerRemainingTraffic = dlg.Config.CustomerRemainingTraffic;
                _config.CustomerStatus = dlg.Config.CustomerStatus;

                // اطلاعات جدید روی همه کانکشن‌های رسمی و پورتال ذخیره شود.
                foreach (var c in _config.Connections.Where(x =>
                    x.IsOfficial || string.Equals(x.Source, "official", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Source, "portal", StringComparison.OrdinalIgnoreCase)))
                {
                    c.Username = u;
                    c.Password = p;
                }

                _config.Save();
                RefreshList();
                UpdateSidebarAccountCard();
                InitializeSubscriptionSummaryCardFromCache();
                XrayLoadData();
                _subAutoCheckDone = false;
                _ = RefreshSubscriptionSummaryAsync();
            };
            dlg.OnLoggedOut = () =>
            {
                _config.CustomerAccessToken = null;
                _config.CustomerRefreshToken = null;
                _config.CustomerTokenExpiresAt = null;
                _config.CustomerDeviceId = null;
                _config.CustomerUsername = null;
                _config.CustomerPassword = null;
                _config.CustomerPackageName = null;
                _config.CustomerRemainingTime = null;
                _config.CustomerRemainingTraffic = null;
                _config.CustomerStatus = null;
                _config.Save();

                RefreshList();
                UpdateSidebarAccountCard();
                ShowSubCardEmpty();
                XrayLoadData();
            };
            dlg.ShowDialog();
        }

        private bool _subAutoCheckDone;
        private bool _subRefreshInFlight;
        private bool _hasSubscriptionSummary;

        private void TryAutoCheckSubscriptionSummaryOnce()
        {
            if (_subAutoCheckDone) return;
            var url = _config.PortalApiUrl.Trim();
            bool hasTokens = !string.IsNullOrWhiteSpace(_config.CustomerRefreshToken) || !string.IsNullOrWhiteSpace(_config.CustomerUsername);
            var hasCreds = hasTokens || _config.Connections.Any(c =>
                (c.IsOfficial
                 || string.Equals(c.Source, "official", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(c.Source, "portal", StringComparison.OrdinalIgnoreCase))
                && c.Username.Length > 0);
            if (url.Length == 0 || !hasCreds) return;
            _subAutoCheckDone = true;
            _ = RefreshSubscriptionSummaryAsync();
        }

        private void SubRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _ = RefreshSubscriptionSummaryAsync();
        }

        private void SubSummaryCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Subscription_Click(sender, new RoutedEventArgs());
        }

        private void UpdateSubActiveBadge(double? remainingSeconds, double? leftGb, double quotaGb, bool notStarted = false, bool isUnlimited = false)
        {
            string text; string hex;
            if (notStarted)
            {
                text = Localization.T("شروع نشده ⏳");
                hex = "#38BDF8";
            }
            else
            {
                var expired = (remainingSeconds != null && remainingSeconds <= 0) || (leftGb != null && quotaGb > 0 && leftGb <= 0);
                var low = !expired && ((remainingSeconds != null && remainingSeconds <= 86400) ||
                                        (leftGb != null && quotaGb > 0 && leftGb <= quotaGb * 0.1));
                if (expired) { text = Localization.T("منقضی"); hex = "#EF4444"; }
                else if (low) { text = Localization.T("رو به اتمام"); hex = "#F59E0B"; }
                else { text = Localization.T("فعال"); hex = "#22C55E"; }
            }

            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            SubActiveText.Text = text;
            SubActiveText.Foreground = brush;
            SubActiveDot.Fill = brush;
            SubActiveBadge.Background = new SolidColorBrush(color) { Opacity = 0.12 };
        }

        private void UpdateSubTrafficProgressBar(double usedMb, double totalMb)
        {
            if (SubTrafficBar == null) return;
            if (totalMb <= 0)
            {
                SubTrafficBar.Visibility = Visibility.Collapsed;
                return;
            }
            SubTrafficBar.Visibility = Visibility.Visible;
            var pct = Math.Clamp((usedMb / totalMb) * 100.0, 0, 100);
            SubTrafficBar.Value = pct;
        }

        private void ShowSubCardEmpty()
        {
            if (_hasSubscriptionSummary) return;
            SubActiveBadge.Visibility = Visibility.Collapsed;
            SubRefreshBtn.Visibility = Visibility.Collapsed;
            SubDetailPanel.Visibility = Visibility.Collapsed;
            if (SubTrafficBar != null) SubTrafficBar.Visibility = Visibility.Collapsed;
            SubEmptyText.Visibility = Visibility.Visible;
            if (SubSectionLabel != null) SubSectionLabel.Text = Localization.T("💳 ورود به حساب / اشتراک");
        }

        private void ShowSubCardFilled()
        {
            _hasSubscriptionSummary = true;
            SubEmptyText.Visibility = Visibility.Collapsed;
            SubActiveBadge.Visibility = Visibility.Visible;
            SubRefreshBtn.Visibility = Visibility.Visible;
            SubDetailPanel.Visibility = Visibility.Visible;
            if (SubSectionLabel != null && !string.IsNullOrWhiteSpace(_config.CustomerPackageName))
            {
                SubSectionLabel.Text = _config.CustomerPackageName;
            }
        }

        private async Task RefreshSubscriptionSummaryAsync()
        {
            if (_subRefreshInFlight) return;
            var url = _config.PortalApiUrl.Trim();

            var prof = ResolveOfficialProfileForSubscription();
            var username = !string.IsNullOrWhiteSpace(_config.CustomerUsername)
                ? _config.CustomerUsername
                : (prof?.Username ?? "");
            var password = !string.IsNullOrWhiteSpace(_config.CustomerPassword)
                ? _config.CustomerPassword
                : (prof?.Password ?? "");

            bool hasValidAuth = !string.IsNullOrWhiteSpace(_config.CustomerRefreshToken) || !string.IsNullOrWhiteSpace(username);

            if (url.Length == 0 || !hasValidAuth)
            {
                ShowSubCardEmpty();
                return;
            }

            // نمایش سریع اطلاعات کش‌شده
            if (!string.IsNullOrWhiteSpace(_config.CustomerRemainingTime) || !string.IsNullOrWhiteSpace(_config.CustomerRemainingTraffic))
            {
                if (!string.IsNullOrWhiteSpace(_config.CustomerRemainingTime))
                    SubTimeLeftText.Text = _config.CustomerRemainingTime;
                if (!string.IsNullOrWhiteSpace(_config.CustomerRemainingTraffic))
                    SubDataLeftText.Text = _config.CustomerRemainingTraffic;
                if (SubSectionLabel != null && !string.IsNullOrWhiteSpace(_config.CustomerPackageName))
                    SubSectionLabel.Text = _config.CustomerPackageName;
                ShowSubCardFilled();
            }

            _subRefreshInFlight = true;
            StartSubRefreshSpin();
            try
            {
                PortalDashboard? dashboard = null;
                var appClient = new CustomerAppApiClient(url)
                {
                    RefreshToken = _config.CustomerRefreshToken,
                    AccessToken = _config.CustomerAccessToken,
                    TokenExpiresAt = _config.CustomerTokenExpiresAt
                };

                // ۱. تلاش با توکن‌های ذخیره‌شده یا یوزر/پسوردهای اپ مشتری
                try
                {
                    if (!string.IsNullOrWhiteSpace(appClient.RefreshToken) || !string.IsNullOrWhiteSpace(appClient.AccessToken))
                    {
                        var services = await appClient.GetServicesAsync();
                        var svc = services.FirstOrDefault();
                        if (svc != null)
                        {
                            dashboard = CustomerAppApiClient.ConvertToPortalDashboard(svc);
                            _config.CustomerPackageName = svc.PackageName ?? svc.Name;
                            _config.CustomerStatus = svc.Status;
                            _config.CustomerAccessToken = appClient.AccessToken;
                            _config.CustomerRefreshToken = appClient.RefreshToken;
                            _config.CustomerTokenExpiresAt = appClient.TokenExpiresAt;
                        }
                    }
                }
                catch { }

                if (dashboard == null && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    try
                    {
                        if (username.StartsWith("09") || username.Contains("@"))
                            await appClient.LoginCustomerAsync(username, password);
                        else
                            await appClient.LoginRadiusAsync(username, password);

                        var services = await appClient.GetServicesAsync();
                        var svc = services.FirstOrDefault();
                        if (svc != null)
                        {
                            dashboard = CustomerAppApiClient.ConvertToPortalDashboard(svc);
                            _config.CustomerPackageName = svc.PackageName ?? svc.Name;
                            _config.CustomerStatus = svc.Status;
                            _config.CustomerAccessToken = appClient.AccessToken;
                            _config.CustomerRefreshToken = appClient.RefreshToken;
                            _config.CustomerTokenExpiresAt = appClient.TokenExpiresAt;
                        }
                    }
                    catch { }
                }

                // ۲. در صورت ناموفق بودن، تلاش با کلاینت پورتال قدیمی
                if (dashboard == null && !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    var client = new PortalApiClient(url);
                    await client.LoginAsync(username, password);
                    dashboard = await client.GetDashboardAsync();
                }

                if (dashboard == null)
                {
                    if (!_hasSubscriptionSummary) ShowSubCardEmpty();
                    return;
                }

                bool notStarted = dashboard.Account.ExpireAt == null && dashboard.Account.FirstLoginAt == null;
                bool isUnlimitedTime = dashboard.Account.ExpireAt == null && dashboard.Account.FirstLoginAt != null;

                var expired = string.Equals(dashboard.Account.Status, "expired", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(dashboard.Account.Status, "suspended", StringComparison.OrdinalIgnoreCase)
                              || (dashboard.Account.ExpireAt.HasValue && dashboard.Account.ExpireAt.Value.ToLocalTime() <= DateTime.Now)
                              || (!notStarted && !isUnlimitedTime && dashboard.Account.RemainingDays == 0);

                double? remaining = null;
                if (expired) remaining = 0;
                else if (dashboard.Account.ExpireAt.HasValue)
                    remaining = Math.Max(0, (dashboard.Account.ExpireAt.Value.ToLocalTime() - DateTime.Now).TotalSeconds);
                else if (!notStarted && !isUnlimitedTime && dashboard.Account.RemainingDays > 0)
                    remaining = dashboard.Account.RemainingDays * 86400.0;

                var quotaGb = dashboard.Traffic.TotalMb / 1024.0;
                double? leftGb = quotaGb > 0 ? dashboard.Traffic.RemainingMb / 1024.0 : (double?)null;
                var checkedAt = DateTime.Now;

                SubTimeLeftText.Text = notStarted
                    ? Localization.T("شروع پس از اتصال ⏳")
                    : (isUnlimitedTime ? Localization.T("نامحدود") : FormatRemainingShort(remaining));
                SubDataLeftText.Text = leftGb != null ? leftGb.Value.ToString("0.##", CultureInfo.InvariantCulture) + " " + Localization.T("گیگ") : Localization.T("نامحدود");
                SubUpdatedText.Text = Localization.T("آخرین به‌روزرسانی: ") + checkedAt.ToString("HH:mm", CultureInfo.InvariantCulture);

                _config.CustomerRemainingTime = SubTimeLeftText.Text;
                _config.CustomerRemainingTraffic = SubDataLeftText.Text;
                if (!string.IsNullOrWhiteSpace(dashboard.Package.Name))
                    _config.CustomerPackageName = dashboard.Package.Name;
                _config.Save();

                if (SubSectionLabel != null && !string.IsNullOrWhiteSpace(_config.CustomerPackageName))
                    SubSectionLabel.Text = _config.CustomerPackageName;

                UpdateSubActiveBadge(remaining, leftGb, quotaGb, notStarted, isUnlimitedTime);
                UpdateSubTrafficProgressBar(dashboard.Traffic.UsedMb, dashboard.Traffic.TotalMb);
                ShowSubCardFilled();
                UpdateSidebarAccountCard();
            }
            catch
            {
                // در صورت خطا، متد ShowSubCardEmpty اجرا نمی‌شود تا کارت ریست نشود
            }
            finally
            {
                _subRefreshInFlight = false;
                StopSubRefreshSpin();
            }
        }

        private static string FormatRemainingShort(double? seconds)
        {
            if (seconds == null) return Localization.T("نامحدود");
            if (seconds <= 0) return Localization.T("منقضی شده ❌");
            var t = TimeSpan.FromSeconds(seconds.Value);
            var days = (int)t.TotalDays;
            var hours = t.Hours;
            return days > 0 ? Localization.T("{0} روز و {1} ساعت").Replace("{0}", days.ToString()).Replace("{1}", hours.ToString()) : Math.Max(1, hours) + " " + Localization.T("ساعت");
        }

        private void StartSubRefreshSpin()
        {
            var sb = (Storyboard)FindResource("SubRefreshSpinStoryboard");
            sb.Begin(this, true);
            SubRefreshBtn.IsEnabled = false;
        }

        private void StopSubRefreshSpin()
        {
            var sb = (Storyboard)FindResource("SubRefreshSpinStoryboard");
            sb.Stop(this);
            SubRefreshRotate.Angle = 0;
            SubRefreshBtn.IsEnabled = true;
        }

        public static async Task TerminateAllVpnProcessesAndResetAdaptersAsync()
        {
            var procNames = new[] { "openvpn", "wireguard", "amneziawg", "sing-box" };
            foreach (var name in procNames)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                key?.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }
            catch { }

            try { await DnsManager.RevertAsync(); } catch { }
            try
            {
                using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { CreateNoWindow = true, UseShellExecute = false });
                await (p?.WaitForExitAsync() ?? Task.CompletedTask);
            }
            catch { }
        }

        private async void KillProc_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.IsRunning || _xrayIsConnected || XrayEngine.IsRunning || _currentPowerState is "connected" or "connecting" or "reconnecting")
            {
                AppendLog(Localization.T("در زمان اتصال فعال، امکان بستن پروسس‌ها وجود ندارد"));
                Notify(Localization.T("در زمان اتصال فعال، امکان بستن پروسس‌ها وجود ندارد"));
                return;
            }

            AppendLog("Terminating all VPN and Core processes (openvpn, wireguard, amneziawg, sing-box)...");
            await TerminateAllVpnProcessesAndResetAdaptersAsync();
            AppendLog("✓ All VPN processes terminated and proxy disabled.");
            Notify(Localization.T("تمام پروسس‌های VPN متوقف و تنظیمات بازنشانی شدند."));
        }

        private void ProxyOff_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                key?.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                AppendLog("System proxy disabled via registry.");
            }
            catch (Exception ex) { AppendLog("Error disabling proxy: " + ex.Message); }
        }

        private async void DnsLeakTest_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("--- DNS Leak Test ---");
            try
            {
                var json = await _http.GetStringAsync("http://ip-api.com/json/?fields=status,query,country,countryCode,isp");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.GetProperty("status").GetString() == "success")
                {
                    var dns = root.GetProperty("query").GetString() ?? "";
                    var cc = root.GetProperty("countryCode").GetString() ?? "";
                    var country = root.GetProperty("country").GetString() ?? "";
                    var isp = root.GetProperty("isp").GetString() ?? "";
                    AppendLog("DNS " + dns + " -> " + Flag(cc) + " " + country + " (" + isp + ")");
                }
            }
            catch (Exception ex) { AppendLog("DNS leak test error: " + ex.Message); }
        }

        private async void ResetAdapter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(IsPhysicalNic);
                if (nic == null) { AppendLog("No active network adapter found."); return; }
                _nicBase = nic.Name;
                AppendLog("> netsh interface set interface \"" + _nicBase + "\" admin=disable");
                await RunToolAsync("netsh", "interface set interface \"" + _nicBase + "\" admin=disable");
                await Task.Delay(2000);
                AppendLog("> netsh interface set interface \"" + _nicBase + "\" admin=enable");
                await RunToolAsync("netsh", "interface set interface \"" + _nicBase + "\" admin=enable");
            }
            catch (Exception ex) { AppendLog("Error resetting adapter: " + ex.Message); }
        }

        private async Task RunToolAsync(string exe, string args)
        {
            try
            {
                using var proc = StartStreamingProc(exe, args);
                if (proc == null) return;
                await proc.WaitForExitAsync();
            }
            catch (Exception ex) { AppendLog(exe + " failed: " + ex.Message); }
        }

        private Process? StartStreamingProc(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var proc = Process.Start(psi);
            if (proc == null) return null;
            proc.OutputDataReceived += (_, ev) => { if (!string.IsNullOrWhiteSpace(ev.Data)) AppendLog(ev.Data!); };
            proc.ErrorDataReceived += (_, ev) => { if (!string.IsNullOrWhiteSpace(ev.Data)) AppendLog(ev.Data!); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            return proc;
        }

        private string? ResolveToolTarget()
        {
            var t = ToolTargetBox.Text.Trim();
            if (t.Length > 0) return t;
            var host = ServerIpText.Text;
            if (string.IsNullOrWhiteSpace(host) || host == "—")
            {
                AppendLog("Enter a target in the box above or connect to a VPN first.");
                return null;
            }
            return host;
        }

        private async void PingToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_pingProc is { HasExited: false })
            {
                try { _pingProc.Kill(true); } catch { }
                AppendLog("Ping stopped.");
                return;
            }

            var target = ResolveToolTarget();
            if (target == null) return;

            PingToggleBtn.Content = Localization.T("⏹ توقف پینگ");
            AppendLog("> ping -t " + target);
            try
            {
                _pingProc = StartStreamingProc("ping", "-t " + target);
                if (_pingProc != null) await _pingProc.WaitForExitAsync();
            }
            catch (Exception ex) { AppendLog("ping failed: " + ex.Message); }
            finally
            {
                _pingProc?.Dispose();
                _pingProc = null;
                PingToggleBtn.Content = Localization.T("🏓 شروع پینگ");
            }
        }

        private async void TracerouteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_tracertProc is { HasExited: false })
            {
                try { _tracertProc.Kill(true); } catch { }
                AppendLog("Traceroute stopped.");
                return;
            }

            var target = ResolveToolTarget();
            if (target == null) return;

            TracerouteToggleBtn.Content = Localization.T("⏹ توقف Traceroute");
            AppendLog("> tracert -h 20 -w 800 " + target);
            try
            {
                _tracertProc = StartStreamingProc("tracert", "-h 20 -w 800 " + target);
                if (_tracertProc != null) await _tracertProc.WaitForExitAsync();
            }
            catch (Exception ex) { AppendLog("tracert failed: " + ex.Message); }
            finally
            {
                _tracertProc?.Dispose();
                _tracertProc = null;
                TracerouteToggleBtn.Content = Localization.T("🛰 شروع Traceroute");
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox?.Clear();
            ConnLogBox?.Clear();
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = LogBox?.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                    Notify(Localization.T("لاگ در کلیپ‌بورد کپی شد"));
                }
            }
            catch { }
        }

        private void OpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logFile = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "app.log");
                if (System.IO.File.Exists(logFile))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logFile) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private void CopyConnLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = ConnLogBox?.Text;
                if (!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                    Notify(Localization.T("لاگ کانکشن‌ها در کلیپ‌بورد کپی شد"));
                }
            }
            catch { }
        }
    }
}