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
            bool isVpnConnected = _engine.IsRunning;
            bool isXrayConnected = _xrayIsConnected && SmartVpn.XrayCore.XrayEngine.IsRunning;
            bool isConnected = isVpnConnected || isXrayConnected;
            string connName = isXrayConnected
                ? (!string.IsNullOrWhiteSpace(_xrayActiveProfile?.Alias) ? _xrayActiveProfile.Alias : (!string.IsNullOrWhiteSpace(XrayTxtActiveName?.Text) ? XrayTxtActiveName.Text : "Xray Service"))
                : ActiveConnText.Text;

            var dlg = new SpeedTestDialog(isConnected, connName, isXrayConnected) { Owner = this };
            dlg.ShowDialog();
        }

        private const string MsgSubNotConfigured = "استعلام اشتراک در این نسخه پیکربندی نشده است (PortalApiUrl در config.json).";

        private ConnectionProfile? ResolveOfficialProfileForSubscription()
        {
            var official = _config.Connections
                .Where(c => c.IsOfficial || string.Equals(c.Source, "official", StringComparison.OrdinalIgnoreCase))
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

        private void Subscription_Click(object sender, RoutedEventArgs e)
        {
            var url = _config.PortalApiUrl.Trim();
            if (url.Length == 0)
            {
                AskDialog.Info(this, MsgSubNotConfigured);
                return;
            }

            var prof = ResolveOfficialProfileForSubscription();

            var dlg = new SubscriptionDialog(url, prof?.Username ?? "", prof?.Password ?? "") { Owner = this };
            dlg.OnSaveCredentials = (u, p) =>
            {
                // اطلاعات جدید روی همه کانکشن‌های رسمی ذخیره شود.
                foreach (var c in _config.Connections.Where(x =>
                    x.IsOfficial || string.Equals(x.Source, "official", StringComparison.OrdinalIgnoreCase)))
                {
                    c.Username = u;
                    c.Password = p;
                }

                _config.Save();
                RefreshList();
                _subAutoCheckDone = false;
                _ = RefreshSubscriptionSummaryAsync();
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
            var hasCreds = _config.Connections.Any(c => (c.IsOfficial || string.Equals(c.Source, "official", StringComparison.OrdinalIgnoreCase)) && c.Username.Length > 0);
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

        private void UpdateSubActiveBadge(double? remainingSeconds, double? leftGb, double quotaGb)
        {
            string text; string hex;
            var expired = (remainingSeconds != null && remainingSeconds <= 0) || (leftGb != null && leftGb <= 0);
            var low = !expired && ((remainingSeconds != null && remainingSeconds <= 86400) ||
                                    (leftGb != null && quotaGb > 0 && leftGb <= quotaGb * 0.1));
            if (expired) { text = Localization.T("منقضی"); hex = "#EF4444"; }
            else if (low) { text = Localization.T("رو به اتمام"); hex = "#F59E0B"; }
            else { text = Localization.T("فعال"); hex = "#22C55E"; }

            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            SubActiveText.Text = text;
            SubActiveText.Foreground = brush;
            SubActiveDot.Fill = brush;
            SubActiveBadge.Background = new SolidColorBrush(color) { Opacity = 0.12 };
        }

        private void ShowSubCardEmpty()
        {
            if (_hasSubscriptionSummary) return;
            SubActiveBadge.Visibility = Visibility.Collapsed;
            SubRefreshBtn.Visibility = Visibility.Collapsed;
            SubDetailPanel.Visibility = Visibility.Collapsed;
            SubEmptyText.Visibility = Visibility.Visible;
        }

        private void ShowSubCardFilled()
        {
            _hasSubscriptionSummary = true;
            SubEmptyText.Visibility = Visibility.Collapsed;
            SubActiveBadge.Visibility = Visibility.Visible;
            SubRefreshBtn.Visibility = Visibility.Visible;
            SubDetailPanel.Visibility = Visibility.Visible;
        }

        private async Task RefreshSubscriptionSummaryAsync()
        {
            if (_subRefreshInFlight) return;
            var url = _config.PortalApiUrl.Trim();

            var prof = ResolveOfficialProfileForSubscription();

            if (url.Length == 0 || prof == null || prof.Username.Length == 0)
            {
                ShowSubCardEmpty();
                return;
            }

            _subRefreshInFlight = true;
            StartSubRefreshSpin();
            try
            {
                var client = new PortalApiClient(url);
                await client.LoginAsync(prof.Username, prof.Password);
                var dashboard = await client.GetDashboardAsync();

                var expired = string.Equals(dashboard.Account.Status, "expired", StringComparison.OrdinalIgnoreCase)
                              || dashboard.Account.RemainingDays <= 0;
                double? remaining;
                if (expired) remaining = 0;
                else if (dashboard.Account.ExpireAt.HasValue)
                    remaining = Math.Max(0, (dashboard.Account.ExpireAt.Value.ToLocalTime() - DateTime.Now).TotalSeconds);
                else
                    remaining = dashboard.Account.RemainingDays * 86400.0;
                var quotaGb = dashboard.Traffic.TotalMb / 1024.0;
                double? leftGb = quotaGb > 0 ? dashboard.Traffic.RemainingMb / 1024.0 : (double?)null;
                var checkedAt = DateTime.Now;

                SubTimeLeftText.Text = FormatRemainingShort(remaining);
                SubDataLeftText.Text = leftGb != null ? leftGb.Value.ToString("0.##", CultureInfo.InvariantCulture) + " " + Localization.T("گیگ") : "—";
                SubUpdatedText.Text = Localization.T("آخرین به‌روزرسانی: ") + checkedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
                UpdateSubActiveBadge(remaining, leftGb, quotaGb);
                ShowSubCardFilled();
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
            if (seconds == null) return "—";
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

        private async void KillProc_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("> taskkill /F /IM openvpn.exe");
            await RunToolAsync("taskkill", "/F /IM openvpn.exe");
        }

        private void ProxyOff_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                key?.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                AppendLog("پراکسی سیستم از طریق رجیستری غیرفعال شد.");
            }
            catch (Exception ex) { AppendLog("خطا در غیرفعال‌کردن پراکسی: " + ex.Message); }
        }

        private async void DnsLeakTest_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("--- تست نشتی DNS ---");
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
            catch (Exception ex) { AppendLog("خطا در تست نشتی DNS: " + ex.Message); }
        }

        private async void ResetAdapter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(IsPhysicalNic);
                if (nic == null) { AppendLog("کارت شبکه فعال پیدا نشد."); return; }
                _nicBase = nic.Name;
                AppendLog("> netsh interface set interface \"" + _nicBase + "\" admin=disable");
                await RunToolAsync("netsh", "interface set interface \"" + _nicBase + "\" admin=disable");
                await Task.Delay(2000);
                AppendLog("> netsh interface set interface \"" + _nicBase + "\" admin=enable");
                await RunToolAsync("netsh", "interface set interface \"" + _nicBase + "\" admin=enable");
            }
            catch (Exception ex) { AppendLog("خطا در ریست آداپتور: " + ex.Message); }
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
                AppendLog("هدف را در کادر بالا وارد کنید یا ابتدا به یک کانکشن وصل شوید.");
                return null;
            }
            return host;
        }

        private async void PingToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_pingProc is { HasExited: false })
            {
                try { _pingProc.Kill(true); } catch { }
                AppendLog("پینگ متوقف شد.");
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
                AppendLog("Traceroute متوقف شد.");
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