using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SmartVpn.XrayCore;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<ProxyProfile> XrayProxies { get; set; } = new ObservableCollection<ProxyProfile>();
        public ObservableCollection<SubscriptionGroup> XrayGroups { get; set; } = new ObservableCollection<SubscriptionGroup>();
        private ProxyProfile? _xraySelectedProfile;
        private Popup? _openXrayRowMenuPopup;
        private bool _xrayIsConnected = false;
        public bool XrayIsConnected => _xrayIsConnected && XrayEngine.IsRunning;
        public ProxyProfile? XrayActiveProfile => _xrayActiveProfile;
        private DispatcherTimer _xrayTimer = new DispatcherTimer();
        private DateTime _xrayStartTime;
        private ProxyProfile? _xrayActiveProfile;
        private long _xrayLastDown = -1;
        private long _xrayLastUp = -1;
        private long _xraySessionStartTotalBytes = 0;
        private int _xrayPingTick = 0;
        private bool _xrayPingInFlight = false;
        private readonly List<double> _xrayDlHistory = new List<double>();
        private readonly List<double> _xrayUlHistory = new List<double>();
        private string? _xrayExitIp = null;
        private int _xrayGeoGen = 0;

        private void InitXrayPanel()
        {
            XrayProxyList.ItemsSource = XrayProxies;
            XrayLoadData();
            
            _xrayTimer.Interval = TimeSpan.FromSeconds(1);
            _xrayTimer.Tick -= XrayTimer_Tick;
            _xrayTimer.Tick += XrayTimer_Tick;

            XrayEngine.Log -= OnXrayLog;
            XrayEngine.Log += OnXrayLog;
        }

        private void OnXrayLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            string clean = Regex.Replace(line, @"\x1B\[[0-9;]*[a-zA-Z]", "").Trim();
            if (string.IsNullOrEmpty(clean)) return;

            // Filter out connection stream tracking messages that cause UI lag
            if (clean.Contains("inbound/") || clean.Contains("outbound/")) return;

            Dispatcher.InvokeAsync(() => AppendConnLog(clean));
        }

        private static NetworkInterface? FindSingBoxTunInterface()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    var name = nic.Name.ToLowerInvariant();
                    var desc = nic.Description.ToLowerInvariant();
                    if (name.Contains("sing-box") || desc.Contains("sing-box") || desc.Contains("wintun"))
                    {
                        return nic;
                    }
                }
            }
            catch { }
            return null;
        }

        private long GetXrayTrafficBytes(bool wantDown)
        {
            try
            {
                var tun = FindSingBoxTunInterface();
                if (tun != null)
                {
                    var stats = tun.GetIPv4Statistics();
                    return wantDown ? stats.BytesReceived : stats.BytesSent;
                }
            }
            catch { }
            return GetTotalBytes(wantDown);
        }

        private void XrayTimer_Tick(object? sender, EventArgs e)
        {
            var diff = DateTime.Now - _xrayStartTime;
            var timeStr = $"{(int)diff.TotalHours:D2}:{diff.Minutes:D2}:{diff.Seconds:D2}";
            DurationText.Text = timeStr;
            if (XrayTxtDuration != null) XrayTxtDuration.Text = timeStr;

            if (!_xrayIsConnected) return;

            var down = GetXrayTrafficBytes(true);
            var up = GetXrayTrafficBytes(false);

            if (_xrayLastDown >= 0)
            {
                var dl = Math.Max(0, down - _xrayLastDown);
                var ul = Math.Max(0, up - _xrayLastUp);

                if (XrayDlSpeedText != null) XrayDlSpeedText.Text = "↓ " + Fmt(dl) + "/s";
                if (XrayUlSpeedText != null) XrayUlSpeedText.Text = "↑ " + Fmt(ul) + "/s";
                if (DlSpeedText != null) DlSpeedText.Text = "↓ " + Fmt(dl) + "/s";
                if (UlSpeedText != null) UlSpeedText.Text = "↑ " + Fmt(ul) + "/s";

                _xrayDlHistory.Add(dl);
                _xrayUlHistory.Add(ul);
                if (_xrayDlHistory.Count > 60) _xrayDlHistory.RemoveAt(0);
                if (_xrayUlHistory.Count > 60) _xrayUlHistory.RemoveAt(0);
                RedrawXrayGraph();
            }
            _xrayLastDown = down;
            _xrayLastUp = up;

            var totalNow = down + up;
            if (XraySessionTrafficText != null)
            {
                var st = Fmt(Math.Max(0, totalNow - _xraySessionStartTotalBytes));
                XraySessionTrafficText.Text = st;
                if (SessionTrafficText != null) SessionTrafficText.Text = st;
            }

            _xrayPingTick++;
            if (_xrayPingTick % 3 == 0 && !_xrayPingInFlight && _xrayActiveProfile != null)
            {
                _xrayPingInFlight = true;
                _ = UpdateXrayPingAsync(_xrayActiveProfile);
            }
        }

        private void RedrawXrayGraph()
        {
            try
            {
                if (XrayGraphCanvas == null) return;
                double w = XrayGraphCanvas.ActualWidth > 0 ? XrayGraphCanvas.ActualWidth : 380;
                double h = XrayGraphCanvas.ActualHeight > 0 ? XrayGraphCanvas.ActualHeight : 54;
                if (XrayDlLine != null) XrayDlLine.Points = BuildPoints(_xrayDlHistory, w, h);
                if (XrayUlLine != null) XrayUlLine.Points = BuildPoints(_xrayUlHistory, w, h);
                if (XrayDlArea != null) XrayDlArea.Points = BuildAreaPoints(_xrayDlHistory, w, h);
                if (XrayUlArea != null) XrayUlArea.Points = BuildAreaPoints(_xrayUlHistory, w, h);
            }
            catch { }
        }

        private async Task FetchXrayGeoIpAsync()
        {
            var gen = ++_xrayGeoGen;
            int[] delays = { 600, 1500, 2500, 4000, 6000 };

            var endpoints = new (string url, Func<string, (string ip, string country)?> parse)[]
            {
                ("https://ipwho.is/", json =>
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var s) && !s.GetBoolean()) return null;
                    var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                    var country = root.TryGetProperty("country", out var cEl) ? cEl.GetString() : "";
                    return !string.IsNullOrWhiteSpace(ip) ? (ip, country ?? "") : null;
                }),
                ("https://api.myip.com", json =>
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                    var country = root.TryGetProperty("country", out var cEl) ? cEl.GetString() : "";
                    return !string.IsNullOrWhiteSpace(ip) ? (ip, country ?? "") : null;
                }),
                ("https://api.ipify.org?format=json", json =>
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                    return !string.IsNullOrWhiteSpace(ip) ? (ip, "") : null;
                }),
                ("https://ipinfo.io/json", json =>
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
                    var country = root.TryGetProperty("country", out var cEl) ? cEl.GetString() : "";
                    return !string.IsNullOrWhiteSpace(ip) ? (ip, country ?? "") : null;
                }),
                ("https://cloudflare.com/cdn-cgi/trace", text =>
                {
                    string? ip = null;
                    string country = "";
                    foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("ip=")) ip = line.Substring(3).Trim();
                        if (line.StartsWith("loc=")) country = line.Substring(4).Trim();
                    }
                    return !string.IsNullOrWhiteSpace(ip) ? (ip, country) : null;
                })
            };

            foreach (var d in delays)
            {
                await Task.Delay(d);
                if (gen != _xrayGeoGen || !_xrayIsConnected) return;

                foreach (var ep in endpoints)
                {
                    if (gen != _xrayGeoGen || !_xrayIsConnected) return;

                    try
                    {
                        using var handler = new HttpClientHandler
                        {
                            UseProxy = false,
                            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                            CheckCertificateRevocationList = false
                        };
                        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
                        var res = await client.GetStringAsync(ep.url);
                        if (gen != _xrayGeoGen || !_xrayIsConnected) return;

                        var parsed = ep.parse(res);
                        if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Value.ip))
                        {
                            var ip = parsed.Value.ip.Trim();
                            var country = parsed.Value.country.Trim();
                            _xrayExitIp = ip;

                            Dispatcher.Invoke(() =>
                            {
                                if (!_xrayIsConnected) return;
                                var geoStr = string.IsNullOrWhiteSpace(country) ? ip : $"{country} — {ip}";
                                if (XrayTxtActiveGeoIP != null) XrayTxtActiveGeoIP.Text = geoStr;
                                GeoIpText.Text = geoStr;
                                if (!string.IsNullOrWhiteSpace(country))
                                {
                                    UpdateActiveBadge(_xrayActiveProfile?.Alias, _xrayActiveProfile?.Protocol, country);
                                }
                                AppendConnLog($"[sing-box] موقعیت خروجی (GeoIP): {geoStr}");
                            });

                            // Trigger ping to this GeoIP immediately
                            _ = UpdateXrayPingAsync(_xrayActiveProfile);
                            return;
                        }
                    }
                    catch { }
                }
            }

            // Fallback if all endpoint retries failed
            Dispatcher.Invoke(() =>
            {
                if (!_xrayIsConnected || !string.IsNullOrEmpty(_xrayExitIp)) return;
                var fallbackStr = _xrayActiveProfile?.Address ?? Localization.T("متصل شد");
                if (XrayTxtActiveGeoIP != null && (XrayTxtActiveGeoIP.Text == "Detecting location..." || XrayTxtActiveGeoIP.Text == Localization.T("Detecting location...")))
                {
                    XrayTxtActiveGeoIP.Text = fallbackStr;
                }
                if (GeoIpText != null && (GeoIpText.Text == "Detecting location..." || GeoIpText.Text == Localization.T("Detecting location...")))
                {
                    GeoIpText.Text = fallbackStr;
                }
            });
        }

        private async Task UpdateXrayPingAsync(ProxyProfile? profile)
        {
            if (profile == null) { _xrayPingInFlight = false; return; }

            long? ms = null;

            // 1. اولویت اول: تست پینگ واقعی انتها به انتها (Real End-to-End Delay) از داخل تانل
            // چون ترافیک از کارت TUN عبور می‌کند، درخواست HTTP به generate_204 تأخیر رفت‌وبرگشت واقعی تا لبه اینترنت را می‌سنجد
            try
            {
                var watch = Stopwatch.StartNew();
                using var handler = new HttpClientHandler
                {
                    UseProxy = false,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                    CheckCertificateRevocationList = false
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(2000) };
                using var resp = await client.GetAsync("http://cp.cloudflare.com/generate_204");
                watch.Stop();
                if (resp.IsSuccessStatusCode && watch.ElapsedMilliseconds > 0)
                {
                    ms = watch.ElapsedMilliseconds;
                }
            }
            catch { }

            // 2. مسیر جایگزین اول: سرور گوگلی 204
            if (ms == null)
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    using var handler = new HttpClientHandler
                    {
                        UseProxy = false,
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                        CheckCertificateRevocationList = false
                    };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(2000) };
                    using var resp = await client.GetAsync("http://www.gstatic.com/generate_204");
                    watch.Stop();
                    if (resp.IsSuccessStatusCode && watch.ElapsedMilliseconds > 0)
                    {
                        ms = watch.ElapsedMilliseconds;
                    }
                }
                catch { }
            }

            // 3. مسیر جایگزین دوم: تست دست‌دهی مستقیم TCP به سرور نود (Node Ping)
            if (ms == null)
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    using var tcp = new TcpClient();
                    var connectTask = tcp.ConnectAsync(profile.Address, profile.Port);
                    var completedTask = await Task.WhenAny(connectTask, Task.Delay(2000));
                    if (completedTask == connectTask && tcp.Connected)
                    {
                        watch.Stop();
                        if (watch.ElapsedMilliseconds > 0)
                        {
                            ms = watch.ElapsedMilliseconds;
                        }
                    }
                }
                catch { }
            }

            _xrayPingInFlight = false;

            if (!_xrayIsConnected || _xrayActiveProfile != profile) return;

            Dispatcher.Invoke(() =>
            {
                if (ms == null)
                {
                    var timeoutBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                    if (XrayPingText != null)
                    {
                        XrayPingText.Text = "Timeout";
                        XrayPingText.Foreground = timeoutBrush;
                    }
                    if (PingText != null)
                    {
                        PingText.Text = "Timeout";
                        PingText.Foreground = timeoutBrush;
                    }
                    profile.Delay = "Timeout";
                }
                else
                {
                    var pingVal = Math.Max(1, ms.Value);
                    var txt = $"{pingVal} ms";
                    var color = pingVal switch
                    {
                        < 100 => Color.FromRgb(0x22, 0xC5, 0x5E),
                        <= 250 => Color.FromRgb(0x3B, 0x82, 0xF6),
                        _ => Color.FromRgb(0xEF, 0x44, 0x44),
                    };
                    var brush = new SolidColorBrush(color);

                    if (XrayPingText != null)
                    {
                        XrayPingText.Text = txt;
                        XrayPingText.Foreground = brush;
                    }
                    if (PingText != null)
                    {
                        PingText.Text = txt;
                        PingText.Foreground = brush;
                    }
                    profile.Delay = txt;
                }
            });
        }

        private async Task ResolveXrayServerIpAsync(ProxyProfile item)
        {
            try
            {
                var host = item.Address;
                if (IPAddress.TryParse(host, out _))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_xrayIsConnected && XrayServerIpText != null)
                        {
                            XrayServerIpText.Text = $"{host}:{item.Port}";
                        }
                    });
                    return;
                }

                var addrs = await Dns.GetHostAddressesAsync(host);
                var ip = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_xrayIsConnected && XrayServerIpText != null)
                        {
                            XrayServerIpText.Text = $"{ip}:{item.Port}";
                            AppendConnLog($"[sing-box] IP سرور مقصد شناسایی شد: {ip} ({host})");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                AppendConnLog($"[sing-box WARN] خطا در تحلیل DNS سرور: {ex.Message}");
            }
        }

        private void UpdateActiveProfileInfo(ProxyProfile? item)
        {
            if (item == null) return;
            MainWindow.LastConnectionType = "xray";
            if (XrayTxtActiveName != null) XrayTxtActiveName.Text = item.Alias;
            string sec = !string.IsNullOrEmpty(item.Tls) ? $" + {item.Tls.ToUpper()}" : "";
            if (XrayTxtActiveProtocol != null) XrayTxtActiveProtocol.Text = $"{item.Protocol.ToUpper()} / {item.Network.ToUpper()}{sec}";
            if (XrayTxtActiveGeoIP != null) XrayTxtActiveGeoIP.Text = $"{item.Address}:{item.Port}";
            if (XrayServerIpText != null) XrayServerIpText.Text = $"{item.Address}:{item.Port}";
            if (XrayProtocolText != null) XrayProtocolText.Text = $"{item.Protocol.ToUpper()} / {item.Network.ToUpper()}";
            if (XrayYouText != null) XrayYouText.Text = !string.IsNullOrEmpty(item.UserId) ? (item.UserId.Length > 12 ? item.UserId.Substring(0, 10) + "..." : item.UserId) : "sing-box";

            if (!_engine.IsRunning)
            {
                if (ActiveConnText != null) ActiveConnText.Text = item.Alias;
                if (ServerSubText != null) ServerSubText.Text = $"{item.Protocol.ToUpper()} / {item.Network.ToUpper()}{sec}";
                UpdateActiveBadge(item.Alias, item.Protocol);
            }
        }

        private static (Color bg, Color border, Color fg) XrayProtocolBadgeColors(string proto, bool light)
        {
            switch ((proto ?? "").ToLowerInvariant())
            {
                case "vless":
                    return light
                        ? (Color.FromRgb(0xE0, 0xF2, 0xFE), Color.FromRgb(0xBA, 0xE6, 0xFD), Color.FromRgb(0x02, 0x84, 0xC7))
                        : (Color.FromArgb(0x26, 0x38, 0xBD, 0xF8), Color.FromArgb(0x48, 0x38, 0xBD, 0xF8), Color.FromRgb(0x7D, 0xD3, 0xFC));
                case "vmess":
                    return light
                        ? (Color.FromRgb(0xF3, 0xE8, 0xFF), Color.FromRgb(0xD8, 0xB4, 0xFE), Color.FromRgb(0x7E, 0x22, 0xCE))
                        : (Color.FromArgb(0x26, 0xA8, 0x55, 0xF7), Color.FromArgb(0x48, 0xA8, 0x55, 0xF7), Color.FromRgb(0xD8, 0xB4, 0xFE));
                case "trojan":
                    return light
                        ? (Color.FromRgb(0xDC, 0xFC, 0xE7), Color.FromRgb(0x86, 0xEF, 0xAC), Color.FromRgb(0x16, 0x65, 0x34))
                        : (Color.FromArgb(0x26, 0x10, 0xB9, 0x81), Color.FromArgb(0x48, 0x10, 0xB9, 0x81), Color.FromRgb(0x6E, 0xEE, 0xB8));
                case "shadowsocks":
                case "ss":
                    return light
                        ? (Color.FromRgb(0xFE, 0xF3, 0xC7), Color.FromRgb(0xFC, 0xD3, 0x4D), Color.FromRgb(0xB4, 0x53, 0x09))
                        : (Color.FromArgb(0x26, 0xF5, 0x9E, 0x0B), Color.FromArgb(0x48, 0xF5, 0x9E, 0x0B), Color.FromRgb(0xFC, 0xD3, 0x4D));
                case "wireguard":
                    return light
                        ? (Color.FromRgb(0xEC, 0xFD, 0xC4), Color.FromRgb(0xA3, 0xE6, 0x35), Color.FromRgb(0x3F, 0x62, 0x12))
                        : (Color.FromArgb(0x26, 0x84, 0xCC, 0x16), Color.FromArgb(0x48, 0x84, 0xCC, 0x16), Color.FromRgb(0xBE, 0xF2, 0x64));
                case "hysteria2":
                case "hy2":
                case "hysteria":
                    return light
                        ? (Color.FromRgb(0xFF, 0xE4, 0xE6), Color.FromRgb(0xFD, 0xA4, 0xAF), Color.FromRgb(0xBE, 0x12, 0x3C))
                        : (Color.FromArgb(0x26, 0xF4, 0x3F, 0x5E), Color.FromArgb(0x48, 0xF4, 0x3F, 0x5E), Color.FromRgb(0xFB, 0x71, 0x85));
                case "tuic":
                    return light
                        ? (Color.FromRgb(0xCC, 0xFB, 0xF1), Color.FromRgb(0x5E, 0xEA, 0xD4), Color.FromRgb(0x0F, 0x76, 0x6E))
                        : (Color.FromArgb(0x26, 0x06, 0xB6, 0xD4), Color.FromArgb(0x48, 0x06, 0xB6, 0xD4), Color.FromRgb(0x67, 0xE8, 0xF9));
                default:
                    return light
                        ? (Color.FromRgb(0xDB, 0xEA, 0xFE), Color.FromRgb(0x93, 0xC5, 0xFD), Color.FromRgb(0x1D, 0x4E, 0xD8))
                        : (Color.FromArgb(0x24, 0x3B, 0x82, 0xF6), Color.FromArgb(0x38, 0x3B, 0x82, 0xF6), Color.FromRgb(0xBF, 0xDB, 0xFE));
            }
        }

        private UIElement MakeXrayRowBtn(ProxyProfile profile, SubscriptionGroup group)
        {
            var selected = profile == _xraySelectedProfile;
            var connected = _xrayIsConnected && _xrayActiveProfile == profile;

            var row = new Grid
            {
                Margin = connected ? new Thickness(4, 6, 4, 8) : new Thickness(4, 0, 4, 4),
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var light = IsLightTheme();
            var accent = connected
                ? (light ? Color.FromRgb(0x16, 0xA3, 0x4A) : Color.FromRgb(0x22, 0xC5, 0x5E))
                : selected ? (light ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromRgb(0x3B, 0x82, 0xF6))
                : (light ? Color.FromRgb(0xCB, 0xD5, 0xE1) : Color.FromRgb(0x47, 0x5A, 0x75));

            var strip = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(7, 0, 0, 7),
                Background = new SolidColorBrush(accent),
                Opacity = connected || selected ? 1.0 : 0.55
            };
            Grid.SetColumn(strip, 0);
            row.Children.Add(strip);

            var cardBg = connected
                ? new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xDC, 0xFC, 0xE7) : Color.FromArgb(0x78, 0x13, 0x28, 0x24))
                : selected
                    ? new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xDB, 0xEA, 0xFE) : Color.FromArgb(0x78, 0x12, 0x2B, 0x52))
                    : new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x70, 0x1E, 0x29, 0x3B));

            var card = new Border
            {
                Background = cardBg,
                BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x28, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0x94, 0xA3, 0xB8)),
                BorderThickness = light ? new Thickness(0, 1, 1, 1) : new Thickness(0),
                CornerRadius = new CornerRadius(0, 8, 8, 0),
                Padding = new Thickness(7, 4, 6, 4)
            };
            Grid.SetColumn(card, 1);

            if (connected)
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                card.BorderThickness = new Thickness(0, 1.4, 1.4, 1.4);
                card.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x22, 0xC5, 0x5E),
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
            }
            else if (selected)
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                card.BorderThickness = new Thickness(0, 1.2, 1.2, 1.2);
            }

            var contentGrid = new Grid { FlowDirection = FlowDirection.LeftToRight };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var catColors = XrayProtocolBadgeColors(profile.Protocol, light);
            var categoryBadge = new Border
            {
                Background = new SolidColorBrush(catColors.bg),
                BorderBrush = new SolidColorBrush(catColors.border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = profile.Protocol.ToUpperInvariant(),
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(catColors.fg),
                    FlowDirection = FlowDirection.LeftToRight
                }
            };
            Grid.SetColumn(categoryBadge, 0);
            Grid.SetRow(categoryBadge, 0);
            Grid.SetRowSpan(categoryBadge, 2);
            contentGrid.Children.Add(categoryBadge);

            var textStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var nameText = new TextBlock
            {
                Text = profile.Alias,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(light ? Color.FromRgb(0x0F, 0x17, 0x2A) : Color.FromRgb(0xF8, 0xFA, 0xFC)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight,
                MaxWidth = 180
            };
            textStack.Children.Add(nameText);

            var subtitle = new TextBlock
            {
                Text = $"{profile.Address}:{profile.Port}",
                FontSize = 9,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = new SolidColorBrush(light ? Color.FromRgb(0x47, 0x55, 0x69) : Color.FromRgb(0x94, 0xA3, 0xB8)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight,
                MaxWidth = 180
            };
            textStack.Children.Add(subtitle);

            Grid.SetColumn(textStack, 1);
            Grid.SetRow(textStack, 0);
            Grid.SetRowSpan(textStack, 2);
            contentGrid.Children.Add(textStack);

            var rowRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (connected)
            {
                var statusPill = new Border
                {
                    Background = new SolidColorBrush(light ? Color.FromArgb(0xFF, 0xD1, 0xFA, 0xDD) : Color.FromArgb(0x28, 0x22, 0xC5, 0x5E)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(5, 1, 5, 1),
                    BorderBrush = new SolidColorBrush(light ? Color.FromRgb(0x16, 0xA3, 0x4A) : Color.FromArgb(0x45, 0x22, 0xC5, 0x5E)),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    Child = new TextBlock
                    {
                        Text = "CONNECTED",
                        FontSize = 8.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(light ? Color.FromRgb(0x15, 0x80, 0x3D) : Color.FromRgb(0x86, 0xEF, 0xAC)),
                        FlowDirection = FlowDirection.LeftToRight
                    }
                };
                rowRight.Children.Add(statusPill);
            }

            Color pingColor = Color.FromRgb(0x22, 0xC5, 0x5E);
            if (!string.IsNullOrEmpty(profile.Delay) && profile.Delay.Contains("ms"))
            {
                var numStr = Regex.Match(profile.Delay, @"\d+").Value;
                if (int.TryParse(numStr, out int ms))
                {
                    pingColor = ms < 150 ? Color.FromRgb(0x22, 0xC5, 0x5E) : (ms < 300 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0xEF, 0x44, 0x44));
                }
            }
            else if (profile.Delay == "Timeout" || profile.Delay == "Error")
            {
                pingColor = Color.FromRgb(0xEF, 0x44, 0x44);
            }
            else
            {
                pingColor = Color.FromRgb(0x94, 0xA3, 0xB8);
            }

            string pingDisplayText = !string.IsNullOrEmpty(profile.Delay) && profile.Delay != "-" ? profile.Delay : "— ms";
            var pingPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x20, pingColor.R, pingColor.G, pingColor.B)),
                BorderBrush = new SolidColorBrush(pingColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "تست پینگ این سرور (کلیک کنید)",
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(pingColor), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = pingDisplayText, FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(pingColor), VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };
            pingPill.MouseLeftButtonDown += (_, e) => e.Handled = true;
            pingPill.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await PingSingleProfileAsync(profile);
                RenderXrayConnList();
            };
            rowRight.Children.Add(pingPill);

            var menuPopup = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade
            };
            var menuStack = new StackPanel { Orientation = Orientation.Vertical, Width = 175 };
            var menuBorder = new Border
            {
                Background = new SolidColorBrush(light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1B, 0x24, 0x38)),
                BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x35, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x45, 0x94, 0xA3, 0xB8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = 0.30 },
                Child = menuStack
            };
            menuPopup.Child = menuBorder;
            menuPopup.Closed += (_, __) => { if (_openXrayRowMenuPopup == menuPopup) _openXrayRowMenuPopup = null; };

            var menuTextNormal = new SolidColorBrush(light ? Color.FromRgb(0x0F, 0x17, 0x2A) : Color.FromRgb(0xE2, 0xE8, 0xF0));
            var menuTextDanger = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            var menuSepBrush = new SolidColorBrush(light ? Color.FromArgb(0x22, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x28, 0x94, 0xA3, 0xB8));

            Button MakeMenuRow(string icon, string text, Brush fg) => new Button
            {
                Content = $"{icon}   {text}",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Thickness(12, 8, 12, 8),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = fg,
                FontSize = 11.5,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var menuConnectRow = MakeMenuRow("🔌", connected ? Localization.T("قطع اتصال") : Localization.T("اتصال به سرور"), connected ? menuTextDanger : menuTextNormal);
            menuConnectRow.Click += (_, __) =>
            {
                MainWindow.LastConnectionType = "xray";
                menuPopup.IsOpen = false;
                _xraySelectedProfile = profile;
                XrayProxyList.SelectedItem = profile;
                XrayConnectBtn_Click(this, new RoutedEventArgs());
                ShowPanel("home");
            };
            menuStack.Children.Add(menuConnectRow);

            var menuPingRow = MakeMenuRow("⚡", Localization.T("تست پینگ"), menuTextNormal);
            menuPingRow.Click += async (_, __) =>
            {
                menuPopup.IsOpen = false;
                await PingSingleProfileAsync(profile);
                RenderXrayConnList();
            };
            menuStack.Children.Add(menuPingRow);

            var menuShareRow = MakeMenuRow("🔗", Localization.T("اشتراک‌گذاری (کد QR و لینک)"), menuTextNormal);
            menuShareRow.Click += (_, __) =>
            {
                menuPopup.IsOpen = false;
                ShowXrayShareModal(profile);
            };
            menuStack.Children.Add(menuShareRow);

            menuStack.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 2, 6, 2), Background = menuSepBrush });

            var menuDeleteRow = MakeMenuRow("🗑", Localization.T("حذف کانکشن"), menuTextDanger);
            menuDeleteRow.Click += (_, __) =>
            {
                menuPopup.IsOpen = false;
                if (_xrayIsConnected && _xrayActiveProfile == profile)
                {
                    ShowInAppMessage("امکان حذف کانکشن فعال وجود ندارد.", "هشدار");
                    return;
                }
                group.Profiles.Remove(profile);
                XrayProxies.Remove(profile);
                SubscriptionGroupManager.SaveGroups(XrayGroups);
                RenderXrayConnList();
            };
            menuStack.Children.Add(menuDeleteRow);

            var dotsBtn = new Border
            {
                FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(light ? Color.FromArgb(0x14, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Options",
                Child = new TextBlock
                {
                    Text = "⋯",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -3, 0, 0),
                    Foreground = new SolidColorBrush(light ? Color.FromRgb(0x33, 0x41, 0x55) : Color.FromRgb(0xE2, 0xE8, 0xF0))
                }
            };
            dotsBtn.MouseEnter += (_, __) => dotsBtn.Background = new SolidColorBrush(light ? Color.FromArgb(0x28, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            dotsBtn.MouseLeave += (_, __) => dotsBtn.Background = new SolidColorBrush(light ? Color.FromArgb(0x14, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            menuPopup.PlacementTarget = dotsBtn;
            menuPopup.Placement = PlacementMode.Bottom;
            dotsBtn.MouseLeftButtonDown += (_, e) => e.Handled = true;
            dotsBtn.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (!menuPopup.IsOpen)
                {
                    if (_openXrayRowMenuPopup != null) _openXrayRowMenuPopup.IsOpen = false;
                    menuPopup.HorizontalOffset = dotsBtn.ActualWidth - menuStack.Width;
                    _openXrayRowMenuPopup = menuPopup;
                }
                else
                {
                    _openXrayRowMenuPopup = null;
                }
                menuPopup.IsOpen = !menuPopup.IsOpen;
            };
            rowRight.Children.Add(dotsBtn);

            Grid.SetColumn(rowRight, 2);
            Grid.SetRow(rowRight, 0);
            Grid.SetRowSpan(rowRight, 2);
            contentGrid.Children.Add(rowRight);

            card.Child = contentGrid;
            row.Children.Add(card);

            if (!connected && !selected)
            {
                row.MouseEnter += (_, __) =>
                {
                    card.Background = new SolidColorBrush(light ? Color.FromArgb(0xFF, 0xEE, 0xF2, 0xF6) : Color.FromArgb(0x95, 0x2A, 0x3A, 0x52));
                    card.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x50, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x50, 0x3B, 0x82, 0xF6));
                    card.BorderThickness = new Thickness(0, 1, 1, 1);
                };
                row.MouseLeave += (_, __) =>
                {
                    card.Background = cardBg;
                    card.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x28, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0x94, 0xA3, 0xB8));
                    card.BorderThickness = light ? new Thickness(0, 1, 1, 1) : new Thickness(0);
                };
            }

            row.MouseLeftButtonUp += (_, e) =>
            {
                MainWindow.LastConnectionType = "xray";
                _xraySelectedProfile = profile;
                XrayProxyList.SelectedItem = profile;
                UpdateActiveProfileInfo(profile);
                RenderXrayConnList();
            };

            row.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    MainWindow.LastConnectionType = "xray";
                    _xraySelectedProfile = profile;
                    XrayProxyList.SelectedItem = profile;
                    XrayConnectBtn_Click(this, new RoutedEventArgs());
                    ShowPanel("home");
                }
            };

            return row;
        }

        private UIElement MakeGroupHeader(SubscriptionGroup group)
        {
            var light = IsLightTheme();
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(light ? Color.FromRgb(0xF1, 0xF5, 0xF9) : Color.FromArgb(0x90, 0x1E, 0x29, 0x3B)),
                BorderBrush = new SolidColorBrush(light ? Color.FromRgb(0xCB, 0xD5, 0xE1) : Color.FromArgb(0x35, 0x94, 0xA3, 0xB8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 8, 6),
                Margin = new Thickness(4, 6, 4, 3),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid { FlowDirection = FlowDirection.LeftToRight };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var chevron = new TextBlock
            {
                Text = group.IsExpanded ? "▼" : "▶",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0)
            };
            leftStack.Children.Add(chevron);

            var titleBlock = new TextBlock
            {
                Text = group.Name,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(light ? Color.FromRgb(0x0F, 0x17, 0x2A) : Color.FromRgb(0xF8, 0xFA, 0xFC)),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            leftStack.Children.Add(titleBlock);

            var countBadge = new Border
            {
                Background = new SolidColorBrush(light ? Color.FromArgb(0x20, 0x3B, 0x82, 0xF6) : Color.FromArgb(0x35, 0x38, 0xBD, 0xF8)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"{group.Profiles.Count}",
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(light ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromRgb(0x38, 0xBD, 0xF8))
                }
            };
            leftStack.Children.Add(countBadge);
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            var summaryStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            if (!string.IsNullOrWhiteSpace(group.DataRemaining) && group.DataRemaining != "Unlimited")
            {
                summaryStack.Children.Add(new TextBlock
                {
                    Text = $"📦 {group.DataRemaining}",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(light ? Color.FromRgb(0x47, 0x55, 0x69) : Color.FromRgb(0x94, 0xA3, 0xB8)),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            if (!string.IsNullOrWhiteSpace(group.DaysRemaining) && group.DaysRemaining != "Unlimited")
            {
                summaryStack.Children.Add(new TextBlock
                {
                    Text = $"⏳ {group.DaysRemaining}",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(light ? Color.FromRgb(0x47, 0x55, 0x69) : Color.FromRgb(0x94, 0xA3, 0xB8)),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            Grid.SetColumn(summaryStack, 1);
            grid.Children.Add(summaryStack);

            var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            Button MakeHeaderBtn(string icon, string tip, Action onClick)
            {
                var btn = new Button
                {
                    Content = icon,
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    Margin = new Thickness(2, 0, 2, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    FontSize = 11,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = tip
                };
                btn.Click += (_, e) =>
                {
                    e.Handled = true;
                    onClick();
                };
                return btn;
            }

            if (!string.IsNullOrWhiteSpace(group.Url))
            {
                actionsStack.Children.Add(MakeHeaderBtn("🔄", Localization.T("بروزرسانی این اشتراک"), async () =>
                {
                    await UpdateSubscriptionGroupAsync(group);
                }));
            }

            actionsStack.Children.Add(MakeHeaderBtn("⚡", Localization.T("تست پینگ این گروه"), async () =>
            {
                await PingGroupAsync(group);
                RenderXrayConnList();
            }));

            actionsStack.Children.Add(MakeHeaderBtn("🗑", Localization.T("حذف این گروه اشتراک"), () =>
            {
                var q = string.Format(Localization.T("آیا از حذف گروه «{0}» و تمام کانکشن‌های آن اطمینان دارید؟"), group.Name);
                if (!AskDialog.Confirm(this, q)) return;
                XrayGroups.Remove(group);
                SubscriptionGroupManager.SaveGroups(XrayGroups);
                XrayLoadData();
            }));

            Grid.SetColumn(actionsStack, 2);
            grid.Children.Add(actionsStack);

            headerBorder.Child = grid;

            headerBorder.MouseLeftButtonUp += (_, e) =>
            {
                group.IsExpanded = !group.IsExpanded;
                SubscriptionGroupManager.SaveGroups(XrayGroups);
                RenderXrayConnList();
            };

            return headerBorder;
        }

        private void RenderXrayConnList()
        {
            if (XrayConnList == null) return;
            XrayConnList.Children.Clear();

            foreach (var group in XrayGroups)
            {
                XrayConnList.Children.Add(MakeGroupHeader(group));

                var cardsStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Visibility = group.IsExpanded ? Visibility.Visible : Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                foreach (var profile in group.Profiles)
                {
                    cardsStack.Children.Add(MakeXrayRowBtn(profile, group));
                }

                XrayConnList.Children.Add(cardsStack);
            }
        }

        private void UpdateXraySubscriptionCard()
        {
            try
            {
                string bestData = "Unlimited";
                string bestDays = "Unlimited";
                DateTime? bestUpdate = null;
                bool hasActive = XrayGroups.Any(g => g.Profiles.Count > 0);

                foreach (var g in XrayGroups)
                {
                    if (!string.IsNullOrWhiteSpace(g.DataRemaining) && g.DataRemaining != "Unlimited")
                        bestData = g.DataRemaining;
                    if (!string.IsNullOrWhiteSpace(g.DaysRemaining) && g.DaysRemaining != "Unlimited")
                        bestDays = g.DaysRemaining;
                    if (g.LastUpdated.HasValue && (!bestUpdate.HasValue || g.LastUpdated > bestUpdate))
                        bestUpdate = g.LastUpdated;
                }

                // 1. Update Xray Panel's sub card
                if (XraySubSummaryCard != null)
                {
                    if (XraySubDataLeftText != null) XraySubDataLeftText.Text = bestData;
                    if (XraySubTimeLeftText != null) XraySubTimeLeftText.Text = bestDays;
                    if (XraySubUpdatedText != null)
                    {
                        XraySubUpdatedText.Text = bestUpdate.HasValue
                            ? $"{Localization.T("آخرین بروزرسانی:")} {bestUpdate.Value:HH:mm}"
                            : "—";
                    }
                    if (XraySubActiveBadge != null)
                    {
                        XraySubActiveBadge.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
                    }
                }

                // 2. Update Dashboard HomeXraySubCard (Right column)
                if (HomeXraySubCard != null)
                {
                    if (hasActive)
                    {
                        if (HomeXraySubDataLeftText != null) HomeXraySubDataLeftText.Text = bestData;
                        if (HomeXraySubTimeLeftText != null) HomeXraySubTimeLeftText.Text = bestDays;
                        if (HomeXraySubEmptyText != null) HomeXraySubEmptyText.Visibility = Visibility.Collapsed;
                        if (HomeXraySubActiveBadge != null) HomeXraySubActiveBadge.Visibility = Visibility.Visible;
                        if (HomeXraySubDetailPanel != null) HomeXraySubDetailPanel.Visibility = Visibility.Visible;
                        if (HomeXraySubRefreshBtn != null) HomeXraySubRefreshBtn.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        if (HomeXraySubActiveBadge != null) HomeXraySubActiveBadge.Visibility = Visibility.Collapsed;
                        if (HomeXraySubDetailPanel != null) HomeXraySubDetailPanel.Visibility = Visibility.Collapsed;
                        if (HomeXraySubEmptyText != null) HomeXraySubEmptyText.Visibility = Visibility.Visible;
                    }
                }

                // 3. Fallback for VPN sub card if needed
                if (!_hasSubscriptionSummary && hasActive)
                {
                    if (SubDataLeftText != null) SubDataLeftText.Text = bestData;
                    if (SubTimeLeftText != null) SubTimeLeftText.Text = bestDays;
                    if (SubEmptyText != null) SubEmptyText.Visibility = Visibility.Collapsed;
                    if (SubActiveBadge != null) SubActiveBadge.Visibility = Visibility.Visible;
                    if (SubDetailPanel != null) SubDetailPanel.Visibility = Visibility.Visible;
                    if (SubRefreshBtn != null) SubRefreshBtn.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private void XraySubSummaryCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            XrayUpdateSub_Click(sender, e);
        }

        private void HomeXraySubCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            XrayUpdateSub_Click(sender, e);
        }

        private void HomeXraySubRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            XrayUpdateSub_Click(sender, e);
        }

        private void StartSubRefreshAnimation()
        {
            try
            {
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.8)))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                XraySubRefreshRotate?.BeginAnimation(RotateTransform.AngleProperty, anim);
                HomeXraySubRefreshRotate?.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
            catch { }
        }

        private void StopSubRefreshAnimation()
        {
            try
            {
                XraySubRefreshRotate?.BeginAnimation(RotateTransform.AngleProperty, null);
                HomeXraySubRefreshRotate?.BeginAnimation(RotateTransform.AngleProperty, null);
            }
            catch { }
        }

        private async Task PingSingleProfileAsync(ProxyProfile p)
        {
            p.Delay = "Pinging...";
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect(p.Address, p.Port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                    if (!success) { p.Delay = "Timeout"; return; }
                    client.EndConnect(result);
                }
                watch.Stop();
                p.Delay = $"{watch.ElapsedMilliseconds} ms";
            }
            catch
            {
                p.Delay = "Error";
            }
        }

        private async Task PingGroupAsync(SubscriptionGroup group)
        {
            var tasks = group.Profiles.Select(p => PingSingleProfileAsync(p));
            await Task.WhenAll(tasks);
        }

        private void XrayLoadData()
        {
            var loadedGroups = SubscriptionGroupManager.LoadGroups();
            XrayGroups.Clear();
            foreach (var g in loadedGroups) XrayGroups.Add(g);

            XrayProxies.Clear();
            foreach (var p in XrayGroups.SelectMany(g => g.Profiles))
            {
                XrayProxies.Add(p);
            }

            if (_xraySelectedProfile == null && XrayProxies.Count > 0)
            {
                _xraySelectedProfile = XrayProxies.FirstOrDefault();
            }

            if (_xraySelectedProfile != null)
            {
                UpdateActiveProfileInfo(_xraySelectedProfile);
                XrayProxyList.SelectedItem = _xraySelectedProfile;
            }

            UpdateXraySubscriptionCard();
            RenderXrayConnList();
        }

        private void XrayAddSub_Click(object sender, RoutedEventArgs e)
        {
            if (XraySubNameInput != null) XraySubNameInput.Text = "";
            if (XraySubUrlInput != null) XraySubUrlInput.Text = "";
            XraySubOverlay.Visibility = Visibility.Visible;
            XraySubUrlInput?.Focus();
        }

        private async void XraySubSubmit_Click(object sender, RoutedEventArgs e)
        {
            string url = XraySubUrlInput.Text.Trim();
            string name = XraySubNameInput?.Text.Trim() ?? "";

            if (string.IsNullOrEmpty(url))
            {
                ShowInAppMessage("لطفاً لینک سابسکریپشن یا کانکشن معتبر وارد کنید.", "خطای ورودی");
                return;
            }

            XraySubOverlay.Visibility = Visibility.Collapsed;

            if (url.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("hysteria://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var p = UrlParser.Parse(url);
                    var customGroup = XrayGroups.FirstOrDefault(g => g.Name == "کانکشن‌های من" || g.Name == "سفارشی");
                    if (customGroup == null)
                    {
                        customGroup = new SubscriptionGroup
                        {
                            Name = !string.IsNullOrWhiteSpace(name) ? name : "کانکشن‌های من",
                            Url = ""
                        };
                        XrayGroups.Add(customGroup);
                    }
                    p.GroupId = customGroup.Id;
                    p.GroupName = customGroup.Name;
                    customGroup.Profiles.Add(p);
                    SubscriptionGroupManager.SaveGroups(XrayGroups);
                    XrayLoadData();
                    ShowInAppMessage($"کانکشن «{p.Alias}» با موفقیت اضافه شد.", "افزودن کانکشن");
                    return;
                }
                catch (Exception ex)
                {
                    ShowInAppMessage($"خطا در خواندن کانکشن:\n{ex.Message}", "خطا");
                    return;
                }
            }

            try
            {
                StartSubRefreshAnimation();
                var (proxies, userInfo, suggestedTitle, error) = await SubscriptionManager.FetchSubscriptionAsync(url);
                if (!string.IsNullOrEmpty(error))
                {
                    ShowInAppMessage($"خطا در دریافت اشتراک:\n{error}", "خطای اشتراک");
                    return;
                }

                if (proxies == null || proxies.Count == 0)
                {
                    ShowInAppMessage("هیچ سرور معتبری در لینک اشتراک یافت نشد.", "اطلاع");
                    return;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    if (!string.IsNullOrWhiteSpace(suggestedTitle)) name = suggestedTitle;
                    else
                    {
                        try { name = new Uri(url).Host; }
                        catch { name = $"اشتراک {XrayGroups.Count + 1}"; }
                    }
                }

                var newGroup = new SubscriptionGroup
                {
                    Name = name,
                    Url = url,
                    LastUpdated = DateTime.Now
                };

                foreach (var p in proxies)
                {
                    p.GroupId = newGroup.Id;
                    p.GroupName = newGroup.Name;
                    newGroup.Profiles.Add(p);
                }

                if (userInfo != null)
                {
                    if (userInfo.Total > 0)
                    {
                        long used = userInfo.Upload + userInfo.Download;
                        long leftBytes = Math.Max(0, userInfo.Total - used);
                        double leftGb = leftBytes / (1024.0 * 1024.0 * 1024.0);
                        double totalGb = userInfo.Total / (1024.0 * 1024.0 * 1024.0);
                        newGroup.DataRemaining = $"{leftGb:0.#} GB / {totalGb:0.#} GB";
                        newGroup.TotalData = $"{totalGb:0.#} GB";
                    }
                    if (userInfo.ExpireDateTimestamp > 0)
                    {
                        var exp = DateTimeOffset.FromUnixTimeSeconds(userInfo.ExpireDateTimestamp).ToLocalTime();
                        var daysLeft = (exp - DateTimeOffset.Now).TotalDays;
                        newGroup.DaysRemaining = daysLeft > 0 ? $"{(int)daysLeft} روز" : "منقضی شده";
                    }
                }

                XrayGroups.Add(newGroup);
                SubscriptionGroupManager.SaveGroups(XrayGroups);
                XrayLoadData();
                ShowInAppMessage($"اشتراک «{name}» با موفقیت افزوده شد.\nتعداد {proxies.Count} کانکشن دریافت گردید.", "افزودن اشتراک");
            }
            catch (Exception ex)
            {
                ShowInAppMessage($"خطا در برقراری ارتباط:\n{ex.Message}", "خطا");
            }
            finally
            {
                StopSubRefreshAnimation();
            }
        }

        private async void XrayUpdateSub_Click(object sender, RoutedEventArgs e)
        {
            var subGroups = XrayGroups.Where(g => !string.IsNullOrWhiteSpace(g.Url)).ToList();
            if (subGroups.Count == 0)
            {
                XrayAddSub_Click(sender, e);
                return;
            }

            try
            {
                StartSubRefreshAnimation();
                int totalServers = 0;
                foreach (var group in subGroups)
                {
                    var (proxies, userInfo, _, _) = await SubscriptionManager.FetchSubscriptionAsync(group.Url);
                    if (proxies != null && proxies.Count > 0)
                    {
                        group.Profiles.Clear();
                        foreach (var p in proxies)
                        {
                            p.GroupId = group.Id;
                            p.GroupName = group.Name;
                            group.Profiles.Add(p);
                        }
                        group.LastUpdated = DateTime.Now;
                        totalServers += proxies.Count;

                        if (userInfo != null)
                        {
                            if (userInfo.Total > 0)
                            {
                                long used = userInfo.Upload + userInfo.Download;
                                long leftBytes = Math.Max(0, userInfo.Total - used);
                                double leftGb = leftBytes / (1024.0 * 1024.0 * 1024.0);
                                double totalGb = userInfo.Total / (1024.0 * 1024.0 * 1024.0);
                                group.DataRemaining = $"{leftGb:0.#} GB / {totalGb:0.#} GB";
                                group.TotalData = $"{totalGb:0.#} GB";
                            }
                            if (userInfo.ExpireDateTimestamp > 0)
                            {
                                var exp = DateTimeOffset.FromUnixTimeSeconds(userInfo.ExpireDateTimestamp).ToLocalTime();
                                var daysLeft = (exp - DateTimeOffset.Now).TotalDays;
                                group.DaysRemaining = daysLeft > 0 ? $"{(int)daysLeft} روز" : "منقضی شده";
                            }
                        }
                    }
                }

                SubscriptionGroupManager.SaveGroups(XrayGroups);
                XrayLoadData();
                ShowInAppMessage($"بروزرسانی تمام ساب‌اسکریپشن‌ها انجام شد.\nمجموعاً {totalServers} کانکشن فعال دریافت گردید.", "بروزرسانی اشتراک");
            }
            catch (Exception ex)
            {
                ShowInAppMessage($"خطا در بروزرسانی:\n{ex.Message}", "خطا");
            }
            finally
            {
                StopSubRefreshAnimation();
            }
        }

        private async Task UpdateSubscriptionGroupAsync(SubscriptionGroup group)
        {
            if (string.IsNullOrWhiteSpace(group.Url)) return;
            try
            {
                StartSubRefreshAnimation();
                var (proxies, userInfo, suggestedTitle, error) = await SubscriptionManager.FetchSubscriptionAsync(group.Url);
                if (!string.IsNullOrEmpty(error))
                {
                    ShowInAppMessage($"خطا در بروزرسانی گروه «{group.Name}»:\n{error}", "خطا");
                    return;
                }

                if (proxies != null && proxies.Count > 0)
                {
                    group.Profiles.Clear();
                    foreach (var p in proxies)
                    {
                        p.GroupId = group.Id;
                        p.GroupName = group.Name;
                        group.Profiles.Add(p);
                    }
                    group.LastUpdated = DateTime.Now;

                    if (userInfo != null)
                    {
                        if (userInfo.Total > 0)
                        {
                            long used = userInfo.Upload + userInfo.Download;
                            long leftBytes = Math.Max(0, userInfo.Total - used);
                            double leftGb = leftBytes / (1024.0 * 1024.0 * 1024.0);
                            double totalGb = userInfo.Total / (1024.0 * 1024.0 * 1024.0);
                            group.DataRemaining = $"{leftGb:0.#} GB / {totalGb:0.#} GB";
                            group.TotalData = $"{totalGb:0.#} GB";
                        }
                        if (userInfo.ExpireDateTimestamp > 0)
                        {
                            var exp = DateTimeOffset.FromUnixTimeSeconds(userInfo.ExpireDateTimestamp).ToLocalTime();
                            var daysLeft = (exp - DateTimeOffset.Now).TotalDays;
                            group.DaysRemaining = daysLeft > 0 ? $"{(int)daysLeft} روز" : "منقضی شده";
                        }
                    }

                    SubscriptionGroupManager.SaveGroups(XrayGroups);
                    XrayLoadData();
                    ShowInAppMessage($"اشتراک «{group.Name}» با موفقیت بروزرسانی شد.\nتعداد {proxies.Count} کانکشن دریافت گردید.", "بروزرسانی موفق");
                }
            }
            catch (Exception ex)
            {
                ShowInAppMessage($"خطا در دریافت اطلاعات: {ex.Message}", "خطا");
            }
            finally
            {
                StopSubRefreshAnimation();
            }
        }

        private void XraySubCancel_Click(object sender, RoutedEventArgs e)
        {
            XraySubOverlay.Visibility = Visibility.Collapsed;
        }

        private void XraySubOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            XraySubOverlay.Visibility = Visibility.Collapsed;
        }

        private void XraySubModal_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void XraySubPaste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    XraySubUrlInput.Text = Clipboard.GetText().Trim();
                }
            }
            catch { }
        }

        private void XraySubUrlInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                XraySubSubmit_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                XraySubCancel_Click(this, new RoutedEventArgs());
            }
        }

        public void ShowInAppMessage(string text, string title = "پیام سیستم")
        {
            InAppMsgTitle.Text = title;
            InAppMsgText.Text = text;
            InAppMessageOverlay.Visibility = Visibility.Visible;
        }

        private void InAppMsgOverlay_Dismiss(object sender, RoutedEventArgs e)
        {
            InAppMessageOverlay.Visibility = Visibility.Collapsed;
        }

        private void InAppMsgBox_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        public void XrayBtnPower_Click(object sender, RoutedEventArgs e)
        {
            if (_xrayIsConnected)
            {
                XrayConnectBtn_Click(this, new RoutedEventArgs());
                return;
            }

            if (XrayProxyList.SelectedItem == null && _xraySelectedProfile != null)
            {
                XrayProxyList.SelectedItem = _xraySelectedProfile;
            }

            if (XrayProxyList.SelectedItem == null && XrayProxies.Count > 0)
            {
                XrayProxyList.SelectedIndex = 0;
                _xraySelectedProfile = XrayProxies[0];
            }

            if (XrayProxyList.SelectedItem != null || _xraySelectedProfile != null)
            {
                XrayConnectBtn_Click(this, new RoutedEventArgs());
            }
            else
            {
                ShowInAppMessage("لطفاً یک کانکشن از لیست انتخاب کنید یا از دکمه «افزودن ساب» استفاده نمایید.", "راهنمای اتصال");
            }
        }

        private void XrayProxyList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (XrayProxyList.SelectedItem is ProxyProfile profile)
            {
                if (_xrayIsConnected && XrayTxtActiveName?.Text == profile.Alias) return;
                MainWindow.LastConnectionType = "xray";
                XrayConnectBtn_Click(this, new RoutedEventArgs());
            }
        }

        private async void XrayConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_xrayIsConnected)
            {
                var item = _xraySelectedProfile ?? XrayProxyList.SelectedItem as ProxyProfile;
                if (item != null)
                {
                    MainWindow.LastConnectionType = "xray";
                    if (_engine.IsRunning)
                    {
                        if (sender != null && sender != this)
                        {
                            var q = string.Format(MsgSwitchConfirm, ActiveConnText.Text, item.Alias);
                            if (!AskDialog.Confirm(this, q)) return;
                        }
                        await StopManuallyAsync();
                        for (int i = 0; i < 20 && _engine.IsRunning; i++)
                            await Task.Delay(150);
                    }

                    // Show connecting state
                    try
                    {
                        StartSpin();
                        SetStatusText("CONNECTING...");
                        PowerHintText.Text = Localization.T("در حال برقراری ارتباط با هسته...");
                        SetPowerState("connecting");
                    }
                    catch { }

                    string stMode = _config.SplitTunnelMode;
                    var stList = _config.SplitTunnelList;
                    string dnsMode = _config.DnsMode;
                    string dnsP = _config.DnsPrimary;
                    string dnsS = _config.DnsSecondary;

                    if (stMode == "deny" && stList.Count > 0)
                    {
                        AppendConnLog($"[sing-box] تونل برنامه‌ها (Bypass): تمام ترافیک از پروکسی عبور می‌کند به جز {stList.Count} برنامه لیست.");
                    }
                    else if (stMode == "allow" && stList.Count > 0)
                    {
                        AppendConnLog($"[sing-box] تونل برنامه‌ها (Allow): فقط {stList.Count} برنامه انتخاب‌شده از پروکسی عبور می‌کنند.");
                    }
                    else
                    {
                        AppendConnLog("[sing-box] تونل سراسری: تمام برنامه‌ها از هسته عبور می‌کنند.");
                    }

                    var (success, error) = await System.Threading.Tasks.Task.Run(() =>
                        XrayEngine.Start(item, stMode, stList, dnsMode, dnsP, dnsS));

                    if (!success)
                    {
                        SetXrayUiDisconnected();
                        ShowInAppMessage($"خطا در راه‌اندازی هسته sing-box:\n{error}", "خطای اتصال");
                        AppendConnLog($"[sing-box ERROR] اتصال برقرار نشد: {error}");
                        return;
                    }

                    SetXrayUiConnected(item);
                }
            }
            else
            {
                XrayEngine.Stop();
                SetXrayUiDisconnected();
                AppendConnLog("[sing-box] اتصال قطع گردید.");
            }
        }

        private void SetXrayUiConnected(ProxyProfile item)
        {
            _xrayIsConnected = true;
            _xrayActiveProfile = item;
            MainWindow.LastConnectionType = "xray";

            try
            {
                StopSpin();
                StartPulse();
                SetStatusText("XRAY CONNECTED");
                PowerHintText.Text = Localization.T("برای قطع اتصال کلیک کنید");
                SetPowerState("connected");
                Notify("Connected via sing-box: " + item.Alias);
                RenderXrayConnList();
                _config.AddRecentConnection(item.Alias);
                RenderRecentServersList();
            }
            catch { }

            string protoFull = $"{item.Protocol.ToUpper()} {item.Network.ToUpper()}";
            ServerSubText.Text = protoFull;
            ActiveConnText.Text = item.Alias;
            UpdateActiveBadge(item.Alias, item.Protocol);
            GeoIpText.Text = $"Connected — {item.Address}";
            if (MiniConnectionText != null) MiniConnectionText.Text = item.Alias;

            if (XrayTxtActiveName != null) XrayTxtActiveName.Text = item.Alias;
            string sec = !string.IsNullOrEmpty(item.Tls) ? $" + {item.Tls.ToUpper()}" : "";
            if (XrayTxtActiveProtocol != null) XrayTxtActiveProtocol.Text = $"{item.Protocol.ToUpper()} / {item.Network.ToUpper()}{sec}";
            
            _xrayExitIp = null;
            if (XrayTxtActiveGeoIP != null) XrayTxtActiveGeoIP.Text = Localization.T("در حال شناسایی موقعیت...");
            GeoIpText.Text = Localization.T("در حال شناسایی موقعیت...");
            _ = FetchXrayGeoIpAsync();

            if (XrayStatusText != null) { XrayStatusText.Text = Localization.T("متصل"); XrayStatusText.Visibility = Visibility.Visible; }
            if (StatusText != null) { StatusText.Text = Localization.T("متصل"); StatusText.Visibility = Visibility.Visible; }
            if (XrayStatusDot != null) { XrayStatusDot.Visibility = Visibility.Visible; XrayStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); }
            if (StatusDot != null) { StatusDot.Visibility = Visibility.Visible; StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)); }
            if (XrayBtnPower != null) XrayBtnPower.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            if (PowerBtn != null) PowerBtn.Background = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            if (PowerStateText != null) PowerStateText.Text = "ON";

            // Populate all 6 detail cards
            string protoTextVal = $"{item.Protocol.ToUpper()} / {item.Network.ToUpper()}{sec}";
            string userVal = !string.IsNullOrEmpty(item.UserId) ? (item.UserId.Length > 12 ? item.UserId.Substring(0, 10) + "..." : item.UserId) : "sing-box";
            if (XrayProtocolText != null) XrayProtocolText.Text = protoTextVal;
            if (ProtocolText != null) ProtocolText.Text = protoTextVal;
            if (XrayYouText != null) XrayYouText.Text = userVal;
            if (YouText != null) YouText.Text = userVal;

            var tun = FindSingBoxTunInterface();
            string tunIp = "172.19.0.1 (TUN)";
            if (tun != null)
            {
                try
                {
                    foreach (var u in tun.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            tunIp = $"{u.Address} (TUN)";
                            break;
                        }
                    }
                }
                catch { }
            }
            if (XrayPrivateIpText != null) XrayPrivateIpText.Text = tunIp;
            if (PrivateIpText != null) PrivateIpText.Text = tunIp;
            if (XrayServerIpText != null) XrayServerIpText.Text = $"{item.Address}:{item.Port}";
            if (ServerIpText != null) ServerIpText.Text = $"{item.Address}:{item.Port}";

            // Resolve server destination IP asynchronously
            _ = ResolveXrayServerIpAsync(item);

            // Traffic, speed and ping initialization
            _xrayStartTime = DateTime.Now;
            _xrayLastDown = -1;
            _xrayLastUp = -1;
            _xraySessionStartTotalBytes = GetXrayTrafficBytes(true) + GetXrayTrafficBytes(false);
            _xrayDlHistory.Clear();
            _xrayUlHistory.Clear();
            _xrayPingTick = 0;
            _xrayPingInFlight = false;

            DurationText.Text = "00:00:00";
            if (XrayTxtDuration != null) XrayTxtDuration.Text = "00:00:00";
            if (XraySessionTrafficText != null) XraySessionTrafficText.Text = "0 B";
            if (XrayPingText != null) XrayPingText.Text = "—";
            if (PingText != null) PingText.Text = "—";

            _xrayTimer.Start();
            try { HsOnVpnConnected(); } catch { }

            // Initial ping
            _ = UpdateXrayPingAsync(item);
        }

        private void SetXrayUiDisconnected()
        {
            _xrayIsConnected = false;
            _xrayActiveProfile = null;
            _xrayExitIp = null;
            _xrayGeoGen++;
            _xrayTimer.Stop();

            try
            {
                StopPulse();
                StopSpin();
                SetStatusText(TxtReady);
                PowerHintText.Text = Localization.T("برای اتصال کلیک کنید");
                SetPowerState("disconnected");
                SetLocked(false);
                RenderXrayConnList();
            }
            catch { }

            ServerSubText.Text = "Select a Server";
            ActiveConnText.Text = "Disconnected";
            GeoIpText.Text = "No Location — 0.0.0.0";
            if (MiniConnectionText != null) MiniConnectionText.Text = "—";

            if (XrayProxyList.SelectedItem is ProxyProfile sel)
            {
                if (XrayTxtActiveName != null) XrayTxtActiveName.Text = sel.Alias;
                string sec = !string.IsNullOrEmpty(sel.Tls) ? $" + {sel.Tls.ToUpper()}" : "";
                if (XrayTxtActiveProtocol != null) XrayTxtActiveProtocol.Text = $"{sel.Protocol.ToUpper()} / {sel.Network.ToUpper()}{sec}";
                if (XrayTxtActiveGeoIP != null) XrayTxtActiveGeoIP.Text = $"{sel.Address}:{sel.Port}";
                if (XrayServerIpText != null) XrayServerIpText.Text = $"{sel.Address}:{sel.Port}";
                if (XrayProtocolText != null) XrayProtocolText.Text = $"{sel.Protocol.ToUpper()} / {sel.Network.ToUpper()}";
                if (XrayYouText != null) XrayYouText.Text = !string.IsNullOrEmpty(sel.UserId) ? (sel.UserId.Length > 12 ? sel.UserId.Substring(0, 10) + "..." : sel.UserId) : "sing-box";
            }
            else
            {
                if (XrayTxtActiveName != null) XrayTxtActiveName.Text = Localization.T("یک سرور انتخاب کنید");
                if (XrayTxtActiveProtocol != null) XrayTxtActiveProtocol.Text = "";
                if (XrayTxtActiveGeoIP != null) XrayTxtActiveGeoIP.Text = Localization.T("آماده اتصال");
                if (XrayServerIpText != null) XrayServerIpText.Text = "—";
                if (XrayProtocolText != null) XrayProtocolText.Text = "—";
                if (XrayYouText != null) XrayYouText.Text = "—";
            }

            if (PrivateIpText != null) PrivateIpText.Text = "—";
            if (ServerIpText != null) ServerIpText.Text = "—";
            if (ProtocolText != null) ProtocolText.Text = "—";
            if (YouText != null) YouText.Text = "—";
            if (XrayPrivateIpText != null) XrayPrivateIpText.Text = "—";
            if (XrayStatusText != null) { XrayStatusText.Text = Localization.T("آماده اتصال"); XrayStatusText.Visibility = Visibility.Collapsed; }
            if (StatusText != null) { StatusText.Text = Localization.T("آماده اتصال"); }
            if (XrayStatusDot != null) XrayStatusDot.Visibility = Visibility.Collapsed;
            if (StatusDot != null) StatusDot.Visibility = Visibility.Collapsed;
            if (XrayBtnPower != null) XrayBtnPower.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
            if (PowerBtn != null) PowerBtn.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
            if (PowerStateText != null) PowerStateText.Text = "OFF";

            DurationText.Text = "00:00:00";
            if (XrayTxtDuration != null) XrayTxtDuration.Text = "00:00:00";
            if (XrayPingText != null) XrayPingText.Text = "—";
            if (PingText != null)
            {
                PingText.Text = "—";
                PingText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            }
            if (XrayDlSpeedText != null) XrayDlSpeedText.Text = "↓ 0 B/s";
            if (XrayUlSpeedText != null) XrayUlSpeedText.Text = "↑ 0 B/s";
            if (DlSpeedText != null) DlSpeedText.Text = "↓ 0 B/s";
            if (UlSpeedText != null) UlSpeedText.Text = "↑ 0 B/s";
            if (XraySessionTrafficText != null) XraySessionTrafficText.Text = "0 B";
            if (SessionTrafficText != null) SessionTrafficText.Text = "0 B";

            _xrayDlHistory.Clear();
            _xrayUlHistory.Clear();
            RedrawXrayGraph();
            try { HsCheckVpnState(); } catch { }
            try { HsHandleVpnDisconnect(); } catch { }
        }

        private void XrayDeleteSingle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProxyProfile profile)
            {
                if (_xrayIsConnected && _xrayActiveProfile == profile)
                {
                    ShowInAppMessage("امکان حذف کانکشنی که در حال حاضر متصل است وجود ندارد.\nابتدا اتصال را قطع کنید.", "هشدار");
                    return;
                }

                if (!AskDialog.Confirm(this, $"آیا از حذف کانکشن «{profile.Alias}» اطمینان دارید؟", "حذف کانکشن", "انصراف"))
                    return;

                XrayProxies.Remove(profile);
                ProfileManager.SaveProfiles(XrayProxies);

                if (XrayProxyList.SelectedItem == profile)
                {
                    XrayProxyList.SelectedItem = XrayProxies.FirstOrDefault();
                }
            }
        }

        private void XrayClearServers_Click(object sender, RoutedEventArgs e)
        {
            if (XrayProxies.Count == 0)
            {
                ShowInAppMessage("هیچ سروری در لیست وجود ندارد.", "اطلاع");
                return;
            }

            if (_xrayIsConnected)
            {
                ShowInAppMessage("برای پاک کردن سرورها، ابتدا اتصال فعلی را قطع کنید.", "هشدار");
                return;
            }

            var choice = AskDialog.Choose(this,
                "لطفاً عملیات مورد نظر را انتخاب کنید:",
                "پاک کردن تمام سرورها",
                "حذف سرور انتخاب‌شده",
                "انصراف");

            if (choice == 0) // Delete all
            {
                if (AskDialog.Confirm(this, "آیا مطمئن هستید که می‌خواهید تمام سرورها را حذف کنید؟", "بله، حذف همه", "انصراف"))
                {
                    XrayProxies.Clear();
                    ProfileManager.SaveProfiles(XrayProxies);
                    SetXrayUiDisconnected();
                    ShowInAppMessage("تمام سرورها با موفقیت حذف شدند.", "حذف سرورها");
                }
            }
            else if (choice == 1) // Delete selected
            {
                if (XrayProxyList.SelectedItem is ProxyProfile sel)
                {
                    XrayProxies.Remove(sel);
                    ProfileManager.SaveProfiles(XrayProxies);
                    XrayProxyList.SelectedItem = XrayProxies.FirstOrDefault();
                }
                else
                {
                    ShowInAppMessage("هیچ سروری انتخاب نشده است.", "اطلاع");
                }
            }
        }

        private async void XrayPingAll_Click(object sender, RoutedEventArgs e)
        {
            var tasks = XrayProxies.Select(async p => 
            {
                p.Delay = "Pinging...";
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using (var client = new System.Net.Sockets.TcpClient())
                    {
                        var result = client.BeginConnect(p.Address, p.Port, null, null);
                        var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                        if (!success) {
                            p.Delay = "Timeout";
                            return;
                        }
                        client.EndConnect(result);
                    }
                    watch.Stop();
                    p.Delay = $"{watch.ElapsedMilliseconds} ms";
                }
                catch
                {
                    p.Delay = "Error";
                }
            });
            await System.Threading.Tasks.Task.WhenAll(tasks);
        }

        private void MenuItem_Connect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.DataContext is ProxyProfile profile)
            {
                if (_engine.IsRunning || _xrayIsConnected)
                {
                    if (_xrayIsConnected && ActiveConnText.Text == profile.Alias) return;
                    string activeName = _xrayIsConnected ? ActiveConnText.Text : ActiveConnText.Text;
                    var q = string.Format(MsgSwitchConfirm, activeName, profile.Alias);
                    if (!AskDialog.Confirm(this, q)) return;
                    
                    if (_engine.IsRunning) { _ = StopManuallyAsync(); }
                    if (_xrayIsConnected) { XrayConnectBtn_Click(this, new RoutedEventArgs()); }
                }

                XrayProxyList.SelectedItem = profile;
                XrayConnectBtn_Click(this, new RoutedEventArgs());
            }
        }

        private async void MenuItem_Ping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.DataContext is ProxyProfile p)
            {
                p.Delay = "Pinging...";
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    using (var client = new System.Net.Sockets.TcpClient())
                    {
                        var result = client.BeginConnect(p.Address, p.Port, null, null);
                        var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));
                        if (!success) { p.Delay = "Timeout"; return; }
                        client.EndConnect(result);
                    }
                    watch.Stop();
                    p.Delay = $"{watch.ElapsedMilliseconds} ms";
                }
                catch { p.Delay = "Error"; }
            }
        }

        private void MenuItem_Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.DataContext is ProxyProfile profile)
            {
                var editWin = new EditWindow(profile) { Owner = this };
                if (editWin.ShowDialog() == true) ProfileManager.SaveProfiles(XrayProxies);
            }
        }

        private void MenuItem_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.DataContext is ProxyProfile profile)
            {
                if (_xrayIsConnected && XrayProxyList.SelectedItem == profile)
                {
                    ShowInAppMessage("امکان حذف کانکشن فعال وجود ندارد.", "هشدار");
                    return;
                }
                XrayProxies.Remove(profile);
                ProfileManager.SaveProfiles(XrayProxies);
            }
        }
    
        private void XrayProxyContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                fe.ContextMenu.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
                foreach (var item in fe.ContextMenu.Items)
                {
                    if (item is MenuItem mi && mi.Header is string h)
                    {
                        mi.Header = Localization.T(h);
                    }
                }
            }
        }

        private void XrayMoreInfoToggle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var open = XrayMoreInfoPanel.Visibility != System.Windows.Visibility.Visible;
            XrayMoreInfoArrow.Text = open ? "⌃" : "⌄";
            XrayMoreInfoText.Text = open ? Localization.T("بستن جزئیات") : Localization.T("جزئیات اتصال");
            XrayMoreInfoPanel.Visibility = open ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        // ================= اشتراک‌گذاری کانکشن Xray (کد QR و لینک) =================
        private ProxyProfile? _currentShareProfile;

        private void XrayShareSingle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProxyProfile p)
            {
                ShowXrayShareModal(p);
            }
        }

        private void MenuItem_Share_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is ProxyProfile p)
            {
                ShowXrayShareModal(p);
            }
            else if (XrayProxyList.SelectedItem is ProxyProfile sel)
            {
                ShowXrayShareModal(sel);
            }
        }

        private void ShowXrayShareModal(ProxyProfile profile)
        {
            _currentShareProfile = profile;
            XrayShareProtoBadge.Text = (profile.Protocol ?? "VLESS").ToUpperInvariant();
            XrayShareAliasText.Text = profile.Alias;
            XrayShareAddressText.Text = $"{profile.Address}:{profile.Port}";

            string shareUrl = !string.IsNullOrWhiteSpace(profile.FullUrl)
                ? profile.FullUrl
                : $"vless://{profile.UserId}@{profile.Address}:{profile.Port}#{Uri.EscapeDataString(profile.Alias)}";

            try
            {
                using var qrGen = new QRCoder.QRCodeGenerator();
                using var qrData = qrGen.CreateQrCode(shareUrl, QRCoder.QRCodeGenerator.ECCLevel.M);
                using var qrCode = new QRCoder.PngByteQRCode(qrData);

                using var ms = new MemoryStream(qrCode.GetGraphic(20));
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                XrayShareQrImage.Source = bitmap;
            }
            catch { }

            XrayShareCopyBtn.Content = Localization.T("📋 کپی لینک کانکشن");
            XrayShareOverlay.Visibility = Visibility.Visible;
        }

        private void XrayShareClose_Click(object sender, RoutedEventArgs e)
        {
            XrayShareOverlay.Visibility = Visibility.Collapsed;
        }

        private void XrayShareOverlay_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            XrayShareOverlay.Visibility = Visibility.Collapsed;
        }

        private void XrayShareModal_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private async void XrayShareCopyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentShareProfile == null) return;
            string shareUrl = !string.IsNullOrWhiteSpace(_currentShareProfile.FullUrl)
                ? _currentShareProfile.FullUrl
                : $"vless://{_currentShareProfile.UserId}@{_currentShareProfile.Address}:{_currentShareProfile.Port}#{Uri.EscapeDataString(_currentShareProfile.Alias)}";

            try
            {
                Clipboard.SetText(shareUrl);
                XrayShareCopyBtn.Content = Localization.T("✓ کپی شد!");
                await Task.Delay(1500);
                XrayShareCopyBtn.Content = Localization.T("📋 کپی لینک کانکشن");
            }
            catch { }
        }
    }
}





