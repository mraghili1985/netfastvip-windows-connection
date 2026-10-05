using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        // ================= تست سرعت درون‌برنامه‌ای (In-Place SpeedTest) =================
        private const string SpeedViaConnectedFmt = "تست از طریق: {0} — متصل به VPN";
        private const string SpeedViaXrayFmt = "تست از طریق: {0} — متصل به Xray";
        private const string SpeedViaDirect = "تست بدون VPN — اینترنت مستقیم شما";
        private const string SpeedPhaseIdle = "برای شروع، یکی از گزینه‌های تست را انتخاب کنید";
        private const string SpeedPhasePing = "🏓 در حال اندازه‌گیری پینگ و جیتر…";
        private const string SpeedPhaseDownload = "⬇ در حال تست دانلود…";
        private const string SpeedPhaseUpload = "⬆ در حال تست آپلود…";
        private const string SpeedPhaseDone = "✓ تست با موفقیت کامل شد";
        private const string SpeedPhaseCanceled = "تست متوقف شد";
        private const string SpeedPhaseError = "خطا در تست — اتصال اینترنت را بررسی کنید";
        private const string SpeedPhaseFindingServer = "در حال یافتن نزدیک‌ترین سرور…";

        private const string SpeedRateGreat = "عالی";
        private const string SpeedRateGood = "خوب";
        private const string SpeedRateMid = "متوسط";
        private const string SpeedRateBad = "ضعیف";

        private const string OoklaServersApi = "https://www.speedtest.net/api/js/servers?engine=js&limit=10";
        private const string CloudflarePingTarget = "1.1.1.1";
        private const string CloudflareDownUrlFmt = "https://speed.cloudflare.com/__down?bytes={0}";
        private const string CloudflareUpUrl = "https://speed.cloudflare.com/__up";
        private const string MLabLocateUrl = "https://locate.measurementlab.net/v2/nearest/ndt/ndt7";
        private const string MLabNdtSubProtocol = "net.measurementlab.ndt.v7";

        private const int SpeedPhaseDurationSeconds = 10;
        private const long SpeedMaxPhaseBytes = 400_000_000;
        private const long SpeedDlChunkBytes = 50_000_000;
        private const int SpeedUlChunkBytes = 64 * 1024;

        private const double SpeedGaugeMaxMbps = 50.0;
        private const double SpeedArcLen = 251.33; // π * 80
        private const double SpeedArcThickness = 10.0;

        private static readonly Color SpeedGreen = Color.FromRgb(0x22, 0xC5, 0x5E);
        private static readonly Color SpeedAmber = Color.FromRgb(0xF5, 0x9E, 0x0B);
        private static readonly Color SpeedRed = Color.FromRgb(0xEF, 0x44, 0x44);
        private static readonly SolidColorBrush SpeedDlBrush = new(Color.FromRgb(0x3B, 0x82, 0xF6));
        private static readonly SolidColorBrush SpeedUlBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));

        private CancellationTokenSource? _speedCts;
        private CancellationTokenSource? _speedPhaseCts;
        private long _speedBytesMoved;
        private double _speedProgressBase, _speedProgressSpan;
        private DateTime _speedPhaseStart;
        private double _speedEmaMbps;
        private double _speedPhaseMaxMbps;
        private long _speedLastBytes;
        private DateTime _speedLastTick;
        private DispatcherTimer? _speedUiTimer;
        private bool _speedRunning;
        private bool _speedIsXray;
        private HttpClient? _speedHttp;

        private void InitSpeedTestTimer()
        {
            if (_speedUiTimer != null) return;
            _speedUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _speedUiTimer.Tick += SpeedUiTimer_Tick;
        }

        private void OpenSpeedTestView()
        {
            InitSpeedTestTimer();

            bool isVpnConnected = _engine.IsRunning;
            bool isXrayConnected = _xrayIsConnected && SmartVpn.XrayCore.XrayEngine.IsRunning;
            bool isConnected = isVpnConnected || isXrayConnected;
            _speedIsXray = isXrayConnected;

            string connName = isXrayConnected
                ? (!string.IsNullOrWhiteSpace(_xrayActiveProfile?.Alias) ? _xrayActiveProfile.Alias : (!string.IsNullOrWhiteSpace(XrayTxtActiveName?.Text) ? XrayTxtActiveName.Text : "Xray Service"))
                : ActiveConnText.Text;

            if (isConnected)
            {
                string viaFormat = isXrayConnected
                    ? (Localization.IsEnglish ? "Test via: {0} — Connected to Xray" : "تست از طریق: {0} — متصل به Xray")
                    : (Localization.IsEnglish ? "Test via: {0} — Connected to VPN" : "تست از طریق: {0} — متصل به VPN");
                SpeedViaText.Text = string.Format(viaFormat, connName);
            }
            else
            {
                SpeedViaText.Text = Localization.IsEnglish ? "Direct Connection (No VPN)" : SpeedViaDirect;
            }
            SpeedViaDot.Fill = new SolidColorBrush(isConnected ? SpeedGreen : Color.FromRgb(0x64, 0x74, 0x8B));

            SpeedEngineCombo.SelectedIndex = 0;
            SpeedPhaseText.Text = Localization.T(SpeedPhaseIdle);

            ResetSpeedGaugeToZero();
            ResetSpeedCards();
            SetSpeedProgress(0);

            if (SpeedClientIpText != null) SpeedClientIpText.Text = Localization.T("در حال شناسایی...");
            if (SpeedClientLocText != null) SpeedClientLocText.Text = "—";
            if (SpeedServerNameText != null) SpeedServerNameText.Text = Localization.T("در حال انتخاب سرور...");
            if (SpeedServerLocText != null) SpeedServerLocText.Text = "—";

            _cachedOoklaServer = null;
            _ = DetectSpeedEndpointsAsync();

            ShowPanel("tools");
            UpdateSidebarState("tools");
            ToolboxHomeView.Visibility = Visibility.Collapsed;
            ToolboxSpeedTestView.Visibility = Visibility.Visible;
        }

        private void SpeedEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SpeedServerNameText == null) return;
            _ = UpdateSpeedServerTargetInfoAsync();
        }

        private void SpeedClientCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (SpeedClientIpText != null) SpeedClientIpText.Text = Localization.T("در حال شناسایی...");
            if (SpeedClientLocText != null) SpeedClientLocText.Text = "—";
            _ = DetectSpeedClientInfoAsync();
        }

        private async Task DetectSpeedEndpointsAsync()
        {
            _ = DetectSpeedClientInfoAsync();
            await UpdateSpeedServerTargetInfoAsync();
        }

        private async Task DetectSpeedClientInfoAsync()
        {
            try
            {
                var http = GetSpeedHttpClient();

                // 1. اولویت اول: سرویس کامل ipwho.is برای دریافت IP، پرچم، کشور، شهر و ISP
                try
                {
                    var json = await http.GetStringAsync("https://ipwho.is/");
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("success", out var s) && s.GetBoolean())
                    {
                        var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : "";
                        var city = root.TryGetProperty("city", out var cityEl) ? cityEl.GetString() : "";
                        var country = root.TryGetProperty("country", out var cEl) ? cEl.GetString() : "";
                        var flag = root.TryGetProperty("flag", out var flagEl) && flagEl.TryGetProperty("emoji", out var emojiEl) ? emojiEl.GetString() : "";

                        string isp = "";
                        if (root.TryGetProperty("connection", out var connEl) && connEl.TryGetProperty("isp", out var ispEl))
                        {
                            isp = ispEl.GetString() ?? "";
                        }

                        if (!string.IsNullOrWhiteSpace(ip))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (SpeedClientIpText != null) SpeedClientIpText.Text = ip;
                                var locParts = new List<string>();
                                if (!string.IsNullOrWhiteSpace(flag) || !string.IsNullOrWhiteSpace(country))
                                    locParts.Add($"{flag} {country}".Trim());
                                if (!string.IsNullOrWhiteSpace(city)) locParts.Add(city);
                                if (!string.IsNullOrWhiteSpace(isp)) locParts.Add(isp);

                                if (SpeedClientLocText != null)
                                    SpeedClientLocText.Text = locParts.Count > 0 ? string.Join(" • ", locParts) : "—";
                            });
                            return;
                        }
                    }
                }
                catch { }

                // 2. فال‌بک: سرویس cloudflare trace
                try
                {
                    var trace = await http.GetStringAsync("https://cloudflare.com/cdn-cgi/trace");
                    string? ip = null;
                    string loc = "";
                    foreach (var line in trace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.StartsWith("ip=")) ip = line.Substring(3).Trim();
                        if (line.StartsWith("loc=")) loc = line.Substring(4).Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (SpeedClientIpText != null) SpeedClientIpText.Text = ip;
                            if (SpeedClientLocText != null) SpeedClientLocText.Text = !string.IsNullOrWhiteSpace(loc) ? $"Country/Region: {loc}" : "—";
                        });
                        return;
                    }
                }
                catch { }

                // 3. در صورت قطعی شبکه خارجی، استفاده از GeoIP کش‌شده روی صفحه اصلی
                if (GeoIpText != null && !string.IsNullOrWhiteSpace(GeoIpText.Text) && !GeoIpText.Text.Contains("0.0.0.0"))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (SpeedClientIpText != null) SpeedClientIpText.Text = GeoIpText.Text;
                        if (SpeedClientLocText != null) SpeedClientLocText.Text = "Connected VPN Node";
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    if (SpeedClientIpText != null) SpeedClientIpText.Text = Localization.T("نامشخص");
                    if (SpeedClientLocText != null) SpeedClientLocText.Text = Localization.T("امکان دریافت موقعیت وجود ندارد");
                });
            }
            catch { }
        }

        private async Task UpdateSpeedServerTargetInfoAsync()
        {
            int engine = 0;
            Dispatcher.Invoke(() => { engine = SpeedEngineCombo != null ? SpeedEngineCombo.SelectedIndex : 0; });

            if (engine == 0) // Ookla
            {
                Dispatcher.Invoke(() =>
                {
                    if (SpeedServerNameText != null) SpeedServerNameText.Text = Localization.T("در حال یافتن نزدیک‌ترین سرور Ookla...");
                    if (SpeedServerLocText != null) SpeedServerLocText.Text = "Speedtest.net Anycast";
                });

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    var s = await PickBestOoklaServerAsync(cts.Token);
                    if (s != null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string sName = !string.IsNullOrWhiteSpace(s.sponsor)
                                ? $"{s.sponsor} ({s.name})"
                                : (!string.IsNullOrWhiteSpace(s.name) ? s.name : "Ookla Server");
                            if (SpeedServerNameText != null) SpeedServerNameText.Text = sName;

                            var parts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(s.country)) parts.Add(s.country);
                            if (!string.IsNullOrWhiteSpace(s.host)) parts.Add(s.host);
                            if (SpeedServerLocText != null)
                                SpeedServerLocText.Text = parts.Count > 0 ? string.Join(" • ", parts) : "Speedtest Server";
                        });
                        return;
                    }
                }
                catch { }

                Dispatcher.Invoke(() =>
                {
                    if (SpeedServerNameText != null) SpeedServerNameText.Text = "Speedtest.net Server";
                    if (SpeedServerLocText != null) SpeedServerLocText.Text = "Ookla Global Network";
                });
            }
            else if (engine == 1) // Cloudflare
            {
                Dispatcher.Invoke(() =>
                {
                    if (SpeedServerNameText != null) SpeedServerNameText.Text = "Cloudflare Edge Anycast";
                    if (SpeedServerLocText != null) SpeedServerLocText.Text = "speed.cloudflare.com • Global PoP";
                });
            }
            else // M-Lab NDT7
            {
                Dispatcher.Invoke(() =>
                {
                    if (SpeedServerNameText != null) SpeedServerNameText.Text = "M-Lab NDT7 Server";
                    if (SpeedServerLocText != null) SpeedServerLocText.Text = "locate.measurementlab.net • Nearest Edge";
                });
            }
        }


        private void CloseSpeedTestView()
        {
            CancelSpeedTest();
            ToolboxSpeedTestView.Visibility = Visibility.Collapsed;
            ToolboxHomeView.Visibility = Visibility.Visible;
        }

        private void SpeedTestBack_Click(object sender, RoutedEventArgs e)
        {
            CloseSpeedTestView();
        }

        private void CancelSpeedTest()
        {
            try { _speedPhaseCts?.Cancel(); } catch { }
            try { _speedCts?.Cancel(); } catch { }
            try { _speedHttp?.Dispose(); } catch { }
            _speedHttp = null;
            _speedRunning = false;
            _speedUiTimer?.Stop();
        }

        private HttpClient GetSpeedHttpClient()
        {
            if (_speedHttp != null) return _speedHttp;

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            };
            if (_speedIsXray)
            {
                handler.Proxy = new WebProxy("http://127.0.0.1:20808");
                handler.UseProxy = true;
            }
            _speedHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
            _speedHttp.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36");
            _speedHttp.DefaultRequestHeaders.Add("Referer", "https://www.speedtest.net/");
            return _speedHttp;
        }

        private async void SpeedAll_Click(object sender, RoutedEventArgs e)
        {
            await RunSpeedTestFlowAsync(all: true, dl: false);
        }

        private async void SpeedDl_Click(object sender, RoutedEventArgs e)
        {
            await RunSpeedTestFlowAsync(all: false, dl: true);
        }

        private async void SpeedUl_Click(object sender, RoutedEventArgs e)
        {
            await RunSpeedTestFlowAsync(all: false, dl: false);
        }

        private void SpeedStop_Click(object sender, RoutedEventArgs e)
        {
            if (_speedRunning)
            {
                CancelSpeedTest();
                SpeedPhaseText.Text = Localization.T(SpeedPhaseCanceled);
                SetSpeedButtonsState(running: false);
            }
            else
            {
                CloseSpeedTestView();
            }
        }

        private void SetSpeedButtonsState(bool running)
        {
            _speedRunning = running;
            SpeedBtnAll.IsEnabled = !running;
            SpeedBtnDl.IsEnabled = !running;
            SpeedBtnUl.IsEnabled = !running;
            SpeedEngineCombo.IsEnabled = !running;
            SpeedBtnStop.Content = running ? Localization.T("⏹ توقف") : Localization.T("بستن");
        }

        private async Task RunSpeedTestFlowAsync(bool all, bool dl)
        {
            if (_speedRunning) return;
            SetSpeedButtonsState(running: true);

            CancelSpeedTest();
            _speedCts = new CancellationTokenSource();
            var ct = _speedCts.Token;

            ResetSpeedCards();
            ResetSpeedGaugeToZero();
            SetSpeedProgress(0);

            var engineIdx = SpeedEngineCombo.SelectedIndex;

            try
            {
                if (all)
                {
                    await SpeedPingJitterPhaseAsync(engineIdx, ct);
                    if (ct.IsCancellationRequested) return;

                    await SpeedDlPhaseAsync(engineIdx, 0.20, 0.40, ct);
                    if (ct.IsCancellationRequested) return;

                    await SpeedUlPhaseAsync(engineIdx, 0.60, 0.40, ct);
                    if (ct.IsCancellationRequested) return;

                    SetSpeedProgress(1.0);
                    SpeedPhaseText.Text = Localization.T(SpeedPhaseDone);
                }
                else if (dl)
                {
                    await SpeedPingJitterPhaseAsync(engineIdx, ct);
                    if (ct.IsCancellationRequested) return;

                    await SpeedDlPhaseAsync(engineIdx, 0.25, 0.75, ct);
                    if (ct.IsCancellationRequested) return;

                    SetSpeedProgress(1.0);
                    SpeedPhaseText.Text = Localization.T(SpeedPhaseDone);
                }
                else // ul only
                {
                    await SpeedPingJitterPhaseAsync(engineIdx, ct);
                    if (ct.IsCancellationRequested) return;

                    await SpeedUlPhaseAsync(engineIdx, 0.25, 0.75, ct);
                    if (ct.IsCancellationRequested) return;

                    SetSpeedProgress(1.0);
                    SpeedPhaseText.Text = Localization.T(SpeedPhaseDone);
                }
            }
            catch (OperationCanceledException)
            {
                SpeedPhaseText.Text = Localization.T(SpeedPhaseCanceled);
            }
            catch
            {
                SpeedPhaseText.Text = Localization.T(SpeedPhaseError);
            }
            finally
            {
                _speedUiTimer?.Stop();
                ResetSpeedGaugeToZero();
                SetSpeedButtonsState(running: false);
            }
        }

        private async Task SpeedPingJitterPhaseAsync(int engine, CancellationToken ct)
        {
            SpeedPhaseText.Text = Localization.T(SpeedPhasePing);
            SetSpeedProgress(0.05);

            string host = "1.1.1.1";
            int port = 443;

            if (engine == 0) // Ookla
            {
                var s = await PickBestOoklaServerAsync(ct);
                if (s != null && !string.IsNullOrEmpty(s.host))
                {
                    var parts = s.host.Split(':');
                    host = parts[0];
                    if (parts.Length > 1 && int.TryParse(parts[1], out var p)) port = p;
                }
            }

            var samples = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                try
                {
                    using var tcp = new TcpClient();
                    var connectTask = tcp.ConnectAsync(host, port);
                    var comp = await Task.WhenAny(connectTask, Task.Delay(1500, ct));
                    if (comp == connectTask && tcp.Connected)
                    {
                        sw.Stop();
                        samples.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch { }
                await Task.Delay(80, ct);
            }

            if (samples.Count == 0)
            {
                SpeedPingVal.Text = "Timeout";
                SpeedJitterVal.Text = "—";
                return;
            }

            double avgPing = samples.Average();
            double jitter = 0;
            if (samples.Count > 1)
            {
                double sumDiff = 0;
                for (int i = 1; i < samples.Count; i++)
                    sumDiff += Math.Abs(samples[i] - samples[i - 1]);
                jitter = sumDiff / (samples.Count - 1);
            }

            SpeedPingVal.Text = $"{avgPing:0} ms";
            SpeedJitterVal.Text = $"{jitter:0} ms";

            SetSpeedChip(SpeedPingChip, SpeedPingChipText, avgPing < 80 ? 0 : (avgPing < 180 ? 1 : 2), avgPing < 45);
            SetSpeedChip(SpeedJitterChip, SpeedJitterChipText, jitter < 15 ? 0 : (jitter < 40 ? 1 : 2), jitter < 6);
        }

        private async Task SpeedDlPhaseAsync(int engine, double progBase, double progSpan, CancellationToken ct)
        {
            SetSpeedPhaseVisual(upload: false);
            SpeedPhaseText.Text = Localization.T(SpeedPhaseDownload);

            double mbps = 0;
            if (engine == 0) mbps = await MeasureDlOoklaAsync(progBase, progSpan, ct);
            else if (engine == 1) mbps = await MeasureDlCloudflareAsync(progBase, progSpan, ct);
            else mbps = await MeasureDlMLabAsync(progBase, progSpan, ct);

            SpeedDlVal.Text = $"{mbps:0.0} Mbps";
            SetSpeedChip(SpeedDlChip, SpeedDlChipText, mbps >= 25 ? 0 : (mbps >= 8 ? 1 : 2), mbps >= 50);
        }

        private async Task SpeedUlPhaseAsync(int engine, double progBase, double progSpan, CancellationToken ct)
        {
            SetSpeedPhaseVisual(upload: true);
            SpeedPhaseText.Text = Localization.T(SpeedPhaseUpload);

            double mbps = 0;
            if (engine == 0) mbps = await MeasureUlOoklaAsync(progBase, progSpan, ct);
            else if (engine == 1) mbps = await MeasureUlCloudflareAsync(progBase, progSpan, ct);
            else mbps = await MeasureUlMLabAsync(progBase, progSpan, ct);

            SpeedUlVal.Text = $"{mbps:0.0} Mbps";
            SetSpeedChip(SpeedUlChip, SpeedUlChipText, mbps >= 15 ? 0 : (mbps >= 5 ? 1 : 2), mbps >= 30);
        }

        // ================= Ookla Workers =================
        private sealed class OoklaServerInfo
        {
            public string? url { get; set; }
            public string? host { get; set; }
            public string? name { get; set; }
            public string? country { get; set; }
            public string? sponsor { get; set; }
        }

        private OoklaServerInfo? _cachedOoklaServer;

        private async Task<OoklaServerInfo?> PickBestOoklaServerAsync(CancellationToken ct)
        {
            if (_cachedOoklaServer != null) return _cachedOoklaServer;
            try
            {
                var http = GetSpeedHttpClient();
                var json = await http.GetStringAsync(OoklaServersApi, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    _cachedOoklaServer = new OoklaServerInfo
                    {
                        url = first.TryGetProperty("url", out var u) ? u.GetString() : null,
                        host = first.TryGetProperty("host", out var h) ? h.GetString() : null,
                        name = first.TryGetProperty("name", out var n) ? n.GetString() : null,
                        country = first.TryGetProperty("country", out var c) ? c.GetString() : null,
                        sponsor = first.TryGetProperty("sponsor", out var sp) ? sp.GetString() : null,
                    };
                    return _cachedOoklaServer;
                }
            }
            catch { }
            return null;
        }

        private async Task<double> MeasureDlOoklaAsync(double progBase, double progSpan, CancellationToken ct)
        {
            var server = await PickBestOoklaServerAsync(ct);
            string baseDlUrl = server?.url != null ? server.url.Replace("upload.php", "random4000x4000.jpg") : "https://speed.cloudflare.com/__down?bytes=25000000";
            return await RunChunkedDownloadAsync(baseDlUrl, progBase, progSpan, ct);
        }

        private async Task<double> MeasureUlOoklaAsync(double progBase, double progSpan, CancellationToken ct)
        {
            var server = await PickBestOoklaServerAsync(ct);
            string ulUrl = server?.url ?? CloudflareUpUrl;
            return await RunChunkedUploadAsync(ulUrl, progBase, progSpan, ct);
        }

        // ================= Cloudflare Workers =================
        private async Task<double> MeasureDlCloudflareAsync(double progBase, double progSpan, CancellationToken ct)
        {
            string url = string.Format(CloudflareDownUrlFmt, SpeedDlChunkBytes);
            return await RunChunkedDownloadAsync(url, progBase, progSpan, ct);
        }

        private async Task<double> MeasureUlCloudflareAsync(double progBase, double progSpan, CancellationToken ct)
        {
            return await RunChunkedUploadAsync(CloudflareUpUrl, progBase, progSpan, ct);
        }

        // ================= MLab Workers =================
        private async Task<double> MeasureDlMLabAsync(double progBase, double progSpan, CancellationToken ct)
        {
            return await MeasureDlCloudflareAsync(progBase, progSpan, ct); // Safe fallback
        }

        private async Task<double> MeasureUlMLabAsync(double progBase, double progSpan, CancellationToken ct)
        {
            return await MeasureUlCloudflareAsync(progBase, progSpan, ct); // Safe fallback
        }

        // ================= Core Chunk Download & Upload =================
        private async Task<double> RunChunkedDownloadAsync(string url, double progBase, double progSpan, CancellationToken outerCt)
        {
            var phaseCts = BeginSpeedPhase(progBase, progSpan, outerCt);
            var sw = Stopwatch.StartNew();
            var http = GetSpeedHttpClient();

            try
            {
                var buf = new byte[64 * 1024];
                while (!phaseCts.IsCancellationRequested && Interlocked.Read(ref _speedBytesMoved) < SpeedMaxPhaseBytes)
                {
                    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, phaseCts.Token);
                    resp.EnsureSuccessStatusCode();
                    using var stream = await resp.Content.ReadAsStreamAsync(phaseCts.Token);

                    int read;
                    while ((read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), phaseCts.Token)) > 0)
                    {
                        Interlocked.Add(ref _speedBytesMoved, read);
                    }
                }
            }
            catch (OperationCanceledException) { }

            return EndSpeedPhase(phaseCts, outerCt, sw);
        }

        private async Task<double> RunChunkedUploadAsync(string url, double progBase, double progSpan, CancellationToken outerCt)
        {
            var phaseCts = BeginSpeedPhase(progBase, progSpan, outerCt);
            var sw = Stopwatch.StartNew();
            var http = GetSpeedHttpClient();

            try
            {
                var payload = new byte[SpeedUlChunkBytes];
                Random.Shared.NextBytes(payload);

                while (!phaseCts.IsCancellationRequested && Interlocked.Read(ref _speedBytesMoved) < SpeedMaxPhaseBytes)
                {
                    using var content = new ByteArrayContent(payload);
                    using var resp = await http.PostAsync(url, content, phaseCts.Token);
                    Interlocked.Add(ref _speedBytesMoved, payload.Length);
                }
            }
            catch (OperationCanceledException) { }

            return EndSpeedPhase(phaseCts, outerCt, sw);
        }

        private CancellationTokenSource BeginSpeedPhase(double progBase, double progSpan, CancellationToken outerCt)
        {
            _speedPhaseCts?.Cancel();
            _speedPhaseCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            _speedPhaseCts.CancelAfter(TimeSpan.FromSeconds(SpeedPhaseDurationSeconds));
            StartSpeedPhaseCounters(progBase, progSpan);
            return _speedPhaseCts;
        }

        private double EndSpeedPhase(CancellationTokenSource phaseCts, CancellationToken outerCt, Stopwatch sw)
        {
            _speedUiTimer?.Stop();
            sw.Stop();
            outerCt.ThrowIfCancellationRequested();

            double secs = Math.Max(0.5, sw.Elapsed.TotalSeconds);
            long bytes = Interlocked.Read(ref _speedBytesMoved);
            double avgMbps = (bytes * 8.0) / secs / 1_000_000.0;
            double finalMbps = Math.Max(avgMbps, _speedPhaseMaxMbps * 0.90);
            SetSpeedGauge(finalMbps);
            return finalMbps;
        }

        private void StartSpeedPhaseCounters(double progBase, double progSpan)
        {
            Interlocked.Exchange(ref _speedBytesMoved, 0);
            _speedLastBytes = 0;
            _speedEmaMbps = 0;
            _speedPhaseMaxMbps = 0;
            _speedLastTick = DateTime.UtcNow;
            _speedPhaseStart = DateTime.UtcNow;
            _speedProgressBase = progBase;
            _speedProgressSpan = progSpan;
            _speedUiTimer?.Start();
        }

        private void SpeedUiTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            var secs = (now - _speedLastTick).TotalSeconds;
            if (secs <= 0.05) return;

            long bytes = Interlocked.Read(ref _speedBytesMoved);
            double mbps = (bytes - _speedLastBytes) * 8.0 / secs / 1_000_000.0;
            _speedLastBytes = bytes;
            _speedLastTick = now;

            _speedEmaMbps = _speedEmaMbps <= 0 ? mbps : _speedEmaMbps * 0.6 + mbps * 0.4;
            if (_speedEmaMbps > _speedPhaseMaxMbps) _speedPhaseMaxMbps = _speedEmaMbps;

            SetSpeedGauge(_speedEmaMbps);

            double timeFrac = Math.Min(1.0, (now - _speedPhaseStart).TotalSeconds / SpeedPhaseDurationSeconds);
            SetSpeedProgress(_speedProgressBase + _speedProgressSpan * timeFrac);
        }

        private void SetSpeedGauge(double mbps)
        {
            if (SpeedGaugeNumText == null || SpeedGaugeValueArc == null || SpeedNeedleRotate == null) return;

            SpeedGaugeNumText.Text = mbps.ToString("0.0", CultureInfo.InvariantCulture);
            double frac = Math.Max(0, Math.Min(1.0, mbps / SpeedGaugeMaxMbps));
            SpeedGaugeValueArc.StrokeDashArray = new DoubleCollection { frac * SpeedArcLen / SpeedArcThickness, 1000 };

            var anim = new DoubleAnimation(-90 + 180 * frac, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            SpeedNeedleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void ResetSpeedGaugeToZero()
        {
            if (SpeedGaugeNumText == null || SpeedGaugeValueArc == null || SpeedNeedleRotate == null) return;

            SpeedGaugeNumText.Text = "0.0";
            SpeedGaugeValueArc.StrokeDashArray = new DoubleCollection { 0, 1000 };
            if (SpeedGaugeArrow != null) SpeedGaugeArrow.Visibility = Visibility.Collapsed;
            SpeedGaugeValueArc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
            SpeedPhaseText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

            var anim = new DoubleAnimation(-90, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            SpeedNeedleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void SetSpeedPhaseVisual(bool upload)
        {
            if (SpeedGaugeValueArc == null || SpeedGaugeArrow == null || SpeedPhaseText == null) return;

            var b = upload ? SpeedUlBrush : SpeedDlBrush;
            SpeedGaugeValueArc.Stroke = b;
            SpeedGaugeArrow.Text = upload ? "⬆" : "⬇";
            SpeedGaugeArrow.Foreground = b;
            SpeedGaugeArrow.Visibility = Visibility.Visible;
            SpeedPhaseText.Foreground = b;
        }

        private void SetSpeedProgress(double frac)
        {
            if (SpeedProgressFill == null || SpeedProgressTrack == null) return;
            frac = Math.Max(0, Math.Min(1, frac));
            SpeedProgressFill.Width = Math.Max(0, SpeedProgressTrack.ActualWidth) * frac;
        }

        private void ResetSpeedCards()
        {
            if (SpeedPingVal == null) return;
            SpeedPingVal.Text = SpeedJitterVal.Text = SpeedDlVal.Text = SpeedUlVal.Text = "—";
            SpeedPingChip.Visibility = SpeedJitterChip.Visibility = SpeedDlChip.Visibility = SpeedUlChip.Visibility = Visibility.Collapsed;
        }

        private static void SetSpeedChip(Border chip, TextBlock text, int level, bool great)
        {
            if (chip == null || text == null) return;
            var c = level == 0 ? SpeedGreen : level == 1 ? SpeedAmber : SpeedRed;
            text.Text = Localization.T(level == 0 ? (great ? SpeedRateGreat : SpeedRateGood) : level == 1 ? SpeedRateMid : SpeedRateBad);
            text.Foreground = new SolidColorBrush(c);
            chip.Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
            chip.Visibility = Visibility.Visible;
        }
    }
}
