using System.IO;
using SmartVpn; // WireGuardProvider

public sealed class VpnEngine
{
    public event Action<string>? Log;
    public event Action<ConnectionProfile, string?>? Connected;
    public event Action? Reconnecting;
    public event Action? Stopped;
    public event Action? AuthFailed;

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private IConnectionProvider? _active;
    private IConnectionProvider? _currentConnecting;
    private volatile bool _forceReconnect;

    public bool IsRunning => _runTask is { IsCompleted: false };

    // درخواست ریکانکت اجباری از بیرون (مثلا نگهبان پینگ) — در چک بعدی حلقه مانیتور (حداکثر ۵ ثانیه) اعمال می‌شود
    public void RequestReconnect()
    {
        if (IsRunning && _active is not null) _forceReconnect = true;
    }

    public void Start(List<ConnectionProfile> profiles)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _runTask = Task.Run(async () =>
        {
            try
            {
                await RunAsync(profiles, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log?.Invoke($"Unexpected error: {ex.Message}");
            }
            finally
            {
                var toClean = _active ?? _currentConnecting;
                if (toClean is not null)
                {
                    try { await toClean.DisconnectAsync(CancellationToken.None); } catch { }
                    _active = null;
                    _currentConnecting = null;
                }
                Stopped?.Invoke();
            }
        });
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_currentConnecting is not null)
        {
            try { await _currentConnecting.DisconnectAsync(CancellationToken.None); } catch { }
            _currentConnecting = null;
        }
        if (_active is not null)
        {
            try { await _active.DisconnectAsync(CancellationToken.None); } catch { }
            _active = null;
        }
        if (_runTask is not null)
        {
            try { await _runTask; } catch { }
        }
        _cts?.Dispose();
        _cts = null;
        _runTask = null;
    }

    // Step 11.6: a custom profile with an embedded personal .ovpn uses its own
    // inline content as the base profile (rewritten to disk before each start).
    private static string ResolveBaseProfile(ConnectionProfile p)
    {
        if (!p.HasInlineOvpn) return "base.ovpn";

        var safe = new string(p.Name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        if (safe.Length == 0) safe = "profile";
        var fileName = $"inline-{safe}.ovpn";

        var content = p.OvpnInline;
        if (!string.IsNullOrWhiteSpace(p.ServerOverride))
        {
            // Server Override: آدرس host در همه خطوط remote با آدرس override جایگزین می‌شود
            var ov = p.ServerOverride.Trim();
            var lines = content.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (!t.StartsWith("remote ")) continue;
                var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) parts[1] = ov;
                lines[i] = string.Join(" ", parts);
            }
            content = string.Join(Environment.NewLine, lines);
        }

        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Data", fileName), content);
        return fileName;
    }

    private static string GetConnectionHost(ConnectionProfile p)
    {
        // برای اتصال واقعی، نام اصلی دامنه/لیست IP را نگه می‌داریم.
        // EffectiveServer در بعضی نسخه‌ها بعد از resolve فقط اولین IP را برمی‌گرداند؛
        // همین باعث می‌شد failover داخلی OpenVPN از بین برود.
        if (!string.IsNullOrWhiteSpace(p.ServerOverride))
            return p.ServerOverride.Trim();
        if (!string.IsNullOrWhiteSpace(p.Server))
            return p.Server.Trim();
        return p.EffectiveServer;
    }

    private static IConnectionProvider CreateProvider(ConnectionProfile p)
    {
        var creds = new CredentialsConfig
        {
            Username = p.Username,
            Password = p.Password,
            L2tpPsk = p.Psk,
        };

        if (p.Type == "openvpn")
        {
            var ep = new OpenVpnEndpoint
            {
                Address = GetConnectionHost(p), // دامنه/همه endpointها حفظ می‌شوند
                Port = p.Port ?? 1194,
                Proto = p.Proto,
            };
            return new OpenVpnProvider(ep, ResolveBaseProfile(p), creds);
        }

        // WireGuard و AmneziaWG — محتوای .conf در پروفایل ذخیره شده
                                if (p.Type == "wireguard" || p.Type == "amneziawg")
            return new WireGuardProvider(p.WireGuardConf);

        var cfg = new ProtocolConfig
        {
            Type = p.Type,
            Kind = "vpn",
            Enabled = true,
            Port = p.Port is > 0 ? p.Port : null,
        };
        return new RasProvider(cfg, creds);
    }

    private async Task RunAsync(List<ConnectionProfile> profiles, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            ConnectionProfile? activeProfile = null;
            var anyAuthFailed = false;

            foreach (var p in profiles)
            {
                ct.ThrowIfCancellationRequested();
                var provider = CreateProvider(p);
                var connectionHost = GetConnectionHost(p);

                Log?.Invoke($"[{provider.Type}] probing {connectionHost}...");
                if (!await provider.ProbeAsync(connectionHost, ct))
                {
                    Log?.Invoke($"[{provider.Type}] not reachable - next");
                    continue;
                }

                Log?.Invoke($"[{provider.Type}] connecting...");
                _currentConnecting = provider;
                bool success = false;
                try
                {
                    success = await provider.ConnectAsync(connectionHost, ct);
                }
                finally
                {
                    _currentConnecting = null;
                }

                if (success)
                {
                    _active = provider;
                    activeProfile = p;
                    break;
                }

                if (provider.AuthFailed)
                {
                    anyAuthFailed = true;
                    Log?.Invoke($"[{provider.Type}] auth rejected - next");
                    continue;
                }

                Log?.Invoke($"[{provider.Type}] failed - next");
            }

            if (_active is null)
            {
                if (anyAuthFailed)
                {
                    Log?.Invoke("no connection succeeded and at least one auth error - stopping");
                    AuthFailed?.Invoke();
                    return;
                }
                Log?.Invoke("no connection succeeded - retrying in 5s...");
                await Task.Delay(5000, ct);
                continue;
            }

            _forceReconnect = false; // درخواست‌های مانده از اتصال قبلی بی‌اثر شوند
            Log?.Invoke($"connected via {_active.Type} - monitoring every 5s");
            Connected?.Invoke(activeProfile!, _active.LocalIp);

            while (true)
            {
                await Task.Delay(3000, ct);

                if (_forceReconnect || !await _active.IsAliveAsync(ct))
                {
                    _forceReconnect = false;
                    Log?.Invoke($"connection {_active.Type} dropped! auto reconnect...");
                    Reconnecting?.Invoke();
                    try { await _active.DisconnectAsync(CancellationToken.None); } catch { }
                    _active = null;
                    break;
                }
            }
        }
    }
}











