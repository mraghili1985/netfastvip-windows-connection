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
        // ================= جعبه‌ابزار (کلیک یک‌باره) =================
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

        // ================= تست سرعت (پاپ‌آپ) =================
        private void SpeedTest_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SpeedTestDialog(_engine.IsRunning, ActiveConnText.Text) { Owner = this };
            dlg.ShowDialog();
        }

        // ================= وضعیت اشتراک (پاپ‌آپ) =================
        private const string MsgSubNotConfigured = "استعلام اشتراک در این نسخه پیکربندی نشده است (PortalApiUrl در config.json).";

        private void Subscription_Click(object sender, RoutedEventArgs e)
        {
            var url = _config.PortalApiUrl.Trim();
            if (url.Length == 0)
            {
                AskDialog.Info(this, MsgSubNotConfigured);
                return;
            }

            // پیش‌فرض: یوزر/پس کانکشن فعال؛ وگرنه اولین پروفایلی که نام کاربری دارد
            var prof = _config.Connections.FirstOrDefault(c => c.Name == ActiveConnText.Text && c.IsOfficial && c.Username.Length > 0)
                    ?? _config.Connections.FirstOrDefault(c => c.IsOfficial && c.Username.Length > 0);

            var dlg = new SubscriptionDialog(url, prof?.Username ?? "", prof?.Password ?? "") { Owner = this };
            // تیک «ذخیره» در دیالوگ اشتراک: یوزر/پسِ تأییدشده روی همه کانکشن‌های رسمی ست می‌شود
            dlg.OnSaveCredentials = (u, p) =>
            {
                foreach (var c in _config.Connections.Where(x => x.Source == "official"))
                {
                    c.Username = u; c.Password = p;
                }
                _config.Save();
                RefreshList();
                TryAutoCheckSubscriptionSummaryOnce();
            };

            // سینک کانکشن‌ها از پورتال بعد از هر لاگین موفق (بدون توجه به تیک ذخیره)
            dlg.OnSyncConnections = async (portalClient, u, p) =>
            {
                var result = await PortalSyncService.SyncFromPortalAsync(portalClient, _config, u, p);
                _config.Save();
                Dispatcher.Invoke(() =>
                {
                    RefreshList();

                });
            };
            dlg.ShowDialog();
        }

        // ================= کارت خلاصه اشتراک (پایین صفحه) =================
        private bool _subAutoCheckDone;
        private bool _subRefreshInFlight;

        // استعلام خودکار فقط یک‌بار در هر سشن — اولین باری که یوزر/پس معتبری وجود داشته باشد (استارتاپ یا بعد از ذخیرهٔ جدید) — بعدازش فقط با کلیک روی دکمهٔ رفرش انجام می‌شود
        private void TryAutoCheckSubscriptionSummaryOnce()
        {
            if (_subAutoCheckDone) return;
            var url = _config.PortalApiUrl.Trim();
            var hasCreds = _config.Connections.Any(c => c.IsOfficial && c.Username.Length > 0);
            if (url.Length == 0 || !hasCreds) return;
            _subAutoCheckDone = true;
            _ = RefreshSubscriptionSummaryAsync();
        }

        private void SubRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            _ = RefreshSubscriptionSummaryAsync();
        }

        // کلیک روی بدنهٔ کارت = همان پاپ‌آپ زندهٔ موجود، با استعلام تازه
        private void SubSummaryCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Subscription_Click(sender, new RoutedEventArgs());
        }

        // باز/بسته‌کردن جزئیات کارت خلاصه — مثل کارت اینفو، کلیک روی فلش صفحه رو بزرگ نمی‌کنه و پاپ‌آپ رو باز نمی‌کنه
        // بجژ رنگی وضعیت کنار عنوان کارت: سبز = فعال، نارنجی = رو به اتمام (کمتر از ۱ روز یا کمتر از ۱۰٪ حجم)، قرمز = منقضی
        private void UpdateSubActiveBadge(double? remainingSeconds, double? leftGb, double quotaGb)
        {
            string text; string hex;
            var expired = (remainingSeconds != null && remainingSeconds <= 0) || (leftGb != null && leftGb <= 0);
            var low = !expired && ((remainingSeconds != null && remainingSeconds <= 86400) ||
                                    (leftGb != null && quotaGb > 0 && leftGb <= quotaGb * 0.1));
            if (expired) { text = "منقضی"; hex = "#EF4444"; }
            else if (low) { text = "رو به اتمام"; hex = "#F59E0B"; }
            else { text = "فعال"; hex = "#22C55E"; }

            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            SubActiveText.Text = text;
            SubActiveText.Foreground = brush;
            SubActiveDot.Fill = brush;
            SubActiveBadge.Background = new SolidColorBrush(color) { Opacity = 0.12 };
        }

        // نمایش حالت خالی کارت اشتراک: قبل از ست‌شدن یوزر/پس رسمی یا وقتی استعلام امکان‌پذیر نیست
        private void ShowSubCardEmpty()
        {
            SubActiveBadge.Visibility = Visibility.Collapsed;
            SubRefreshBtn.Visibility = Visibility.Collapsed;
            SubDetailPanel.Visibility = Visibility.Collapsed;
            SubEmptyText.Visibility = Visibility.Visible;
        }

        // نمایش حالت پر کارت اشتراک: بعد از استعلام موفق با یوزر/پس یک کانکشن رسمی
        private void ShowSubCardFilled()
        {
            SubEmptyText.Visibility = Visibility.Collapsed;
            SubActiveBadge.Visibility = Visibility.Visible;
            SubRefreshBtn.Visibility = Visibility.Visible;
            SubDetailPanel.Visibility = Visibility.Visible;
        }

        private async Task RefreshSubscriptionSummaryAsync()
        {
            if (_subRefreshInFlight) return;
            var url = _config.PortalApiUrl.Trim();
            var prof = _config.Connections.FirstOrDefault(c => c.Name == ActiveConnText.Text && c.IsOfficial && c.Username.Length > 0)
                    ?? _config.Connections.FirstOrDefault(c => c.IsOfficial && c.Username.Length > 0);
            if (url.Length == 0 || prof == null)
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
                SubDataLeftText.Text = leftGb != null ? leftGb.Value.ToString("0.##", CultureInfo.InvariantCulture) + " گیگ" : "—";
                SubUpdatedText.Text = "آخرین به‌روزرسانی: " + checkedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
                UpdateSubActiveBadge(remaining, leftGb, quotaGb);
                ShowSubCardFilled();
            }
            catch
            {
                // خطای شبکه/احراز هویت — بی‌صدا نادیده می‌گیریم، مقدار قبلی روی کارت (اگر بود) دست‌نخورده باقی می‌ماند
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
            if (seconds <= 0) return "منقضی شده ❌";
            var t = TimeSpan.FromSeconds(seconds.Value);
            var days = (int)t.TotalDays;
            var hours = t.Hours;
            return days > 0 ? $"{days} روز و {hours} ساعت" : $"{Math.Max(1, hours)} ساعت";
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

        // اجرای پروسه با خروجی زنده — هر خط همان لحظه در گزارش ثبت می‌شود، نه بعد از پایان پروسه
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

        // ================= پینگ / Traceroute با هدف دلخواه و Start/Stop =================
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

            PingToggleBtn.Content = "⏹ توقف پینگ";
            AppendLog("> ping -t " + target);
            try
            {
                // پینگ پیوسته با خروجی زنده — هر جواب همان لحظه دیده می‌شود؛ تا زدن «توقف» ادامه دارد
                _pingProc = StartStreamingProc("ping", "-t " + target);
                if (_pingProc != null) await _pingProc.WaitForExitAsync();
            }
            catch (Exception ex) { AppendLog("ping failed: " + ex.Message); }
            finally
            {
                _pingProc?.Dispose();
                _pingProc = null;
                PingToggleBtn.Content = "🏓 شروع پینگ";
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

            TracerouteToggleBtn.Content = "⏹ توقف Traceroute";
            AppendLog("> tracert -h 20 -w 800 " + target);
            try
            {
                // خروجی زنده — هر هاپ همان لحظه در گزارش دیده می‌شود
                _tracertProc = StartStreamingProc("tracert", "-h 20 -w 800 " + target);
                if (_tracertProc != null) await _tracertProc.WaitForExitAsync();
            }
            catch (Exception ex) { AppendLog("tracert failed: " + ex.Message); }
            finally
            {
                _tracertProc?.Dispose();
                _tracertProc = null;
                TracerouteToggleBtn.Content = "🛰 شروع Traceroute";
            }
        }

        // ================= متفرقه =================
        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Clear();
        }


        // ================= WiFi Hotspot =================
        private readonly HotspotService _hotspot = new();
        private System.Windows.Threading.DispatcherTimer? _hotspotStatusTimer;

        private async void HotspotToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_hotspot.IsRunning)
            {
                HotspotToggleBtn.IsEnabled = false;
                await _hotspot.StopAsync();
                _hotspotStatusTimer?.Stop();
                SetHotspotUiOff();
                return;
            }

            var ssid = HotspotSsidBox.Text.Trim();
            var pass = HotspotPassBox.Text.Trim();
            if (ssid.Length == 0) { AppendLog("[Hotspot] نام شبکه را وارد کنید."); return; }
            if (pass.Length < 8)  { AppendLog("[Hotspot] رمز عبور حداقل ۸ کاراکتر باشد."); return; }

            // بررسی پشتیبانی درایور
            HotspotToggleBtn.IsEnabled = false;
            HotspotToggleBtn.Content = "در حال بررسی...";
            AppendLog("> netsh wlan show drivers");
            var driversOut = await RunToolOutputAsync("netsh", "wlan show drivers");
            bool supported = driversOut.Contains("Hosted network supported  : Yes",
                StringComparison.OrdinalIgnoreCase);
            if (!supported)
            {
                AppendLog("[Hotspot] وایرلس لن سیستم شما این متد را پشتیبانی نمی‌کند.");
                SetHotspotUiOff();
                return;
            }

            HotspotToggleBtn.Content = "در حال راه‌اندازی...";
            HotspotService.Log += msg => Dispatcher.Invoke(() => AppendLog(msg));
            var (ok, err) = await _hotspot.StartAsync(ssid, pass);
            if (!ok) { AppendLog("[Hotspot] خطا: " + err); SetHotspotUiOff(); return; }

            SetHotspotUiOn(ssid, pass);
            _hotspotStatusTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(5) };
            _hotspotStatusTimer.Tick += async (_, _) =>
            {
                var c = await HotspotService.GetConnectedDevicesCountAsync();
                HotspotDevicesText.Text = c == 0 ? "" : $"{c} دستگاه متصل";
            };
            _hotspotStatusTimer.Start();
        }

        // خروجی RunToolAsync با خروجی string — برای خواندن نتیجه دستور
        private async Task<string> RunToolOutputAsync(string exe, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return stdout + stderr;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private void SetHotspotUiOn(string ssid, string pass)
        {
            HotspotToggleBtn.IsEnabled  = true;
            HotspotToggleBtn.Content    = "⏹ خاموش‌کردن Hotspot";
            HotspotStatusText.Text      = "✅ فعال";
            HotspotStatusText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#22C55E"));
            HotspotSsidBox.IsEnabled    = false;
            HotspotPassBox.IsEnabled    = false;
            HotspotDevicesText.Text     = "";
            AppendLog($"[Hotspot] فعال — {ssid} / {pass}");
        }

        private void SetHotspotUiOff()
        {
            HotspotToggleBtn.IsEnabled  = true;
            HotspotToggleBtn.Content    = "📶 روشن‌کردن Hotspot";
            HotspotStatusText.Text      = "غیرفعال";
            HotspotStatusText.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#9CA3AF"));
            HotspotSsidBox.IsEnabled    = true;
            HotspotPassBox.IsEnabled    = true;
            HotspotDevicesText.Text     = "";
        }

    }
}
