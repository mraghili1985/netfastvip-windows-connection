using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
        // ================= آمار سرعت/ترافیک =================
        private void StartStatsTimer()
        {
            _lastDownBytes = -1;
            _lastUpBytes = -1;
            _tunnelNicId = null; // آداپتور تانل دوباره شناسایی می‌شود
            _nicFindTries = 0;
            _sessionStartTotalBytes = GetTotalBytes(true) + GetTotalBytes(false); // موقت تا شناسایی تانل

            _statsTimer?.Stop();
            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();
        }

        private void StopStatsTimer()
        {
            _statsTimer?.Stop();
            _statsTimer = null;
        }

        // فقط کارت‌های شبکه‌ی فیزیکی شمرده می‌شن — قبلاً آداپتور تونل VPN (PPP/DCO/TAP) و مجازی‌ها هم
        // جمع می‌شدن و هر بایت دو بار حساب می‌شد — Session چند برابر واقعیت نشون می‌داد
        private static long GetTotalBytes(bool wantDown = true)
        {
            long total = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsPhysicalNic(nic)) continue;
                var stats = nic.GetIPv4Statistics();
                total += wantDown ? stats.BytesReceived : stats.BytesSent;
            }
            return total;
        }

        // منبع دقیق حجم/سرعت: کانترهای آداپتور خود تانل (همان چیزی که سرور می‌شمارد)
        // آداپتور تانل از روی IP تانل پیدا می‌شود؛ تا آن لحظه از کارت‌های فیزیکی (مثل قبل) استفاده می‌شود
        private long GetTrafficBytes(bool wantDown)
        {
            try
            {
                if (_tunnelNicId == null)
                {
                    // شناسایی آداپتور تانل از روی امضای خود آداپتور — به IP تانل وابسته نیست
                    // (اگر موتور IP را نداده باشد، قبلاً بی‌صدا روی کارت‌های فیزیکی می‌افتاد و کل ترافیک سیستم شمرده می‌شد)
                    NetworkInterface? found = null;
                    string? foundIp = null;
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        if (IsPhysicalNic(nic)) continue;

                        // فقط آداپتورهای با امضای تانل — نه مجازی‌های VMware/Hyper-V
                        var d = nic.Description.ToLowerInvariant();
                        var tunnelLike = nic.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel
                            || d.Contains("tap") || d.Contains("tun") || d.Contains("openvpn") || d.Contains("wireguard");
                        if (!tunnelLike) continue;

                        string? ip4 = null;
                        try
                        {
                            foreach (var u in nic.GetIPProperties().UnicastAddresses)
                                if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                                { ip4 = u.Address.ToString(); break; }
                        }
                        catch { }
                        if (ip4 == null) continue; // بدون IPv4 یعنی هنوز وصل/dial نشده

                        if (!string.IsNullOrEmpty(_tunnelLocalIp) && ip4 == _tunnelLocalIp)
                        { found = nic; foundIp = ip4; break; } // تطابق دقیق با IP تانل — بهترین حالت
                        if (found == null) { found = nic; foundIp = ip4; }
                    }

                    if (found != null)
                    {
                        _tunnelNicId = found.Id;
                        // از این لحظه مبنای Session کانترهای خود تانل است
                        var st0 = found.GetIPv4Statistics();
                        _sessionStartTotalBytes = st0.BytesReceived + st0.BytesSent;
                        _lastDownBytes = -1; // سرعت هم از منبع جدید از نو محاسبه شود
                        AppendConnLog("[stats] traffic source: " + found.Description + " (" + foundIp + ")");
                        if (string.IsNullOrEmpty(_tunnelLocalIp))
                        {
                            _tunnelLocalIp = foundIp; // موتور IP تانل را نداده بود — از خود آداپتور خواندیم
                            if (PrivateIpText.Text == "—") PrivateIpText.Text = foundIp!;
                        }
                    }
                    else if (++_nicFindTries == 8)
                        AppendConnLog("[stats] tunnel adapter not found — fallback: physical NICs (inaccurate)");
                }

                if (_tunnelNicId != null)
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.Id != _tunnelNicId) continue;
                        var st = nic.GetIPv4Statistics();
                        return wantDown ? st.BytesReceived : st.BytesSent;
                    }
                    // آداپتور تانل موقتاً در دسترس نیست (وسط قطع/وصل) — آخرین مقدار حفظ می‌شود
                    return wantDown ? Math.Max(0, _lastDownBytes) : Math.Max(0, _lastUpBytes);
                }
            }
            catch { }
            return GetTotalBytes(wantDown);
        }

        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            var down = GetTrafficBytes(true);
            var up = GetTrafficBytes(false);

            if (_lastDownBytes >= 0)
            {
                var dl = Math.Max(0, down - _lastDownBytes);
                var ul = Math.Max(0, up - _lastUpBytes);
                DlSpeedText.Text = "↓ " + Fmt(dl) + "/s";
                UlSpeedText.Text = "↑ " + Fmt(ul) + "/s";
                if (MiniSpeedText != null)
                    MiniSpeedText.Text = "↓ " + Fmt(dl) + "/s   ↑ " + Fmt(ul) + "/s";

                _idleWindowBytes += dl + ul;

                _dlHistory.Add(dl);
                _ulHistory.Add(ul);
                if (_dlHistory.Count > 60) _dlHistory.RemoveAt(0);
                if (_ulHistory.Count > 60) _ulHistory.RemoveAt(0);
                RedrawGraph();
            }
            _lastDownBytes = down;
            _lastUpBytes = up;

            var totalNow = down + up;
            SessionTrafficText.Text = Fmt(Math.Max(0, totalNow - _sessionStartTotalBytes));
            DurationText.Text = (DateTime.Now - _connectStart).ToString(@"hh\:mm\:ss");

            // هندشیک WireGuard/AmneziaWG هر ۵ ثانیه به‌روز شود
            if (_activeIsWg && _pingTick % 5 == 0)
            {
                var hs = WireGuardProvider.LastHandshake;
                if (hs != null)
                    YouText.Text = hs;
            }

            // پینگ زنده هر ۲ ثانیه — قبلاً هر ۳۰ ثانیه بود که برای کاربر «لایو» حس نمی‌شد
            _pingTick++;
            if (_pingTick % 2 == 0 && !_pingInFlight)
            {
                _pingInFlight = true;
                _ = UpdatePingAsync();
            }
            // اینفو (IP خروجی/پرچم) هم هر ۳۰ ثانیه دوباره چک می‌شود تا با تغییر مسیر وسط کار به‌روز بماند
            if (_pingTick % 30 == 0)
                _ = RefreshGeoIpAsync();

            // قطع خودکار در بی‌استفادگی — هر ۶۰ ثانیه دقیقه جاری ارزیابی می‌شود
            if (++_idleTick % 60 == 0)
            {
                // آستانه: کمتر از ۱۵۰ کیلوبایت در دقیقه = بی‌استفاده (ترافیک پس‌زمینه ویندوز/پیام‌رسان‌ها لحاظ شده)
                if (_idleWindowBytes < 150 * 1024) _idleMinutes++; else _idleMinutes = 0;
                _idleWindowBytes = 0;

                if (_config.IdleDisconnectMinutes > 0 && _idleMinutes >= _config.IdleDisconnectMinutes && _engine.IsRunning)
                {
                    var mins = _idleMinutes;
                    _idleMinutes = 0;
                    AppendConnLog(string.Format(LogIdleStopped, mins));
                    Notify(NotifyIdleStopped);
                    // از مسیر «قطع دستی» — تا اتصال هوشمند دوباره وصلش نکند
                    _ = StopManuallyAsync();
                }
            }
        }

        private async Task UpdatePingAsync()
        {
            var host = _pingHost;
            if (string.IsNullOrEmpty(host)) { _pingInFlight = false; return; }

            long? ms = null;
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 1500);
                if (reply.Status == IPStatus.Success) ms = reply.RoundtripTime;
            }
            catch { }

            if (_pingHost != host) { _pingInFlight = false; return; } // در این فاصله قطع یا عوض شده

            Dispatcher.Invoke(() =>
            {
                if (ms is null)
                {
                    // یک شکست = ممکن است تصادفی باشد؛ فقط وقتی پیاپی شد لاگ می‌کنیم تا اسپم نشود
                    _pingFails++;
                    PingText.Text = "—";
                    PingText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                    if (_pingFails == 2) AppendConnLog("[ping] " + host + " → timeout (پیاپی)");
                    return;
                }

                if (_pingFails >= 2) AppendConnLog("[ping] " + host + " → دوباره پاسخ داد (" + ms + " ms)");
                _pingFails = 0;
                PingText.Text = ms + " ms";
                // رنگ فقط بر اساس خودِ عدد پینگ — دیگر برچسب «کیفیت» مطلق نداریم چون برای سرورهای
                // جغرافیایی دور (مثلاً آمریکا) پینگ ذاتاً بالاتر است و به‌معنی اتصال ناپایدار نیست
                var color = ms.Value switch
                {
                    < 100 => Color.FromRgb(0x22, 0xC5, 0x5E),  // سبز
                    <= 200 => Color.FromRgb(0x3B, 0x82, 0xF6), // آبی
                    _ => Color.FromRgb(0xEF, 0x44, 0x44),      // قرمز
                };
                PingText.Foreground = new SolidColorBrush(color);
            });
            _pingInFlight = false;
        }

        // ================= Graph =================
        private void RedrawGraph()
        {
            try
            {
                double w = GraphCanvas.ActualWidth > 0 ? GraphCanvas.ActualWidth : 380;
                double h = GraphCanvas.ActualHeight > 0 ? GraphCanvas.ActualHeight : 54;
                DlLine.Points = BuildPoints(_dlHistory, w, h);
                UlLine.Points = BuildPoints(_ulHistory, w, h);
                DlArea.Points = BuildAreaPoints(_dlHistory, w, h);
                UlArea.Points = BuildAreaPoints(_ulHistory, w, h);
            }
            catch { }
        }

        private static PointCollection BuildPoints(List<double> values, double w, double h)
        {
            var pts = new PointCollection();
            if (values.Count == 0) return pts;
            double max = Math.Max(1, values.Max());
            double stepX = values.Count > 1 ? w / (values.Count - 1) : w;
            for (int i = 0; i < values.Count; i++)
            {
                double x = i * stepX;
                double y = h - (values[i] / max) * (h - 4) - 2;
                pts.Add(new Point(x, y));
            }
            return pts;
        }

        private static PointCollection BuildAreaPoints(List<double> values, double w, double h)
        {
            var pts = BuildPoints(values, w, h);
            var area = new PointCollection(pts);
            if (pts.Count == 0) return area;
            area.Add(new Point(pts[pts.Count - 1].X, h));
            area.Add(new Point(pts[0].X, h));
            return area;
        }


    }
}
