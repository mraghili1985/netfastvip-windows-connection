using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
        public bool IsHotspotBoundToSelectedProfile => _hotspotBoundToSelectedProfile;
        private string? _lastClientDiagnostic;

        public static bool IsHotspotTargetAdapter(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return false;

            var raw = adapterName.Trim();

            // الگوی Local Area Connection* به همراه هر شماره‌ای
            if (Regex.IsMatch(raw, @"^Local Area Connection\s*\*\s*\d+", RegexOptions.IgnoreCase))
                return true;

            var normalized = raw.Replace("*", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ");

            bool hasWifi = normalized.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("WiFi", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Wireless", StringComparison.OrdinalIgnoreCase);

            bool hasVirtual = normalized.Contains("Virtual Adapter", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

            bool hasDirect = normalized.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("WiFi Direct", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Microsoft Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Microsoft WiFi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase)
                || (normalized.Contains("Direct", StringComparison.OrdinalIgnoreCase) && hasWifi);

            bool hasHosted = normalized.Contains("Hosted Network", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("HostedNetwork", StringComparison.OrdinalIgnoreCase);

            if (hasDirect || hasHosted)
                return true;

            try
            {
                var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name.Equals(raw, StringComparison.OrdinalIgnoreCase));
                if (nic != null)
                {
                    var d = nic.Description ?? "";
                    if (d.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase) ||
                        d.Contains("WiFi Direct", StringComparison.OrdinalIgnoreCase) ||
                        d.Contains("Hosted Network", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return hasVirtual && hasWifi;
        }

        public static bool IsLocalAreaHotspotTargetAdapter(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return false;

            var raw = adapterName.Trim();
            if (Regex.IsMatch(raw, @"^Local Area Connection\s*\*\s*\d+", RegexOptions.IgnoreCase))
                return true;

            var normalized = raw.Replace("*", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ");

            return normalized.Contains("Local Area Connection", StringComparison.OrdinalIgnoreCase)
                && (normalized.EndsWith(" 10", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith(" 11", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith(" 12", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith(" 13", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith(" 14", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith(" 15", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsWifiDirectTargetAdapter(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return false;

            var normalized = adapterName.Trim().Replace("*", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ");

            bool isLocalArea = IsLocalAreaHotspotTargetAdapter(adapterName);
            if (isLocalArea)
                return false;

            return normalized.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("WiFi Direct", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Microsoft Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("Microsoft WiFi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase)
                || (normalized.Contains("Direct", StringComparison.OrdinalIgnoreCase) &&
                    (normalized.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                     || normalized.Contains("WiFi", StringComparison.OrdinalIgnoreCase)
                     || normalized.Contains("WLAN", StringComparison.OrdinalIgnoreCase)));
        }

        public static bool IsLikelyVpnSourceAdapter(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return false;

            var name = adapterName.Trim();
            return name.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase)
                || name.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)
                || name.Contains("sing-box", StringComparison.OrdinalIgnoreCase)
                || name.Contains("singbox", StringComparison.OrdinalIgnoreCase)
                || name.Contains("xray", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Wintun", StringComparison.OrdinalIgnoreCase)
                || name.Contains("TAP", StringComparison.OrdinalIgnoreCase)
                || name.Contains("TUN", StringComparison.OrdinalIgnoreCase)
                || name.Contains("ovpn", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VPN", StringComparison.OrdinalIgnoreCase)
                || name.Contains("L2TP", StringComparison.OrdinalIgnoreCase)
                || name.Contains("SSTP", StringComparison.OrdinalIgnoreCase)
                || name.Contains("IKEv2", StringComparison.OrdinalIgnoreCase)
                || name.Contains("PPTP", StringComparison.OrdinalIgnoreCase)
                || name.Contains("RAS", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPhysicalNic(NetworkInterface nic)
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Ppp
                or NetworkInterfaceType.Tunnel) return false;

            var d = (nic.Description ?? "").ToLowerInvariant();
            if (d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("vethernet")
                || d.Contains("virtualbox") || d.Contains("tap") || d.Contains("tun") || d.Contains("openvpn")
                || d.Contains("wireguard") || d.Contains("wintun") || d.Contains("wan miniport") || d.Contains("bluetooth"))
                return false;

            return true;
        }

        public static string? GetValidIpv4(NetworkInterface nic)
        {
            try
            {
                var ipProps = nic.GetIPProperties();
                if (ipProps?.UnicastAddresses == null) return null;
                foreach (var u in ipProps.UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ipStr = u.Address.ToString();
                        if (!ipStr.StartsWith("127.", StringComparison.Ordinal) &&
                            !ipStr.StartsWith("169.254.", StringComparison.Ordinal) &&
                            ipStr != "0.0.0.0")
                        {
                            return ipStr;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static string? FindBestVpnSourceAdapter(string? activeConn, string? tunnelLocalIp, IEnumerable<string>? extraCandidateNames = null)
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();

            // ۱. اولویت اول و قطعی: تطابق ۱۰۰٪ بر اساس آی‌پی تانل اختصاص داده شده
            if (!string.IsNullOrWhiteSpace(tunnelLocalIp))
            {
                foreach (var nic in nics)
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.OperationalStatus == OperationalStatus.Down) continue;
                    if (IsHotspotTargetAdapter(nic.Name)) continue;
                    if (IsPhysicalNic(nic)) continue;

                    var ip = GetValidIpv4(nic);
                    if (ip == tunnelLocalIp)
                    {
                        return nic.Name;
                    }
                }
            }

            var conn = (activeConn ?? string.Empty).Trim();
            bool isXray = conn.Contains("sing-box", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("singbox", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("xray", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("vless", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("vmess", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("trojan", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("shadowsocks", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("tuic", StringComparison.OrdinalIgnoreCase) ||
                          conn.Contains("hysteria", StringComparison.OrdinalIgnoreCase) ||
                          (tunnelLocalIp != null && tunnelLocalIp.StartsWith("172.19."));
            bool isOvpn = conn.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase);
            bool isWg = conn.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) || conn.Contains("Amnezia", StringComparison.OrdinalIgnoreCase);
            bool isRas = conn.Contains("L2TP", StringComparison.OrdinalIgnoreCase) ||
                        conn.Contains("SSTP", StringComparison.OrdinalIgnoreCase) ||
                        conn.Contains("IKEv2", StringComparison.OrdinalIgnoreCase) ||
                        conn.Contains("PPTP", StringComparison.OrdinalIgnoreCase);

            string? candidateByProtocol = null;
            string? candidateByLikelyVpn = null;
            string? candidateNonPhysicalUp = null;

            // ۲. اولویت دوم: تطابق بر اساس درایور، مشخصات اینترفیس (Description) و نام (Name)
            // بسیار مهم: کارتی که وضعیتش Down است یا هیچ آدرس IPv4 فعالی ندارد یا فیزیکی است، رد می‌شود!
            foreach (var nic in nics)
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.OperationalStatus == OperationalStatus.Down) continue;

                var name = nic.Name;
                if (IsHotspotTargetAdapter(name)) continue;
                if (IsPhysicalNic(nic)) continue;

                var ip = GetValidIpv4(nic);
                if (string.IsNullOrEmpty(ip)) continue; // کارت‌های بدون IP فعال رد می‌شوند

                var desc = nic.Description ?? "";

                // تطابق مستقیم برای sing-box
                if (isXray)
                {
                    bool matchXray = desc.Contains("sing-box", StringComparison.OrdinalIgnoreCase)
                                  || desc.Contains("singbox", StringComparison.OrdinalIgnoreCase)
                                  || name.Contains("sing-box", StringComparison.OrdinalIgnoreCase)
                                  || name.Contains("singbox", StringComparison.OrdinalIgnoreCase)
                                  || (ip != null && ip.StartsWith("172.19."));
                    if (matchXray)
                    {
                        return name;
                    }
                }

                // تطابق نام کانکشن در اپ با نام آداپتور
                if (!string.IsNullOrWhiteSpace(conn) &&
                    (name.Equals(conn, StringComparison.OrdinalIgnoreCase) ||
                     name.Contains(conn, StringComparison.OrdinalIgnoreCase) ||
                     conn.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }

                // درایورهای OpenVPN: DCO, TAP, Wintun
                if (isOvpn)
                {
                    bool matchOvpn = desc.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase)
                                  || desc.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase)
                                  || desc.Contains("Data Channel Offload", StringComparison.OrdinalIgnoreCase)
                                  || desc.Contains("dco", StringComparison.OrdinalIgnoreCase)
                                  || desc.Contains("wintun", StringComparison.OrdinalIgnoreCase)
                                  || name.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase)
                                  || name.Contains("TAP", StringComparison.OrdinalIgnoreCase)
                                  || name.Contains("DCO", StringComparison.OrdinalIgnoreCase);
                    if (matchOvpn)
                    {
                        if (candidateByProtocol == null || (!name.StartsWith("Local Area", StringComparison.OrdinalIgnoreCase) && candidateByProtocol.StartsWith("Local Area", StringComparison.OrdinalIgnoreCase)))
                        {
                            candidateByProtocol = name;
                        }
                    }
                }

                // درایورهای WireGuard / Amnezia
                if (isWg)
                {
                    bool matchWg = desc.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)
                                || desc.Contains("Amnezia", StringComparison.OrdinalIgnoreCase)
                                || desc.Contains("Wintun", StringComparison.OrdinalIgnoreCase)
                                || name.Contains("WireGuard", StringComparison.OrdinalIgnoreCase)
                                || name.Contains("Amnezia", StringComparison.OrdinalIgnoreCase)
                                || name.Contains("Wintun", StringComparison.OrdinalIgnoreCase)
                                || name.Contains("wg", StringComparison.OrdinalIgnoreCase);

                    if (matchWg)
                    {
                        if (candidateByProtocol == null || (!name.StartsWith("Local Area", StringComparison.OrdinalIgnoreCase) && candidateByProtocol.StartsWith("Local Area", StringComparison.OrdinalIgnoreCase)))
                        {
                            candidateByProtocol = name;
                        }
                    }
                }

                // پروتکل‌های RAS
                if (isRas)
                {
                    bool matchRas = nic.NetworkInterfaceType == NetworkInterfaceType.Ppp
                                 || desc.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase)
                                 || desc.Contains("SSTP", StringComparison.OrdinalIgnoreCase)
                                 || desc.Contains("L2TP", StringComparison.OrdinalIgnoreCase)
                                 || desc.Contains("IKEv2", StringComparison.OrdinalIgnoreCase)
                                 || desc.Contains("PPTP", StringComparison.OrdinalIgnoreCase)
                                 || name.Contains("SSTP", StringComparison.OrdinalIgnoreCase)
                                 || name.Contains("L2TP", StringComparison.OrdinalIgnoreCase)
                                 || name.Contains("IKEv2", StringComparison.OrdinalIgnoreCase)
                                 || name.Contains("PPTP", StringComparison.OrdinalIgnoreCase);

                    if (matchRas)
                    {
                        if (candidateByProtocol == null || (!name.StartsWith("Local Area", StringComparison.OrdinalIgnoreCase) && candidateByProtocol.StartsWith("Local Area", StringComparison.OrdinalIgnoreCase)))
                        {
                            candidateByProtocol = name;
                        }
                    }
                }

                // امضای کلی VPN در نام یا توصیف
                bool isLikely = IsLikelyVpnSourceAdapter(name) || IsLikelyVpnSourceAdapter(desc) ||
                                desc.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("TUN", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains("Data Channel Offload", StringComparison.OrdinalIgnoreCase) ||
                                nic.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel;

                if (isLikely && candidateByLikelyVpn == null)
                {
                    candidateByLikelyVpn = name;
                }

                if (candidateNonPhysicalUp == null)
                {
                    candidateNonPhysicalUp = name;
                }
            }

            if (candidateByProtocol != null) return candidateByProtocol;
            if (candidateByLikelyVpn != null) return candidateByLikelyVpn;

            // ۳. بررسی اسامی لیست کاندیداها (مثلاً از PowerShell یا کش قبلی)
            if (extraCandidateNames != null)
            {
                foreach (var adp in extraCandidateNames)
                {
                    if (IsHotspotTargetAdapter(adp)) continue;

                    var matchNic = nics.FirstOrDefault(n => n.Name.Equals(adp, StringComparison.OrdinalIgnoreCase));
                    if (matchNic != null)
                    {
                        if (matchNic.OperationalStatus == OperationalStatus.Down) continue;
                        if (IsPhysicalNic(matchNic)) continue;
                        if (string.IsNullOrEmpty(GetValidIpv4(matchNic))) continue;
                    }

                    if (!string.IsNullOrWhiteSpace(conn) &&
                        (adp.Equals(conn, StringComparison.OrdinalIgnoreCase) ||
                         adp.Contains(conn, StringComparison.OrdinalIgnoreCase) ||
                         conn.Contains(adp, StringComparison.OrdinalIgnoreCase)))
                    {
                        return adp;
                    }

                    if (isOvpn && (adp.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
                                  adp.Contains("DCO", StringComparison.OrdinalIgnoreCase) ||
                                  adp.Contains("TAP", StringComparison.OrdinalIgnoreCase)))
                    {
                        return adp;
                    }

                    if (isWg && (adp.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                                adp.Contains("Wintun", StringComparison.OrdinalIgnoreCase) ||
                                adp.Contains("wg", StringComparison.OrdinalIgnoreCase)))
                    {
                        return adp;
                    }

                    if (IsLikelyVpnSourceAdapter(adp))
                    {
                        return adp;
                    }
                }
            }

            if (candidateNonPhysicalUp != null) return candidateNonPhysicalUp;

            return null;
        }

        public static string? FindBestTargetAdapter(IEnumerable<string>? adapterNames = null)
        {
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces().ToList();

                // ۱. اولویت اول: کارت Wi-Fi Direct که وضعیت آن دقیقاً Up است (کارت فعال SoftAP مانند Local Area Connection* 10)
                var activeUp = nics.FirstOrDefault(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    IsWifiDirectDescription(nic.Description));
                if (activeUp != null) return activeUp.Name;

                // ۲. اولویت دوم: بررسی میان نام‌های ارسالی با وضعیت Up
                if (adapterNames != null)
                {
                    foreach (var adp in adapterNames)
                    {
                        var matching = nics.FirstOrDefault(n => n.Name.Equals(adp, StringComparison.OrdinalIgnoreCase));
                        if (matching != null && matching.OperationalStatus == OperationalStatus.Up && IsWifiDirectDescription(matching.Description))
                            return adp;
                    }
                }

                // ۳. اولویت سوم: هر کارت با توصیف Wi-Fi Direct
                var anyWifiDirect = nics.FirstOrDefault(nic => IsWifiDirectDescription(nic.Description));
                if (anyWifiDirect != null) return anyWifiDirect.Name;

                // ۴. اولویت چهارم: بررسی از روی نام‌ها
                if (adapterNames != null)
                {
                    foreach (var adp in adapterNames)
                    {
                        if (IsWifiDirectTargetAdapter(adp) || IsLocalAreaHotspotTargetAdapter(adp))
                            return adp;
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool IsWifiDirectDescription(string? desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return false;
            return desc.Contains("Wi-Fi Direct", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("WiFi Direct", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("Hosted Network", StringComparison.OrdinalIgnoreCase);
        }

        private void L(string msg) => Log?.Invoke(msg);
        private static string PsQuote(string v) => v.Replace("'", "''");

        public async Task<List<string>> GetAllAdaptersAsync()
        {
            var list = new List<string>();

            // ۱. خواندن سریع بدون درنگ از سیستم .NET
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var name = nic.Name.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name, StringComparer.OrdinalIgnoreCase))
                        list.Add(name);
                }
            }
            catch { }

            // ۲. خواندن کانکشن‌های اختصاصی ویندوز (Get-VpnConnection و ...)
            var lines = new[]
            {
                "$ErrorActionPreference = 'SilentlyContinue'",
                "$valid = @()",
                "try { $valid += @(Get-VpnConnection -ErrorAction SilentlyContinue | Where-Object ConnectionStatus -eq 'Connected' | ForEach-Object Name) } catch {}",
                "try { $valid += @(Get-VpnConnection -AllUserConnection -ErrorAction SilentlyContinue | Where-Object ConnectionStatus -eq 'Connected' | ForEach-Object Name) } catch {}",
                "try { $valid += @(Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue | Where-Object Status -eq 'Up' | ForEach-Object Name) } catch {}",
                "$valid | Select-Object -Unique | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }"
            };

            try
            {
                var ps1 = Path.Combine(Path.GetTempPath(), "nfv_get_adapters.ps1");
                await File.WriteAllLinesAsync(ps1, lines, new UTF8Encoding(false));
                var (_, output) = await RunPs1Async(ps1, 15);
                try { File.Delete(ps1); } catch { }

                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed) && !list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                        list.Add(trimmed);
                }
            }
            catch { }

            return list;
        }

        public async Task<(bool ok, string ssid, string pass, string err)> StartHotspotOnlyAsync(string sourceName)
        {
            await _lock.WaitAsync();
            try
            {
                L($"[Hotspot] Attempting to start mobile hotspot bound to profile ({sourceName})...");

                var lines = new[]
                {
                    "$ErrorActionPreference = 'Stop'",
                    "$preferred = '" + PsQuote(sourceName) + "'",
                    "try {",
                    "    $netInfo = [Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]",
                    "    $tethType = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]",
                    "    $profiles = @($netInfo::GetConnectionProfiles())",
                    "    $adapter = Get-NetAdapter -Name $preferred -IncludeHidden -ErrorAction SilentlyContinue | Select-Object -First 1",
                    "    if ($null -eq $adapter) {",
                    "        $adapter = Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue | Where-Object { $_.InterfaceDescription -eq $preferred -or $_.Name -like ('*' + $preferred + '*') } | Select-Object -First 1",
                    "    }",
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
                        L("[Hotspot] Mobile hotspot started directly via VPN profile (native routing).");
                    else
                        L("[Hotspot] Direct binding not available; fallback to ICS routing.");

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
                L($"[Hotspot] Native routing active on {sourceName}; skipping redundant COM ICS.");
                await EnableHotspotRoutingAndDnsAsync(sourceName, targetName);
                return (true, "");
            }

            // On some Windows systems the Wi‑Fi Direct virtual adapter appears as "Local Area Connection* 10".
            // Treat that adapter as the valid share target for the hotspot, but still avoid unsupported raw Direct names.
            if (IsWifiDirectTargetAdapter(targetName) && !IsLocalAreaHotspotTargetAdapter(targetName))
            {
                _activeSrc = sourceName;
                _activeTgt = targetName;
                L("[Hotspot] Target Wi-Fi Direct virtual adapter detected; skipping redundant ICS.");
                await EnableHotspotRoutingAndDnsAsync(sourceName, targetName);
                return (true, "");
            }

            await _lock.WaitAsync();
            try
            {
                L($"[Hotspot] Establishing ICS bridge ({sourceName} ➔ {targetName})...");

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
                    "    try {",
                    "        Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters' -Name 'IPEnableRouter' -Value 1 -ErrorAction SilentlyContinue",
                    "        Set-NetIPInterface -InterfaceAlias $src -Forwarding Enabled -WeakHostSend Enabled -WeakHostReceive Enabled -ErrorAction SilentlyContinue",
                    "        Set-NetIPInterface -InterfaceAlias $tgt -Forwarding Enabled -WeakHostSend Enabled -WeakHostReceive Enabled -ErrorAction SilentlyContinue",
                    "        Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -like '*sing*' -or $_.InterfaceAlias -like '*Local Area*' } | Set-NetIPInterface -Forwarding Enabled -WeakHostSend Enabled -WeakHostReceive Enabled -ErrorAction SilentlyContinue",
                    "        if ($src -like '*sing*') { Set-DnsClientServerAddress -InterfaceAlias $src -ServerAddresses @('1.1.1.1', '8.8.8.8') -ErrorAction SilentlyContinue }",
                    "    } catch {}",
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
                    await EnableHotspotRoutingAndDnsAsync(sourceName, targetName);
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
                L("[Hotspot] Stopping all services and sharing...");
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
                    "try {",
                    "    Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.Forwarding -eq 'Enabled' } | Set-NetIPInterface -Forwarding Disabled -WeakHostSend Disabled -WeakHostReceive Disabled",
                    "    Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters' -Name 'IPEnableRouter' -Value 0 -ErrorAction SilentlyContinue",
                    "    Remove-NetFirewallRule -DisplayName 'SmartVpn Hotspot DNS' -ErrorAction SilentlyContinue",
                    "    Remove-NetFirewallRule -DisplayName 'SmartVpn Hotspot Proxy' -ErrorAction SilentlyContinue",
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

        public async Task EnableHotspotRoutingAndDnsAsync(string sourceName, string targetName)
        {
            try
            {
                var lines = new List<string>
                {
                    "$ErrorActionPreference = 'SilentlyContinue'",
                    "$src = '" + PsQuote(sourceName) + "'",
                    "$tgt = '" + PsQuote(targetName) + "'",
                    "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters' -Name 'IPEnableRouter' -Value 1 -ErrorAction SilentlyContinue",
                    "Set-NetIPInterface -InterfaceAlias $src -Forwarding Enabled -WeakHostSend Enabled -WeakHostReceive Enabled -ErrorAction SilentlyContinue",
                    "Set-NetIPInterface -InterfaceAlias $tgt -Forwarding Enabled -WeakHostSend Enabled -WeakHostReceive Enabled -ErrorAction SilentlyContinue",
                    "Get-NetIPInterface -AddressFamily IPv4 | Where-Object { $_.InterfaceAlias -like '*sing*' -or $_.InterfaceAlias -like '*Local Area*' } | Set-NetIPInterface -Forwarding Enabled -WeakHostSend Enabled -WeakHostReceive Enabled -ErrorAction SilentlyContinue",
                    "if ($src -like '*sing*') { Set-DnsClientServerAddress -InterfaceAlias $src -ServerAddresses @('1.1.1.1', '8.8.8.8') -ErrorAction SilentlyContinue }",
                    "$tgtCurIp = (Get-NetIPAddress -InterfaceAlias $tgt -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '169.254*' -and $_.IPAddress -ne '0.0.0.0' }).IPAddress",
                    "if ($tgtCurIp -ne '192.168.137.1') {",
                    "    Get-NetIPAddress -InterfaceAlias $tgt -AddressFamily IPv4 -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue",
                    "    New-NetIPAddress -InterfaceAlias $tgt -IPAddress '192.168.137.1' -PrefixLength 24 -ErrorAction SilentlyContinue | Out-Null",
                    "}",
                    "Restart-Service -Name SharedAccess -Force -ErrorAction SilentlyContinue",
                    "New-NetFirewallRule -DisplayName 'SmartVpn Hotspot DNS' -Direction Inbound -LocalPort 53 -Protocol UDP -Action Allow -ErrorAction SilentlyContinue | Out-Null",
                    "New-NetFirewallRule -DisplayName 'SmartVpn Hotspot DHCP' -Direction Inbound -LocalPort 67 -Protocol UDP -Action Allow -ErrorAction SilentlyContinue | Out-Null",
                    "New-NetFirewallRule -DisplayName 'SmartVpn Hotspot Proxy' -Direction Inbound -LocalPort 20808 -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null",
                    "Write-Output 'OK'"
                };

                var ps1 = Path.Combine(Path.GetTempPath(), "nfv_enable_routing.ps1");
                await File.WriteAllLinesAsync(ps1, lines, new UTF8Encoding(false));
                await RunPs1Async(ps1, 15);
                try { File.Delete(ps1); } catch { }
                L("[Hotspot] IP packet forwarding (Forwarding/WeakHost/DNS) enabled successfully.");
            }
            catch (Exception ex)
            {
                L("[Hotspot WARN] Error enabling packet forwarding: " + ex.Message);
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