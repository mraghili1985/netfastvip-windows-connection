using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SmartVpn
{
    /// <summary>
    /// Applies custom DNS servers to the VPN tunnel interface and restores
    /// automatic DNS on disconnect. The interface is located by the tunnel's
    /// local IP, so this works for both RAS (l2tp/sstp/pptp/ikev2) and
    /// OpenVPN (TAP/Wintun) without knowing adapter names in advance.
    /// </summary>
    public static class DnsManager
    {
        public static event Action<string>? Log;

        // interfaces we changed in this session (restored on RevertAsync)
        private static readonly List<string> _touched = new List<string>();

        public static async Task ApplyToTunnelAsync(string? tunnelLocalIp, string primary, string secondary)
        {
            if (string.IsNullOrWhiteSpace(primary)) return;

            string? alias = null;

            // 1) find the interface that owns the tunnel local IP
            if (!string.IsNullOrWhiteSpace(tunnelLocalIp))
            {
                alias = await RunPsAsync(
                    $"(Get-NetIPAddress -IPAddress '{tunnelLocalIp}' -ErrorAction SilentlyContinue | Select-Object -First 1).InterfaceAlias");
            }

            // 2) fallback: newest tunnel-like adapter that is up
            if (string.IsNullOrWhiteSpace(alias))
            {
                alias = await RunPsAsync(
                    "(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and ($_.InterfaceDescription -match 'WAN Miniport|TAP|Wintun|OpenVPN') } | Sort-Object ifIndex -Descending | Select-Object -First 1).Name");
            }

            if (string.IsNullOrWhiteSpace(alias))
            {
                Log?.Invoke("[dns] tunnel interface not found — skipped");
                return;
            }

            alias = alias.Trim();
            var servers = string.IsNullOrWhiteSpace(secondary)
                ? $"'{primary}'"
                : $"'{primary}','{secondary}'";

            await RunPsAsync($"Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ServerAddresses {servers}");
            if (!_touched.Contains(alias)) _touched.Add(alias);
            Log?.Invoke($"[dns] {primary}{(string.IsNullOrWhiteSpace(secondary) ? "" : ", " + secondary)} applied on '{alias}'");

            // flush resolver cache so the new DNS takes effect immediately
            await RunPsAsync("Clear-DnsClientCache");
        }

        public static async Task RevertAsync()
        {
            foreach (var alias in _touched.ToList())
            {
                try
                {
                    await RunPsAsync($"Set-DnsClientServerAddress -InterfaceAlias '{alias}' -ResetServerAddresses");
                    Log?.Invoke($"[dns] restored automatic DNS on '{alias}'");
                }
                catch
                {
                    // interface may already be gone after disconnect — fine
                }
            }
            _touched.Clear();
        }

        private static async Task<string> RunPsAsync(string script)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi);
            if (p is null) return "";
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output.Trim();
        }
    }
}
