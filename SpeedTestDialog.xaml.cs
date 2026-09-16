using System;
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

// دیالوگ تست سرعت — پینگ/جیتر + دانلود/آپلود، با دو موتور قابل انتخاب: Cloudflare یا M-Lab (NDT7)
// هر دو موتور زمان‌محورند (نه حجم‌محور مثل قبل): هر فاز حداکثر تا PhaseDurationSeconds طول می‌کشد
// (یا زودتر اگر به سقف ایمنی حجمی MaxPhaseBytes برسد) — روی لینک‌های کند بی‌خودی طولانی نمی‌شود و
// روی لینک‌های خیلی سریع هم مصرف حجم کنترل‌شده می‌ماند.
// هیچ فایلی روی دیسک نوشته نمی‌شود: بایت‌ها فقط شمرده و دور ریخته می‌شوند.
public partial class SpeedTestDialog : Window
{
    // ---- متن‌های فارسی ----
    private const string TitleFmt = "تست سرعت — {0}";
    private const string ViaConnectedFmt = "تست از طریق: {0} — متصل به VPN";
    private const string ViaDirect = "تست بدون VPN — اینترنت مستقیم شما";
    private const string PhaseIdle = "برای شروع، یکی از دو تست را انتخاب کنید";
    private const string PhasePing = "🏓 در حال اندازه‌گیری پینگ و جیتر…";
    private const string PhaseDownload = "⬇ در حال تست دانلود…";
    private const string PhaseUpload = "⬆ در حال تست آپلود…";
    private const string PhaseDone = "✓ تست کامل شد";
    private const string PhaseCanceled = "تست لغو شد";
    private const string PhaseError = "خطا در تست — اتصال اینترنت را بررسی کنید";
    private const string PhaseErrorMLab = "خطا در اتصال به سرور M-Lab — ممکن است در شبکه فعلی مسدود باشد؛ Cloudflare را امتحان کنید";
    private const string RateGreat = "عالی";
    private const string RateGood = "خوب";
    private const string RateMid = "متوسط";
    private const string RateBad = "ضعیف";
    private const string BtnAllText = "Full Test";
    private const string BtnDlText = "⬇ DOWNLOAD";
    private const string BtnUlText = "⬆ UPLOAD";
    private const string BtnCloseText = "بستن";
    private const string BtnCancelText = "لغو";
    private const string WarnTraffic = "⚠️ هر فاز حداکثر ۱۰ ثانیه اجرا می‌شود و از حجم اشتراک شما مصرف می‌کند — نتیجه، بالاترین سرعت ثبت‌شده در همین بازه است";

    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // ---- تنظیمات تست (موتور Cloudflare) ----
    private const string PingTarget = "1.1.1.1";
    private const string DownUrlFmt = "https://speed.cloudflare.com/__down?bytes={0}";
    private const string UpUrl = "https://speed.cloudflare.com/__up";

    // ---- تنظیمات تست (موتور M-Lab / NDT7) ----
    private const string MLabLocateUrl = "https://locate.measurementlab.net/v2/nearest/ndt/ndt7";
    private const string NdtSubProtocol = "net.measurementlab.ndt.v7";

    // ---- زمان‌بندی و سقف ایمنی حجم (به‌جای حجم ثابت قبلی) ----
    private const int PhaseDurationSeconds = 10;     // سقف زمانی هر فاز دانلود/آپلود
    private const long MaxPhaseBytes = 400_000_000;  // سقف ایمنی حجم هر فاز — برای لینک‌های خیلی سریع (گیگابیت و بالاتر)
    private const long DlChunkBytes = 50_000_000;    // اندازه‌ی هر تکه‌ی درخواستی از Cloudflare — با اتمام هر تکه، تکه‌ی بعدی خواسته می‌شود تا زمان/سقف فاز برسد
    private const int UlChunkBytes = 64 * 1024;      // اندازه‌ی هر فریم آپلود (Cloudflare/NDT7)

    private const double GaugeMaxMbps = 50.0;   // انتهای مدرج گیج
    private const double ArcLen = 251.33;       // طول قوس نیم‌دایره (π×80)
    private const double ArcThickness = 10.0;   // StrokeDashArray بر حسب ضخامت قلم است

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly Color Green = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color Amber = Color.FromRgb(0xF5, 0x9E, 0x0B);
    private static readonly Color Red = Color.FromRgb(0xEF, 0x44, 0x44);

    // تفکیک رنگ فازها: آبی برای دانلود، صورتی برای آپلود
    private static readonly SolidColorBrush DlBrush = new(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly SolidColorBrush UlBrush = new(Color.FromRgb(0x39, 0xFF, 0x14));

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _phaseCts;   // فقط برای پایان زودهنگام فاز (پایان زمان/رسیدن به سقف حجمی) — کنسل‌شدنش به‌معنای لغو کل تست توسط کاربر نیست
    private long _bytesMoved;                 // شمارنده‌ی بایت‌های فاز جاری (دانلود یا آپلود)
    private double _progressBase, _progressSpan;
    private DateTime _phaseStart;             // برای محاسبه‌ی درصد پیشرفت بر اساس زمان سپری‌شده (نه حجم؛ چون حجم هدف از پیش معلوم نیست)
    private double _emaMbps;
    private double _phaseMaxMbps;   // بالاترین سرعت دیده‌شده در فاز جاری — برای نتیجه نهایی
    private long _lastBytes;
    private DateTime _lastTick;
    private readonly DispatcherTimer _uiTimer;
    private bool _running;

    public SpeedTestDialog(bool vpnConnected, string connectionName)
    {
        InitializeComponent();
        Title = string.Format(TitleFmt, AppConfig.BrandName); // نام برند از config.json
        ViaText.Text = vpnConnected ? string.Format(ViaConnectedFmt, connectionName) : ViaDirect;
        ViaDot.Fill = new SolidColorBrush(vpnConnected ? Green : Color.FromRgb(0x64, 0x74, 0x8B));
        PhaseText.Text = PhaseIdle;
        BtnAll.Content = BtnAllText;
        BtnDl.Content = BtnDlText;
        BtnUl.Content = BtnUlText;
        BtnCloseCancel.Content = BtnCloseText;
        WarnText.Text = WarnTraffic;
        EngineCombo.SelectedIndex = 0; // پیش‌فرض: Cloudflare
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _uiTimer.Tick += UiTimer_Tick;
        SourceInitialized += (_, __) => ApplyDarkTitleBar();
        Closing += (_, __) => { try { _cts?.Cancel(); } catch { } };
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
        BtnCloseCancel.Content = BtnCancelText;
        ResetCards();
        SetGauge(0);
        SetProgress(0);

        bool useMLab = EngineCombo.SelectedIndex == 1;
        const int streams = 4; // فقط برای Cloudflare — NDT7 طبق طراحی خودش تک‌جریانه است
        double dlSpan = doUl ? 0.55 : 0.90;
        double ulStart = doDl ? 0.65 : 0.10;
        double ulSpan = doDl ? 0.35 : 0.90;
        string? mlabDlUrl = null, mlabUlUrl = null;

        try
        {
            if (useMLab)
            {
                (mlabDlUrl, mlabUlUrl) = await ResolveMLabServerAsync(ct);
                if ((doDl && mlabDlUrl == null) || (doUl && mlabUlUrl == null))
                {
                    PhaseText.Text = PhaseErrorMLab;
                    return;
                }
            }

            // ---- فاز ۱: پینگ و جیتر (۱۰ نمونه) ----
            PhaseText.Text = PhasePing;
            var (ping, jitter) = await MeasurePingAsync(ct);
            PingVal.Text = ping < 0 ? "—" : N(ping, "0") + " ms";
            JitterVal.Text = jitter < 0 ? "—" : N(jitter, "0") + " ms";
            SetProgress(0.10);

            // ---- فاز ۲: دانلود (اختیاری) ----
            if (doDl)
            {
                PhaseText.Text = PhaseDownload;
                SetPhaseVisual(upload: false);
                double dl = useMLab
                    ? await MeasureDownloadMLabAsync(mlabDlUrl!, 0.10, dlSpan, ct)
                    : await MeasureDownloadAsync(streams, 0.10, dlSpan, ct);
                dl = Math.Max(dl, _phaseMaxMbps);   // نتیجه = بالاترین سرعتِ دیده‌شده در طول فاز، نه آخرین عدد
                DlVal.Text = N(dl, "0.0") + " Mbps";
                DlVal.Foreground = DlBrush;
            }

            // ---- فاز ۳: آپلود (اختیاری) ----
            if (doUl)
            {
                PhaseText.Text = PhaseUpload;
                SetPhaseVisual(upload: true);
                double ul = useMLab
                    ? await MeasureUploadMLabAsync(mlabUlUrl!, ulStart, ulSpan, ct)
                    : await MeasureUploadAsync(ulStart, ulSpan, ct);
                ul = Math.Max(ul, _phaseMaxMbps);   // نتیجه = بالاترین سرعتِ دیده‌شده در طول فاز
                UlVal.Text = N(ul, "0.0") + " Mbps";
                UlVal.Foreground = UlBrush;
            }

            SetProgress(1);
            PhaseText.Text = PhaseDone;
        }
        catch (OperationCanceledException) { PhaseText.Text = PhaseCanceled; }
        catch { PhaseText.Text = PhaseError; }
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
            BtnCloseCancel.Content = BtnCloseText;
        }
    }

    // ================= اندازه‌گیری‌ها: مشترک =================
    private static async Task<(double ping, double jitter)> MeasurePingAsync(CancellationToken ct)
    {
        var times = new System.Collections.Generic.List<double>();
        using var p = new Ping();
        for (int i = 0; i < 10; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await p.SendPingAsync(PingTarget, 3000);
                if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime);
            }
            catch { }
            await Task.Delay(120, ct);
        }
        if (times.Count == 0) return (-1, -1);
        if (times.Count == 1) return (times[0], -1);
        double avg = times.Average();
        double jit = 0;
        for (int i = 1; i < times.Count; i++) jit += Math.Abs(times[i] - times[i - 1]);
        jit /= times.Count - 1;
        return (avg, jit);
    }

    // یک فاز (دانلود یا آپلود) را حداکثر تا PhaseDurationSeconds اجرا می‌کند؛ اگر بایت‌های منتقل‌شده
    // زودتر از MaxPhaseBytes عبور کند، UiTimer_Tick خودش phaseCts را کنسل می‌کند (پایان زودهنگام امن).
    // لغو phaseCts (پایان طبیعی فاز) با لغو واقعی توسط کاربر (outerCt) فرق دارد — فقط دومی به بیرون پرتاب می‌شود.
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
        outerCt.ThrowIfCancellationRequested(); // فقط اگر کاربر واقعاً لغو کرده باشد به بیرون منتقل می‌شود
        long bytes = Interlocked.Read(ref _bytesMoved);
        return bytes * 8.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds) / 1_000_000.0;
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
        catch (OperationCanceledException) { /* پایان زمان/سقف فاز یا لغو کاربر — در EndPhase بررسی می‌شود */ }
        return EndPhase(phaseCts, outerCt, sw);
    }

    // به‌جای یک حجم ثابت از پیش تعیین‌شده، هر استریم پیوسته تکه‌های ۵۰ مگابایتی می‌خواهد و تا لغو
    // (پایان زمان یا سقف حجمی فاز) ادامه می‌دهد — روی لینک‌های کند زودتر لغو می‌شود، روی لینک‌های خیلی
    // سریع چند تکه پشت‌هم دانلود می‌شود، در هر دو حالت زمان واقعی معیار محاسبه‌ی سرعت است.
    private async Task DownloadOneAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var resp = await Http.GetAsync(string.Format(CultureInfo.InvariantCulture, DownUrlFmt, DlChunkBytes),
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            var buf = new byte[64 * 1024];
            int n;
            while ((n = await s.ReadAsync(buf.AsMemory(), ct)) > 0)
                Interlocked.Add(ref _bytesMoved, n);   // فقط می‌شماریم — چیزی ذخیره نمی‌شود
        }
    }

    private async Task<double> MeasureUploadAsync(double progBase, double progSpan, CancellationToken outerCt)
    {
        var phaseCts = BeginPhase(progBase, progSpan, outerCt);
        var sw = Stopwatch.StartNew();
        try
        {
            using var content = new CountingContent(n => Interlocked.Add(ref _bytesMoved, n), phaseCts.Token);
            using var resp = await Http.PostAsync(UpUrl, content, phaseCts.Token);
        }
        catch (OperationCanceledException) { /* پایان زمان/سقف فاز یا لغو کاربر — در EndPhase بررسی می‌شود */ }
        return EndPhase(phaseCts, outerCt, sw);
    }

    // محتوای آپلود: دیتای رندوم از حافظه — بدون فایل موقت — طول نامعلوم (تا لغو ادامه می‌دهد، نه یک حجم ثابت)
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

        // طول از پیش معلوم نیست — HttpClient درخواست را به‌صورت Transfer-Encoding: chunked ارسال می‌کند
        protected override bool TryComputeLength(out long length) { length = -1; return false; }
    }

    // ================= اندازه‌گیری‌ها: موتور M-Lab (NDT7 روی WebSocket) =================
    // یک کلاینت NDT7 ساده‌شده و عمل‌گرا: مثل موتور Cloudflare فقط بایت‌های واقعی روی سیم و زمان سپری‌شده
    // را می‌شمارد (نه پردازش پیام‌های اندازه‌گیری BBR/TCPInfo که سرور NDT7 هم می‌فرستد) — تا محاسبه‌ی
    // سرعت بین دو موتور قابل‌مقایسه و یکسان بماند.
    private async Task<(string? dl, string? ul)> ResolveMLabServerAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync(MLabLocateUrl, ct);
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
        catch (OperationCanceledException) { /* پایان زمان/سقف فاز یا لغو کاربر — در EndPhase بررسی می‌شود */ }
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
        catch (OperationCanceledException) { /* پایان زمان/سقف فاز یا لغو کاربر — در EndPhase بررسی می‌شود */ }
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
        if (_emaMbps > _phaseMaxMbps) _phaseMaxMbps = _emaMbps;   // ثبت پیک برای نتیجه نهایی
        SetGauge(_emaMbps);

        // پیشرفت بر اساس زمان سپری‌شده از سقف فاز محاسبه می‌شود (نه حجم — چون دیگر حجم هدف از پیش معلوم نیست)
        double timeFrac = Math.Min(1.0, (now - _phaseStart).TotalSeconds / PhaseDurationSeconds);
        SetProgress(_progressBase + _progressSpan * timeFrac);

        // سقف ایمنی حجمی: اگر لینک آن‌قدر سریع است که زودتر از پایان زمان به سقف رسیدیم، فاز را همین‌جا تمام کن
        if (bytes >= MaxPhaseBytes) { try { _phaseCts?.Cancel(); } catch { } }
    }

    private void SetGauge(double mbps)
    {
        GaugeNumText.Text = N(mbps, "0.0");
        double frac = Math.Max(0, Math.Min(1, mbps / GaugeMaxMbps));
        GaugeValueArc.StrokeDashArray = new DoubleCollection { frac * ArcLen / ArcThickness, 1000 };
        var anim = new DoubleAnimation(-90 + 180 * frac, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        NeedleRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    // برگشت نرم عقربه و قوس به صفر — نتیجه‌ها در کارت‌ها می‌مانند
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

    // تفکیک رنگ فاز: قوس گیج، فلش کوچک کنار عدد و متن فاز هم‌رنگ می‌شوند
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

    // 0 = خوب، 1 = متوسط، 2 = ضعیف
    private static int RateLowerBetter(double v, double good, double mid) =>
        v < 0 ? 2 : v <= good ? 0 : v <= mid ? 1 : 2;

    private static int RateHigherBetter(double v, double good, double mid) =>
        v >= good ? 0 : v >= mid ? 1 : 2;

    private static void SetChip(System.Windows.Controls.Border chip, System.Windows.Controls.TextBlock text, int level, bool great)
    {
        var c = level == 0 ? Green : level == 1 ? Amber : Red;
        text.Text = level == 0 ? (great ? RateGreat : RateGood) : level == 1 ? RateMid : RateBad;
        text.Foreground = new SolidColorBrush(c);
        chip.Background = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B));
        chip.Visibility = Visibility.Visible;
    }

    // فرمت اعداد همیشه با ارقام لاتین — جلوگیری از به‌هم‌ریختگی RTL
    private static string N(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
}
