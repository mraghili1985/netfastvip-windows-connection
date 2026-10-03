using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

public sealed class OpenVpnProvider : IConnectionProvider
{
    private readonly OpenVpnEndpoint _ep;
    private readonly string _baseProfileName;
    private readonly CredentialsConfig _creds;
    private Process? _proc;
    private int _mgmtPort;
    private string _mgmtPassword = "";


    // زمان آخرین قطع openvpn — static تا بین همه اتصال‌ها مشترک باشد.
    // برای مهلت تمیزکاری ویندوز (آزاد شدن آداپتور dco و حذف روت‌ها) قبل از اتصال بعدی —
    // بدون آن، سوییچ سریع بین کانکشن‌ها گاهی هنگ می‌کند و IP تانل نمی‌گیرد
    private static DateTime _lastTeardownUtc = DateTime.MinValue;

    // اتصال دائمی به کانال مدیریت (مثل OpenVPN-GUI) — وصل/قطع مکرر باعث کرش openvpn روی ویندوز می‌شه
    private TcpClient? _mgmtClient;
    private StreamReader? _mgmtReader;
    private StreamWriter? _mgmtWriter;
    private readonly SemaphoreSlim _mgmtLock = new(1, 1);

    // مسیر openvpn.exe — اول کنار خود برنامه (حالت پورتابل)، بعد مسیرهای نصب معمول ویندوز
    private static readonly string[] ExeCandidates =
    [
        Path.Combine(AppContext.BaseDirectory, "Data", "openvpn", "openvpn.exe"),
        Path.Combine(AppContext.BaseDirectory, "Data", "openvpn", "bin", "openvpn.exe"),
        @"C:\Program Files\OpenVPN\bin\openvpn.exe",
        @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe",
    ];

    // null یعنی OpenVPN در دسترس نیست — برای پیام راهنما در UI هم قابل استفاده است
    public static string? FindExe()
    {
        foreach (var p in ExeCandidates)
            if (File.Exists(p)) return p;
        return null;
    }

    // ---- درایور ovpn-dco برای حالت پورتابل ----
    // openvpn.exe به‌تنهایی کافی نیست: آداپتور مجازی به درایور ovpn-dco نیاز دارد.
    // اگر OpenVPN نصب باشد درایور هست؛ در حالت پورتابل از openvpn\driver کنار برنامه
    // خودمان با pnputil نصبش می‌کنیم (نیاز به ادمین — مانیفست برنامه تأمین می‌کند).
    private static bool DcoDriverInstalled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\ovpn-dco");
            return key != null;
        }
        catch { return false; }
    }

    private static async Task EnsureDcoDriverAsync(CancellationToken ct)
    {
        // چک اول: اگر قبلاً نصب شده، اصلا وارد منطق نصب نمی‌شویم
        if (DcoDriverInstalled()) return;
        var driverDir = Path.Combine(AppContext.BaseDirectory, "Data", "openvpn", "driver");
        var inf = Path.Combine(driverDir, "ovpn-dco.inf");
        if (!File.Exists(inf))
        {
            Log?.Invoke("درایور ovpn-dco نصب نیست و فایل driver همراه برنامه هم نیست — اگر اتصال برقرار نشد، OpenVPN را نصب کنید");
            return;
        }

        // ⚠ نکته‌ی حیاتی (تست‌شده ۲۰۲۶-۰۸-۲۳): devcon باید حتماً نسخه‌ی ۶۴ بیتی باشد — نسخه‌ی
        // ۳۲ بیتی روی ویندوز ۱۱ (۶۴ بیتی) نمی‌تواند device واقعی بسازد و باعث خطاهای
        // «create_adapter: could not talk to service» و «ovpn-dco-win driver is missing»
        // می‌شود، حتی اگر خودِ pnputil یا devcon بدون خطای ظاهری اجرا شوند. با devcon ۶۴
        // بیتی (devcon install ovpn-dco.inf ovpn-dco) اتصال کامل و تست‌شده کار می‌کند —
        // بنابراین نیازی به نصاب سنگین‌تر MSI نیست.
        var devcon = Path.Combine(driverDir, "devcon.exe");
        if (File.Exists(devcon))
        {
            Log?.Invoke("در حال نصب درایور ovpn-dco (فقط بار اول)…");
            var psiDevcon = new ProcessStartInfo(devcon, $"install \"{inf}\" ovpn-dco")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                using var p = Process.Start(psiDevcon);
                if (p is not null)
                {
                    await p.WaitForExitAsync(ct);
                    Log?.Invoke(p.ExitCode == 0
                        ? "درایور ovpn-dco نصب شد"
                        : $"نصب درایور ovpn-dco با devcon ناموفق بود (کد {p.ExitCode})");
                    return;
                }
            }
            catch (Exception ex) { Log?.Invoke("خطا در نصب درایور ovpn-dco با devcon: " + ex.Message); }
        }
        else
        {
            Log?.Invoke("devcon.exe کنار درایور پیدا نشد — تلاش با pnputil (ممکن است device واقعی نسازد)");
        }

        var psi = new ProcessStartInfo("pnputil.exe", $"/add-driver \"{inf}\" /install")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return;
            await p.WaitForExitAsync(ct);
            Log?.Invoke(p.ExitCode == 0
                ? "درایور ovpn-dco نصب شد"
                : $"نصب درایور ovpn-dco ناموفق بود (کد {p.ExitCode})");
        }
        catch (Exception ex) { Log?.Invoke("خطا در نصب درایور ovpn-dco: " + ex.Message); }
    }

    // خروجی openvpn و پیام‌های این کلاس به لاگ اپ — مثل RasProvider.Log (Console.WriteLine در WPF به جایی نمی‌رسد)
    public static event Action<string>? Log;

    public OpenVpnProvider(OpenVpnEndpoint ep, string baseProfileName, CredentialsConfig creds)
    {
        _ep = ep;
        _baseProfileName = baseProfileName;
        _creds = creds;
    }

    public string Type => $"openvpn-{_ep.Proto}-{_ep.Port}";
    public string Kind => "vpn";
    public bool IsEnabled => true;
    public bool AuthFailed { get; private set; }
    public string? LocalIp { get; private set; }

    // وقتی openvpn ری‌استارت داخلی می‌زند (SIGUSR1/ping-restart) حتی اگر خودش زیر ۵ ثانیه ترمیم شود،
    // جلسه مستعد زامبی شدن است (ترافیک رد نمی‌شود) و اینفو هم کهنه می‌ماند —
    // با این پرچم، چک بعدی IsAlive (حداکثر ۵ ثانیه بعد) همیشه اتصال مجدد کامل و تمیز انجام می‌دهد
    private volatile bool _restartDetected;

    public Task<bool> ProbeAsync(string host, CancellationToken ct)
    {
        // پیش‌آزمایش TCP حذف شد. این تست فقط اولین IP دامنه را بررسی می‌کرد،
        // سه ثانیه به هر اتصال اضافه می‌کرد و ممکن بود قبل از رسیدن OpenVPN
        // به IP بعدی، همان پروفایل را رد کند. خود OpenVPN باید DNS و failover را انجام دهد.
        return Task.FromResult(true);
    }

    private async Task<List<string>> ResolveRemoteTargetsAsync(string host, CancellationToken ct)
    {
        var value = (host ?? string.Empty).Trim();
        if (value.Length == 0) return [];

        // پشتیبانی از چند IP که کاربر با ; یا , وارد کرده است.
        var tokens = value
            .Split([';', ',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToList();
        if (tokens.Count == 0) tokens.Add(value);

        var targets = new List<string>();
        foreach (var token in tokens)
        {
            var before = targets.Count;

            if (IPAddress.TryParse(token, out var literal))
            {
                targets.Add(literal.ToString());
            }
            else
            {
                try
                {
                    using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    dnsCts.CancelAfter(TimeSpan.FromSeconds(2));
                    var addresses = await Dns.GetHostAddressesAsync(token, dnsCts.Token);
                    foreach (var address in addresses)
                    {
                        // کانفیگ‌های فعلی برنامه IPv4 هستند؛ IPv6 را وارد لیست
                        // نکن تا روی سیستم‌های بدون IPv6 باعث timeout اضافه نشود.
                        if (address.AddressFamily == AddressFamily.InterNetwork)
                            targets.Add(address.ToString());
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // در صورت کند بودن DNS، خود OpenVPN با نام دامنه تلاش می‌کند.
                }
                catch when (!ct.IsCancellationRequested)
                {
                    // خطای موقت DNS نباید اتصال را متوقف کند.
                }

                // اگر DNS در این لحظه جواب نداد، hostname را نگه می‌داریم تا
                // resolv-retry خود OpenVPN آن را دوباره resolve کند.
                if (targets.Count == before) targets.Add(token);
            }
        }

        return targets
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static string RemoveOptionLines(string text, params string[] optionNames)
    {
        var names = new HashSet<string>(optionNames, StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return string.Join(Environment.NewLine, lines.Where(line =>
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#") || trimmed.Length == 0) return true;
            var option = trimmed.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)[0];
            return !names.Contains(option);
        }));
    }

    // پورت آزاد از خود سیستم می‌گیریم — دیگه هیچ‌وقت Socket bind failed نمی‌گیریم
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---- رفع سوییچ رندوم بین کانکشن‌ها ----
    // مشاهده شد که هنگام سوییچ سریع، دیتا گاهی رد می‌شود و گاهی نه. علت: بین قطع تانل قبلی
    // و برقراری تانل جدید، هر دو ممکن است هم‌زمان روت اسپلیت-دیفالت (0.0.0.0/1 و 128.0.0.0/1)
    // با گیت‌وی‌های متفاوت در جدول روت ویندوز داشته باشند — انتخاب ویندوز بین دو روت با
    // پیشوند یکسان و گیت‌وی متفاوت قطعی نیست. OpenVPN Connect این مشکل را با پاک‌کردن
    // صریح همه‌ی روت‌های آداپتور قبل از افزودن روت جدید حل می‌کند (ActionDeleteAllRoutesOnInterface).
    // اینجا دو تضمین معادل اضافه می‌شود: پاک‌سازی اجباری روت‌های یتیم بعد از قطع (حتی اگر Kill
    // اجباری شده و openvpn فرصت پاک‌سازی خودش را نداشته) + تأیید فعال محو شدن آداپتور قبلی
    // به‌جای حدس‌زدن با یک مهلت ثابت.

    // اگر Kill اجباری شود، openvpn فرصت اجرای پاک‌سازی روت داخلی خودش را ندارد و روت‌های
    // اسپلیت-دیفالت قبلی یتیم می‌مانند — این‌ها را همیشه صریحاً حذف می‌کنیم (بی‌خطر اگر وجود نداشته باشند).
    private static async Task CleanupStaleRoutesAsync(CancellationToken ct)
    {
        foreach (var (net, mask) in new[] { ("0.0.0.0", "128.0.0.0"), ("128.0.0.0", "128.0.0.0") })
        {
            try
            {
                var psi = new ProcessStartInfo("route.exe", $"delete {net} mask {mask}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p is not null) await p.WaitForExitAsync(ct);
            }
            catch { /* بی‌خطر — اگر روتی نبود فقط پیام "not found" برمی‌گردد */ }
        }
    }

    // به‌جای صبر ثابت، واقعاً چک می‌کنیم آداپتور تانل قبلی (ovpn-dco / TAP-Windows) از
    // لیست آداپتورهای ویندوز محو شده یا نه — حداکثر ۳ ثانیه، هر ۲۰۰ میلی‌ثانیه یک بار.
    private static async Task WaitForAdapterGoneAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(3000);
        while (DateTime.UtcNow < deadline)
        {
            if (!await AdapterPresentAsync(ct)) return;
            await Task.Delay(200, ct);
        }
    }

    private static async Task<bool> AdapterPresentAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -Command \"(Get-NetAdapter -IncludeHidden | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -match 'OpenVPN|TAP-Windows|Wintun' }).Count\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return int.TryParse(output.Trim(), out var n) && n > 0;
        }
        catch { return false; }
    }

    public async Task<bool> ConnectAsync(string host, CancellationToken ct)
    {
        AuthFailed = false;
        LocalIp = null;

        var exePath = FindExe();
        if (exePath is null)
        {
            Log?.Invoke("openvpn.exe پیدا نشد — OpenVPN نصب نیست؛ فقط اتصال‌های openvpn به آن نیاز دارند (راهنما: README.txt)");
            return false;
        }

        // حالت پورتابل: اگر درایور نصب نیست و همراه برنامه هست، همین‌جا نصب می‌شود
        await EnsureDcoDriverAsync(ct);

        // مهلت تمیزکاری: فقط وقتی از قطع قبلی کمتر از ۳ ثانیه گذشته باشد اصلا لازم است چک کنیم
        // (اتصال اول یا اتصال بعد از مکث طولانی هیچ تاخیری ندارد). به‌جای صبر کورکورانه‌ی ثابت،
        // واقعاً تأیید می‌کنیم آداپتور تانل قبلی محو شده — همین جایگزینی، ریشه‌ی رفتار رندوم
        // (هم‌پوشانی موقت روت‌های دو تانل) را می‌بندد.
        // نکته‌ی مهم (ریشه‌ی باگ «سوییچ گاهی موفق و گاهی ترافیک واقعی رد نمی‌کرد در حالی که پینگ عادی جواب می‌داد»):
        // قبلاً این چک فقط وقتی اجرا می‌شد که کمتر از ۳ ثانیه از قطع قبلی گذشته باشد، با این فرض که بعد از ۳ ثانیه
        // آداپتور قطعاً محو شده. اما مسیر سوییچ (StopManuallyAsync + تاخیرهای بین‌راهی + Probe) خودش گاهی بیش از
        // ۳ ثانیه طول می‌کشد، پس وقتی به اینجا می‌رسیدیم چک کاملاً رد می‌شد بدون اینکه واقعاً تأیید شود آداپتور قبلی
        // رفته — همان لحظه‌ای که هم‌پوشانی موقت روت‌های دو تانل ممکن است رخ دهد (پینگ از یک مسیر رد می‌شود ولی
        // ترافیک واقعی از مسیر درست نمی‌رود). حالا همیشه (اگر قبلاً قطعی داشته‌ایم) واقعاً تأیید می‌کنیم — این چک
        // وقتی آداپتور از قبل رفته تقریباً بی‌هزینه و فوری برمی‌گردد.
        if (_lastTeardownUtc != DateTime.MinValue)
        {
            Log?.Invoke($"[{Type}] در انتظار آزاد شدن کامل آداپتور اتصال قبلی…");
            await WaitForAdapterGoneAsync(ct);
        }

        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
        var basePath = Path.Combine(AppContext.BaseDirectory, "Data", _baseProfileName);
        if (!File.Exists(basePath))
        {
            Log?.Invoke($"{_baseProfileName} پیدا نشد: {basePath}");
            return false;
        }

        // پروفایل داینامیک (قدم ۷)
        var proto = _ep.Proto == "udp" ? "udp" : "tcp-client";
        var ovpn = Path.Combine(AppContext.BaseDirectory, "Data", $"gen-{_ep.Proto}-{_ep.Port}.ovpn");
        var baseText = await File.ReadAllTextAsync(basePath, ct);

        // این گزینه‌ها را خود برنامه مدیریت می‌کند؛ اگر در base.ovpn هم باشند،
        // نسخه قدیمی می‌توانست با مقدار کندتر/متناقض آن‌ها را override کند.
        baseText = RemoveOptionLines(baseText,
            "resolv-retry", "connect-timeout", "connect-retry", "connect-retry-max");

        var remoteTargets = await ResolveRemoteTargetsAsync(_ep.Address, ct);
        if (remoteTargets.Count == 0) remoteTargets.Add(_ep.Address);
        var remoteLines = string.Join(Environment.NewLine,
            remoteTargets.Select(target => $"remote {target} {_ep.Port}"));
        Log?.Invoke($"[{Type}] remote candidates: {string.Join(", ", remoteTargets)}");

        // --- سخت‌سازی سمت کلاینت: فقط بستن نشت IPv6 (معادل رفتار OpenVPN Connect) ---
        // وگرنه ویندوز یوتیوب/واتس‌اپ را از IPv6 مستقیم (فیلترشده) می‌فرستد و فقط همین اپ‌ها از کار می‌افتند.
        // ⚠ گزینه‌های DNS (dhcp-option / block-outside-dns) عمدا حذف شدند:
        //   بعضی سرورها خودشان DNS پوش می‌کنند و دوتا شدن DNS باعث خطای
        //   «netsh add dns → error code 1» و قطع کامل اتصال می‌شد (تست‌شده ۲۰۲۶-۰۸-۱۷).
        // block-outside-dns بی‌خطر است: netsh ندارد (فقط فیلتر WFP) و چون DNS دستی اضافه نمی‌کنیم،
        //   با DNS پوش‌شده سرور هم تداخلی ندارد. بدون آن، ویندوز موازی از DNS آلودهٔ
        //   آداپتور فیزیکی هم استعلام می‌گیرد و دامنه‌های فیلترشده IP قلابی می‌گیرند.
        // ⚠ redirect-gateway ipv6 عمدا حذف شد: برای پیدا کردن گیت‌وی IPv6 فعلی سیستم از
        //   GetBestInterfaceEx استفاده می‌کند و روی سیستم‌هایی که اصلا IPv6 فعال/متصل ندارند
        //   (رایج روی ویندوزهای تازه/بدون OpenVPN قبلی) با خطای «GetBestInterfaceEx ... 1168»
        //   شکست می‌خورد و کل برقراری تانل را می‌بلعد (تست‌شده ۲۰۲۶-۰۸-۲۲). block-ipv6 به‌تنهایی
        //   برای بستن نشت IPv6 کافی است (کاملا محلی روی خود آداپتور تانل کار می‌کند، به گیت‌وی
        //   فیزیکی سیستم نیازی ندارد) — دقیقا همین ترکیب سال‌ها روش خود OpenVPN Connect بوده.
        const string hardening =
            "\n# --- client hardening: IPv6 leak block + DNS leak block (mirror OpenVPN Connect) ---\n" +
            "block-outside-dns\n" +
            "block-ipv6\n";
        // نکته: ping-restart را دستکاری نکن! هر سرور تایمر ضربان خودش را push می‌کند (مثلا ping 10 یا ping 20)
        // و مقدار ثابت کوتاه‌تر از ضربان سرور باعث قطع/وصل مداوم در حالت idle می‌شود (باگ ۱۸ اوت).
        // تشخیص سریع قطعی به عهده نگهبان پینگ برنامه است (MainWindow.Stats.cs) که پروتکل-مستقل است.

        const string connectionTuning =
            "# --- fast connection and endpoint failover ---\n" +
            "resolv-retry infinite\n" +
            "connect-timeout 6\n" +
            "connect-retry 1 2\n" +
            "connect-retry-max 1\n";

        await File.WriteAllTextAsync(ovpn,
            $"{remoteLines}\nproto {proto}\n{connectionTuning}{baseText}{hardening}", ct);

        var authPath = Path.Combine(AppContext.BaseDirectory, "Data", "auth.txt");
        await File.WriteAllLinesAsync(authPath, [_creds.Username, _creds.Password], ct);

        // پورت داینامیک + پسورد تصادفی برای Management Interface (قدم ۸)
        _mgmtPort = GetFreePort();
        _mgmtPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        var mgmtPwFile = Path.Combine(AppContext.BaseDirectory, "Data", "mgmt-pass.txt");
        await File.WriteAllTextAsync(mgmtPwFile, _mgmtPassword + "\n", ct);

        var psi = new ProcessStartInfo(exePath,
            $"--config \"{ovpn}\" --auth-user-pass \"{authPath}\" " +
            $"--management 127.0.0.1 {_mgmtPort} \"{mgmtPwFile}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "Data"),
        };

        var connected = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void OnLine(string? line)
        {
            if (line is null) return;
            if (line.Contains("MANAGEMENT:")) return; // نویز کانال مدیریت
            Log?.Invoke($"[{Type}] {line}");
            if (line.Contains("SIGUSR1[") || line.Contains("Inactivity timeout"))
                _restartDetected = true; // ری‌استارت داخلی دیده شد — عمدا با InitSeqCompleted پاک نمی‌شود تا ترمیم سریع هم از دست نرود
            if (line.Contains("Initialization Sequence Completed"))
                connected.TrySetResult(true);
            if (line.Contains("AUTH_FAILED"))
            {
                AuthFailed = true;
                connected.TrySetResult(false);
            }
            if (line.Contains("Exiting due to fatal error") ||
                line.Contains("Connection refused"))
                connected.TrySetResult(false);
        }

        _proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        _proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        _proc.Exited += (_, _) => connected.TrySetResult(false);

        _proc.Start();
        KillOnCloseJob.Add(_proc); // اگر اپ کرش یا End Task شد، ویندوز خودش openvpn را می‌کشد و تانل قطع می‌شود
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();

        var winner = await Task.WhenAny(connected.Task, Task.Delay(35000, ct));
        if (winner != connected.Task)
        {
            Log?.Invoke($"[{Type}] timeout — وصل نشد");
            await DisconnectAsync(ct);
            return false;
        }
        var ok = await connected.Task;
        if (!ok)
        {
            await DisconnectAsync(ct);
            return false;
        }

        // 🔑 قدم ۱۰.۶: IP خصوصی از دستور state کانال مدیریت
        LocalIp = await GetLocalIpAsync(ct);
        return true;
    }

    private async Task<string?> GetLocalIpAsync(CancellationToken ct)
    {
        var lines = await MgmtAsync("state", ct);
        var stateLine = lines?.FirstOrDefault(l => l.Contains(",CONNECTED,SUCCESS,"));
        var parts = stateLine?.Split(',');
        // فرمت: time,CONNECTED,SUCCESS,localIp,remoteIp,remotePort,...
        // فیلد پنجم = IP واقعی سرور از زبان خود openvpn — با فرمت مارکر لاگ می‌فرستیم تا UI همان‌جا بردارد
        if (parts is { Length: > 4 } && parts[4].Length > 0)
            Log?.Invoke($"[{Type}] state: link remote: [AF_INET]{parts[4]}:{(parts.Length > 5 ? parts[5] : "0")}");
        return parts is { Length: > 3 } && parts[3].Length > 0 ? parts[3] : null;
    }

    // کانال مدیریت دائمی: یک بار وصل می‌شیم و همه‌ی دستورها از همون می‌رن
    private async Task<List<string>?> MgmtAsync(string command, CancellationToken ct)
    {
        if (_proc is not { HasExited: false }) return null;
        await _mgmtLock.WaitAsync(ct);
        try
        {
            if (_mgmtClient is not { Connected: true })
            {
                CloseMgmt();
                var client = new TcpClient();
                var connect = client.ConnectAsync("127.0.0.1", _mgmtPort);
                if (await Task.WhenAny(connect, Task.Delay(2000, ct)) != connect || !client.Connected)
                {
                    client.Dispose();
                    return null;
                }
                var stream = client.GetStream();
                _mgmtClient = client;
                _mgmtReader = new StreamReader(stream);
                _mgmtWriter = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };

                // پسورد بلافاصله — openvpn خط اول دریافتی رو به‌عنوان پسورد می‌خونه
                await _mgmtWriter.WriteLineAsync(_mgmtPassword.AsMemory(), ct);
            }

            await _mgmtWriter!.WriteLineAsync(command.AsMemory(), ct);

            var lines = new List<string>();
            var deadline = Task.Delay(3000, ct);
            while (true)
            {
                var readLine = _mgmtReader!.ReadLineAsync(ct).AsTask();
                if (await Task.WhenAny(readLine, deadline) != readLine) { CloseMgmt(); break; }
                var line = await readLine;
                if (line is null) { CloseMgmt(); break; }
                if (line.StartsWith(">")) continue; // پیام‌های realtime
                if (line.Contains("SUCCESS: password is correct")) continue;
                lines.Add(line);
                if (line == "END" || line.StartsWith("SUCCESS:") || line.StartsWith("ERROR:"))
                    break;
            }
            return lines;
        }
        catch { CloseMgmt(); return null; }
        finally { _mgmtLock.Release(); }
    }

    private void CloseMgmt()
    {
        try { _mgmtClient?.Dispose(); } catch { }
        _mgmtClient = null;
        _mgmtReader = null;
        _mgmtWriter = null;
    }

    public async Task<bool> IsAliveAsync(CancellationToken ct)
    {
        if (_proc is not { HasExited: false }) return false;

        if (_restartDetected)
        {
            _restartDetected = false;
            Log?.Invoke($"[{Type}] ری‌استارت داخلی openvpn دیده شد — اتصال مجدد کامل و تمیز");
            return false;
        }

        var lines = await MgmtAsync("state", ct);
        var stateLine = lines?.FirstOrDefault(l =>
            l.Split(',').Length >= 2 && long.TryParse(l.Split(',')[0], out _));
        if (stateLine is null) return true;

        var state = stateLine.Split(',')[1];
        if (state == "CONNECTED") return true;

        // openvpn فقط وقتی از CONNECTED خارج می‌شود که خودش قطعی را قطعی تشخیص داده باشد
        // (TUN error، exit-notify سرور یا ping-restart) — پس مثل OpenVPN Connect همان لحظه
        // مرده اعلام می‌کنیم تا موتور اتصال بلافاصله اتصال مجدد کامل و تمیز انجام دهد.
        Log?.Invoke($"[{Type}] قطعی تانل تشخیص داده شد (state={state}) — ��تصال مجدد کامل");
        return false;
    }

    // ترافیک لحظه‌ای — TUN/TAP write = دانلود، read = آپلود
    public async Task<string?> GetStatusLineAsync(CancellationToken ct)
    {
        var lines = await MgmtAsync("status", ct);
        if (lines is null) return null;

        string? up = null, down = null;
        foreach (var l in lines)
        {
            if (l.StartsWith("TUN/TAP read bytes,")) up = l.Split(',')[1];
            if (l.StartsWith("TUN/TAP write bytes,")) down = l.Split(',')[1];
        }
        if (up is null || down is null) return null;
        return $"⬇ {FormatBytes(down)}   ⬆ {FormatBytes(up)}";
    }

    private static string FormatBytes(string s)
    {
        if (!double.TryParse(s, out var b)) return s;
        string[] units = ["B", "KB", "MB", "GB"];
        var i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return $"{b:0.#} {units[i]}";
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        LocalIp = null;
        if (_proc is { HasExited: false })
        {
            // اول تلاش برای خاموشی تمیز با SIGTERM
            var res = await MgmtAsync("signal SIGTERM", ct);
            if (res is not null)
            {
                var exited = _proc.WaitForExitAsync(ct);
                if (await Task.WhenAny(exited, Task.Delay(2500, ct)) == exited)
                    Log?.Invoke($"[{Type}] خاموشی تمیز انجام شد ✓");
            }
            if (_proc is { HasExited: false })
            {
                Log?.Invoke($"[{Type}] SIGTERM جواب نداد — kill");
                _proc.Kill(entireProcessTree: true);
                await _proc.WaitForExitAsync(ct);
            }
        }
        if (_proc != null)
        {
            // صرف‌نظر از اینکه قطع تمیز بوده یا با Kill اجباری انجام شده، روت‌های یتیم احتمالی
            // را صریحاً پاک می‌کنیم تا با روت تانل جدید هم‌پوشانی نکنند
            await CleanupStaleRoutesAsync(CancellationToken.None);
            _lastTeardownUtc = DateTime.UtcNow; // شروع مهلت تمیزکاری برای اتصال بعدی
        }
        CloseMgmt();
        _proc?.Dispose();
        _proc = null;

        foreach (var f in new[] { "auth.txt", "mgmt-pass.txt" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", f);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
