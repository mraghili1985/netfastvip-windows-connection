using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVpn;

/// <summary>
/// سرویس هات‌اسپات — VPN connection رو از طریق WiFi Hosted Network شیر می‌کنه.
///
/// جریان کار:
///   1. netsh wlan set hostednetwork  — آداپتور مجازی WiFi رو پیکربندی می‌کنه
///   2. netsh wlan start hostednetwork — آداپتور رو راه‌اندازی می‌کنه
///   3. ICS (Internet Connection Sharing) — ترافیک VPN رو به هات‌اسپات هدایت می‌کنه
///   4. netsh wlan stop hostednetwork  — هات‌اسپات رو خاموش می‌کنه
///
/// نیازمندی: کارت WiFi که Hosted Network رو پشتیبانی کنه
///            اجرای برنامه با دسترسی Administrator
/// </summary>
public sealed class HotspotService
{
    public static event Action<string>? Log;

    public bool IsRunning { get; private set; }

    // ===== Start =====

    public async Task<(bool Success, string Error)> StartAsync(
        string ssid,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ssid))
            return (false, "نام شبکه نمی‌تواند خالی باشد.");
        if (password.Length < 8)
            return (false, "رمز عبور باید حداقل ۸ کاراکتر باشد.");

        Log?.Invoke("[Hotspot] configuring hosted network...");

        // ۱. پیکربندی آداپتور
        var set = await RunAsync(
            "netsh",
            $"wlan set hostednetwork mode=allow ssid=\"{ssid}\" key=\"{password}\"",
            ct);
        if (set.ExitCode != 0)
            return (false, $"پیکربندی آداپتور: {Squash(set.Output)}");

        // ۲. راه‌اندازی
        Log?.Invoke("[Hotspot] starting hosted network...");
        var start = await RunAsync("netsh", "wlan start hostednetwork", ct);
        if (start.ExitCode != 0)
            return (false, $"راه‌اندازی هات‌اسپات: {Squash(start.Output)}");

        // ۳. پیدا کردن آداپتور‌ها و فعال‌کردن ICS
        await Task.Delay(1500, ct); // صبر برای بالا اومدن آداپتور

        var vpnAdapter   = FindVpnAdapter();
        var hostedAdapter = FindHostedNetworkAdapter();

        if (vpnAdapter == null)
            Log?.Invoke("[Hotspot] warning: VPN adapter not found — make sure VPN is connected before sharing");

        if (hostedAdapter == null)
            return (false, "آداپتور Hosted Network پیدا نشد — کارت WiFi شما ممکن است Hosted Network رو پشتیبانی نکنه.");

        Log?.Invoke($"[Hotspot] adapters: vpn={vpnAdapter ?? "(none)"} hotspot={hostedAdapter}");

        var icsResult = await EnableIcsAsync(vpnAdapter, hostedAdapter, ct);
        if (!icsResult.Success)
            Log?.Invoke($"[Hotspot] ICS warning: {icsResult.Error} — hotspot is up but sharing may not work");
        else
            Log?.Invoke("[Hotspot] ICS enabled — VPN is being shared");

        IsRunning = true;
        return (true, string.Empty);
    }

    // ===== Stop =====

    public async Task StopAsync(CancellationToken ct = default)
    {
        Log?.Invoke("[Hotspot] stopping...");
        await DisableIcsAsync(ct);
        await RunAsync("netsh", "wlan stop hostednetwork", ct);
        IsRunning = false;
        Log?.Invoke("[Hotspot] stopped.");
    }

    // ===== ICS via PowerShell COM (HNetCfg.HNetShare) =====

    private static async Task<(bool Success, string Error)> EnableIcsAsync(
        string? publicAdapterName,
        string privateAdapterName,
        CancellationToken ct)
    {
        // public = VPN adapter (ترافیک از اینجا میاد)
        // private = Hosted Network adapter (دستگاه‌ها به اینجا وصل میشن)
        var publicPart = publicAdapterName != null
            ? $"if ($p.Name -eq '{Esc(publicAdapterName)}') {{ $cfg = $share.INetSharingConfigurationForINetConnection($c); $cfg.EnableSharing(0) }}"
            : "";

        var ps = $@"
$share = New-Object -ComObject HNetCfg.HNetShare
foreach ($c in @($share.EnumEveryConnection())) {{
    try {{
        $p = $share.NetConnectionProps($c)
        $cfg = $share.INetSharingConfigurationForINetConnection($c)
        $cfg.DisableSharing()
        {publicPart}
        if ($p.Name -eq '{Esc(privateAdapterName)}') {{ $cfg = $share.INetSharingConfigurationForINetConnection($c); $cfg.EnableSharing(1) }}
    }} catch {{}}
}}
";
        var res = await RunAsync("powershell", $"-NoProfile -Command \"{EscPs(ps)}\"", ct);
        return res.ExitCode == 0
            ? (true, string.Empty)
            : (false, Squash(res.Output));
    }

    private static async Task DisableIcsAsync(CancellationToken ct)
    {
        var ps = @"
$share = New-Object -ComObject HNetCfg.HNetShare
foreach ($c in @($share.EnumEveryConnection())) {
    try { $share.INetSharingConfigurationForINetConnection($c).DisableSharing() } catch {}
}
";
        await RunAsync("powershell", $"-NoProfile -Command \"{EscPs(ps)}\"", ct);
    }

    // ===== Adapter discovery =====

    /// <summary>آداپتور VPN فعال رو پیدا می‌کنه</summary>
    public static string? FindVpnAdapter()
    {
        // اسم‌های معمول آداپتورهای VPN
        var keywords = new[] { "SmartVpn", "TAP", "WireGuard", "Wintun", "OpenVPN", "PPTP", "L2TP", "SSTP", "Miniport" };
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && keywords.Any(k => n.Description.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.Name)
            .FirstOrDefault();
    }

    /// <summary>آداپتور Hosted Network (Virtual WiFi) رو پیدا می‌کنه</summary>
    public static string? FindHostedNetworkAdapter()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.Description.Contains("Microsoft Hosted", StringComparison.OrdinalIgnoreCase)
                     || n.Description.Contains("Virtual WiFi", StringComparison.OrdinalIgnoreCase)
                     || n.Description.Contains("Microsoft Wi-Fi Direct", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Name)
            .FirstOrDefault();
    }

    /// <summary>تعداد دستگاه‌های متصل به هات‌اسپات</summary>
    public static async Task<int> GetConnectedDevicesCountAsync(CancellationToken ct = default)
    {
        var res = await RunAsync("netsh", "wlan show hostednetwork", ct);
        var m = Regex.Match(res.Output, @"Number of clients\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }

    /// <summary>بررسی اینکه کارت WiFi Hosted Network رو پشتیبانی می‌کنه</summary>
    public static async Task<bool> IsSupportedAsync(CancellationToken ct = default)
    {
        var res = await RunAsync("netsh", "wlan show drivers", ct);
        return res.Output.Contains("Hosted network supported  : Yes", StringComparison.OrdinalIgnoreCase)
            || res.Output.Contains("Yes", StringComparison.OrdinalIgnoreCase)
               && res.Output.Contains("Hosted network", StringComparison.OrdinalIgnoreCase);
    }

    // ===== Helpers =====

    private static string Squash(string s) =>
        string.Join(" | ", s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    private static string Esc(string s) => s.Replace("'", "''");

    private static string EscPs(string s) => s.Replace('"', '`').Replace("\r", "").Replace("\n", " ");

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string file, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = file,
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        return (proc.ExitCode, (stdout + "\n" + stderr).Trim());
    }
}
