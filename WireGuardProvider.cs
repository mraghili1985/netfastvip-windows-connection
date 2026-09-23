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

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ServiceQueryTimeout = TimeSpan.FromSeconds(3);

    public string Type      => _isAmnezia ? "amneziawg" : "wireguard";
    public string Kind      => "vpn";
    public bool   IsEnabled => FindMainExe() != null;
    public bool   AuthFailed => _authFailed;
    public string? LocalIp  => _localIp;

    public static string? LastHandshake { get; internal set; }

    public event Action<string>? Log;
    public event Action<string?, string?>? Connected;
    public event Action? Disconnected;

    public WireGuardProvider(string confContent)
    {
        _confContent = confContent;
        _isAmnezia = IsAmneziaConf(confContent);
    }

    private static bool IsAmneziaConf(string conf) =>
        conf.Contains("\nJc =") || conf.Contains("\nH1 =") ||
        conf.Contains("\nJmin =") || conf.Contains("# amw");

    private string? FindMainExe()
    {
        var folder = _isAmnezia ? "amneziawg" : "wireguard";
        var exe = _isAmnezia ? "amneziawg.exe" : "wireguard.exe";
        var path = Path.Combine(AppContext.BaseDirectory, "Data", folder, exe);
        return File.Exists(path) ? path : null;
    }

    private string? FindWgCliExe()
    {
        var folder = _isAmnezia ? "amneziawg" : "wireguard";
        var exe = _isAmnezia ? "awg.exe" : "wg.exe";
        var path = Path.Combine(AppContext.BaseDirectory, "Data", folder, exe);
        return File.Exists(path) ? path : null;
    }

    private string SvcName => (_isAmnezia ? "AmneziaWGTunnel$" : "WireGuardTunnel$") + _tunnelName;

    private static string ParseTunnelName(string conf)
    {
        foreach (var line in conf.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("# Name", StringComparison.OrdinalIgnoreCase))
            {
                var eq = l.IndexOf('=');
                if (eq >= 0)
                {
                    var name = l[(eq + 1)..].Trim();
                    if (name.Length > 0) return name;
                }
            }

            if (l.StartsWith("[Interface]", StringComparison.OrdinalIgnoreCase))
                break;
        }

        return "wg0";
    }

    private static string? ParseAddress(string conf)
    {
        foreach (var line in conf.Split('\n'))
        {
            var l = line.Trim();
            if (!l.StartsWith("Address", StringComparison.OrdinalIgnoreCase)) continue;

            var eq = l.IndexOf('=');
            if (eq < 0) continue;

            var value = l[(eq + 1)..].Trim().Split(',')[0].Trim();
            var slash = value.IndexOf('/');
            return slash > 0 ? value[..slash] : value;
        }

        return null;
    }

    /// <summary>
    /// اجرای امن process. خروجی و خطا هم‌زمان خوانده می‌شوند تا pipe پر نشود
    /// و wireguard.exe باعث deadlock یا هنگ برنامه نشود.
    /// </summary>
    private static async Task<(bool Completed, int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string exe,
        string arguments,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (false, -1, string.Empty, "process start failed");

        // خواندن هر دو pipe قبل از انتظار برای خروج، جلوی deadlock را می‌گیرد.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var completed = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }

            try { await process.WaitForExitAsync().ConfigureAwait(false); }
            catch { }
        }

        string stdout;
        string stderr;
        try { stdout = await stdoutTask.ConfigureAwait(false); }
        catch { stdout = string.Empty; }
        try { stderr = await stderrTask.ConfigureAwait(false); }
        catch { stderr = string.Empty; }

        var exitCode = -1;
        try
        {
            if (process.HasExited)
                exitCode = process.ExitCode;
        }
        catch { }

        return (completed, exitCode, stdout, stderr);
    }

    private static async Task<string> RunAndGetOutputAsync(
        string exe,
        string arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            exe,
            arguments,
            cancellationToken,
            ServiceQueryTimeout).ConfigureAwait(false);

        return result.StdOut + Environment.NewLine + result.StdErr;
    }

    private async Task<bool> RunExeAsync(
        string exe,
        string arguments,
        CancellationToken cancellationToken)
    {
        Log?.Invoke($"[wg] {Path.GetFileName(exe)} {arguments}");

        var result = await RunProcessAsync(
            exe,
            arguments,
            cancellationToken,
            CommandTimeout).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.StdOut))
            Log?.Invoke("[wg] " + result.StdOut.Trim());
        if (!string.IsNullOrWhiteSpace(result.StdErr))
            Log?.Invoke("[wg] err: " + result.StdErr.Trim());

        if (!result.Completed)
        {
            Log?.Invoke($"[wg] command timeout or cancellation: {Path.GetFileName(exe)}");
            return false;
        }

        if (result.ExitCode != 0)
        {
            Log?.Invoke($"[wg] command exited with code {result.ExitCode}");
            return false;
        }

        return true;
    }

    private async Task<bool> IsServiceInstalledAsync(CancellationToken cancellationToken)
    {
        var output = await RunAndGetOutputAsync(
            "sc",
            $"query \"{SvcName}\"",
            cancellationToken).ConfigureAwait(false);

        return output.Contains("SERVICE_NAME", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsServiceRunningAsync(CancellationToken cancellationToken)
    {
        var output = await RunAndGetOutputAsync(
            "sc",
            $"query \"{SvcName}\"",
            cancellationToken).ConfigureAwait(false);

        return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    private async Task UpdateHandshakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cli = FindWgCliExe();
            if (cli == null)
            {
                LastHandshake = null;
                return;
            }

            var result = await RunProcessAsync(
                cli,
                $"show \"{_tunnelName}\" latest-handshakes",
                cancellationToken,
                ServiceQueryTimeout).ConfigureAwait(false);

            var output = result.StdOut;
            if (!result.Completed || string.IsNullOrWhiteSpace(output))
            {
                LastHandshake = "—";
                return;
            }

            long bestTimestamp = 0;
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split('\t');
                if (parts.Length >= 2 &&
                    long.TryParse(parts[^1].Trim(), out var timestamp) &&
                    timestamp > bestTimestamp)
                {
                    bestTimestamp = timestamp;
                }
            }

            if (bestTimestamp == 0)
            {
                LastHandshake = "never";
                return;
            }

            var ago = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - bestTimestamp;
            LastHandshake = ago switch
            {
                < 0 => "—",
                < 60 => $"{ago}s ago",
                < 3600 => $"{ago / 60}m ago",
                _ => $"{ago / 3600}h ago",
            };
        }
        catch (OperationCanceledException)
        {
            // توقف عادی اتصال است.
        }
        catch
        {
            LastHandshake = "—";
        }
    }

    public Task<bool> ProbeAsync(string host, CancellationToken cancellationToken)
        => Task.FromResult(IsEnabled);

    public async Task<bool> ConnectAsync(string host, CancellationToken cancellationToken)
    {
        _authFailed = false;
        _localIp = null;
        LastHandshake = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        var mainExe = FindMainExe();
        if (mainExe == null)
        {
            Log?.Invoke($"[wg] {(_isAmnezia ? "amneziawg.exe" : "wireguard.exe")} not found");
            Disconnected?.Invoke();
            return false;
        }

        _tunnelName = ParseTunnelName(_confContent);
        Log?.Invoke($"[wg] tunnel: {_tunnelName} | amnezia: {_isAmnezia}");

        var dataDir = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            _isAmnezia ? "amneziawg" : "wireguard");
        Directory.CreateDirectory(dataDir);

        _confPath = Path.Combine(dataDir, $"{_tunnelName}.conf");
        await File.WriteAllTextAsync(_confPath, _confContent, ct).ConfigureAwait(false);
        Log?.Invoke($"[wg] conf written: {_confPath}");

        if (await IsServiceInstalledAsync(ct).ConfigureAwait(false))
        {
            Log?.Invoke("[wg] existing tunnel — uninstalling first");
            await RunExeAsync(
                mainExe,
                $"/uninstalltunnelservice \"{_tunnelName}\"",
                ct).ConfigureAwait(false);

            // فرصت کوتاه برای آزادشدن سرویس و آداپتور Wintun.
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }

        Log?.Invoke("[wg] installing tunnel service");
        var installed = await RunExeAsync(
            mainExe,
            $"/installtunnelservice \"{_confPath}\"",
            ct).ConfigureAwait(false);

        if (!installed)
        {
            Log?.Invoke("[wg] tunnel service installation failed");
            Disconnected?.Invoke();
            return false;
        }

        var started = false;
        for (var i = 0; i < 24; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsServiceRunningAsync(ct).ConfigureAwait(false))
            {
                started = true;
                break;
            }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        if (!started)
        {
            Log?.Invoke("[wg] service did not start in time");
            await RunExeAsync(
                mainExe,
                $"/uninstalltunnelservice \"{_tunnelName}\"",
                CancellationToken.None).ConfigureAwait(false);
            Disconnected?.Invoke();
            return false;
        }

        _localIp = ParseAddress(_confContent);
        Log?.Invoke("[wg] tunnel service started; waiting for handshake");
        Connected?.Invoke(_localIp, null);
        _ = Task.Run(() => MonitorAsync(ct), ct);
        return true;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) break;

                await UpdateHandshakeAsync(cancellationToken).ConfigureAwait(false);
                if (!await IsServiceRunningAsync(cancellationToken).ConfigureAwait(false))
                {
                    Log?.Invoke("[wg] service stopped unexpectedly");
                    Disconnected?.Invoke();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // توقف عادی اتصال است.
        }
        catch (Exception ex)
        {
            Log?.Invoke("[wg] monitor error: " + ex.Message);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _authFailed = false;
        _localIp = null;
        LastHandshake = null;

        var mainExe = FindMainExe();
        if (mainExe == null) return;

        Log?.Invoke("[wg] uninstalling tunnel service");
        await RunExeAsync(
            mainExe,
            $"/uninstalltunnelservice \"{_tunnelName}\"",
            CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (_confPath != null && File.Exists(_confPath))
                File.Delete(_confPath);
        }
        catch { }
    }

    public async Task<bool> IsAliveAsync(CancellationToken cancellationToken)
        => await IsServiceRunningAsync(cancellationToken).ConfigureAwait(false);
}
