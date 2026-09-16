using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

// Route-based split tunneling (global, applies to all connections).
// Modes:
//   deny  -> everything via VPN except listed destinations (they go direct)
//   allow -> only listed destinations via VPN, everything else goes direct
// List entries (one per line): single IP, CIDR range, or domain name.
// Lines starting with # are comments.
public static class SplitTunnel
{
    private sealed record RouteEntry(string Ip, string Mask, string Gateway);

    private static string _localGateway = "";
    private static readonly List<RouteEntry> _added = [];

    public static event Action<string>? Log;

    // Call BEFORE connecting, while the default route is still the physical one.
    public static void CaptureLocalGateway()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Ppp
                    or NetworkInterfaceType.Tunnel) continue;

                var d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                if (d.Contains("tap") || d.Contains("tun") || d.Contains("wintun")
                    || d.Contains("wan miniport") || d.Contains("vpn")
                    || d.Contains("virtual")) continue;

                foreach (var gw in ni.GetIPProperties().GatewayAddresses)
                {
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        _localGateway = gw.Address.ToString();
                        Log?.Invoke($"split-tunnel: local gateway = {_localGateway}");
                        return;
                    }
                }
            }
            Log?.Invoke("split-tunnel: no local gateway found");
        }
        catch (Exception ex)
        {
            Log?.Invoke("split-tunnel: gateway detect failed: " + ex.Message);
        }
    }

    // Call AFTER a successful connect (tunnelIp = private IP shown in the UI).
    public static async Task ApplyAsync(string mode, IEnumerable<string> entries, string? tunnelIp)
    {
        if (mode is not ("deny" or "allow")) return;

        // Re-applying on reconnect: remove our previous routes first.
        await ClearAsync();

        var targets = await ResolveAsync(entries);
        if (targets.Count == 0)
        {
            Log?.Invoke("split-tunnel: list is empty or nothing resolved - skipped");
            return;
        }

        if (mode == "deny")
        {
            if (_localGateway.Length == 0)
            {
                Log?.Invoke("split-tunnel: no local gateway - deny mode skipped");
                return;
            }
            foreach (var (ip, mask) in targets)
                await AddRouteAsync(ip, mask, _localGateway);
            Log?.Invoke($"split-tunnel: DENY active - {targets.Count} destination(s) bypass the VPN");
            return;
        }

        // allow mode
        if (string.IsNullOrWhiteSpace(tunnelIp))
        {
            Log?.Invoke("split-tunnel: no tunnel ip - allow mode skipped");
            return;
        }

        // Remove the tunnel's default routes so ordinary traffic goes direct:
        // - OpenVPN redirect-gateway def1 adds this /1 pair
        // - RAS adds a 0.0.0.0/0 via the tunnel ip
        await RunRouteAsync("delete 0.0.0.0 mask 128.0.0.0");
        await RunRouteAsync("delete 128.0.0.0 mask 128.0.0.0");
        await RunRouteAsync($"delete 0.0.0.0 mask 0.0.0.0 {tunnelIp}");

        foreach (var (ip, mask) in targets)
            await AddRouteAsync(ip, mask, tunnelIp);
        Log?.Invoke($"split-tunnel: ALLOW active - only {targets.Count} destination(s) go via VPN");
    }

    // Call on disconnect/stop.
    public static async Task ClearAsync()
    {
        if (_added.Count == 0) return;
        foreach (var r in _added)
            await RunRouteAsync($"delete {r.Ip} mask {r.Mask} {r.Gateway}");
        _added.Clear();
        Log?.Invoke("split-tunnel: routes cleared");
    }

    private static async Task<List<(string Ip, string Mask)>> ResolveAsync(IEnumerable<string> entries)
    {
        var result = new List<(string, string)>();
        foreach (var raw in entries)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            try
            {
                if (line.Contains('/'))
                {
                    var parts = line.Split('/');
                    if (parts.Length == 2
                        && IPAddress.TryParse(parts[0], out var net)
                        && net.AddressFamily == AddressFamily.InterNetwork
                        && int.TryParse(parts[1], out var prefix)
                        && prefix is >= 1 and <= 32)
                    {
                        result.Add((net.ToString(), PrefixToMask(prefix)));
                    }
                    else
                    {
                        Log?.Invoke($"split-tunnel: invalid CIDR skipped: {line}");
                    }
                }
                else if (IPAddress.TryParse(line, out var ip))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        result.Add((ip.ToString(), "255.255.255.255"));
                }
                else
                {
                    // Domain: resolve now; CDN-backed domains may rotate IPs (accepted limitation)
                    var addrs = await Dns.GetHostAddressesAsync(line);
                    var v4 = addrs.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Take(8).ToList();
                    if (v4.Count == 0) Log?.Invoke($"split-tunnel: no IPv4 for domain: {line}");
                    foreach (var a in v4)
                        result.Add((a.ToString(), "255.255.255.255"));
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"split-tunnel: entry skipped '{line}' ({ex.Message})");
            }
        }
        return result;
    }

    private static string PrefixToMask(int prefix)
    {
        var m = uint.MaxValue << (32 - prefix);
        return $"{(m >> 24) & 255}.{(m >> 16) & 255}.{(m >> 8) & 255}.{m & 255}";
    }

    private static async Task AddRouteAsync(string ip, string mask, string gateway)
    {
        var code = await RunRouteAsync($"add {ip} mask {mask} {gateway} metric 1");
        if (code == 0) _added.Add(new RouteEntry(ip, mask, gateway));
    }

    private static async Task<int> RunRouteAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("route", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var text = (stdout + stderr).Trim();
            if (text.Length > 0 && !text.Contains("OK!"))
                Log?.Invoke($"route {args} -> {text}");
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"route {args} failed: {ex.Message}");
            return -1;
        }
    }
}