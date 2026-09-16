using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {

        private async Task ResolveServerIpAsync(string host)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(host)) return;
                if (_tunnelPeerIp != null) { ServerIpText.Text = _tunnelPeerIp; return; } // IP از لاگ خود OpenVPN آمده — فقط چیپ را هماهنگ کن
                var addrs = await Dns.GetHostAddressesAsync(host);
                var candidates = addrs.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                                      .Select(a => a.ToString()).Distinct().ToList();
                if (candidates.Count == 0) return;

                // اگر دامنه چند رکورد DNS داشته باشد، IP واقعی از جدول route ویندوز پیدا می‌شود:
                // هنگام اتصال، یک مسیر /32 فقط به IP سرور واقعی اضافه می‌شود (add route x.x.x.x/32)
                if (candidates.Count > 1 || !IPAddress.TryParse(host, out _))
                {
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        if (attempt > 0) await Task.Delay(2000);
                        if (!_engine.IsRunning) return;
                        if (_tunnelPeerIp != null) return; // sniff لاگ زودتر جواب را پیدا کرده
                        var hostRoutes = GetHostRouteDestinations();
                        var gws = GetPhysicalGateways();
                        var vpnRoutes = hostRoutes.Where(r => gws.Contains(r.Gw) && IsPublicIp(r.Dest))
                                                  .Select(r => r.Dest).Distinct().ToList();

                        // اولویت ۰: routeای که «بعد از شروع همین اتصال» اضافه شده — دقیق‌ترین نشانه؛
                        // routeهای بازمانده از اتصال‌های قبلی را حذف می‌کند (علت خطای یک‌درمیان)
                        var newRoutes = vpnRoutes.Where(d => !_routesBeforeConnect.Contains(d)).ToList();
                        if (newRoutes.Count == 1)
                        {
                            _tunnelPeerIp = newRoutes[0];
                            ServerIpText.Text = newRoutes[0];
                            AppendConnLog("[ip] server route added at connect: " + newRoutes[0]);
                            return;
                        }

                        // اولویت ۱: مسیر /32 جدیدی که با یکی از IPهای DNS همین دامنه بخواند؛
                        // اگر route سرور از قبل مانده باشد (The object already exists) دیف خالی می‌ماند و این مسیر جواب می‌دهد
                        var found = newRoutes.FirstOrDefault(d => candidates.Contains(d))
                                    ?? candidates.FirstOrDefault(c => vpnRoutes.Contains(c));
                        if (found != null)
                        {
                            _tunnelPeerIp = found;
                            ServerIpText.Text = found;
                            AppendConnLog("[ip] server matched DNS candidate route: " + found);
                            return;
                        }

                        // اولویت ۲: فقط یک route عمومی /32 روی gateway فیزیکی وجود دارد — همان bypass سرور VPN است
                        if (vpnRoutes.Count == 1)
                        {
                            _tunnelPeerIp = vpnRoutes[0];
                            ServerIpText.Text = vpnRoutes[0];
                            AppendConnLog("[ip] server from route table: " + vpnRoutes[0]);
                            return;
                        }
                    }
                }

                if (_tunnelPeerIp == null)
                {
                    ServerIpText.Text = candidates[0];
                    AppendConnLog("[ip] server fallback (first DNS record): " + candidates[0]);
                }
            }
            catch { }
        }

        // مقصد مسیرهای /32 (host route) از جدول مسیریابی ویندوز
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetIpForwardTable(IntPtr pIpForwardTable, ref int pdwSize, bool bOrder);

        private static List<(string Dest, string Gw)> GetHostRouteDestinations()
        {
            var result = new List<(string Dest, string Gw)>();
            try
            {
                int size = 0;
                GetIpForwardTable(IntPtr.Zero, ref size, false);
                if (size <= 0) return result;
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetIpForwardTable(buf, ref size, false) != 0) return result;
                    int count = Marshal.ReadInt32(buf);
                    const int rowSize = 56; // MIB_IPFORWARDROW = 14 x DWORD
                    for (int i = 0; i < count; i++)
                    {
                        int off = 4 + i * rowSize;
                        uint dest = (uint)Marshal.ReadInt32(buf, off);
                        uint mask = (uint)Marshal.ReadInt32(buf, off + 4);
                        if (mask == 0xFFFFFFFF)
                        {
                            uint gw = (uint)Marshal.ReadInt32(buf, off + 12); // dwForwardNextHop
                            result.Add((new IPAddress(BitConverter.GetBytes(dest)).ToString(),
                                        new IPAddress(BitConverter.GetBytes(gw)).ToString()));
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            return result;
        }

        // gatewayهای IPv4 کارت‌های فیزیکی — برای تشخیص bypass route سرور VPN
        private static HashSet<string> GetPhysicalGateways()
        {
            var set = new HashSet<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!IsPhysicalNic(nic)) continue;
                    foreach (var g in nic.GetIPProperties().GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork)
                            set.Add(g.Address.ToString());
                }
            }
            catch { }
            return set;
        }

        private static bool IsPublicIp(string ip)
        {
            if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            if (b[0] == 10 || b[0] == 127 || b[0] == 0 || b[0] >= 224) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false; // CGNAT
            if (b[0] == 169 && b[1] == 254) return false;
            return true;
        }

        private static bool IsPhysicalNic(NetworkInterface nic)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) return false;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Ppp
                or NetworkInterfaceType.Tunnel) return false;
            var d = nic.Description.ToLowerInvariant();
            if (d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("vethernet")
                || d.Contains("virtualbox") || d.Contains("tap") || d.Contains("tun") || d.Contains("openvpn")
                || d.Contains("wireguard") || d.Contains("wan miniport") || d.Contains("bluetooth"))
                return false;
            return true;
        }


        // ================= GeoIP =================
        private async Task FetchGeoIp()
        {
            var gen = ++_geoGen;
            int[] delays = { 1500, 4000, 4000 };
            foreach (var d in delays)
            {
                await Task.Delay(d);
                if (gen != _geoGen || !_engine.IsRunning) return;
                try
                {
                    var json = await _http.GetStringAsync(
                        "http://ip-api.com/json/?fields=status,query,country,countryCode");
                    if (gen != _geoGen || !_engine.IsRunning) return;
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.GetProperty("status").GetString() != "success") continue;
                    var ip = root.GetProperty("query").GetString() ?? "";
                    var cc = root.GetProperty("countryCode").GetString() ?? "";
                    var country = root.GetProperty("country").GetString() ?? "";
                    SetGeoFlag(cc);
                    GeoIpText.Text = country + " — " + ip;
                    _pingHost = ip; // پینگ زنده از این به بعد به IP خروجی (سرور مقصد) گرفته می‌شود
                    return;
                }
                catch { }
            }
        }

        // چک دوره‌ای اینفو: اگر IP خروجی وسط کار عوض شده باشد، نمایش، پرچم و هدف پینگ به‌روز می‌شوند
        private async Task RefreshGeoIpAsync()
        {
            if (!_engine.IsRunning) return;
            var gen = _geoGen;
            try
            {
                var json = await _http.GetStringAsync(
                    "http://ip-api.com/json/?fields=status,query,country,countryCode");
                if (gen != _geoGen || !_engine.IsRunning) return;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.GetProperty("status").GetString() != "success") return;
                var ip = root.GetProperty("query").GetString() ?? "";
                var cc = root.GetProperty("countryCode").GetString() ?? "";
                var country = root.GetProperty("country").GetString() ?? "";
                var newText = country + " — " + ip;
                if (GeoIpText.Text != newText)
                {
                    if (GeoIpText.Text != "—")
                        AppendConnLog(string.Format(LogExitIpChanged, GeoIpText.Text, newText));
                    SetGeoFlag(cc);
                    GeoIpText.Text = newText;
                }
                if (!string.IsNullOrEmpty(ip)) _pingHost = ip;
            }
            catch { }
        }

        private void SetGeoFlag(string cc)
        {
            if (string.IsNullOrWhiteSpace(cc) || cc.Length != 2) { GeoFlagImg.Source = null; return; }
            try
            {
                var bmp = new BitmapImage(new Uri("https://flagcdn.com/24x18/" + cc.ToLowerInvariant() + ".png", UriKind.Absolute));
                bmp.DownloadFailed += (_, __) => Dispatcher.Invoke(() => GeoFlagImg.Source = null);
                GeoFlagImg.Source = bmp;
            }
            catch { GeoFlagImg.Source = null; }
        }

        // فقط برای خطوط متنی در گزارش (LogBox) استفاده می‌شود
        private static string Flag(string cc)
        {
            if (cc.Length != 2) return "";
            cc = cc.ToUpperInvariant();
            return char.ConvertFromUtf32(0x1F1E6 + cc[0] - 'A')
                + char.ConvertFromUtf32(0x1F1E6 + cc[1] - 'A');
        }


    }
}
