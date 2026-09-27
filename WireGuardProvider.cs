using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVpn;

internal class WireGuardProvider : IConnectionProvider
{
    private readonly bool _isAmnezia;
    private readonly string _confContent;
    private string _tunnelName = "wg0";
    private string? _confPath;
    private CancellationTokenSource? _cts;
    private bool _authFailed;
    private string? _localIp;

    public string  Type      => _isAmnezia ? "amneziawg" : "wireguard";
    public string  Kind      => "vpn";
    public bool    IsEnabled => FindMainExe() != null;
    public bool    AuthFailed => _authFailed;
    public string? LocalIp   => _localIp;
    public string? EndpointHost { get; }
    public int? EndpointPort { get; }

    public static string? LastHandshake { get; internal set; }

    public event Action<string>?          Log;
    public event Action<string?, string?>? Connected;
    public event Action?                  Disconnected;

    public WireGuardProvider(string confContent)
    {
        _confContent = confContent;
        _isAmnezia   = IsAmneziaConf(confContent);
        var (host, port) = ParseEndpoint(confContent);
        EndpointHost = host;
        EndpointPort = port;
    }

    public static bool IsAmneziaConf(string conf)
    {
        if (string.IsNullOrWhiteSpace(conf)) return false;
        if (conf.Contains("# amw", StringComparison.OrdinalIgnoreCase)) return true;
        if (conf.Contains("HeaderProtectionKey", StringComparison.OrdinalIgnoreCase)) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(conf, @"(?mi)^\s*(Jc|Jmin|Jmax|H[1-4]|S[1-4]|HeaderProtectionKey)\s*=");
    }

    private string? FindMainExe()
    {
        var exe = _isAmnezia ? "amneziawg.exe" : "wireguard.exe";
        var p = Path.Combine(AppContext.BaseDirectory, "Data", "wireguard", exe);
        if (File.Exists(p)) return p;
        p = Path.Combine(AppContext.BaseDirectory, "Data", _isAmnezia ? "amneziawg" : "wireguard", exe);
        if (File.Exists(p)) return p;
        p = Path.Combine(AppContext.BaseDirectory, "wireguard", exe);
        if (File.Exists(p)) return p;
        return null;
    }

    private string? FindWgCliExe()
    {
        var exe = _isAmnezia ? "awg.exe" : "wg.exe";
        var p = Path.Combine(AppContext.BaseDirectory, "Data", "wireguard", exe);
        if (File.Exists(p)) return p;
        p = Path.Combine(AppContext.BaseDirectory, "Data", _isAmnezia ? "amneziawg" : "wireguard", exe);
        if (File.Exists(p)) return p;
        p = Path.Combine(AppContext.BaseDirectory, "wireguard", exe);
        if (File.Exists(p)) return p;
        return null;
    }

    private string SvcName => (_isAmnezia ? "AmneziaWGTunnel$" : "WireGuardTunnel$") + _tunnelName;

    private bool IsServiceInstalled()
        => RunAndGetOutput("sc", $"query \"{SvcName}\"").Contains("SERVICE_NAME");

    private bool IsServiceRunning()
        => RunAndGetOutput("sc", $"query \"{SvcName}\"").Contains("RUNNING");

    public static string ParseTunnelName(string conf, bool isAmnezia = false)
    {
        foreach (var line in conf.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("# Name", StringComparison.OrdinalIgnoreCase))
            {
                var eq = l.IndexOf('=');
                if (eq >= 0) return l.Substring(eq + 1).Trim();
            }
            if (l.StartsWith("[Interface]", StringComparison.OrdinalIgnoreCase)) break;
        }
        return isAmnezia ? "awg0" : "wg0";
    }

    public static (string? Host, int? Port) ParseEndpoint(string conf)
    {
        foreach (var line in conf.Split('\n'))
        {
            var l = line.Trim();
            if (!l.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase)) continue;

            var eq = l.IndexOf('=');
            if (eq < 0) continue;

            var value = l[(eq + 1)..].Trim();
            if (string.IsNullOrEmpty(value)) continue;

            var lastColon = value.LastIndexOf(':');
            if (lastColon > 0)
            {
                var host = value[..lastColon].Trim('[', ']', ' ');
                var portStr = value[(lastColon + 1)..].Trim();
                if (int.TryParse(portStr, out var port))
                {
                    return (host, port);
                }
                return (host, null);
            }
            return (value, null);
        }
        return (null, null);
    }

    private static string? ParseAddress(string conf)
    {
        foreach (var line in conf.Split('\n'))
        {
            var l = line.Trim();
            if (!l.StartsWith("Address", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = l.IndexOf('=');
            if (eq < 0) continue;
            var val   = l.Substring(eq + 1).Trim().Split(',')[0].Trim();
            var slash = val.IndexOf('/');
            return slash > 0 ? val.Substring(0, slash) : val;
        }
        return null;
    }

    private static string RunAndGetOutput(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return string.Empty;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output;
        }
        catch { return string.Empty; }
    }

    private async Task RunExeAsync(string exe, string arguments, CancellationToken ct)
    {
        Log?.Invoke($"[wg] {Path.GetFileName(exe)} {arguments}");
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        using var p = Process.Start(psi);
        if (p == null) { Log?.Invoke("[wg] failed to start process"); return; }
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(ct);
        if (!string.IsNullOrWhiteSpace(stdout)) Log?.Invoke("[wg] " + stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr)) Log?.Invoke("[wg] err: " + stderr.Trim());
    }

    private void UpdateHandshake()
    {
        try
        {
            var cli = FindWgCliExe();
            if (cli == null) { LastHandshake = null; return; }

            var output = RunAndGetOutput(cli, $"show {_tunnelName} latest-handshakes");
            if (string.IsNullOrWhiteSpace(output)) { LastHandshake = "—"; return; }

            long bestTs = 0;
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[^1].Trim(), out var ts) && ts > bestTs)
                    bestTs = ts;
            }

            if (bestTs == 0) { LastHandshake = "never"; return; }

            // همه انگلیسی — مثل بقیه status label ها
            var ago = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - bestTs;
            LastHandshake = ago switch
            {
                < 0    => "—",
                < 60   => $"{ago}s ago",
                < 3600 => $"{ago / 60}m ago",
                _      => $"{ago / 3600}h ago",
            };
        }
        catch { LastHandshake = "—"; }
    }

    private void EnsureNetworkCleanState()
    {
        try
        {
            // ۱) غیرفعال‌سازی Forwarding و WeakHost روی تمام اینترفیس‌ها جهت جلوگیری از لوپ دیتای WireGuard/AmneziaWG
            // ۲) حذف روت‌های باقیمانده‌ی 0.0.0.0/1 و 128.0.0.0/1 از VPNهای دیگر که ترافیک را می‌ربایند
            var script = "$ErrorActionPreference = 'SilentlyContinue'; " +
                         "Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.Forwarding -eq 'Enabled' } | Set-NetIPInterface -Forwarding Disabled -WeakHostSend Disabled; " +
                         "Get-NetRoute -DestinationPrefix '0.0.0.0/1','128.0.0.0/1' | Where-Object { $_.InterfaceAlias -notmatch '(?i)wireguard|amnezia|wintun' } | Remove-NetRoute -Confirm:$false";

            var psi = new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            Log?.Invoke("[wg] network clean-up complete (forwarding disabled, lingering /1 routes cleared)");
        }
        catch (Exception ex)
        {
            Log?.Invoke("[wg] network clean-up notice: " + ex.Message);
        }
    }

    public Task<bool> ProbeAsync(string host, CancellationToken ct)
        => Task.FromResult(IsEnabled);

    public async Task<bool> ConnectAsync(string host, CancellationToken ct)
    {
        _authFailed   = false;
        _localIp      = null;
        LastHandshake = null;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var mainExe = FindMainExe();
        if (mainExe == null)
        {
            Log?.Invoke($"[wg] {(_isAmnezia ? "amneziawg.exe" : "wireguard.exe")} not found");
            Disconnected?.Invoke();
            return false;
        }

        // پاک‌سازی تداخل‌های شبکه و جلوگیری از لوپ دیتا
        EnsureNetworkCleanState();

        _tunnelName = ParseTunnelName(_confContent, _isAmnezia);
        Log?.Invoke($"[wg] tunnel: {_tunnelName} | amnezia: {_isAmnezia}");

        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data", "wireguard");
        Directory.CreateDirectory(dataDir);
        _confPath = Path.Combine(dataDir, $"{_tunnelName}.conf");
        await File.WriteAllTextAsync(_confPath, _confContent, ct);
        Log?.Invoke($"[wg] conf written: {_confPath}");

        // پاک‌سازی تانل قدیمی هم‌نام از هر دو سرویس wireguard و amneziawg جهت رفع کامل هرگونه تداخل
        var wgExePath = Path.Combine(AppContext.BaseDirectory, "Data", "wireguard", "wireguard.exe");
        var awgExePath = Path.Combine(AppContext.BaseDirectory, "Data", "wireguard", "amneziawg.exe");
        if (File.Exists(wgExePath) && RunAndGetOutput("sc", $"query \"WireGuardTunnel${_tunnelName}\"").Contains("SERVICE_NAME"))
        {
            Log?.Invoke($"[wg] removing existing WireGuardTunnel${_tunnelName}");
            await RunExeAsync(wgExePath, $"/uninstalltunnelservice {_tunnelName}", _cts.Token);
            await Task.Delay(1000, _cts.Token);
        }
        if (File.Exists(awgExePath) && RunAndGetOutput("sc", $"query \"AmneziaWGTunnel${_tunnelName}\"").Contains("SERVICE_NAME"))
        {
            Log?.Invoke($"[wg] removing existing AmneziaWGTunnel${_tunnelName}");
            await RunExeAsync(awgExePath, $"/uninstalltunnelservice {_tunnelName}", _cts.Token);
            await Task.Delay(1000, _cts.Token);
        }

        Log?.Invoke("[wg] installing tunnel service");
        await RunExeAsync(mainExe, $"/installtunnelservice \"{_confPath}\"", _cts.Token);

        bool started = false;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500, _cts.Token);
            if (IsServiceRunning()) { started = true; break; }
        }

        if (!started)
        {
            Log?.Invoke("[wg] service did not start in time");
            Disconnected?.Invoke();
            return false;
        }

        _localIp = ParseAddress(_confContent);
        Log?.Invoke("[wg] connected!");
        Connected?.Invoke(_localIp, null);
        _ = Task.Run(() => MonitorAsync(_cts.Token), _cts.Token);
        return true;
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(5000, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;
            UpdateHandshake();
            if (!IsServiceRunning())
            {
                Log?.Invoke("[wg] service stopped unexpectedly");
                Disconnected?.Invoke();
                return;
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        _authFailed   = false;
        _localIp      = null;
        LastHandshake = null;
        var mainExe = FindMainExe();
        if (mainExe == null) return;
        Log?.Invoke("[wg] uninstalling tunnel service");
        await RunExeAsync(mainExe, $"/uninstalltunnelservice {_tunnelName}", CancellationToken.None);
        try { if (_confPath != null && File.Exists(_confPath)) File.Delete(_confPath); } catch { }
    }

    public Task<bool> IsAliveAsync(CancellationToken ct)
        => Task.FromResult(IsServiceRunning());
}
