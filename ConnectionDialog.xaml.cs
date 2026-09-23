using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SmartVpn;

public partial class ConnectionDialog : Window
{
    private const string MsgBadOvpn     = "فایل .ovpn معتبر نیست.";
    private const string MsgTapRejected = "پروفایل TAP پشتیبانی نمی‌شود.";
    private const string MsgNameRequired = "نام الزامی است.";

    public ConnectionProfile? Result { get; private set; }

    private readonly ConnectionProfile? _existing;
    private string _ovpnInline = "";
    private string _wgConf    = "";

    public ConnectionDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Localization.Watch(this);
        TypeCombo.SelectedIndex = 0;
        UpdateFieldVisibility();
    }

    public ConnectionDialog(ConnectionProfile existing) : this()
    {
        _existing   = existing;
        _ovpnInline = existing.OvpnInline;
        _wgConf     = existing.WireGuardConf;

        NameBox.Text = existing.Name;

        foreach (ComboBoxItem item in TypeCombo.Items)
            if ((string)item.Tag == existing.Type) { TypeCombo.SelectedItem = item; break; }

        var isWg = existing.Type == "wireguard" || existing.Type == "amneziawg";

        if (isWg)
        {
            // پر کردن فیلدهای WG از کانفیگ ذخیره‌شده
            if (_wgConf.Trim().Length > 0)
            {
                WgStatusText.Text = Localization.T("کانفیگ: بارگذاری شده ✔");
                PopulateWgFields(_wgConf);
            }
        }
        else
        {
            ServerBox.Text   = existing.Server;
            PortBox.Text     = existing.Port?.ToString() ?? "";
            OverrideBox.Text = existing.ServerOverride;
            UserBox.Text     = existing.Username;
            PassBox.Password = existing.Password;
            PskBox.Password  = existing.Psk;

            foreach (ComboBoxItem item in ProtoCombo.Items)
                if ((string)item.Tag == existing.Proto) { ProtoCombo.SelectedItem = item; break; }

            if (_ovpnInline.Trim().Length > 0)
                OvpnStatusText.Text = Localization.T("embedded profile: yes");
        }

        UpdateFieldVisibility();
    }

    private string SelectedType =>
        (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "openvpn";

    private void Type_Changed(object sender, SelectionChangedEventArgs e) => UpdateFieldVisibility();

    private void UpdateFieldVisibility()
    {
        if (StandardPanel is null || WgPanel is null) return;

        var isWg = SelectedType == "wireguard" || SelectedType == "amneziawg";

        // StandardPanel — فقط برای غیر WireGuard
        StandardPanel.Visibility = isWg ? Visibility.Collapsed : Visibility.Visible;

        // WgPanel — فقط برای WireGuard / AmneziaWG
        WgPanel.Visibility = isWg ? Visibility.Visible : Visibility.Collapsed;

        if (!isWg)
        {
            OvpnPanel.Visibility = SelectedType == "openvpn" ? Visibility.Visible : Visibility.Collapsed;
            PskPanel.Visibility  = SelectedType == "l2tp"    ? Visibility.Visible : Visibility.Collapsed;
            UpdateCredsEnabled();
        }
    }

    private void UpdateCredsEnabled()
    {
        var certOnly = SelectedType == "openvpn"
            && _ovpnInline.Trim().Length > 0
            && !_ovpnInline.Contains("auth-user-pass");
        UserBox.IsEnabled = !certOnly;
        PassBox.IsEnabled = !certOnly;
    }

    // =========================================================
    //  WireGuard / AmneziaWG — import .conf و نمایش پارامترها
    // =========================================================

    private void AwgCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (AwgPanel is null) return;
        AwgPanel.Visibility = (AwgCheckBox.IsChecked == true)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string BuildWgConf()
    {
        var priv     = WgPrivateKeyBox.Text.Trim();
        var addr     = WgAddressBox.Text.Trim();
        var pubKey   = WgPublicKeyBox.Text.Trim();
        var endpoint = WgEndpointBox.Text.Trim();

        static bool Missing(string value) =>
            string.IsNullOrWhiteSpace(value) || value == "—";

        if (Missing(priv) || Missing(addr) || Missing(pubKey) || Missing(endpoint))
            return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {priv}");
        sb.AppendLine($"Address = {addr}");
        var dns = WgDnsBox.Text.Trim();
        if (dns.Length > 0 && dns != "—") sb.AppendLine($"DNS = {dns}");
        var mtu = WgMtuBox.Text.Trim();
        if (mtu.Length > 0 && mtu != "—") sb.AppendLine($"MTU = {mtu}");
        if (AwgCheckBox.IsChecked == true)
        {
            void A(string k, string v) { if (v.Length > 0 && v != "—") sb.AppendLine($"{k} = {v}"); }
            A("Jc",   AwgJcBox.Text.Trim());
            A("Jmin", AwgJminBox.Text.Trim());
            A("Jmax", AwgJmaxBox.Text.Trim());
            A("S1",   AwgS1Box.Text.Trim());
            A("S2",   AwgS2Box.Text.Trim());
            A("S3",   AwgS3Box.Text.Trim());
            A("S4",   AwgS4Box.Text.Trim());
            A("H1",   AwgH1Box.Text.Trim());
            A("H2",   AwgH2Box.Text.Trim());
            A("H3",   AwgH3Box.Text.Trim());
            A("H4",   AwgH4Box.Text.Trim());
            var extra = AwgExtraBox.Text.Trim();
            if (extra.Length > 0) sb.AppendLine(extra);
        }
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {pubKey}");

        // PresharedKey اختیاری است؛ بعضی کانفیگ‌های WireGuard آن را دارند
        // و بعضی ندارند. اگر خالی باشد، اصلاً این خط را تولید نمی‌کنیم.
        var presharedKey = WgPresharedKeyBox.Password.Trim();
        if (presharedKey.Length > 0 && presharedKey != "—")
            sb.AppendLine($"PresharedKey = {presharedKey}");

        sb.AppendLine($"Endpoint = {endpoint}");
        var ips = WgAllowedIPsBox.Text.Trim();
        sb.AppendLine($"AllowedIPs = {(ips.Length > 0 && ips != "—" ? ips : "0.0.0.0/0, ::/0")}");
        var kpa = WgKeepaliveBox.Text.Trim();
        if (kpa.Length > 0 && kpa != "—") sb.AppendLine($"PersistentKeepalive = {kpa}");
        return sb.ToString();
    }

    private void ImportWgConf_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "WireGuard / AmneziaWG config (*.conf)|*.conf|All files (*.*)|*.*",
            Title  = Localization.T("انتخاب فایل .conf"),
        };
        if (ofd.ShowDialog(this) != true) return;

        string conf;
        try { conf = File.ReadAllText(ofd.FileName).Replace("\r\n", "\n"); }
        catch { MessageBox.Show(this, Localization.T("فایل .conf معتبر نیست."), AppConfig.BrandName); return; }

        if (!conf.Contains("[Interface]") || !conf.Contains("[Peer]"))
        {
            MessageBox.Show(this, Localization.T("فایل .conf معتبر نیست (باید شامل [Interface] و [Peer] باشد)."), AppConfig.BrandName);
            return;
        }

        _wgConf = conf;

        if (NameBox.Text.Trim().Length == 0)
            NameBox.Text = Path.GetFileNameWithoutExtension(ofd.FileName);

        PopulateWgFields(conf);
        WgStatusText.Text = Localization.T("کانفیگ بارگذاری شد: ") + Path.GetFileName(ofd.FileName) + " ✔";
        WgFieldsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// تجزیه .conf و پر کردن تمام فیلدهای قابل نمایش
    /// </summary>
    private void PopulateWgFields(string conf)
    {
        // ساختار conf: [Interface] ... [Peer] ...
        var iface = ParseSection(conf, "[Interface]");
        var peer  = ParseSection(conf, "[Peer]");

        // ─── Interface ───
        // PrivateKey قبلاً در Import خوانده نمی‌شد و به همین دلیل
        // بعد از Import فرم آن را خالی نشان می‌داد.
        WgPrivateKeyBox.Text = iface.GetValueOrDefault("privatekey", "");
        WgAddressBox.Text    = iface.GetValueOrDefault("address",    "");
        WgDnsBox.Text        = iface.GetValueOrDefault("dns",        "");
        WgMtuBox.Text        = iface.GetValueOrDefault("mtu",        "");

        // ─── Peer ───
        WgEndpointBox.Text        = peer.GetValueOrDefault("endpoint",    "");
        WgPublicKeyBox.Text       = peer.GetValueOrDefault("publickey",   "");
        WgPresharedKeyBox.Password = peer.GetValueOrDefault("presharedkey", "");
        WgAllowedIPsBox.Text      = peer.GetValueOrDefault("allowedips",  "0.0.0.0/0, ::/0");

        var keepalive = peer.GetValueOrDefault("persistentkeepalive", "");
        if (keepalive.Length > 0)
        {
            WgKeepaliveLbl.Visibility = Visibility.Visible;
            WgKeepaliveBox.Visibility = Visibility.Visible;
            WgKeepaliveBox.Text = keepalive;
        }
        else
        {
            WgKeepaliveLbl.Visibility = Visibility.Collapsed;
            WgKeepaliveBox.Visibility = Visibility.Collapsed;
        }

        // ─── تشخیص AmneziaWG ───
        var isAmnezia = iface.ContainsKey("jc") || iface.ContainsKey("h1") ||
                        iface.ContainsKey("jmin") || conf.Contains("# amw");

        AwgPanel.Visibility = isAmnezia ? Visibility.Visible : Visibility.Collapsed;

        if (isAmnezia)
        {
            AwgJcBox.Text   = iface.GetValueOrDefault("jc",   "—");
            AwgJminBox.Text = iface.GetValueOrDefault("jmin", "—");
            AwgJmaxBox.Text = iface.GetValueOrDefault("jmax", "—");
            AwgS1Box.Text   = iface.GetValueOrDefault("s1",   "—");
            AwgS2Box.Text   = iface.GetValueOrDefault("s2",   "—");
            AwgS3Box.Text   = iface.GetValueOrDefault("s3",   "—");
            AwgS4Box.Text   = iface.GetValueOrDefault("s4",   "—");
            AwgH1Box.Text   = iface.GetValueOrDefault("h1",   "—");
            AwgH2Box.Text   = iface.GetValueOrDefault("h2",   "—");
            AwgH3Box.Text   = iface.GetValueOrDefault("h3",   "—");
            AwgH4Box.Text   = iface.GetValueOrDefault("h4",   "—");

            // سایر پارامترهای amnezia — هر چیزی بجز پارامترهای مشترک
            var knownKeys = new HashSet<string>
            {
                "privatekey","address","dns","mtu",
                "jc","jmin","jmax","s1","s2","s3","s4",
                "h1","h2","h3","h4"
            };
            var extras = new System.Text.StringBuilder();
            foreach (var kv in iface)
                if (!knownKeys.Contains(kv.Key))
                    extras.AppendLine($"{kv.Key} = {kv.Value}");

            AwgExtraBox.Text = extras.ToString().Trim();
        }

        WgFieldsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// تجزیه یک Section از فایل conf — کلید lowercase
    /// </summary>
    private static Dictionary<string, string> ParseSection(string conf, string header)
    {
        var result  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inSection = false;

        foreach (var rawLine in conf.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#')) continue;          // comment

            if (line.Equals(header, StringComparison.OrdinalIgnoreCase))
            { inSection = true; continue; }

            if (line.StartsWith('[') && inSection) break; // next section

            if (inSection && line.Contains('='))
            {
                var eq  = line.IndexOf('=');
                var key = line[..eq].Trim().ToLowerInvariant();
                var val = line[(eq + 1)..].Trim();
                result[key] = val;
            }
        }
        return result;
    }

    // =========================================================
    //  OpenVPN import
    // =========================================================

    private void ImportOvpn_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "OpenVPN profile (*.ovpn)|*.ovpn|All files (*.*)|*.*" };
        if (ofd.ShowDialog(this) != true) return;

        string norm;
        try { norm = File.ReadAllText(ofd.FileName).Replace("\r\n", "\n"); }
        catch { MessageBox.Show(this, Localization.T(MsgBadOvpn), AppConfig.BrandName); return; }

        var lines = norm.Split('\n');
        if (!lines.Any(l => l.Trim() == "client") || !lines.Any(l => l.TrimStart().StartsWith("remote ")))
        { MessageBox.Show(this, Localization.T(MsgBadOvpn), AppConfig.BrandName); return; }
        if (lines.Any(l => l.TrimStart().StartsWith("dev tap")))
        { MessageBox.Show(this, Localization.T(MsgTapRejected), AppConfig.BrandName); return; }

        var host = ""; var port = 0; var proto = "";
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (!t.StartsWith("remote ")) continue;
            var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) host  = parts[1];
            if (parts.Length >= 3) int.TryParse(parts[2], out port);
            if (parts.Length >= 4) proto = parts[3].ToLowerInvariant();
            break;
        }
        if (proto.Length == 0)
        {
            var pl = lines.Select(l => l.Trim()).FirstOrDefault(t => t.StartsWith("proto "));
            if (pl is not null)
                proto = pl.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1)?.ToLowerInvariant() ?? "";
        }

        var kept = lines.Where(l =>
        {
            var t = l.TrimStart();
            return !(t.StartsWith("remote ") || t.StartsWith("proto ")
                || t.StartsWith("up ") || t.StartsWith("down ")
                || t.StartsWith("script-security"));
        });
        _ovpnInline = string.Join(Environment.NewLine, kept);

        if (NameBox.Text.Trim().Length == 0)
            NameBox.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
        if (host.Length > 0) ServerBox.Text = host;
        if (port > 0) PortBox.Text = port.ToString();
        if (proto.StartsWith("tcp")) SelectProto("tcp");
        else if (proto.StartsWith("udp")) SelectProto("udp");

        OvpnStatusText.Text = Localization.T("embedded profile: ") + Path.GetFileName(ofd.FileName);
        UpdateCredsEnabled();
    }

    private void SelectProto(string proto)
    {
        foreach (ComboBoxItem item in ProtoCombo.Items)
            if ((string)item.Tag == proto) { ProtoCombo.SelectedItem = item; return; }
    }

    // =========================================================
    //  Ok / Cancel
    // =========================================================

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        { MessageBox.Show(this, Localization.T(MsgNameRequired), AppConfig.BrandName); return; }

        var isWg = SelectedType == "wireguard" || SelectedType == "amneziawg";

        if (isWg)
        {
            // همیشه از مقادیر فعلی فرم کانفیگ را بساز؛
            // تا تغییر Preshared Key یا سایر فیلدها بعد از Import هم ذخیره شود.
            _wgConf = BuildWgConf();
            if (_wgConf.Trim().Length == 0)
            { MessageBox.Show(this, Localization.T("لطفاً Private Key، Address، Public Key و Endpoint را پر کنید."), AppConfig.BrandName); return; }

            // Server/Port را از Endpoint در conf می‌خوانیم
            var ep = WgEndpointBox.Text.Trim();
            var server = "";
            int? port  = null;
            if (ep != "—" && ep.Length > 0)
            {
                var lastColon = ep.LastIndexOf(':');
                if (lastColon > 0)
                {
                    server = ep[..lastColon];
                    if (int.TryParse(ep[(lastColon + 1)..], out var pv) && pv > 0) port = pv;
                }
                else server = ep;
            }

            // تشخیص نوع واقعی از روی محتوای .conf
            var realType = AwgPanel.Visibility == Visibility.Visible ? "amneziawg" : "wireguard";

            Result = new ConnectionProfile
            {
                Name          = name,
                Type          = realType,
                Server        = server,
                Port          = port,
                Proto         = "udp",
                Source        = _existing?.Source ?? "custom",
                WireGuardConf = _wgConf,
                OvpnInline    = "",
                Username      = "",
                Password      = "",
                Psk           = "",
            };
        }
        else
        {
            var server = ServerBox.Text.Trim();
            if (server.Length == 0)
            { MessageBox.Show(this, Localization.T("نام و آدرس سرور الزامی است."), AppConfig.BrandName); return; }

            int? port = null;
            if (int.TryParse(PortBox.Text.Trim(), out var pv) && pv > 0) port = pv;

            Result = new ConnectionProfile
            {
                Name           = name,
                Type           = SelectedType,
                Server         = server,
                Port           = port,
                Proto          = (ProtoCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "udp",
                Source         = _existing?.Source ?? "custom",
                OvpnInline     = SelectedType == "openvpn" ? _ovpnInline : "",
                ServerOverride = SelectedType == "openvpn" ? OverrideBox.Text.Trim() : "",
                WireGuardConf  = "",
                Username       = UserBox.Text.Trim(),
                Password       = PassBox.Password,
                Psk            = PskBox.Password,
            };
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
