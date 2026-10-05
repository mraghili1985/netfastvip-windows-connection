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
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SmartVpn.XrayCore;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        // ================= Power / Engine =================
        private async void Power_Click(object sender, RoutedEventArgs e)
        {
            // ۱. اگر هر نوع اتصالی در حال حاضر فعال است، کلیک روی پاور یعنی قطع اتصال
            if (_xrayIsConnected || XrayEngine.IsRunning)
            {
                XrayBtnPower_Click(sender, e);
                return;
            }
            if (_engine.IsRunning)
            {
                await StopManuallyAsync();
                return;
            }

            // ۲. اگر کانکشن انتخاب‌شده در حال حاضر از نوع Xray است
            bool isXraySelected = MainWindow.LastConnectionType == "xray" ||
                                 (_xraySelectedProfile != null && MainWindow.LastConnectionType != "vpn");

            if (isXraySelected)
            {
                XrayBtnPower_Click(sender, e);
                return;
            }

            // ۳. در غیر این صورت، کانکشن انتخاب‌شده VPN Core را وصل کن
            ConnectSelected();
        }

        // قطع دستی (بدون رنگ قرمز خطا)
        private async Task StopManuallyAsync()
        {
            _manualStop = true;
            PowerBtn.IsEnabled = false;
            try
            {
                await SplitTunnel.ClearAsync();
                await KillSwitch.DisableAsync();
                await _engine.StopAsync();
            }
            finally { PowerBtn.IsEnabled = true; }
        }

        // پیداکردن Interface Index آداپتوری که IP تانل روی آن نشسته — برای استثنای آداپتور تانل در Kill Switch
        private static int? FindInterfaceIndexByIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return null;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var props = ni.GetIPProperties();
                    var match = props.UnicastAddresses.Any(u =>
                        u.Address.AddressFamily == AddressFamily.InterNetwork && u.Address.ToString() == ip);
                    if (!match) continue;
                    var v4 = props.GetIPv4Properties();
                    if (v4 != null) return v4.Index;
                }
            }
            catch { /* نادیده گرفته می‌شود — استثنای آداپتور تانل اضافه نمی‌شود */ }
            return null;
        }

        private static int? FindWireGuardInterfaceIndex(string confContent)
        {
            try
            {
                var isAmnezia = WireGuardProvider.IsAmneziaConf(confContent);
                var tunnelName = WireGuardProvider.ParseTunnelName(confContent, isAmnezia);
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var name = ni.Name.ToLowerInvariant();
                    var desc = ni.Description.ToLowerInvariant();

                    if (name == tunnelName.ToLowerInvariant() ||
                        desc.Contains("wireguard") ||
                        desc.Contains("amnezia") ||
                        desc.Contains("wintun"))
                    {
                        var v4 = ni.GetIPProperties()?.GetIPv4Properties();
                        if (v4 != null) return v4.Index;
                    }
                }
            }
            catch { }
            return null;
        }

        // فعال‌سازی Kill Switch پس از اتصال موفق (یا بعد از هر اتصال مجدد پس از قطعی موقت) —
        // فیلترها را با آداپتور/سرور تازه به‌روز می‌کند (تا بعد از هر reconnect هم معتبر بماند).
        private async Task ApplyKillSwitchAsync(ConnectionProfile profile, string? tunnelLocalIp)
        {
            if (!KillSwitch.Enabled) return;
            try
            {
                var serverIp = _tunnelPeerIp;
                int? serverPort = profile.Port > 0 ? profile.Port : null;

                if (profile.Type == "wireguard" || profile.Type == "amneziawg")
                {
                    var (wgHost, wgPort) = WireGuardProvider.ParseEndpoint(profile.WireGuardConf);
                    if (wgPort.HasValue) serverPort = wgPort.Value;

                    if (string.IsNullOrWhiteSpace(serverIp) && !string.IsNullOrWhiteSpace(wgHost))
                    {
                        if (IPAddress.TryParse(wgHost, out var parsedIp) && parsedIp.AddressFamily == AddressFamily.InterNetwork)
                        {
                            serverIp = wgHost;
                        }
                        else
                        {
                            try
                            {
                                var addrs = await Dns.GetHostAddressesAsync(wgHost);
                                serverIp = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
                            }
                            catch { }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(serverIp))
                {
                    try
                    {
                        var addrs = await Dns.GetHostAddressesAsync(profile.EffectiveServer);
                        serverIp = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
                    }
                    catch { /* استثنای سرور بدون IP اضافه می‌شود — ورودی به سرور جدید تا اتصال بعدی ممکن است بلاک بماند */ }
                }

                // نکته‌ی مهم (باگ‌فیکس مشکل «بعضی وقت‌ها IP نمی‌گیرد و باید قطع/وصل کرد»): بلافاصله بعد از رویداد Connected
                // ممکن است IP آداپتور تانل هنوز در اسنپ‌شات NetworkInterface ویندوز/.NET منعکس نشده باشد
                // (تأخیر چند صدمیلی‌ثانیه بین لحظه‌ای که OpenVPN می‌گوید «متصل شد» و لحظه‌ای که خود ویندوز
                // IP را روی آداپتور ثبت می‌کند). اگر بدون این تلاش مجدد جلو برویم و ifIndex ناپیدا شود،
                // Kill Switch بدون استثنای آداپتور تانل فعال می‌شود — فیلتر پایه‌ی «بلاک همه» حتی خود
                // ترافیک VPN را هم می‌بندد — دقیقاً همان عارضه‌ای که نیاز به چند بار قطع/وصل دارد. برای
                // همین تا سقف ذایقه‌ای (تا ۳ ثانیه) منتطر می‌مانیم تا ifIndex پیدا شود.
                int? ifIndex = null;
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    ifIndex = FindInterfaceIndexByIp(tunnelLocalIp);
                    if (ifIndex == null && (profile.Type == "wireguard" || profile.Type == "amneziawg"))
                    {
                        ifIndex = FindWireGuardInterfaceIndex(profile.WireGuardConf);
                    }
                    if (ifIndex != null) break;
                    await Task.Delay(200);
                }
                if (ifIndex == null)
                    AppendConnLog("killswitch: Warning — tunnel adapter not found after multiple attempts; connection might be blocked, please reconnect");

                var ok = await KillSwitch.EnableAsync(serverIp, serverPort, ifIndex);
                if (!ok) AppendConnLog("killswitch: Activation failed — traffic was not blocked");
            }
            catch (Exception ex) { AppendConnLog("killswitch enable failed: " + ex.Message); }
        }

        // پروتکل واقعی اتصال برای نمایش به‌جای «Quality» گمراه‌کننده — UDP/TCP، نه یک برچسب کیفیت
        // که به فاصله جغرافیایی سرور وابسته بود
        private static string GetProtocolLabel(ConnectionProfile profile)
        {
            var proto = profile.Type.ToLowerInvariant() switch
            {
                "openvpn" => profile.Proto.Equals("tcp", StringComparison.OrdinalIgnoreCase) ? "TCP" : "UDP",
                "sstp" => "TCP",
                "pptp" => "TCP",
                "ikev2" => "UDP",
                "l2tp" => "UDP",
                "wireguard" => "WireGuard",
                "amneziawg" => "AmneziaWG",
                _ => "—",
            };
            // پورت به همین کادر منتقل شد تا اینفوی بالا خلوت‌تر باشد (قبلاً زیر نام سرور جدا نشان داده می‌شد)
            // WireGuard/AmneziaWG: پورت داخل .conf هست، دیگر نمایش جدا لازم نیست
            if (profile.Type == "wireguard" || profile.Type == "amneziawg") return proto;
            return profile.Port is int p ? $"{proto} {p}" : proto;
        }

        // اتصال کانکشن انتخاب‌شده (بدنه قبلی Power_Click)
        private void ConnectSelected()
        {
            MainWindow.LastConnectionType = "vpn";
            if (_xrayIsConnected || XrayEngine.IsRunning)
            {
                XrayEngine.Stop();
                _xrayIsConnected = false;
                _xrayTimer?.Stop();
                if (XrayTxtActiveName != null) XrayTxtActiveName.Text = "—";
            }

            var run = BuildRunList();
            if (run == null) return; // کاربر دیالوگ یوزر/پس را لغو کرد
            if (run.Count == 0) { AskDialog.Info(this, MsgNoConn); return; }

            if (run.Any(p => string.Equals(p.Type, "openvpn", StringComparison.OrdinalIgnoreCase))
                && !_config.EnsureBaseOvpnFile())
            {
                AskDialog.Info(this, MsgBaseMissing);
                return;
            }

            // 🔒 گواهی CA برای sstp/ikev2 — نصب فقط با اجازه کاربر در اولین اتصال (نصب سایلنت از اینستالر حذف شد)
            var needsCa = run.Where(p =>
                string.Equals(p.Type, "sstp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Type, "ikev2", StringComparison.OrdinalIgnoreCase)).ToList();
            if (needsCa.Count > 0)
            {
                _config.EnsureCaFile();
            }

            if (needsCa.Count > 0 && CaInstaller.Exists && !CaInstaller.IsInstalled())
            {
                var installed = false;
                if (AskDialog.Confirm(this, MsgCaInstall, BtnInstallCa, BtnNotNow))
                {
                    installed = CaInstaller.TryInstall(AppendLog);
                    AppendConnLog(installed ? LogCaInstalled : LogCaFailed);
                }
                else
                {
                    AppendConnLog(LogCaSkipped);
                }

                if (!installed)
                {
                    // بدون CA این پروتکل‌ها به خطای گواهی می‌خورند — از لیست اجرا حذف می‌شوند، بقیه عادی وصل می‌شوند
                    run = run.Except(needsCa).ToList();
                    if (run.Count == 0) { AskDialog.Info(this, MsgCaRequired); return; }
                }
            }

            // ثبت gateway محلی قبل از تغییر مسیرها توسط تونل
            SplitTunnel.CaptureLocalGateway();

            // عکس از routeهای /32 فعلی — routeهای بازمانده از اتصال‌های قبلی در ویندوز می‌مانند؛
            // بعد از اتصال، فقط routeای که «جدید اضافه شده» ملاک IP سرور واقعی است
            _tunnelPeerIp = null; // شروع تمیز — IP سرور فقط از همین اتصال جدید خوانده شود

            SetStatusText(TxtConnecting);
            PowerHintText.Text = Localization.T("در حال اتصال...");
            StartSpin();
            SetPowerState("connecting");
            _manualStop = false;
            SetLocked(true);
            _engine.Start(run);
            RefreshList(); // غیرفعال‌شدن دکمه‌های ویرایش/حذف ردیف‌ها هنگام اتصال
        }


        // سوییچ امن کانکشن: بعد از Stop صبر می‌کنیم تا route/adapter ویندوز واقعاً آزاد شود.
        // بدون این وقفه، مخصوصاً در OpenVPN/WARP ممکن است اتصال جدید بالا بیاید ولی ترافیک هنوز از مسیر قبلی/معمولی برود.
        private async Task SwitchToConnectionAsync(ConnectionProfile profile)
        {
            MainWindow.LastConnectionType = "vpn";
            _selectedName = profile.Name;
            ActiveConnText.Text = $"{CategoryLabel(profile.Type)} {profile.Name}";
            ServerSubText.Text = profile.ServerLine;
            UpdateActiveBadge(profile.Name, profile.Type);
            RefreshList();

            await StopManuallyAsync();

            for (int i = 0; i < 20 && _engine.IsRunning; i++)
                await Task.Delay(150);

            try { await SplitTunnel.ClearAsync(); } catch { }
            await Task.Delay(700);

            _manualStop = false;
            ConnectSelected();
        }

        // پروفایل‌های اجرا + گرفتن یوزر/پس برای کانکشن‌هایی که ذخیره ندارند
        private List<ConnectionProfile>? BuildRunList()
        {
            var smart = _config.SmartSwitch;
            List<ConnectionProfile> list;
            var target = _config.Connections.FirstOrDefault(c => c.Name == _selectedName);

            if (smart)
            {
                list = new List<ConnectionProfile>();
                if (target != null)
                {
                    list.Add(target);
                    list.AddRange(_config.Connections.Where(c => c != target));
                }
                else
                {
                    list.AddRange(_config.Connections);
                }
            }
            else
            {
                list = target != null ? new List<ConnectionProfile> { target } : new List<ConnectionProfile>();
                if (list.Count == 0 && _config.Connections.Count > 0)
                    list = new List<ConnectionProfile> { _config.Connections[0] };
            }

            var run = new List<ConnectionProfile>();
            foreach (var p in list)
            {
                if (!p.NeedsCredentials || p.Username.Length > 0) { run.Add(p); continue; }
                // WireGuard / AmneziaWG — کلیدها داخل .conf هستند، یوزر/پس ندارند
                if (p.Type == "wireguard" || p.Type == "amneziawg") { run.Add(p); continue; }

                var dlg = new CredentialsDialog(p.Name + " — " + CredPromptSuffix) { Owner = this };
                if (dlg.ShowDialog() != true) return null;

                var user = dlg.UserBox.Text.Trim();
                var pass = dlg.PassBox.Password;
                if (dlg.SaveCheck.IsChecked == true)
                {
                    p.Username = user; p.Password = pass;
                    // همان یوزر/پس روی بقیه کانکشن‌های رسمیِ بدون یوزرنیم هم ذخیره می‌شود — از ویرایش هر کانکشن قابل تغییر است
                    foreach (var o in _config.Connections.Where(x => x.Source == "official" && x.NeedsCredentials && x.Username.Length == 0))
                    {
                        o.Username = user; o.Password = pass;
                    }
                    _config.Save();
                    TryAutoCheckSubscriptionSummaryOnce(); // تازه یوزر/پس ذخیره شد — کارت خلاصه اشتراک را یک‌بار پر کن
                    run.Add(p);
                }
                else
                {
                    run.Add(p.CloneWithCreds(user, pass));
                }
            }
            return run;
        }

        private async void OnEngineConnected(ConnectionProfile profile, string? localIp)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                StopSpin();
                StartPulse();
                SetStatusText(TxtConnected);
                PowerHintText.Text = Localization.T("برای قطع اتصال کلیک کنید");
                SetPowerState("connected");
                Notify((_wasReconnecting ? NotifyReconnected : NotifyConnected) + " — " + profile.Name);
                _wasReconnecting = false;
                _everConnected = true;
                _selectedName = profile.Name;
                _connectedName = profile.Name; // تنها اینجا مشخص می‌شود چه چیزی واقعا وصل شده
                _config.AddRecentConnection(profile.Name);
                RenderRecentServersList();
                MiniConnectionText.Text = profile.Name;
                MoveToTop(profile.Name); // کانکشن متصل‌شده می‌رود صدر لیست
                ActiveConnText.Text = $"{CategoryLabel(profile.Type)} {profile.Name}";
                HsVpnConnectionText.Text = ActiveConnText.Text;
                ServerSubText.Text = profile.ServerLine;
                UpdateActiveBadge(profile.Name, profile.Type);
                PrivateIpText.Text = string.IsNullOrEmpty(localIp) ? "—" : localIp;
                ServerIpText.Text = _tunnelPeerIp ?? "—"; // sniff لاگ openvpn از قبل IP را گرفته — پاک نشود
                // WireGuard/AmneziaWG: به‌جای User، هندشیک نشان داده می‌شود
                bool isWg = profile.Type == "wireguard" || profile.Type == "amneziawg";
                _activeIsWg = isWg;
                if (isWg)
                {
                    var (wgHost, _) = WireGuardProvider.ParseEndpoint(profile.WireGuardConf);
                    if (!string.IsNullOrWhiteSpace(wgHost))
                    {
                        if (IPAddress.TryParse(wgHost, out _))
                        {
                            _tunnelPeerIp = wgHost;
                            ServerIpText.Text = wgHost;
                        }
                        else
                        {
                            _ = ResolveServerIpAsync(wgHost);
                        }
                    }
                    WireGuardProvider.LastHandshake = null;
                    YouText.Text = "—";
                    YouLbl.Text = Localization.T("Last Handshake");
                }
                else
                {
                    YouText.Text = string.IsNullOrEmpty(profile.Username) ? "—" : profile.Username;
                    YouLbl.Text = Localization.T("User");
                }
                ProtocolText.Text = GetProtocolLabel(profile);

                _connectStart = DateTime.Now;
                _tunnelLocalIp = localIp; // برای شناسایی آداپتور تانل و شمار�� دقیق حجم
                _pingHost = profile.EffectiveServer; // موقت تا رسیدن IP خروجی از GeoIP — Server Override در اولویت
                _pingTick = 0;
                _pingFails = 0;
                _idleTick = 0;
                _idleWindowBytes = 0;
                _idleMinutes = 0;
                StartStatsTimer();

                // مهم: FetchGeoIp/ResolveServerIpAsync باید قبل از تعویض DNS اجرا شود.
                // این دو متود خودشان از Dns.GetHostAddressesAsync استفاده می‌کنند؛ اگر بعد از
                // تعویض DNS به سمت DNS تونل اجرا شوند، برای اتصال‌های نیت��و (sstp/l2tp)
                // که هنوز IP از لاگ سنیف نشده، resolve می‌تواند فیل شود/هنگ کند و Server IP "—" بماند.
                _geoGen++;
                _ = FetchGeoIp();
                _ = ResolveServerIpAsync(profile.EffectiveServer);
            });

            _ = ApplyKillSwitchAsync(profile, localIp);

            // توجه: قابلیت اسپلیت‌تانل تفکیک برنامه‌ها (Per-App) منحصراً برای کانکشن‌های Xray/sing-box فعال است
            // و روی کانکشن‌های سنتی اعمال نمی‌شود تا دستکاری جدول Route انجام نگیرد.

            bool isWgProfile = profile.Type == "wireguard" || profile.Type == "amneziawg";
            if (!isWgProfile && ResolveDnsChoice() is { } dns)
            {
                try { await DnsManager.ApplyToTunnelAsync(localIp, dns.primary, dns.secondary); }
                catch (Exception ex) { AppendConnLog("dns apply failed: " + ex.Message); }
            }
        }

        private async void OnEngineReconnecting()
        {
            Dispatcher.Invoke(() =>
            {
                SetStatusText(TxtReconnecting);
                PowerHintText.Text = Localization.T("تلاش مجدد برای اتصال...");
                StartSpin();
                SetPowerState("reconnecting");
                Notify(NotifyReconnecting);
                _wasReconnecting = true;
                GeoIpText.Text = "—";
                RefreshList(); // بج ردیف کانکشن هم وضعیت «تلاش مجدد» را نشان دهد
            });
            try { await SplitTunnel.ClearAsync(); } catch { }
            try { await DnsManager.RevertAsync(); } catch { }
        }

        private async void OnEngineStopped()
        {
            StopStatsTimer();
            try { await SplitTunnel.ClearAsync(); } catch { }
            try { await KillSwitch.DisableAsync(); } catch { }
            try { await DnsManager.RevertAsync(); } catch { }

            Dispatcher.Invoke(() =>
            {
                StopSpin();
                StopPulse();
                SetStatusText(TxtReady);
                PowerHintText.Text = Localization.T("روشن/خاموش اتصال");
                SetPowerState(_manualStop ? "off" : "error");
                _manualStop = false;
                _connectedName = null; // دیگر هیچ کارتی واقعا وصل نیست
                MiniConnectionText.Text = "—";
                HsVpnConnectionText.Text = Localization.T("عدم اتصال");
                DurationText.Text = "00:00:00";
                DlSpeedText.Text = "↓ 0 B/s";
                UlSpeedText.Text = "↑ 0 B/s";
                PrivateIpText.Text = "—";
                ServerIpText.Text = "—";
                SessionTrafficText.Text = "0 B";
                _pingHost = null;
                _pingInFlight = false;
                _tunnelPeerIp = null;
                _tunnelLocalIp = null;
                _tunnelNicId = null;
                PingText.Text = "—";
                PingText.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                ProtocolText.Text = "—";
                GeoIpText.Text = "—";
                YouText.Text = "—";
                YouLbl.Text = Localization.T("User"); // reset label برای اتصال بعدی
                _activeIsWg = false;
                _dlHistory.Clear();
                _ulHistory.Clear();
                RedrawGraph();
                SetLocked(false);
                RefreshList(); // فعال‌شدن دوباره ویرایش/حذف
                if (_everConnected) { Notify(NotifyStopped); _everConnected = false; }
                _wasReconnecting = false;
            });
        }

        private void OnEngineAuthFailed()
        {
            Dispatcher.Invoke(() =>
            {
                StopSpin();
                SetStatusText(TxtAuthFailed);
                PowerHintText.Text = Localization.T("روشن/خاموش اتصال");
                AppendConnLog("Error: Authentication failed (invalid credentials).");
                // دکمه «بررسی اشتراک» — چون علت رایج AUTH_FAILED پایان اشتراک است، مستقیم به پنل کاربری هدایت می‌شود
                var choice = AskDialog.Choose(this, MsgAuthFailed, BtnCheckSubscription, BtnOkText);
                if (choice == 0)
                {
                    try { Process.Start(new ProcessStartInfo(_config.PanelUrl) { UseShellExecute = true }); }
                    catch { }
                }
            });
        }

        // ================= Navigation =================
        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((Button)sender).Tag;
            ShowPanel(tag);
        }

        private void ShowPanel(string tag)
        {
            UpdateSidebarState(tag == "home" ? "dashboard" : tag == "xray" ? "xray" : tag == "vpn" ? "vpn" : tag);
            if (DrawerOverlay != null) DrawerOverlay.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
            ToolboxPanel.Visibility = tag == "tools" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
            ConnLogPanel.Visibility = tag == "connlog" ? Visibility.Visible : Visibility.Collapsed;
            if (HotspotPanel != null) HotspotPanel.Visibility = tag == "hotspot" ? Visibility.Visible : Visibility.Collapsed;
            if (XrayPanel != null) XrayPanel.Visibility = tag == "xray" ? Visibility.Visible : Visibility.Collapsed;
            if (VpnPanel != null) VpnPanel.Visibility = tag == "vpn" ? Visibility.Visible : Visibility.Collapsed;

            // هدر: صفحه خانه = برند و آیکون‌ها، بقیه صفحات = فلش برگشت + عنوان صفحه
            HeaderBrandPanel.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
            HeaderPagePanel.Visibility = tag == "home" ? Visibility.Collapsed : Visibility.Visible;
            HeaderTitleText.Text = tag == "vpn" ? "VPN Core" : tag == "xray" ? "Xray Core" : tag == "tools" ? Localization.T("ابزارهای شبکه") : tag == "settings" ? Localization.T("تنظیمات") : tag == "connlog" ? Localization.T("لاگ کانکشن‌ها") : "";

            switch (tag)
            {
                case "home": AnimatePanelIn(HomePanel, HomeTransform); break;
                case "vpn": if (VpnPanel != null) AnimatePanelIn(VpnPanel, VpnTransform); break;
                case "tools":
                    AnimatePanelIn(ToolboxPanel, ToolsTransform);
                    if (LogBox.Text.Length == 0)
                    {
                        LogBox.Text = Localization.T("برای اجرای تست پینگ یا تریسرورت، آدرس مقصد را وارد کرده و دکمه را بزنید...") + Environment.NewLine;
                    }
                    LogBox.CaretIndex = LogBox.Text.Length;
                    LogBox.ScrollToEnd();
                    break;
                case "connlog": AnimatePanelIn(ConnLogPanel, ConnLogTransform); ConnLogBox.CaretIndex = ConnLogBox.Text.Length; ConnLogBox.ScrollToEnd(); break;
                case "settings": AnimatePanelIn(SettingsPanel, SettingsTransform); break;
                case "xray": if (XrayPanel != null) AnimatePanelIn(XrayPanel, XrayTransform); break;
            }
        }

        // دکمهٔ گزارش مستقل در هدر — ربطی به گزینهٔ «ابزارها و گزارش» در منوی همبرگری ندارد
        private void HeaderLogBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("connlog");

        private void ClearConnLog_Click(object sender, RoutedEventArgs e) => ConnLogBox.Clear();

        private void BackBtn_Click(object sender, RoutedEventArgs e) => ShowPanel("home");

        // کلیک روی نام برند در هدر — فعلاً لینک کانال تلگرام (قابل تغییر بعداً به لینک واقعی سایت/پنل)
        private const string BrandChannelUrl = "https://t.me/netfastvip";
        private void BrandTitleText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(BrandChannelUrl) { UseShellExecute = true }); } catch { }
        }

        private static void AnimatePanelIn(UIElement panel, TranslateTransform transform)
        {
            transform.Y = 10;
            panel.Opacity = 0;
            var anim = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(180));
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            transform.BeginAnimation(TranslateTransform.YProperty, anim);
            panel.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // ================= Power button animations =================
        private void StartSpin()
        {
            var sb = (Storyboard)FindResource("SpinStoryboard");
            sb.Begin(this, true);
        }

        private void StopSpin()
        {
            var sb = (Storyboard)FindResource("SpinStoryboard");
            sb.Stop(this);
            PowerGlyphRotate.Angle = 0;
        }

        private void StartPulse()
        {
            var sb = (Storyboard)FindResource("PulseStoryboard");
            sb.Begin(this, true);
        }

        private void StopPulse()
        {
            var sb = (Storyboard)FindResource("PulseStoryboard");
            sb.Stop(this);
            PowerScale.ScaleX = 1;
            PowerScale.ScaleY = 1;
        }

        private void SetHotspotHeaderState(string state)
        {
            var brush = state switch
            {
                "active" => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                "busy" => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                _ => new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            };

            HotspotHeaderIcon.Foreground = brush;
            HeaderHotspotBtn.ToolTip = state switch
            {
                "active" => Localization.T("هات‌اسپات فعال است"),
                "busy" => Localization.T("هات‌اسپات در حال آماده‌سازی است"),
                _ => Localization.T("باز کردن پنجره Hotspot"),
            };

            HotspotHeaderGlow.Color = brush.Color;
            HotspotHeaderGlow.Opacity = state == "active" ? 0.75 : state == "busy" ? 0.35 : 0;

            var pulse = (Storyboard)FindResource("HotspotPulseStoryboard");
            pulse.Stop(this);
            HotspotHeaderScale.ScaleX = 1;
            HotspotHeaderScale.ScaleY = 1;
            if (state == "active") pulse.Begin(this, true);
        }


        private void MoreInfoToggle_Click(object sender, RoutedEventArgs e)
        {
            var open = MoreInfoPanel.Visibility != Visibility.Visible;
            MoreInfoArrow.Text = open ? " ▴" : " ▾";
            MoreInfoText.Text = open ? Localization.T("بستن جزئیات") : Localization.T("جزئیات بیشتر");
            _config.HomeDetailsExpanded = open;
            _config.Save();

            // باز/بسته‌شدن نرم به جای توگل لحظه‌ای Visibility — ۲۰۰ میلی‌ثانیه بر روی Opacity
            var anim = new DoubleAnimation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            };
            if (open)
            {
                MoreInfoPanel.Opacity = 0;
                MoreInfoPanel.Visibility = Visibility.Visible;
                anim.From = 0;
                anim.To = 1;
                MoreInfoPanel.BeginAnimation(OpacityProperty, anim);
            }
            else
            {
                anim.From = 1;
                anim.To = 0;
                anim.Completed += (_, __) =>
                {
                    // اگر در این فاصله دوباره باز شده باشد، بسته‌شدن را لغو نکند
                    if (MoreInfoPanel.Visibility != Visibility.Collapsed && MoreInfoArrow.Text == " ▾")
                        MoreInfoPanel.Visibility = Visibility.Collapsed;
                };
                MoreInfoPanel.BeginAnimation(OpacityProperty, anim);
            }
            _ = Dispatcher.InvokeAsync(() => RedrawGraph(), System.Windows.Threading.DispatcherPriority.Background);
        }

        // کپی IP با یک کلیک روی مقدار IP سرور/تانل در بخش Details — بازخورد کوتاه با تقییر رنگ تایید کپی
        private void CopyIpText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not TextBlock tb) return;
            var text = tb.Text;
            if (string.IsNullOrWhiteSpace(text) || text == "—") return;
            try { Clipboard.SetText(text); } catch { return; }

            var original = tb.Foreground;
            tb.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
            var revert = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            revert.Tick += (_, __) =>
            {
                tb.Foreground = original;
                revert.Stop();
            };
            revert.Start();
        }

        private void SetStatusText(string text)
        {
            // text همیشه کلید فارسی است — ذخیره می‌کنیم تا هنگام تغییر زبان بتوانیم دوباره ترجمه کنیم
            _currentStatusKey = text;
            var translated = Localization.T(text);
            StatusText.Text = translated;
            if (MiniStatusText != null) MiniStatusText.Text = translated;
        }

        // فراخوانی از Settings هنگام تغییر زبان — وضعیت جاری را با زبان جدید دوباره نمایش می‌دهد
        internal void ReapplyCurrentStatusText() => SetStatusText(_currentStatusKey);

        // رنگ کلید پاور: خاکستری=خاموش — آبی=در حال اتصال — سبز=متصل — قرمز=قطع ناخواسته
        private void SetPowerState(string state)
        {
            _currentPowerState = state; // ثبت وضعیت واقعی — لیست کانکشن‌ها هم از همین منبع می‌خوانند تا با کارت بالا هم‌خوان بماند
            var brush = state switch
            {
                "connecting" or "reconnecting" => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                "connected" => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                "error" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                _ => new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            };
            PowerBtn.Background = brush;
            if (MiniPowerBtn != null) MiniPowerBtn.Background = brush;
            if (XrayBtnPower != null) XrayBtnPower.Background = brush;

            if (PowerOuterRing != null)
            {
                PowerOuterRing.BorderBrush = state switch
                {
                    "connecting" or "reconnecting" => new SolidColorBrush(Color.FromArgb(0x70, 0x3B, 0x82, 0xF6)),
                    "connected" => new SolidColorBrush(Color.FromArgb(0x70, 0x22, 0xC5, 0x5E)),
                    "error" => new SolidColorBrush(Color.FromArgb(0x70, 0xEF, 0x44, 0x44)),
                    _ => (Brush)FindResource("GlassBorderBrush"),
                };
            }

            PowerHintText.Text = Localization.T(state switch
            {
                "connected" => "برای قطع اتصال کلیک کنید",
                "connecting" or "reconnecting" => "لطفاً شکیبا باشید...",
                "error" => "جهت تلاش مجدد کلیک کنید",
                _ => "جهت اتصال کلیک کنید"
            });

            if (PowerStateText != null)
            {
                PowerStateText.Text = Localization.T(state switch
                {
                    "connected" => "متصل و ایمن",
                    "connecting" or "reconnecting" => "در حال اتصال...",
                    "error" => "خطا در اتصال",
                    _ => "خاموش"
                });
                PowerStateText.Foreground = state switch
                {
                    "connected" => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                    "connecting" or "reconnecting" => new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                    "error" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                    _ => (Brush)FindResource("TextBrush"),
                };
            }

            if (StatusText != null)
            {
                StatusText.Foreground = state switch
                {
                    "connected" => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                    "connecting" or "reconnecting" => new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
                    "error" => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                    _ => (Brush)FindResource("SubTextBrush"),
                };
            }

            // نقطه وضعیت کنار متن هم همان پیام رنگی را می‌دهد
            if (StatusDot != null)
                StatusDot.Fill = state == "off" ? new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)) : brush;
            if (MiniStatusDot != null)
                MiniStatusDot.Fill = state == "off" ? new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)) : brush;
            if (XrayStatusDot != null)
                XrayStatusDot.Fill = state == "off" ? new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)) : brush;

            if (MiniPowerGlow != null)
            {
                MiniPowerGlow.Color = brush.Color;
                MiniPowerGlow.Opacity = state == "off" ? 0.0 : 0.45;
            }

            if (PowerBtn.Effect is DropShadowEffect glow)
            {
                glow.Color = state switch
                {
                    "connecting" or "reconnecting" => Color.FromRgb(0x3B, 0x82, 0xF6),
                    "connected" => Color.FromRgb(0x22, 0xC5, 0x5E),
                    "error" => Color.FromRgb(0xEF, 0x44, 0x44),
                    _ => Color.FromRgb(0x33, 0x41, 0x55),
                };
                glow.Opacity = state == "off" ? 0.0 : 0.45;
            }

            if (XrayBtnPower?.Effect is DropShadowEffect xrayGlow)
            {
                xrayGlow.Color = state switch
                {
                    "connecting" => Color.FromRgb(0x3B, 0x82, 0xF6),
                    "connected" => Color.FromRgb(0x22, 0xC5, 0x5E),
                    "error" => Color.FromRgb(0xEF, 0x44, 0x44),
                    _ => Color.FromRgb(0x33, 0x41, 0x55),
                };
                xrayGlow.Opacity = state == "off" ? 0.0 : 0.35;
            }

            // کلید پاور کوچک بالای صفحه هم همان رنگ/درخشش را می‌گیرد تا همیشه مشخص باشد وصل است یا نه — بدون نیاز به بازکردن صفحه خانه
            if (HeaderPowerBtn != null)
            {
                HeaderPowerBtn.Foreground = brush;
                if (HeaderPowerBtn.Effect is DropShadowEffect headerGlow)
                {
                    headerGlow.Color = brush.Color;
                    headerGlow.Opacity = state == "off" ? 0.0 : 0.55;
                }
            }

            if (SidebarNavDashboardSubtext != null)
            {
                SidebarNavDashboardSubtext.Text = state switch
                {
                    "connected" => Localization.T("متصل"),
                    "connecting" or "reconnecting" => Localization.T("در حال اتصال..."),
                    _ => Localization.T("آماده اتصال")
                };
                SidebarNavDashboardSubtext.Foreground = state switch
                {
                    "connected" => (Brush)FindResource("Accent2Brush"),
                    "connecting" or "reconnecting" => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                    "error" => (Brush)FindResource("DangerBrush"),
                    _ => (Brush)FindResource("SubTextBrush")
                };
            }

            UpdateCoreSwitcherCards();
            UpdateKillProcsState();
        }

        public void UpdateKillProcsState()
        {
            bool isBusyOrConnected = _engine.IsRunning
                                  || _xrayIsConnected
                                  || XrayEngine.IsRunning
                                  || _currentPowerState is "connected" or "connecting" or "reconnecting";

            if (DashToolKillCard != null)
            {
                DashToolKillCard.IsEnabled = !isBusyOrConnected;
                DashToolKillCard.Opacity = isBusyOrConnected ? 0.38 : 1.0;
                DashToolKillCard.Cursor = isBusyOrConnected ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
                DashToolKillCard.ToolTip = isBusyOrConnected
                    ? Localization.T("در زمان اتصال فعال، امکان بستن پروسس‌ها وجود ندارد")
                    : Localization.T("بستن تمام پروسس‌های گیرکرده و ریست آداپتورها");
            }

            if (KillProcBtn != null)
            {
                KillProcBtn.IsEnabled = !isBusyOrConnected;
                KillProcBtn.Opacity = isBusyOrConnected ? 0.38 : 1.0;
                KillProcBtn.ToolTip = isBusyOrConnected
                    ? Localization.T("در زمان اتصال فعال، امکان بستن پروسس‌ها وجود ندارد")
                    : Localization.T("بستن پروسه‌های گیرکرده");
            }
        }

        private void SetLocked(bool locked)
        {
            AddBtn.IsEnabled = !locked;
            ImportBtn.IsEnabled = !locked;
            ExportBtn.IsEnabled = !locked;
            UpdateBaseBtn.IsEnabled = !locked;
            RenewIpBtn.IsEnabled = !locked;
            ProxyOffBtn.IsEnabled = !locked;
            ResetAdapterBtn.IsEnabled = !locked;
            PowerBtn.IsEnabled = true;
            UpdateKillProcsState();
        }


    
        public void SwitchToXrayBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowPanel("xray");
        }

        public void SwitchToVpnBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowPanel("home");
        }

        private void HomeVpnTile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SidebarNavVpnCore_Click(sender, new RoutedEventArgs());
        }

        private void HomeXrayTile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SidebarNavXrayCore_Click(sender, new RoutedEventArgs());
        }

        public void UpdateCoreSwitcherCards()
        {
            if (HomeVpnTile == null || HomeXrayTile == null) return;

            bool isVpnRunning = _engine.IsRunning;
            bool isXrayRunning = _xrayIsConnected || XrayEngine.IsRunning;
            bool isConnected = _currentPowerState == "connected" || isVpnRunning || isXrayRunning;
            bool isConnecting = _currentPowerState is "connecting" or "reconnecting";

            // Determine which core owns the active or selected connection
            bool isXray;
            if (_xrayIsConnected || XrayEngine.IsRunning)
            {
                isXray = true;
            }
            else if (_engine.IsRunning)
            {
                isXray = false;
            }
            else
            {
                string activeText = ActiveConnText?.Text ?? "";
                bool isVpnProfile = (_config.Connections != null && _config.Connections.Any(c => !string.IsNullOrEmpty(c.Name) && activeText.Contains(c.Name, StringComparison.OrdinalIgnoreCase)))
                                 || activeText.StartsWith("WireGuard", StringComparison.OrdinalIgnoreCase)
                                 || activeText.StartsWith("OpenVPN", StringComparison.OrdinalIgnoreCase)
                                 || activeText.StartsWith("Cisco", StringComparison.OrdinalIgnoreCase)
                                 || activeText.StartsWith("Shadowsocks", StringComparison.OrdinalIgnoreCase);

                bool isXrayProfile = (XrayProxies != null && XrayProxies.Any(x => !string.IsNullOrEmpty(x.Alias) && activeText.Equals(x.Alias, StringComparison.OrdinalIgnoreCase)))
                                  || (_xraySelectedProfile != null && activeText.Equals(_xraySelectedProfile.Alias, StringComparison.OrdinalIgnoreCase));

                if (isVpnProfile)
                {
                    isXray = false;
                }
                else if (isXrayProfile)
                {
                    isXray = true;
                }
                else
                {
                    isXray = MainWindow.LastConnectionType == "xray";
                }
            }

            var borderNeutral = (Brush)FindResource("GlassBorderBrush");
            var neutralBadgeBg = new SolidColorBrush(Color.FromArgb(0x15, 0x64, 0x74, 0x8B));
            var subtextBrush = (Brush)FindResource("SubTextBrush");

            bool isDark = App.IsDark;

            // VPN Core Accent Colors (Royal Blue in Dark, Sapphire Blue in Light)
            var vpnBorderAccent = isDark ? new SolidColorBrush(Color.FromArgb(0x75, 0x3B, 0x82, 0xF6)) : new SolidColorBrush(Color.FromArgb(0xA0, 0x25, 0x63, 0xEB));
            var vpnBadgeBg = isDark ? new SolidColorBrush(Color.FromArgb(0x28, 0x3B, 0x82, 0xF6)) : new SolidColorBrush(Color.FromArgb(0x20, 0x25, 0x63, 0xEB));
            var vpnTextAccent = isDark ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)) : new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));

            // Xray Core Accent Colors (Electric Purple in Dark, Vivid Purple in Light)
            var xrayBorderAccent = isDark ? new SolidColorBrush(Color.FromArgb(0x75, 0xA8, 0x55, 0xF7)) : new SolidColorBrush(Color.FromArgb(0xA0, 0x93, 0x33, 0xEA));
            var xrayBadgeBg = isDark ? new SolidColorBrush(Color.FromArgb(0x28, 0xA8, 0x55, 0xF7)) : new SolidColorBrush(Color.FromArgb(0x20, 0x93, 0x33, 0xEA));
            var xrayTextAccent = isDark ? new SolidColorBrush(Color.FromRgb(0xC0, 0x84, 0xFC)) : new SolidColorBrush(Color.FromRgb(0x7E, 0x22, 0xCE));

            // Dynamic binding to FieldBrush so cards always adapt seamlessly to dark/light theme
            HomeVpnTile.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
            HomeXrayTile.SetResourceReference(Border.BackgroundProperty, "FieldBrush");

            if (isXray)
            {
                // Xray is active/selected
                HomeXrayTile.BorderBrush = xrayBorderAccent;
                HomeXrayTile.BorderThickness = new Thickness(1.5);
                if (HomeXrayStatusBadge != null)
                {
                    HomeXrayStatusBadge.Background = xrayBadgeBg;
                    HomeXrayStatusBadge.BorderBrush = xrayBorderAccent;
                }
                if (HomeXrayStatusText != null)
                {
                    HomeXrayStatusText.Text = isConnected ? Localization.T("● متصل") :
                                              isConnecting ? Localization.T("در حال اتصال...") :
                                              Localization.T("● انتخاب‌شده");
                    HomeXrayStatusText.Foreground = xrayTextAccent;
                }

                // VPN Core is idle/inactive
                HomeVpnTile.BorderBrush = borderNeutral;
                HomeVpnTile.BorderThickness = new Thickness(1);
                if (HomeVpnStatusBadge != null)
                {
                    HomeVpnStatusBadge.Background = neutralBadgeBg;
                    HomeVpnStatusBadge.BorderBrush = borderNeutral;
                }
                if (HomeVpnStatusText != null)
                {
                    HomeVpnStatusText.Text = Localization.T("ورود →");
                    HomeVpnStatusText.Foreground = subtextBrush;
                }

                // Tier 1 Hero Card Badge
                if (ActiveCoreTypePill != null && ActiveCoreTypeText != null)
                {
                    ActiveCoreTypePill.Visibility = Visibility.Visible;
                    ActiveCoreTypeText.Text = "⚡ Xray Core";
                    ActiveCoreTypeText.Foreground = xrayTextAccent;
                    ActiveCoreTypePill.Background = xrayBadgeBg;
                    ActiveCoreTypePill.BorderBrush = xrayBorderAccent;
                }
            }
            else
            {
                // VPN Core is active/selected
                HomeVpnTile.BorderBrush = vpnBorderAccent;
                HomeVpnTile.BorderThickness = new Thickness(1.5);
                if (HomeVpnStatusBadge != null)
                {
                    HomeVpnStatusBadge.Background = vpnBadgeBg;
                    HomeVpnStatusBadge.BorderBrush = vpnBorderAccent;
                }
                if (HomeVpnStatusText != null)
                {
                    HomeVpnStatusText.Text = isConnected ? Localization.T("● متصل") :
                                            isConnecting ? Localization.T("در حال اتصال...") :
                                            Localization.T("● انتخاب‌شده");
                    HomeVpnStatusText.Foreground = vpnTextAccent;
                }

                // Xray Core is idle/inactive
                HomeXrayTile.BorderBrush = borderNeutral;
                HomeXrayTile.BorderThickness = new Thickness(1);
                if (HomeXrayStatusBadge != null)
                {
                    HomeXrayStatusBadge.Background = neutralBadgeBg;
                    HomeXrayStatusBadge.BorderBrush = borderNeutral;
                }
                if (HomeXrayStatusText != null)
                {
                    HomeXrayStatusText.Text = Localization.T("ورود →");
                    HomeXrayStatusText.Foreground = subtextBrush;
                }

                // Tier 1 Hero Card Badge
                if (ActiveCoreTypePill != null && ActiveCoreTypeText != null)
                {
                    ActiveCoreTypePill.Visibility = Visibility.Visible;
                    ActiveCoreTypeText.Text = "🛡️ VPN Core";
                    ActiveCoreTypeText.Foreground = vpnTextAccent;
                    ActiveCoreTypePill.Background = vpnBadgeBg;
                    ActiveCoreTypePill.BorderBrush = vpnBorderAccent;
                }
            }
        }

        public void ApplyDashboardBottomWidgetMode()
        {
            if (RecentServersCard == null) return;

            int mode = _config.DashboardBottomWidgetMode;
            if (mode < 0 || mode > 2) mode = 0;

            if (mode == 0)
            {
                if (RecentServersList != null) RecentServersList.Visibility = Visibility.Visible;
                if (DashSecurityControlsView != null) DashSecurityControlsView.Visibility = Visibility.Collapsed;
                if (DashQuickToolsView != null) DashQuickToolsView.Visibility = Visibility.Collapsed;

                if (DashBottomWidgetModeBadge != null) DashBottomWidgetModeBadge.Text = Localization.T("سرورها");
                if (RecentCardIcon != null) RecentCardIcon.Text = "⭐";
                if (RecentCardTitle != null) RecentCardTitle.Text = Localization.T("⭐ سرورهای منتخب و اخیر");
                if (RecentCardSubtitle != null) RecentCardSubtitle.Text = Localization.T("اتصال سریع با یک کلیک به آخرین کانکشن‌ها");

                RenderRecentServersList();
            }
            else if (mode == 1)
            {
                if (RecentServersList != null) RecentServersList.Visibility = Visibility.Collapsed;
                if (DashSecurityControlsView != null) DashSecurityControlsView.Visibility = Visibility.Visible;
                if (DashQuickToolsView != null) DashQuickToolsView.Visibility = Visibility.Collapsed;

                if (DashBottomWidgetModeBadge != null) DashBottomWidgetModeBadge.Text = Localization.T("امنیت");
                if (RecentCardIcon != null) RecentCardIcon.Text = "🛡️";
                if (RecentCardTitle != null) RecentCardTitle.Text = Localization.T("کنترل‌های سریع امنیتی");
                if (RecentCardSubtitle != null) RecentCardSubtitle.Text = Localization.T("وضعیت کیل‌سوئیچ، اسپلیت تانل و DNS");

                UpdateDashboardSecurityControls();
            }
            else // mode == 2
            {
                if (RecentServersList != null) RecentServersList.Visibility = Visibility.Collapsed;
                if (DashSecurityControlsView != null) DashSecurityControlsView.Visibility = Visibility.Collapsed;
                if (DashQuickToolsView != null) DashQuickToolsView.Visibility = Visibility.Visible;

                if (DashBottomWidgetModeBadge != null) DashBottomWidgetModeBadge.Text = Localization.T("ابزارها");
                if (RecentCardIcon != null) RecentCardIcon.Text = "🧰";
                if (RecentCardTitle != null) RecentCardTitle.Text = Localization.T("ابزارهای سریع شبکه");
                if (RecentCardSubtitle != null) RecentCardSubtitle.Text = Localization.T("فلاش DNS، بستن پروسس‌ها و تست سرعت");
            }
        }

        public void UpdateDashboardSecurityControls()
        {
            if (DashKsText != null && DashKsPill != null)
            {
                bool ks = _config.KillSwitchEnabled;
                DashKsText.Text = ks ? Localization.T("فعال") : Localization.T("غیرفعال");
                DashKsText.Foreground = ks ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)) : (Brush)FindResource("SubTextBrush");
                DashKsPill.Background = new SolidColorBrush(ks ? Color.FromArgb(0x25, 0x22, 0xC5, 0x5E) : Color.FromArgb(0x18, 0x64, 0x74, 0x8B));
            }

            if (DashStText != null && DashStPill != null)
            {
                bool stActive = _config.SplitTunnelMode != "off";
                DashStText.Text = stActive ? Localization.T("هوشمند") : Localization.T("کل ترافیک");
                DashStText.Foreground = stActive ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SubTextBrush");
                DashStPill.Background = new SolidColorBrush(stActive ? Color.FromArgb(0x25, 0x3B, 0x82, 0xF6) : Color.FromArgb(0x18, 0x64, 0x74, 0x8B));
            }

            if (DashDnsText != null && DashDnsPill != null)
            {
                bool customDns = _config.DnsMode != "auto";
                DashDnsText.Text = customDns ? _config.DnsMode.ToUpperInvariant() : Localization.T("خودکار");
                DashDnsText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                DashDnsPill.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x22, 0xC5, 0x5E));
            }
        }

        private void DashBottomWidgetCycle_Click(object sender, MouseButtonEventArgs e)
        {
            _config.DashboardBottomWidgetMode = (_config.DashboardBottomWidgetMode + 1) % 3;
            try { _config.Save(); } catch { }
            ApplyDashboardBottomWidgetMode();
        }

        private async void DashQuickKillSwitch_Click(object sender, MouseButtonEventArgs e)
        {
            _config.KillSwitchEnabled = !_config.KillSwitchEnabled;
            try { _config.Save(); } catch { }
            UpdateDashboardSecurityControls();
            if (_config.KillSwitchEnabled)
            {
                Notify(Localization.T("کیل‌سوئیچ فعال شد."));
            }
            else
            {
                await KillSwitch.DisableAsync();
                Notify(Localization.T("کیل‌سوئیچ غیرفعال شد."));
            }
        }

        private void DashQuickSplitTunnel_Click(object sender, MouseButtonEventArgs e)
        {
            ShowPanel("settings");
        }

        private void DashQuickDns_Click(object sender, MouseButtonEventArgs e)
        {
            ShowPanel("settings");
        }

        private async void DashToolFlushDns_Click(object sender, MouseButtonEventArgs e)
        {
            if (DashToolFlushCard == null || DashToolFlushIcon == null || DashToolFlushText == null) return;
            try
            {
                DashToolFlushIcon.Text = "⏳";
                AppendLog("> ipconfig /flushdns");
                await RunToolAsync("ipconfig", "/flushdns");

                // Visual confirmation checkmark
                DashToolFlushIcon.Text = "✓";
                DashToolFlushText.Text = Localization.T("انجام شد!");
                DashToolFlushText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                DashToolFlushCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x22, 0xC5, 0x5E));
                DashToolFlushCard.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x22, 0xC5, 0x5E));

                await Task.Delay(1800);
            }
            catch (Exception ex)
            {
                AppendLog("Flush DNS error: " + ex.Message);
            }
            finally
            {
                if (DashToolFlushIcon != null) DashToolFlushIcon.Text = "🧹";
                if (DashToolFlushText != null)
                {
                    DashToolFlushText.Text = Localization.T("فلاش DNS");
                    DashToolFlushText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                }
                if (DashToolFlushCard != null)
                {
                    DashToolFlushCard.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
                    DashToolFlushCard.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush");
                }
            }
        }

        private async void DashToolKillProcs_Click(object sender, MouseButtonEventArgs e)
        {
            if (DashToolKillCard == null || DashToolKillIcon == null || DashToolKillText == null) return;
            if (_engine.IsRunning || _xrayIsConnected || XrayEngine.IsRunning || _currentPowerState is "connected" or "connecting" or "reconnecting")
            {
                AppendLog(Localization.T("در زمان اتصال فعال، امکان بستن پروسس‌ها وجود ندارد"));
                Notify(Localization.T("در زمان اتصال فعال، امکان بستن پروسس‌ها وجود ندارد"));
                return;
            }
            try
            {
                DashToolKillIcon.Text = "⏳";
                AppendLog("Terminating all VPN and Core processes...");
                await TerminateAllVpnProcessesAndResetAdaptersAsync();
                AppendLog("✓ All VPN processes terminated and proxy disabled.");

                // Visual confirmation checkmark
                DashToolKillIcon.Text = "✓";
                DashToolKillText.Text = Localization.T("انجام شد!");
                DashToolKillText.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                DashToolKillCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0x22, 0xC5, 0x5E));
                DashToolKillCard.Background = new SolidColorBrush(Color.FromArgb(0x25, 0x22, 0xC5, 0x5E));

                await Task.Delay(1800);
            }
            catch (Exception ex)
            {
                AppendLog("Kill procs error: " + ex.Message);
            }
            finally
            {
                if (DashToolKillIcon != null) DashToolKillIcon.Text = "⚡";
                if (DashToolKillText != null)
                {
                    DashToolKillText.Text = Localization.T("کیل پروسس");
                    DashToolKillText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                }
                if (DashToolKillCard != null)
                {
                    DashToolKillCard.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
                    DashToolKillCard.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush");
                }
            }
        }

        private void DashToolSpeedTest_Click(object sender, MouseButtonEventArgs e)
        {
            ShowPanel("tools");
            OpenSpeedTestView();
        }
    }
}
