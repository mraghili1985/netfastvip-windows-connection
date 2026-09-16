using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

/// <summary>
/// پروایدر اتصال از طریق RAS ویندوز (rasdial) — برای l2tp / pptp / sstp / ikev2
/// </summary>
public class RasProvider : IConnectionProvider
{
    private readonly ProtocolConfig _cfg;
    private readonly CredentialsConfig _creds;
    private readonly string _entryName;

    // لاگ این پروایدر — در MainWindow به AppendLog وصل می‌شود
    public static event Action<string>? Log;

    public RasProvider(ProtocolConfig cfg, CredentialsConfig creds)
    {
        _cfg = cfg;
        _creds = creds;
        _entryName = $"SmartVpn-{cfg.Type}";
    }

    public string Type => _cfg.Type;
    public string Kind => _cfg.Kind;
    public bool IsEnabled => _cfg.Enabled;
    public bool AuthFailed { get; private set; }
    public string? LocalIp { get; private set; }

    private string TunnelType => _cfg.Type switch
    {
        "ikev2" => "Ikev2",
        "sstp" => "Sstp",
        "l2tp" => "L2tp",
        "pptp" => "Pptp",
        _ => throw new NotSupportedException($"نوع {_cfg.Type} پشتیبانی نمی‌شود"),
    };

    public async Task<bool> ProbeAsync(string host, CancellationToken ct)
    {
        // 🔑 قدم ۱۰.۷: پروب با پورت پروفایل (sstp پیش‌فرض 443، pptp پیش‌فرض 1723)
        // l2tp و ikev2 روی UDP هستن و پروب TCP معنی نداره — همیشه true
        if (_cfg.Type is not ("sstp" or "pptp")) return true;

        var port = _cfg.Port ?? (_cfg.Type == "sstp" ? 443 : 1723);
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connectTask, Task.Delay(3000, ct));
            return done == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ConnectAsync(string host, CancellationToken ct)
    {
        AuthFailed = false;
        LocalIp = null;

        // 🔑 قدم ۱۰.۷: اگر sstp با پورت غیر 443 بود، آدرس سرور به شکل host:port ساخته می‌شه
        var server = _cfg.Type == "sstp" && _cfg.Port is int sstpPort && sstpPort > 0 && sstpPort != 443
            ? $"{host}:{sstpPort}"
            : host;

        // انتری معلق از اجرای قبلی (کرش/بسته‌شدن ناگهانی) را قطع کن،
        // وگرنه Remove-VpnConnection شکست می‌خورد و Add خطای «از قبل وجود دارد» می‌دهد
        await RunAsync("rasdial", $"\"{_entryName}\" /disconnect", ct);

        var psk = _cfg.Type == "l2tp"
            ? $" -L2tpPsk '{_creds.L2tpPsk}' -Force"
            : "";

        // ساخت (یا بازسازی) انتری VPN با پاورشل — پاک‌سازی در هر دو سطح user و all-user
        // کشف باگ: در Add-VpnConnection ویندوز، تانل IKEv2 فقط از Eap یا MachineCertificate پشتیبانی می‌کند؛ MSChapv2 فقط مال sstp/l2tp/pptp است و روی ikev2 خطای «WIN32 87 - The parameter is incorrect» می‌دهد
        var authMethod = _cfg.Type == "ikev2" ? "Eap" : "MSChapv2";

        var ps =
            $"Remove-VpnConnection -Name '{_entryName}' -Force -ErrorAction SilentlyContinue; " +
            $"Remove-VpnConnection -Name '{_entryName}' -AllUserConnection -Force -ErrorAction SilentlyContinue; " +
            $"Add-VpnConnection -Name '{_entryName}' -ServerAddress '{server}' -TunnelType {TunnelType}{psk} " +
            $"-AuthenticationMethod {authMethod} -EncryptionLevel Optional -RememberCredential:$false";

        var create = await RunAsync("powershell", $"-NoProfile -Command \"{ps}\"", ct);
        if (create.ExitCode != 0)
        {
            Log?.Invoke($"[{Type}] entry create failed: {Squash(create.Output)}");
            return false;
        }

        // شماره‌گیری با rasdial
        var dial = await RunAsync("rasdial", $"\"{_entryName}\" \"{_creds.Username}\" \"{_creds.Password}\"", ct);

        if (dial.ExitCode != 0)
        {
            var code = ExtractRasError(dial.Output);

            // خطای 691 = یوزرنیم/پسورد اشتباه یا پایان اشتراک — خطای قطعی است، صبرکردن برای بررسی مجدد لازم نیست
            if (code == "691")
            {
                Log?.Invoke($"[{Type}] rasdial failed (error {code}): " + Squash(dial.Output));
                var authHint = HintFor(code);
                if (authHint != null) Log?.Invoke($"[{Type}] hint: {authHint}");
                AuthFailed = true;
                return false;
            }

            // 🔑 باگ‌فیکس: برای SSTP (TLS handshake) و L2TP (نگوسیشن IPsec/NAT-T)، سرویس RAS ویندوز گاهی مستقل از خود پروسهٔ rasdial.exe
            // چند ثانیه دیرتر تونل را کامل می‌کند و کانکشن واقعاً وصل می‌شود — بدون اینکه rasdial.exe (که قبلاً خارج شده) به ما خبر بدهد.
            // قبل از اعلام شکست قطعی، تا ۸ ثانیه (هر یک ثانیه) وضعیت واقعی RAS را با همان روش IsAliveAsync دوباره چک می‌کنیم.
            Log?.Invoke($"[{Type}] rasdial reported failure" + (code != null ? $" (error {code})" : "") + " - rechecking actual RAS state before giving up...");
            var recovered = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(1000, ct);
                if (await IsAliveAsync(ct)) { recovered = true; break; }
            }

            if (!recovered)
            {
                Log?.Invoke($"[{Type}] rasdial failed" + (code != null ? $" (error {code})" : "") + ": " + Squash(dial.Output));
                var hint = code != null ? HintFor(code) : null;
                if (hint != null) Log?.Invoke($"[{Type}] hint: {hint}");
                return false;
            }

            Log?.Invoke($"[{Type}] recovered - Windows RAS connected shortly after the reported failure");
        }

        // 🔑 قدم ۱۰.۸: گرفتن IP لوکال اینترفیس VPN برای پنل اینفو و شمارنده ترافیک
        LocalIp = await GetLocalIpAsync(ct);
        return true;
    }

    private static string? ExtractRasError(string output)
    {
        var m = Regex.Match(output, @"\b(6\d{2}|7\d{2}|8\d{2})\b");
        return m.Success ? m.Value : null;
    }

    private static string? HintFor(string code) => code switch
    {
        "691" => "username/password rejected by server (or account expired)",
        "789" or "809" => "L2TP/IPsec negotiation failed - check PSK; behind NAT set registry AssumeUDPEncapsulationContextOnSendRule=2 and reboot",
        "800" => "tunnel unreachable - server down or port blocked by ISP/firewall",
        "807" or "628" or "619" => "connection interrupted - network instability or filtering",
        "720" => "PPP negotiation failed - protocol settings mismatch with server",
        "812" or "919" => "server-side policy rejected the connection",
        _ => null,
    };

    private static string Squash(string s) =>
        string.Join(" | ", s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    private async Task<string?> GetLocalIpAsync(CancellationToken ct)
    {
        var ps = $"(Get-NetIPAddress -InterfaceAlias '{_entryName}' -AddressFamily IPv4 -ErrorAction SilentlyContinue).IPAddress";
        var res = await RunAsync("powershell", $"-NoProfile -Command \"{ps}\"", ct);
        foreach (var line in res.Output.Split('\n'))
        {
            var s = line.Trim();
            if (IPAddress.TryParse(s, out _)) return s;
        }
        return null;
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        LocalIp = null;
        await RunAsync("rasdial", $"\"{_entryName}\" /disconnect", ct);
    }

    public async Task<bool> IsAliveAsync(CancellationToken ct)
    {
        var res = await RunAsync("rasdial", "", ct);
        return res.Output.Contains(_entryName);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, (stdout + "\n" + stderr).Trim());
    }
}