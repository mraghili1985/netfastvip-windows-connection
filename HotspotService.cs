using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVpn
{
    public sealed class HotspotClientInfo
    {
        public string Name { get; init; } = "Unknown device";
        public string IpAddress { get; init; } = "—";
        public string MacAddress { get; init; } = "—";
    }

    public class HotspotService
    {
        public event Action<string>? Log;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private string? _activeSrc;
        private string? _activeTgt;
        
        // این فلگ طلایی مسیردهی نیتیو است
        private bool _hotspotBoundToSelectedProfile;
        private string? _lastClientDiagnostic;

        private void L(string msg) => Log?.Invoke(msg);
        private static string PsQuote(string v) => v.Replace("'", "''");

        public async Task<List<string>> GetAllAdaptersAsync()
        {
            var lines = new[]
            {
                "$ErrorActionPreference = 'SilentlyContinue'",
                "$valid = @()",
                "try { $valid += @(Get-VpnConnection -ErrorAction SilentlyContinue | Where-Object ConnectionStatus -eq 'Connected' | ForEach-Object Name) } catch {}",
                "try { $valid += @(Get-VpnConnection -AllUserConnection -ErrorAction SilentlyContinue | Where-Object ConnectionStatus -eq 'Connected' | ForEach-Object Name) } catch {}",
                "try { $valid += @(Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up' | ForEach-Object Name) } catch {}",
                "$valid | Select-Object -Unique | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }"
            };

            var ps1 = Path.Combine(Path.GetTempPath(), "nfv_get_adapters.ps1");
            await File.WriteAllLinesAsync(ps1, lines, new UTF8Encoding(false));
            var (_, output) = await RunPs1Async(ps1, 15);
            try { File.Delete(ps1); } catch { }

            var list = new List<string>();
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !list.Contains(trimmed))
                    list.Add(trimmed);
            }
            return list;
        }

        public async Task<(bool ok, string ssid, string pass, string err)> StartHotspotOnlyAsync(string sourceName)
        {
            await _lock.WaitAsync();
            try
            {
                L($"[Hotspot] تلاش برای روشن کردن هات‌اسپات با بایند مستقیم به پروفایل ({sourceName})...");

                var lines = new[]
                {
                    "$ErrorActionPreference = 'Stop'",
                    "$preferred = '" + PsQuote(sourceName) + "'",
                    "try {",
                    "    $netInfo = [Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]",
                    "    $tethType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]",
                    "    $profiles = @($netInfo::GetConnectionProfiles())",
                    "    $adapter = Get-NetAdapter -Name $preferred -IncludeHidden -ErrorAction SilentlyContinue | Select-Object -First 1",
                    "    $wantedGuid = $null",
                    "    if ($null -ne $adapter) { $wantedGuid = [Guid]$adapter.InterfaceGuid }",
                    "",
                    "    # تطبیق هوشمند پروفایل متصل با کارت VPN",
                    "    $profile = $profiles | Where-Object { $_.ProfileName -eq $preferred } | Select-Object -First 1",
                    "    if ($null -eq $profile -and $null -ne $wantedGuid) {",
                    "        $profile = $profiles | Where-Object { $_.NetworkAdapter -and $_.NetworkAdapter.NetworkAdapterId -eq $wantedGuid } | Select-Object -First 1",
                    "    }",
                    "    $boundToProfile = $true",
                    "    if ($null -eq $profile) {",
                    "        $boundToProfile = $false",
                    "        $profile = $netInfo::GetInternetConnectionProfile()",
                    "        if ($null -eq $profile) {",
                    "            $profile = $profiles | Where-Object { $_.GetNetworkConnectivityLevel() -ge 1 } | Select-Object -First 1",
                    "        }",
                    "    }",
                    "    if ($null -eq $profile) { Write-Output 'ERROR|NO_INTERNET_PROFILE'; exit 1 }",
                    "",
                    "    # پاکسازی اختلالات احتمالی قبلی برای روانی ترافیک",
                    "    try {",
                    "        $shareMgr = New-Object -ComObject HNetCfg.HNetShare",
                    "        foreach ($c in @($shareMgr.EnumEveryConnection())) {",
                    "            try {",
                    "                $props = $shareMgr.NetConnectionProps($c)",
                    "                $cfg = $shareMgr.INetSharingConfigurationForINetConnection($c)",
                    "                if ($cfg.SharingEnabled -and ($props.Name -eq $preferred -or $props.Name -like 'Local Area Connection* *')) {",
                    "                    $cfg.DisableSharing()",
                    "                }",
                    "            } catch {}",
                    "        }",
                    "    } catch {}",
                    "    Start-Sleep -Milliseconds 700",
                    "",
                    "    $tethMgr = $tethType::CreateFromConnectionProfile($profile)",
                    "    if ($tethMgr.TetheringOperationalState -ne 1) {",
                    "        $op = $tethMgr.StartTetheringAsync()",
                    "        $to = 150",
                    "        while ($op.Status -eq 0 -and $to -gt 0) { Start-Sleep -Milliseconds 100; $to-- }",
                    "    }",
                    "",
                    "    $to2 = 120",
                    "    while ($tethMgr.TetheringOperationalState -ne 1 -and $to2 -gt 0) { Start-Sleep -Milliseconds 100; $to2-- }",
                    "",
                    "    if ($tethMgr.TetheringOperationalState -eq 1) {",
                    "        $conf = $tethMgr.GetCurrentAccessPointConfiguration()",
                    "        Write-Output ('SUCCESS|' + $conf.Ssid + '|' + $conf.Passphrase + '|' + $boundToProfile)",
                    "    } else {",
                    "        Write-Output 'ERROR|HOTSPOT_TIMEOUT'",
                    "        exit 1",
                    "    }",
                    "} catch {",
                    "    Write-Output ('ERROR|' + $_.Exception.Message)",
                    "    exit 1",
                    "}"
                };

                var ps1 = Path.Combine(Path.GetTempPath(), "nfv_start_hotspot.ps1");
                await File.WriteAllLinesAsync(ps1, lines, new UTF8Encoding(false));
                var (_, output) = await RunPs1Async(ps1, 40);
                try { File.Delete(ps1); } catch { }

                string last = "";
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("SUCCESS|") || line.StartsWith("ERROR|")) last = line.Trim();
                }

                if (last.StartsWith("SUCCESS|"))
                {
                    var p = last.Split('|');
                    // اگر در خروجی True بود یعنی بایندینگ نیتیو به کارت VPN موفقیت‌آمیز بوده است
                    _hotspotBoundToSelectedProfile = (p.Length >= 4 && p[3].Equals("True", StringComparison.OrdinalIgnoreCase));
                    _activeSrc = sourceName;
                    
                    if (_hotspotBoundToSelectedProfile)
                        L("[Hotspot] هات‌اسپات مستقیماً از طریق پروفایل VPN راه‌اندازی شد. (مسیردهی نیتیو)");
                    else
                        L("[Hotspot] بایندینگ مستقیم شکست خورد، هات‌اسپات نیازمند مسیردهی ICS است.");

                    return (true, p[1], p[2], "");
                }

                _hotspotBoundToSelectedProfile = false;
                return (false, "", "", last.StartsWith("ERROR|") ? last.Substring(6) : output);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<(bool ok, string err)> ApplySharingOnlyAsync(string sourceName, string targetName)
        {
            // کلید طلایی! اگر از قبل بایند شده، شیرینگ دستی را متوقف کن تا ترافیک قطع (Blackhole) نشود.
            if (_hotspotBoundToSelectedProfile && string.Equals(_activeSrc, sourceName, StringComparison.OrdinalIgnoreCase))
            {
                _activeTgt = targetName;
                L($"[Hotspot] نیازی به اعمال زورکی COM ICS نیست؛ ترافیک به طور نیتیو از {sourceName} در جریان است.");
                return (true, "");
            }

            await _lock.WaitAsync();
            try
            {
                L($"[Hotspot] برقراری پل ارتباطی ICS ({sourceName} ➔ {targetName})...");

                var lines = new List<string>
                {
                    "$ErrorActionPreference = 'Stop'",
                    "$src = '" + PsQuote(sourceName) + "'",
                    "$tgt = '" + PsQuote(targetName) + "'",
                    "function New-ShareManager { New-Object -ComObject HNetCfg.HNetShare }",
                    "function Find-Connection($mgr, [string]$name) {",
                    "    foreach ($c in @($mgr.EnumEveryConnection())) {",
                    "        try { if ($mgr.NetConnectionProps($c).Name -eq $name) { return $c } } catch {}",
                    "    }",
                    "    return $null",
                    "}",
                    "try {",
                    "    Start-Service -Name SharedAccess -ErrorAction Stop",
                    "    $lastErr = ''",
                    "    $success = $false",
                    "    for ($retry = 1; $retry -le 2 -and -not $success; $retry++) {",
                    "        try {",
                    "            $mgr = New-ShareManager",
                    "            $sourceConn = Find-Connection $mgr $src",
                    "            $targetConn = Find-Connection $mgr $tgt",
                    "            if ($null -eq $sourceConn) { throw 'ERR_SRC_MISSING' }",
                    "            if ($null -eq $targetConn) { throw 'ERR_TGT_MISSING' }",
                    "",
                    "            $releasedOldPublic = $false",
                    "            foreach ($c in @($mgr.EnumEveryConnection())) {",
                    "                try {",
                    "                    $p = $mgr.NetConnectionProps($c)",
                    "                    $cfg = $mgr.INetSharingConfigurationForINetConnection($c)",
                    "                    if ($cfg.SharingEnabled -and $cfg.SharingConnectionType -eq 0 -and $p.Name -ne $src) {",
                    "                        $cfg.DisableSharing()",
                    "                        $releasedOldPublic = $true",
                    "                    }",
                    "                } catch {}",
                    "            }",
                    "            if ($releasedOldPublic) { Start-Sleep -Milliseconds 800 }",
                    "",
                    "            $mgr = New-ShareManager",
                    "            $targetConn = Find-Connection $mgr $tgt",
                    "            $tgtConfig = $mgr.INetSharingConfigurationForINetConnection($targetConn)",
                    "            if ($tgtConfig.SharingEnabled -and $tgtConfig.SharingConnectionType -ne 1) {",
                    "                $tgtConfig.DisableSharing()",
                    "                Start-Sleep -Milliseconds 700",
                    "                $mgr = New-ShareManager",
                    "                $targetConn = Find-Connection $mgr $tgt",
                    "                $tgtConfig = $mgr.INetSharingConfigurationForINetConnection($targetConn)",
                    "            }",
                    "            if (-not $tgtConfig.SharingEnabled) {",
                    "                try { $tgtConfig.EnableSharing(1) }",
                    "                catch { if ($_.Exception.HResult.ToString('X8') -ne '80040201') { throw } }",
                    "            }",
                    "            Start-Sleep -Milliseconds 1200",
                    "",
                    "            $mgr = New-ShareManager",
                    "            $sourceConn = Find-Connection $mgr $src",
                    "            $srcConfig = $mgr.INetSharingConfigurationForINetConnection($sourceConn)",
                    "            if ($srcConfig.SharingEnabled -and $srcConfig.SharingConnectionType -ne 0) {",
                    "                $srcConfig.DisableSharing()",
                    "                Start-Sleep -Milliseconds 700",
                    "                $mgr = New-ShareManager",
                    "                $sourceConn = Find-Connection $mgr $src",
                    "                $srcConfig = $mgr.INetSharingConfigurationForINetConnection($sourceConn)",
                    "            }",
                    "            if (-not $srcConfig.SharingEnabled) {",
                    "                try { $srcConfig.EnableSharing(0) }",
                    "                catch { if ($_.Exception.HResult.ToString('X8') -ne '80040201') { throw } }",
                    "            }",
                    "",
                    "            $verified = $false",
                    "            for ($check = 1; $check -le 8 -and -not $verified; $check++) {",
                    "                Start-Sleep -Milliseconds 500",
                    "                $mgr = New-ShareManager",
                    "                $sourceConn = Find-Connection $mgr $src",
                    "                $targetConn = Find-Connection $mgr $tgt",
                    "                if ($null -eq $sourceConn -or $null -eq $targetConn) { continue }",
                    "                $srcConfig = $mgr.INetSharingConfigurationForINetConnection($sourceConn)",
                    "                $tgtConfig = $mgr.INetSharingConfigurationForINetConnection($targetConn)",
                    "                $srcOk = $srcConfig.SharingEnabled -and $srcConfig.SharingConnectionType -eq 0",
                    "                $tgtOk = $tgtConfig.SharingEnabled -and $tgtConfig.SharingConnectionType -eq 1",
                    "                $verified = $srcOk -and $tgtOk",
                    "            }",
                    "            if (-not $verified) { throw 'ERR_VERIFY_FAILED' }",
                    "            $success = $true",
                    "        } catch {",
                    "            $lastErr = $_.Exception.Message",
                    "            Start-Sleep -Milliseconds 800",
                    "        }",
                    "    }",
                    "    if (-not $success) { throw ('ERR_SHARING_FAILED|' + $lastErr) }",
                    "    Write-Output 'SUCCESS'",
                    "} catch {",
                    "    Write-Output ('ERROR|' + $_.Exception.Message)",
                    "    exit 1",
                    "}"
                };

                var ps1 = Path.Combine(Path.GetTempPath(), "nfv_ics_explicit.ps1");
                await File.WriteAllLinesAsync(ps1, lines, new UTF8Encoding(false));
                var (_, output) = await RunPs1Async(ps1, 30);
                try { File.Delete(ps1); } catch { }

                string lastLine = "";
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (trimmed == "SUCCESS" || trimmed.StartsWith("ERROR|", StringComparison.Ordinal))
                        lastLine = trimmed;
                }

                if (lastLine == "SUCCESS")
                {
                    _activeSrc = sourceName;
                    _activeTgt = targetName;
                    return (true, "");
                }

                if (lastLine.StartsWith("ERROR|", StringComparison.Ordinal))
                {
                    string err = lastLine.Substring(6);
                    if (err.StartsWith("ERR_SRC_MISSING", StringComparison.Ordinal))
                        return (false, $"آداپتور VPN در سیستم شیرینگ ویندوز دیده نشد.");
                    if (err.StartsWith("ERR_TGT_MISSING", StringComparison.Ordinal))
                        return (false, $"آداپتور Wi-Fi Direct در سیستم شیرینگ ویندوز دیده نشد.");
                    if (err.StartsWith("ERR_SHARING_FAILED|", StringComparison.Ordinal))
                        return (false, "هسته ICS ویندوز اجازه اعمال شیرینگ را نداد:\n" + err.Substring("ERR_SHARING_FAILED|".Length));
                    return (false, "خطای ICS:\n" + err);
                }

                return (false, $"خروجی نامعتبر اسکریپت:\n{output}");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<List<HotspotClientInfo>> GetConnectedClientsAsync()
        {
            var clients = new List<HotspotClientInfo>();
            var targetName = _activeTgt;
            if (string.IsNullOrWhiteSpace(targetName)) return clients;

            // Windows Mobile Hotspot در این سیستم روی Local Area Connection* 10
            // و InterfaceIndex=4 قرار دارد. خواندن Neighbor با نام Alias قابل اعتماد
            // نیست، بنابراین ابتدا InterfaceIndex را پیدا می‌کنیم.
            var lines = new[]
            {
                "$ErrorActionPreference = 'SilentlyContinue'",
                "$ifName = '" + PsQuote(targetName) + "'",
                "$adapter = Get-NetAdapter -Name $ifName -IncludeHidden -ErrorAction SilentlyContinue | Select-Object -First 1",
                "$ifIndex = if ($adapter) { [int]$adapter.ifIndex } else { -1 }",
                "Write-Output ('DIAG|target=' + $ifName + '|ifIndex=' + [string]$ifIndex)",
                "if ($ifIndex -gt 0) {",
                "    $neighbors = @(Get-NetNeighbor -InterfaceIndex $ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {",
                "        $_.IPAddress -and $_.LinkLayerAddress -and",
                "        $_.LinkLayerAddress -notmatch '^00-00-00-00-00-00$' -and",
                "        $_.LinkLayerAddress -notmatch '^FF-FF-FF-FF-FF-FF$' -and",
                "        $_.LinkLayerAddress -notmatch '^01-00-5E' -and",
                "        $_.IPAddress -notmatch '^(224\\.|239\\.|255\\.)' -and",
                "        $_.IPAddress -notmatch '\\.255$' -and",
                "        $_.State -notmatch 'Incomplete|Unreachable'",
                "    })",
                "    Write-Output ('DIAG|neighborCount=' + [string]$neighbors.Count)",
                "    foreach ($n in $neighbors) {",
                "        $name = [string]$n.IPAddress",
                "        try {",
                "            $ptr = Resolve-DnsName -Name $n.IPAddress -Type PTR -ErrorAction SilentlyContinue | Select-Object -First 1",
                "            if ($ptr -and $ptr.NameHost) { $name = [string]$ptr.NameHost.TrimEnd('.') }",
                "        } catch {}",
                "        # اگر Reverse DNS نام نداد، NetBIOS نام کامپیوترهای ویندوزی را امتحان می‌کند.",
                "        if ($name -eq [string]$n.IPAddress) {",
                "            try {",
                "                $nbtLine = @(nbtstat -A $n.IPAddress 2>$null) | Where-Object { $_ -match '<00>' -and $_ -match 'UNIQUE' } | Select-Object -First 1",
                "                if ($nbtLine) {",
                "                    $nbtParts = ([string]$nbtLine).Trim() -split ' +';",
                "                    if ($nbtParts.Count -gt 0) { $name = $nbtParts[0] }",
                "                }",
                "            } catch {}",
                "        }",
                "        Write-Output ('CLIENT|' + [string]$n.IPAddress + '|' + [string]$n.LinkLayerAddress + '|' + $name)",
                "    }",
                "} else {",
                "    Write-Output 'DIAG|targetAdapter=NOT_FOUND'",
                "}"
            };

            var ps1 = Path.Combine(Path.GetTempPath(), "nfv_hotspot_clients.ps1");
            await File.WriteAllLinesAsync(ps1, lines, new UTF8Encoding(false));
            var (_, output) = await RunPs1Async(ps1, 10);
            try { File.Delete(ps1); } catch { }

            var rawClientLines = new List<string>();
            var diagnosticLines = new List<string>();
            foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.StartsWith("DIAG|", StringComparison.Ordinal))
                {
                    diagnosticLines.Add(line);
                    continue;
                }
                if (!line.StartsWith("CLIENT|", StringComparison.Ordinal)) continue;
                rawClientLines.Add(line);

                var parts = line.Split('|', 4);
                if (parts.Length < 4) continue;

                var ip = parts[1].Trim();
                var mac = parts[2].Trim();
                var name = string.IsNullOrWhiteSpace(parts[3]) ? ip : parts[3].Trim();

                clients.Add(new HotspotClientInfo
                {
                    Name = name,
                    IpAddress = ip,
                    MacAddress = mac
                });
            }

            var snapshot = string.Join(" || ", clients.ConvertAll(c =>
                $"{c.Name}|{c.IpAddress}|{c.MacAddress}"));
            var diagnostic = $"target={targetName}; raw={rawClientLines.Count}; parsed={clients.Count}; {snapshot}";
            if (!string.Equals(_lastClientDiagnostic, diagnostic, StringComparison.Ordinal))
            {
                _lastClientDiagnostic = diagnostic;
                L("[Hotspot] client scan: " + diagnostic);
                if (diagnosticLines.Count > 0)
                    L("[Hotspot] client diag: " + string.Join(" || ", diagnosticLines));
                if (rawClientLines.Count > 0)
                    L("[Hotspot] client raw: " + string.Join(" || ", rawClientLines));
            }

            return clients;
        }

        public async Task StopAsync()
        {
            await _lock.WaitAsync();
            try
            {
                L("[Hotspot] متوقف‌سازی کامل سرویس‌ها و شیرینگ...");
                var lines = new[]
                {
                    "$ErrorActionPreference = 'SilentlyContinue'",
                    "$netInfo = [Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]",
                    "foreach ($p in @($netInfo::GetConnectionProfiles())) {",
                    "    try {",
                    "        $mgr = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]::CreateFromConnectionProfile($p)",
                    "        if ($mgr.TetheringOperationalState -eq 1) { $mgr.StopTetheringAsync() | Out-Null }",
                    "    } catch {}",
                    "}",
                    "try {",
                    "    $shareMgr = New-Object -ComObject HNetCfg.HNetShare",
                    "    foreach ($c in @($shareMgr.EnumEveryConnection())) {",
                    "        try {",
                    "            $cfg = $shareMgr.INetSharingConfigurationForINetConnection($c)",
                    "            if ($cfg.SharingEnabled) { $cfg.DisableSharing() }",
                    "        } catch {}",
                    "    }",
                    "} catch {}",
                    "Write-Output 'SUCCESS'"
                };

                var ps1 = Path.Combine(Path.GetTempPath(), "nfv_stop_hotspot.ps1");
                await File.WriteAllLinesAsync(ps1, lines, new UTF8Encoding(false));
                await RunPs1Async(ps1, 20);
                try { File.Delete(ps1); } catch { }

                _activeSrc = null;
                _activeTgt = null;
                _hotspotBoundToSelectedProfile = false;
                _lastClientDiagnostic = null;
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<(int exit, string output)> RunPs1Async(string ps1Path, int timeoutSeconds = 30)
        {
            var args = "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + ps1Path + "\"";
            var psi = new ProcessStartInfo("powershell.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi)!;
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            var waitTask = proc.WaitForExitAsync();

            var finished = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
            if (finished != waitTask)
            {
                try { proc.Kill(true); } catch { }
                return (-2, "ERROR|Timeout");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (proc.ExitCode, (stdout + Environment.NewLine + stderr).Trim());
        }
    }
}