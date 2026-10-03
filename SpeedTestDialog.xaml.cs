using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SmartVpn;

// دیالوگ تست سرعت — پینگ/جیتر + دانلود/آپلود با سه موتور:
// 1. Speedtest.net (Ookla) — معتبرترین استاندارد جهانی با انتخاب نزدیک‌ترین سرور
// 2. Cloudflare — سریع، پایدار و با لبه‌های جهانی
// 3. M-Lab (NDT7) — تست باز و مستقل
// هر فاز زمان‌محور است و میانگین واقعی بایت‌های منتقل‌شده در ثانیه را محاسبه می‌کند.
public partial class SpeedTestDialog : Window
{
    // ---- متن‌های فارسی ----
    private const string TitleFmt = "تست سرعت — {0}";
    private const string ViaConnectedFmt = "تست از طریق: {0} — متصل به VPN";
    private const string ViaXrayFmt = "تست از طریق: {0} — متصل به Xray";
    private const string ViaDirect = "تست بدون VPN — اینترنت مستقیم شما";
    private const string PhaseIdle = "برای شروع، یکی از دو تست را انتخاب کنید";
    private const string PhasePing = "🏓 در حال اندازه‌گیری پینگ و جیتر…";
    private const string PhaseDownload = "⬇ در حال تست دانلود…";
    private const string PhaseUpload = "⬆ در حال تست آپلود…";
    private const string PhaseDone = "✓ تست کامل شد";
    private const string PhaseCanceled = "تست لغو شد";
    private const string PhaseError = "خطا در تست — اتصال اینترنت را بررسی کنید";
    private const string PhaseErrorMLab = "خطا در اتصال به سرور M-Lab — ممکن است در شبکه فعلی مسدود باشد؛ Cloudflare را امتحان کنید";
    private const string PhaseFindingServer = "در حال یافتن نزدیک‌ترین سرور Speedtest…";
    private const string RateGreat = "عالی";
    private const string RateGood = "خوب";
    private const string RateMid = "متوسط";
    private const string RateBad = "ضعیف";
    private const string BtnAllText = "تست کامل";
    private const string BtnDlText = "⬇ دانلود";
    private const string BtnUlText = "⬆ آپلود";
    private const string BtnCloseText = "بستن";
    private const string BtnCancelText = "لغو";
    private const string WarnTraffic = "⚠️ هر فاز حداکثر ۱۰ ثانیه اجرا می‌شود و از حجم اشتراک شما مصرف می‌کند — نتیجه، میانگین سرعت واقعی در همین بازه است";

    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // ---- تنظیمات تست (موتور Speedtest.net / Ookla) ----
    private const string SpeedtestServersApi = "https://www.speedtest.net/api/js/servers?engine=js&limit=10";

    // ---- تنظیمات تست (موتور Cloudflare) ----
    private const string PingTarget = "1.1.1.1";
    private const string DownUrlFmt = "https://speed.cloudflare.com/__down?bytes={0}";
    private const string UpUrl = "https://speed.cloudflare.com/__up";

    // ---- تنظیمات تست (موتور M-Lab / NDT7) ----
    private const string MLabLocateUrl = "https://locate.measurementlab.net/v2/nearest/ndt/ndt7";
    private const string NdtSubProtocol = "net.measurementlab.ndt.v7";

    // ---- زمان‌بندی و سقف ایمنی حجم ----
    private const int PhaseDurationSeconds = 10;     // سقف زمانی هر فاز دانلود/آپلود
    private const long MaxPhaseBytes = 400_000_000;  // سقف ایمنی حجم هر فاز — برای لینک‌های خیلی سریع
    private const long DlChunkBytes = 50_000_000;    // اندازه‌ی هر تکه‌ی درخواستی از Cloudflare
    private const int UlChunkBytes = 64 * 1024;      // اندازه‌ی هر فریم آپلود (Cloudflare/NDT7)

    private const double GaugeMaxMbps = 50.0;   // انتهای مدرج گیج
    private const double ArcLen = 251.33;       // طول قوس نیم‌دایره (π×80)
    private const double ArcThickness = 10.0;   // StrokeDashArray بر حسب ضخامت قلم است

    private readonly bool _isXray;
    private readonly HttpClient _http;

    private static readonly Color Green = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color Amber = Color.FromRgb(0xF5, 0x9E, 0x0B);
    private static readonly Color Red = Color.FromRgb(0xEF, 0x44, 0x44);

    // تفکیک رنگ فازها: آبی برای دانلود، سبز برای آپلود
    private static readonly SolidColorBrush DlBrush = new(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush UlBrush = new(Color.FromRgb(0x39, 0xFF, 0x14));

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _phaseCts;
    private long _bytesMoved;
    private double _progressBase, _progressSpan;
    private DateTime _phaseStart;
    private double _emaMbps;
    private double _phaseMaxMbps;
    private long _lastBytes;
    private DateTime _lastTick;
    private readonly DispatcherTimer _uiTimer;
    private bool _running;

    public SpeedTestDialog(bool vpnConnected, string connectionName, bool isXray = false)
    {
        InitializeComponent();
        _isXray = isXray;
        _http = CreateHttpClient(isXray);

        Loaded += (_, _) => Localization.Watch(this);
        Title = string.Format(Localization.T(TitleFmt), AppConfig.BrandName);

        string viaTemplate = isXray ? ViaXrayFmt : ViaConnectedFmt;
        ViaText.Text = Localization.T(vpnConnected ? string.Format(viaTemplate, connectionName) : ViaDirect);
        ViaDot.Fill = new SolidColorBrush(vpnConnected ? Green : Color.FromRgb(0x64, 0x74, 0x8B));

        PhaseText.Text = Localization.T(PhaseIdle);
        BtnAll.Content = Localization.T(BtnAllText);
        BtnDl.Content = Localization.T(BtnDlText);
        BtnUl.Content = Localization.T(BtnUlText);
        BtnCloseCancel.Content = Localization.T(BtnCloseText);
        WarnText.Text = Localization.T(WarnTraffic);
        EngineCombo.SelectedIndex = 0; // پیش‌فرض: Speedtest.net (Ookla)

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _uiTimer.Tick += UiTimer_Tick;
        SourceInitialized += (_, __) => ApplyDarkTitleBar();
        Closing += (_, __) =>
        {
            try { _cts?.Cancel(); } catch { }
            try { _http.Dispose(); } catch { }
        };
    }

    private static HttpClient CreateHttpClient(bool useXrayProxy)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        if (useXrayProxy)
        {
            handler.Proxy = new WebProxy("http://127.0.0.1:20808");
            handler.UseProxy = true;
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Referer", "https://www.speedtest.net/");
        return client;
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

    // ================= دکمه‌ها =================
    private async void All_Click(object sender, RoutedEventArgs e) => await RunTestAsync(doDl: true, doUl: true);
    private async void Dl_Click(object sender, RoutedEventArgs e) => await RunTestAsync(doDl: true, doUl: false);
    private async void Ul_Click(object sender, RoutedEventArgs e) => await RunTestAsync(doDl: false, doUl: true);

    private void CloseCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_running) { try { _cts?.Cancel(); } catch { } }
        else Close();
    }

    // ================= اجرای تست =================
    private async Task RunTestAsync(bool doDl, bool doUl)
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        BtnAll.IsEnabled = BtnDl.IsEnabled = BtnUl.IsEnabled = false;
        EngineCombo.IsEnabled = false;
        BtnCloseCancel.Content = Localization.T(BtnCancelText);
        ResetCards();
        SetGauge(0);
        SetProgress(0);

        int engineIndex = EngineCombo.SelectedIndex;
        if (engineIndex < 0) engineIndex = 0;

        const int streams = 4;
        double dlSpan = doUl ? 0.55 : 0.90;
        double ulStart = doDl ? 0.65 : 0.10;
        double ulSpan = doDl ? 0.35 : 0.90;

        string? mlabDlUrl = null, mlabUlUrl = null;
        string? speedtestDlUrl = null, speedtestUpUrl = null, speedtestPingUrl = null, speedtestServerName = null;

        try
        {
            if (engineIndex == 0) // Speedtest.net (Ookla)
            {
                PhaseText.Text = Localization.T(PhaseFindingServer);
                (speedtestDlUrl, speedtestUpUrl, speedtestPingUrl, speedtestServerName) = await ResolveSpeedtestServerAsync(ct);
                if ((doDl && speedtestDlUrl == null) || (doUl && speedtestUpUrl == null))
                {
                    // فال‌بک نرم به کلاودفلر در صورت بروز مشکل در API اسپیدتست
                    engineIndex = 1;
                }
            }

            if (engineIndex == 2) // M-Lab (NDT7)
            {
                (mlabDlUrl, mlabUlUrl) = await ResolveMLabServerAsync(ct);
                if ((doDl && mlabDlUrl == null) || (doUl && mlabUlUrl == null))
                {
                    PhaseText.Text = Localization.T(PhaseErrorMLab);
                    return;
                }
            }

            // ---- فاز ۱: پینگ و جیتر ----
            PhaseText.Text = Localization.T(PhasePing);
            var (ping, jitter) = await MeasurePingAsync(engineIndex, speedtestPingUrl, ct);
            PingVal.Text = ping < 0 ? "—" : N(ping, "0") + " ms";
            JitterVal.Text = jitter < 0 ? "—" : N(jitter, "0") + " ms";
            SetProgress(0.10);

            // ---- فاز ۲: دانلود (اختیاری) ----
            if (doDl)
            {
                PhaseText.Text = Localization.T(PhaseDownload);
                SetPhaseVisual(upload: false);
                double dl = 0;
                if (engineIndex == 2)
                {
                    dl = await MeasureDownloadMLabAsync(mlabDlUrl!, 0.10, dlSpan, ct);
                }
                else if (engineIndex == 0 && speedtestDlUrl != null)
                {
                    dl = await MeasureDownloadSpeedtestAsync(speedtestDlUrl, streams, 0.10, dlSpan, ct);
                }
                else
                {
                    dl = await MeasureDownloadAsync(streams, 0.10, dlSpan, ct);
                }

                // نتیجه = میانگین سرعت واقعی محاسبه‌شده در طول فاز تست
                DlVal.Text = N(dl, "0.0") + " Mbps";
                DlVal.Foreground = DlBrush;
            }

            // ---- فاز ۳: آپلود (اختیاری) ----
            if (doUl)
            {
                PhaseText.Text = Localization.T(PhaseUpload);
                SetPhaseVisual(upload: true);
                double ul = 0;
                if (engineIndex == 2)
                {
                    ul = await MeasureUploadMLabAsync(mlabUlUrl!, ulStart, ulSpan, ct);
                }
                else if (engineIndex == 0 && speedtestUpUrl != null)
                {
                    ul = await MeasureUploadSpeedtestAsync(speedtestUpUrl, ulStart, ulSpan, ct);
                }
                else
                {
                    ul = await MeasureUploadAsync(ulStart, ulSpan, ct);
                }

                // نتیجه = میانگین سرعت واقعی محاسبه‌شده در طول فاز تست
                UlVal.Text = N(ul, "0.0") + " Mbps";
                UlVal.Foreground = UlBrush;
            }

            SetProgress(1);
            PhaseText.Text = Localization.T(PhaseDone);
        }
        catch (OperationCanceledException) { PhaseText.Text = Localization.T(PhaseCanceled); }
        catch { PhaseText.Text = Localization.T(PhaseError); }
        finally
        {
            _uiTimer.Stop();
            ResetGaugeToZero();   // پایان تست (یا لغو/خطا): عقربه نرم برمی‌گردد روی صفر
            _running = false;
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            _phaseCts = null;
            BtnAll.IsEnabled = BtnDl.IsEnabled = BtnUl.IsEnabled = true;
            EngineCombo.IsEnabled = true;
            BtnCloseCancel.Content = Localization.T(BtnCloseText);
        }
    }

    // ================= اندازه‌گیری‌ها: پینگ و جیتر =================
    private async Task<(double ping, double jitter)> MeasurePingAsync(int engineIndex, string? speedtestPingUrl, CancellationToken ct)
    {
        // برای Xray یا موتورهای وب (Speedtest و Cloudflare): پینگ HTTP با اندازه‌گیری دقیق میلی‌ثانیه‌ای رفت‌وبرگشت
        // این روش مشکل فریز و عدم عبور ICMP در پروکسی‌ها را به‌طور کامل حل می‌کند
        string pingUrl = engineIndex switch
        {
            0 => !string.IsNullOrEmpty(speedtestPingUrl) ? speedtestPingUrl : "https://speed.cloudflare.com/__down?bytes=0",
            1 => "https://speed.cloudflare.com/__down?bytes=0",
            _ => "http://cp.cloudflare.com/generate_204"
        };

        var times = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var sw = Stopwatch.StartNew();
                using var req = new HttpRequestMessage(HttpMethod.Get, pingUrl);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                sw.Stop();
                if (resp.IsSuccessStatusCode && sw.ElapsedMilliseconds > 0)
                {
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            catch { }
            await Task.Delay(60, ct);
        }

        // اگر پینگ HTTP به هر دلیلی خالی بود و Xray نبود، از ICMP ویندوز به عنوان جایگزین استفاده کن
        if (times.Count == 0 && !_isXray)
        {
            using var p = new Ping();
            for (int i = 0; i < 4; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var r = await p.SendPingAsync(PingTarget, 800);
                    if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime);
                }
                catch { }
                await Task.Delay(80, ct);
            }
        }

        if (times.Count == 0) return (-1, -1);
        if (times.Count == 1) return (times[0], -1);

        double avg = times.Average();
        double jit = 0;
        for (int i = 1; i < times.Count; i++) jit += Math.Abs(times[i] - times[i - 1]);
        jit /= times.Count - 1;
        return (avg, jit);
    }

    private CancellationTokenSource BeginPhase(double progBase, double progSpan, CancellationToken outerCt)
    {
        StartPhaseCounters(progBase, progSpan);
        var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        phaseCts.CancelAfter(TimeSpan.FromSeconds(PhaseDurationSeconds));
        _phaseCts = phaseCts;
        return phaseCts;
    }

    private double EndPhase(CancellationTokenSource phaseCts, CancellationToken outerCt, Stopwatch sw)
    {
        sw.Stop();
        _uiTimer.Stop();
        _phaseCts = null;
        phaseCts.Dispose();
        outerCt.ThrowIfCancellationRequested();
        long bytes = Interlocked.Read(ref _bytesMoved);
        // میانگین واقعی بایت‌های جابجا شده در کل مدت زمان سپری‌شده (Mbps)
        return bytes * 8.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1_000_000.0;
    }

    // ================= اندازه‌گیری‌ها: موتور Speedtest.net (Ookla) =================
    private async Task<(string? dlUrl, string? upUrl, string? pingUrl, string? serverName)> ResolveSpeedtestServerAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, SpeedtestServersApi);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return (null, null, null, null);

            var first = doc.RootElement.EnumerateArray().First();
            string uploadUrl = first.GetProperty("url").GetString() ?? "";
            string sponsor = first.TryGetProperty("sponsor", out var sp) ? sp.GetString() ?? "" : "";
            string name = first.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(uploadUrl)) return (null, null, null, null);

            var baseUri = new Uri(uploadUrl);
            string dlUrl = $"{baseUri.Scheme}://{baseUri.Authority}/speedtest/random2500x2500.jpg";
            string upUrl = $"{baseUri.Scheme}://{baseUri.Authority}/speedtest/upload.php";
            string pingUrl = $"{baseUri.Scheme}://{baseUri.Authority}/speedtest/latency.txt";
            string sName = string.IsNullOrEmpty(sponsor) ? name : $"{sponsor} ({name})";
            return (dlUrl, upUrl, pingUrl, sName);
        }
        catch
        {
            return (null, null, null, null);
        }
    }

    private async Task<double> MeasureDownloadSpeedtestAsync(string dlUrl, int streams, double progBase, double progSpan, CancellationToken outerCt)
    {
        var phaseCts = BeginPhase(progBase, progSpan, outerCt);
        var sw = Stopwatch.StartNew();
        try
        {
            var tasks = Enumerable.Range(0, streams).Select(_ => DownloadSpeedtestWorkerAsync(dlUrl, phaseCts.Token)).ToArray();
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        return EndPhase(phaseCts, outerCt, sw);
    }

    private async Task DownloadSpeedtestWorkerAsync(string dlUrl, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var resp = await _http.GetAsync(dlUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var s = await resp.Content.ReadAsStreamAsync(ct);
                var buf = new byte[64 * 1024];
                int n;
                while ((n = await s.ReadAsync(buf.AsMemory(), ct)) > 0)
                {
                    Interlocked.Add(ref _bytesMoved, n);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(100, ct); }
        }
    }

    private async Task<double> MeasureUploadSpeedtestAsync(string upUrl, double progBase, double progSpan, CancellationToken outerCt)
    {
        var phaseCts = BeginPhase(progBase, progSpan, outerCt);
        var sw = Stopwatch.StartNew();
        try
        {
            const int workers = 2;
            var tasks = Enumerable.Range(0, workers).Select(_ => UploadSpeedtestWorkerAsync(upUrl, phaseCts.Token)).ToArray();
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        return EndPhase(phaseCts, outerCt, sw);
    }

    private async Task UploadSpeedtestWorkerAsync(string upUrl, CancellationToken ct)
    {
        var dummyData = new byte[256 * 1024];
        Random.Shared.NextBytes(dummyData);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var content = new ByteArrayContent(dummyData);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var resp = await _http.PostAsync(upUrl, content, ct);
                if (resp.IsSuccessStatusCode)
                {
                    Interlocked.Add(ref _bytesMoved, dummyData.Length);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(100, ct); }
        }
    }

    // ================= اندازه‌گیری‌ها: موتور Cloudflare =================
    private async Task<double> MeasureDownloadAsync(int streams, double progBase, double progSpan, CancellationToken outerCt)
    {
        var phaseCts = BeginPhase(progBase, progSpan, outerCt);
        var sw = Stopwatch.StartNew();
        try
        {
            var tasks = Enumerable.Range(0, streams).Select(_ => DownloadOneAsync(phaseCts.Token)).ToArray();
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        return EndPhase(phaseCts, outerCt, sw);
    }

    private async Task DownloadOneAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var resp = await _http.GetAsync(string.Format(CultureInfo.InvariantCulture, DownUrlFmt, DlChunkBytes),
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            var buf = new byte[64 * 1024];
            int n;
            while ((n = await s.ReadAsync(buf.AsMemory(), ct)) > 0)
                Interlocked.Add(ref _bytesMoved, n);
        }
    }

    private async Task<double> MeasureUploadAsync(double progBase, double progSpan, CancellationToken outerCt)
    {
        var phaseCts = BeginPhase(progBase, progSpan, outerCt);
        var sw = Stopwatch.StartNew();
        try
        {
            using var content = new CountingContent(n => Interlocked.Add(ref _bytesMoved, n), phaseCts.Token);
            using var resp = await _http.PostAsync(UpUrl, content, phaseCts.Token);
        }
        catch (OperationCanceledException) { }
        return EndPhase(phaseCts, outerCt, sw);
    }

    // محتوای آپلود Cloudflare: دیتای رندوم استریم‌شده
    private sealed class CountingContent : HttpContent
    {
        private readonly Action<int> _progress;
        private readonly CancellationToken _ct;

        public CountingContent(Action<int> progress, CancellationToken ct)
        {
            _progress = progress; _ct = ct;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buf = new byte[UlChunkBytes];
            Random.Shared.NextBytes(buf);
            while (!_ct.IsCancellationRequested)
            {
                await stream.WriteAsync(buf, 0, buf.Length, _ct);
                _progress(buf.Length);
            }
        }

        protected override bool TryComputeLength(out long length) { length = -1; return false; }
    }

    // ================= اندازه‌گیری‌ها: موتور M-Lab (NDT7 روی WebSocket) =================
    private async Task<(string? dl, string? ul)> ResolveMLabServerAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(MLabLocateUrl, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return (null, null);
            var first = results.EnumerateArray().First();
            var urls = first.GetProperty("urls");
            string? dl = null, ul = null;
            foreach (var prop in urls.EnumerateObject())
            {
                if (!prop.Name.Contains("wss", StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.Name.Contains("download", StringComparison.OrdinalIgnoreCase)) dl ??= prop.Value.GetString();
                else if (prop.Name.Contains("upload", StringComparison.OrdinalIgnoreCase)) ul ??= prop.Value.GetString();
            }
            return (dl, ul);
        }
        catch { return (null, null); }
    }

    private async Task<double> MeasureDownloadMLabAsync(string url, double progBase, double progSpan, CancellationToken outerCt)
    {
        var phaseCts = BeginPhase(progBase, progSpan, outerCt);
        var sw = Stopwatch.StartNew();
        try
        {
            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol(NdtSubProtocol);
            if (_isXray)
            {
                ws.Options.Proxy = new WebProxy("http://127.0.0.1:20808");
            }
            await ws.ConnectAsync(new Uri(url), phaseCts.Token);
            var buf = new byte[1 << 20];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buf.AsMemory(), phaseCts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                Interlocked.Add(ref _bytesMoved, result.Count);
            }
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
        }
        catch (OperationCanceledException) { }
        return EndPhase(phaseCts, outerCt, sw);
    }

    private async Task<double> MeasureUploadMLabAsync(string url, double progBase, double progSpan, CancellationToken outerCt)
    {
        var phaseCts = BeginPhase(progBase, progSpan, outerCt);
        var sw = Stopwatch.StartNew();
        try
        {
            using var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol(NdtSubProtocol);
            if (_isXray)
            {
                ws.Options.Proxy = new WebProxy("http://127.0.0.1:20808");
            }
            await ws.ConnectAsync(new Uri(url), phaseCts.Token);
            var buf = new byte[UlChunkBytes];
            Random.Shared.NextBytes(buf);
            while (ws.State == WebSocketState.Open && !phaseCts.IsCancellationRequested)
            {
                await ws.SendAsync(buf.AsMemory(), WebSocketMessageType.Binary, true, phaseCts.Token);
                Interlocked.Add(ref _bytesMoved, buf.Length);
            }
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
        }
        catch (OperationCanceledException) { }
        return EndPhase(phaseCts, outerCt, sw);
    }

    // ================= به‌روزرسانی UI =================
    private void StartPhaseCounters(double progBase, double progSpan)
    {
        Interlocked.Exchange(ref _bytesMoved, 0);
        _lastBytes = 0;
        _emaMbps = 0;
        _phaseMaxMbps = 0;
        _lastTick = DateTime.UtcNow;
        _phaseStart = DateTime.UtcNow;
        _progressBase = progBase;
        _progressSpan = progSpan;
        _uiTimer.Start();
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var secs = (now - _lastTick).TotalSeconds;
        if (secs <= 0.05) return;
        long bytes = Interlocked.Read(ref _bytesMoved);
        double mbps = (bytes - _lastBytes) * 8.0 / secs / 1_000_000.0;
        _lastBytes = bytes;
        _lastTick = now;
        _emaMbps = _emaMbps <= 0 ? mbps : _emaMbps * 0.6 + mbps * 0.4;   // نرم‌سازی حرکت عقربه
        if (_emaMbps > _phaseMaxMbps) _phaseMaxMbps = _emaMbps;
        SetGauge(_emaMbps);

        double timeFrac = Math.Min(1.0, (now - _phaseStart).TotalSeconds / PhaseDurationSeconds);
        SetProgress(_progressBase + _progressSpan * timeFrac);
    }

    private void SetGauge(double mbps)
    {
        GaugeNumText.Text = N(mbps, "0.0");
        double frac = Math.Max(0, Math.Min(1.0, mbps / GaugeMaxMbps));
        GaugeValueArc.StrokeDashArray = new DoubleCollection { frac * ArcLen / ArcThickness, 1000 };
        var anim = new DoubleAnimation(-90 + 180 * frac, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        NeedleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void ResetGaugeToZero()
    {
        GaugeNumText.Text = N(0, "0.0");
        GaugeValueArc.StrokeDashArray = new DoubleCollection { 0, 1000 };
        GaugeArrow.Visibility = Visibility.Collapsed;
        GaugeValueArc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
        PhaseText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
        var anim = new DoubleAnimation(-90, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        NeedleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void SetPhaseVisual(bool upload)
    {
        var b = upload ? UlBrush : DlBrush;
        GaugeValueArc.Stroke = b;
        GaugeArrow.Text = upload ? "⬆" : "⬇";
        GaugeArrow.Foreground = b;
        GaugeArrow.Visibility = Visibility.Visible;
        PhaseText.Foreground = b;
    }

    private void SetProgress(double frac)
    {
        frac = Math.Max(0, Math.Min(1, frac));
        ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth) * frac;
    }

    private void ResetCards()
    {
        PingVal.Text = JitterVal.Text = DlVal.Text = UlVal.Text = "—";
        PingChip.Visibility = JitterChip.Visibility = DlChip.Visibility = UlChip.Visibility = Visibility.Collapsed;
    }

    private static int RateLowerBetter(double v, double good, double mid) =>
        v < 0 ? 2 : v <= good ? 0 : v <= mid ? 1 : 2;

    private static int RateHigherBetter(double v, double good, double mid) =>
        v >= good ? 0 : v >= mid ? 1 : 2;

    private static void SetChip(System.Windows.Controls.Border chip, System.Windows.Controls.TextBlock text, int level, bool great)
    {
        var c = level == 0 ? Green : level == 1 ? Amber : Red;
        text.Text = Localization.T(level == 0 ? (great ? RateGreat : RateGood) : level == 1 ? RateMid : RateBad);
        text.Foreground = new SolidColorBrush(c);
        chip.Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
        chip.Visibility = Visibility.Visible;
    }

    // فرمت اعداد همیشه با ارقام لاتین — جلوگیری از به‌هم‌ریختگی RTL
    private static string N(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
}
