using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SmartVpn.XrayCore
{
    public static class SystemProxyHelper
    {
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        public static void Enable(string proxyServer = "127.0.0.1:20808")
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
                    key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>", RegistryValueKind.String);
                }
                Refresh();
            }
            catch { }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }
                Refresh();
            }
            catch { }
        }

        public static void Refresh()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }
    }

    public static class XrayEngine
    {
        private static Process? _coreProcess;
        public static event Action<string>? Log;
        public static bool IsRunning => _coreProcess != null && !_coreProcess.HasExited;

        public static string? FindSingBoxBinary()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                Path.Combine(baseDir, "Data", "sing-box", "sing-box.exe"),
                Path.Combine(baseDir, "sing-box", "sing-box.exe"),
                Path.Combine(baseDir, "sing-box.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "sing-box", "sing-box.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "Data", "sing-box", "sing-box.exe"),
                Path.Combine(baseDir, "Data", "sing-box.exe")
            };

            var exe = candidates.FirstOrDefault(File.Exists);
            if (exe != null)
            {
                EnsureWintunDll(Path.GetDirectoryName(exe) ?? "");
            }
            return exe;
        }

        private static void EnsureWintunDll(string targetDir)
        {
            try
            {
                if (string.IsNullOrEmpty(targetDir)) return;
                string destWintun = Path.Combine(targetDir, "wintun.dll");
                if (File.Exists(destWintun)) return;

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] sourceCandidates = {
                    Path.Combine(baseDir, "Data", "wireguard", "wintun.dll"),
                    Path.Combine(baseDir, "wireguard", "wintun.dll"),
                    Path.Combine(Directory.GetCurrentDirectory(), "wireguard", "wintun.dll"),
                    Path.Combine(Directory.GetCurrentDirectory(), "sing-box", "wintun.dll")
                };

                var src = sourceCandidates.FirstOrDefault(File.Exists);
                if (src != null)
                {
                    File.Copy(src, destWintun, true);
                }
            }
            catch { }
        }

        public static string BuildSingBoxConfig(
            ProxyProfile profile,
            bool enableTun = true,
            string splitMode = "off",
            List<string>? splitList = null,
            string dnsMode = "auto",
            string dnsPrimary = "",
            string dnsSecondary = "")
        {
            var root = new JsonObject();

            // 1. Log (warn level to avoid micro-stutter and flood of inbound/outbound connection logs)
            root["log"] = new JsonObject
            {
                ["level"] = "warn",
                ["timestamp"] = true
            };

            // 2. DNS
            string primaryDns = "1.1.1.1";
            string secondaryDns = "8.8.8.8";
            if (dnsMode == "cloudflare") { primaryDns = "1.1.1.1"; secondaryDns = "1.0.0.1"; }
            else if (dnsMode == "google") { primaryDns = "8.8.8.8"; secondaryDns = "8.8.4.4"; }
            else if (dnsMode == "adguard") { primaryDns = "94.140.14.14"; secondaryDns = "94.140.15.15"; }
            else if (dnsMode == "adguard_family") { primaryDns = "94.140.14.15"; secondaryDns = "94.140.15.16"; }
            else if (dnsMode == "custom" && !string.IsNullOrWhiteSpace(dnsPrimary))
            {
                primaryDns = dnsPrimary.Trim();
                secondaryDns = !string.IsNullOrWhiteSpace(dnsSecondary) ? dnsSecondary.Trim() : primaryDns;
            }

            var dnsServers = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "dns-remote",
                    ["type"] = "tcp",
                    ["server"] = primaryDns,
                    ["server_port"] = 53,
                    ["detour"] = "proxy"
                },
                new JsonObject
                {
                    ["tag"] = "dns-direct",
                    ["type"] = "udp",
                    ["server"] = primaryDns,
                    ["server_port"] = 53
                }
            };
            root["dns"] = new JsonObject
            {
                ["servers"] = dnsServers
            };

            // 3. Inbounds: TUN + Mixed (HTTP + SOCKS5)
            var inbounds = new JsonArray();

            if (enableTun)
            {
                inbounds.Add(new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = "sing-box",
                    ["address"] = new JsonArray { "172.19.0.1/30" },
                    ["auto_route"] = true,
                    ["strict_route"] = false,
                    ["stack"] = "mixed",
                    ["route_exclude_address"] = new JsonArray { "192.168.137.0/24" }
                });
            }

            inbounds.Add(new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = "mixed-in",
                ["listen"] = "0.0.0.0",
                ["listen_port"] = 20808
            });

            root["inbounds"] = inbounds;

            // 4. Outbounds: Proxy + Direct
            var proxyOutbound = new JsonObject
            {
                ["tag"] = "proxy",
                ["server"] = profile.Address,
                ["server_port"] = profile.Port,
                ["domain_resolver"] = "dns-direct"
            };

            string proto = (profile.Protocol ?? "VLESS").ToLowerInvariant();
            if (proto == "vless")
            {
                proxyOutbound["type"] = "vless";
                proxyOutbound["uuid"] = profile.UserId;
                if (!string.IsNullOrEmpty(profile.Flow))
                {
                    proxyOutbound["flow"] = profile.Flow;
                }
                proxyOutbound["network"] = string.IsNullOrEmpty(profile.Network) ? "tcp" : profile.Network;
            }
            else if (proto == "vmess")
            {
                proxyOutbound["type"] = "vmess";
                proxyOutbound["uuid"] = profile.UserId;
                proxyOutbound["security"] = "auto";
                proxyOutbound["network"] = string.IsNullOrEmpty(profile.Network) ? "tcp" : profile.Network;
            }
            else if (proto == "trojan")
            {
                proxyOutbound["type"] = "trojan";
                proxyOutbound["password"] = profile.UserId;
                proxyOutbound["network"] = string.IsNullOrEmpty(profile.Network) ? "tcp" : profile.Network;
            }
            else if (proto == "shadowsocks" || proto == "ss")
            {
                proxyOutbound["type"] = "shadowsocks";
                proxyOutbound["method"] = string.IsNullOrEmpty(profile.Security) ? "aes-256-gcm" : profile.Security;
                proxyOutbound["password"] = profile.UserId;
                proxyOutbound["network"] = "tcp";
            }
            else if (proto == "hysteria2" || proto == "hy2")
            {
                proxyOutbound["type"] = "hysteria2";
                proxyOutbound["password"] = profile.UserId;
                if (!string.IsNullOrEmpty(profile.Obfs))
                {
                    proxyOutbound["obfs"] = new JsonObject
                    {
                        ["type"] = profile.Obfs,
                        ["password"] = profile.ObfsPassword ?? ""
                    };
                }
            }
            else if (proto == "hysteria")
            {
                proxyOutbound["type"] = "hysteria";
                proxyOutbound["auth_str"] = profile.UserId;
            }
            else if (proto == "wireguard")
            {
                proxyOutbound["type"] = "wireguard";
                proxyOutbound["private_key"] = profile.UserId;
                proxyOutbound["peer_public_key"] = profile.PublicKey;
                string localAddr = !string.IsNullOrEmpty(profile.Path) ? profile.Path : "10.0.0.2/32";
                proxyOutbound["local_address"] = new JsonArray { localAddr };
            }
            else if (proto == "tuic")
            {
                proxyOutbound["type"] = "tuic";
                proxyOutbound["uuid"] = profile.UserId;
                proxyOutbound["password"] = profile.Password ?? "";
                proxyOutbound["congestion_control"] = string.IsNullOrWhiteSpace(profile.CongestionControl) ? "bbr" : profile.CongestionControl;
                proxyOutbound["udp_relay_mode"] = string.IsNullOrWhiteSpace(profile.UdpRelayMode) ? "native" : profile.UdpRelayMode;
                proxyOutbound["zero_rtt_handshake"] = false;
            }
            else
            {
                proxyOutbound["type"] = "vless";
                proxyOutbound["uuid"] = profile.UserId;
                proxyOutbound["network"] = "tcp";
            }

            // TLS / Reality Options
            string tlsType = profile.Tls?.ToLowerInvariant() ?? "none";
            string secType = profile.Security?.ToLowerInvariant() ?? "none";
            bool isTls = tlsType == "tls" || secType == "tls" || tlsType == "reality" || secType == "reality"
                         || proto == "hysteria2" || proto == "hy2" || proto == "hysteria" || proto == "tuic";

            if (isTls)
            {
                var tlsObj = new JsonObject
                {
                    ["enabled"] = true,
                    ["insecure"] = profile.Insecure
                };

                if (!string.IsNullOrEmpty(profile.Sni))
                {
                    tlsObj["server_name"] = profile.Sni;
                }
                else if (!string.IsNullOrEmpty(profile.Host))
                {
                    tlsObj["server_name"] = profile.Host;
                }
                else if (proto == "hysteria2" || proto == "hy2" || proto == "hysteria" || proto == "tuic")
                {
                    tlsObj["server_name"] = profile.Address;
                }

                if (!string.IsNullOrEmpty(profile.Fingerprint))
                {
                    tlsObj["utls"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["fingerprint"] = profile.Fingerprint
                    };
                }

                if (tlsType == "reality" || secType == "reality")
                {
                    tlsObj["reality"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["public_key"] = profile.PublicKey ?? "",
                        ["short_id"] = profile.ShortId ?? ""
                    };
                }

                if (!string.IsNullOrEmpty(profile.Alpn))
                {
                    var alpnArr = new JsonArray();
                    foreach (var a in profile.Alpn.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        alpnArr.Add(a.Trim());
                    }
                    if (alpnArr.Count > 0) tlsObj["alpn"] = alpnArr;
                }
                else if (proto == "hysteria2" || proto == "hy2" || proto == "tuic")
                {
                    tlsObj["alpn"] = new JsonArray { "h3" };
                }

                proxyOutbound["tls"] = tlsObj;
            }

            // Transport Options (ws, grpc, http)
            string net = profile.Network?.ToLowerInvariant() ?? "tcp";
            if (net == "ws")
            {
                var wsObj = new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = string.IsNullOrEmpty(profile.Path) ? "/" : profile.Path
                };
                if (!string.IsNullOrEmpty(profile.Host))
                {
                    wsObj["headers"] = new JsonObject
                    {
                        ["Host"] = profile.Host
                    };
                }
                proxyOutbound["transport"] = wsObj;
            }
            else if (net == "grpc")
            {
                proxyOutbound["transport"] = new JsonObject
                {
                    ["type"] = "grpc",
                    ["service_name"] = profile.ServiceName ?? ""
                };
            }
            else if (net == "http")
            {
                proxyOutbound["transport"] = new JsonObject
                {
                    ["type"] = "http",
                    ["host"] = new JsonArray { profile.Host ?? profile.Address },
                    ["path"] = string.IsNullOrEmpty(profile.Path) ? "/" : profile.Path
                };
            }

            root["outbounds"] = new JsonArray
            {
                proxyOutbound,
                new JsonObject
                {
                    ["type"] = "direct",
                    ["tag"] = "direct"
                }
            };

            // 5. Route with Per-App Split Tunnel support
            var rules = new JsonArray
            {
                new JsonObject
                {
                    ["action"] = "sniff"
                },
                new JsonObject
                {
                    ["port"] = 53,
                    ["action"] = "hijack-dns"
                },
                new JsonObject
                {
                    ["protocol"] = "dns",
                    ["action"] = "hijack-dns"
                },
                new JsonObject
                {
                    ["ip_is_private"] = true,
                    ["outbound"] = "direct"
                }
            };

            var cleanApps = new List<string>();
            if (splitList != null)
            {
                foreach (var item in splitList)
                {
                    if (string.IsNullOrWhiteSpace(item)) continue;
                    var trimmed = item.Trim();
                    if (trimmed.StartsWith("#") || trimmed.StartsWith("!")) continue;
                    var clean = Path.GetFileName(trimmed)?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(clean) && !cleanApps.Contains(clean))
                    {
                        cleanApps.Add(clean);
                    }
                }
            }

            string finalOutbound = "proxy";

            if (splitMode == "deny" && cleanApps.Count > 0)
            {
                // Bypass mode: All traffic goes via proxy EXCEPT listed apps which go direct
                var appArr = new JsonArray();
                foreach (var app in cleanApps) appArr.Add(app);
                rules.Add(new JsonObject
                {
                    ["process_name"] = appArr,
                    ["outbound"] = "direct"
                });
                finalOutbound = "proxy";
            }
            else if (splitMode == "allow" && cleanApps.Count > 0)
            {
                // Allow mode: ONLY listed apps go via proxy, everything else goes direct
                var appArr = new JsonArray();
                foreach (var app in cleanApps) appArr.Add(app);
                rules.Add(new JsonObject
                {
                    ["process_name"] = appArr,
                    ["outbound"] = "proxy"
                });
                finalOutbound = "direct";
            }

            root["route"] = new JsonObject
            {
                ["default_domain_resolver"] = "dns-direct",
                ["rules"] = rules,
                ["final"] = finalOutbound,
                ["auto_detect_interface"] = true
            };

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        public static (bool Success, string Error) Start(
            ProxyProfile profile,
            string splitMode = "off",
            List<string>? splitList = null,
            string dnsMode = "auto",
            string dnsPrimary = "",
            string dnsSecondary = "")
        {
            Stop();

            string? exe = FindSingBoxBinary();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                string errMsg = "فایل اجرایی sing-box.exe در مسیرهای برنامه یافت نشد.";
                Log?.Invoke("[sing-box ERROR] " + errMsg);
                return (false, errMsg);
            }

            string configDir = Path.GetDirectoryName(exe) ?? AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(configDir, "singbox-run.json");

            Log?.Invoke($"[sing-box] هسته در حال راه‌اندازی: {Path.GetFileName(exe)}");
            Log?.Invoke($"[sing-box] کانکشن انتخابی: {profile.Alias} ({profile.Protocol} {profile.Network})");

            // Attempt 1: Full TUN mode
            var result = LaunchProcess(exe, configPath, profile, enableTun: true, splitMode, splitList, dnsMode, dnsPrimary, dnsSecondary);
            if (!result.Success)
            {
                Log?.Invoke("[sing-box WARN] حالت TUN با خطا مواجه شد؛ تلاش برای راه‌اندازی در حالت System Proxy...");
                // Attempt 2: Fallback to Mixed System Proxy only
                result = LaunchProcess(exe, configPath, profile, enableTun: false, splitMode, splitList, dnsMode, dnsPrimary, dnsSecondary);
            }

            if (result.Success)
            {
                if (splitMode == "allow" && (splitList?.Count ?? 0) > 0)
                {
                    Log?.Invoke($"[sing-box] تونل تفکیک‌شده برنامه‌ها (Allow): تنها {splitList!.Count} برنامه انتخاب‌شده از پروکسی عبور داده می‌شوند.");
                }
                else
                {
                    SystemProxyHelper.Enable("127.0.0.1:20808");
                    Log?.Invoke("[sing-box] تنظیمات پراکسی سیستم ویندوز فعال شد (127.0.0.1:20808)");
                }
                Log?.Invoke("[sing-box] اتصال با موفقیت برقرار شد.");
                return (true, "");
            }
            else
            {
                Stop();
                return (false, result.Error);
            }
        }

        public static (bool Success, string Error) Start(string vlessUri)
        {
            try
            {
                var profile = UrlParser.Parse(vlessUri);
                return Start(profile);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[sing-box ERROR] خطا در پارس لینک اتصال: {ex.Message}");
                return (false, ex.Message);
            }
        }

        private static (bool Success, string Error) LaunchProcess(
            string exe,
            string configPath,
            ProxyProfile profile,
            bool enableTun,
            string splitMode,
            List<string>? splitList,
            string dnsMode,
            string dnsPrimary,
            string dnsSecondary)
        {
            try
            {
                string jsonConfig = BuildSingBoxConfig(profile, enableTun, splitMode, splitList, dnsMode, dnsPrimary, dnsSecondary);
                File.WriteAllText(configPath, jsonConfig, new System.Text.UTF8Encoding(false));

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"run -c \"{configPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                string startupError = "";
                var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Log?.Invoke("[sing-box] " + e.Data.Trim());
                    }
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        startupError += e.Data + "\n";
                        Log?.Invoke("[sing-box] " + e.Data.Trim());
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Check for immediate crash (e.g. within 1200ms)
                for (int i = 0; i < 12; i++)
                {
                    Thread.Sleep(100);
                    if (proc.HasExited)
                    {
                        int exitCode = proc.ExitCode;
                        string err = string.IsNullOrWhiteSpace(startupError) ? $"Process exited with code {exitCode}" : startupError.Trim();
                        return (false, err);
                    }
                }

                _coreProcess = proc;
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static void Stop()
        {
            try
            {
                SystemProxyHelper.Disable();
            }
            catch { }

            try
            {
                if (_coreProcess != null && !_coreProcess.HasExited)
                {
                    try { _coreProcess.Kill(); } catch { }
                    try { _coreProcess.Dispose(); } catch { }
                }
            }
            catch { }
            finally
            {
                _coreProcess = null;
            }

            try
            {
                var psi = new ProcessStartInfo("taskkill", "/F /IM sing-box.exe /T")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit(1000);
            }
            catch { }

            try
            {
                foreach (var z in Process.GetProcessesByName("sing-box"))
                {
                    try { z.Kill(); } catch { }
                }
            }
            catch { }

            Log?.Invoke("[sing-box] سرویس sing-box متوقف و پراکسی سیستم غیرفعال شد.");
        }
    }
}
