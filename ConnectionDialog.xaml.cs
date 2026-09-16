using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SmartVpn;

public partial class ConnectionDialog : Window
{
    // ---- Persian strings isolated here ----
    private const string MsgBadOvpn = "فایل .ovpn معتبر نیست.";
    private const string MsgTapRejected = "پروفایل TAP پشتیبانی نمی‌شود.";
    private const string MsgNameRequired = "نام و آدرس سرور الزامی است.";

    public ConnectionProfile? Result { get; private set; }

    private readonly ConnectionProfile? _existing;
    private string _ovpnInline = "";

    public ConnectionDialog()
    {
        InitializeComponent();
        TypeCombo.SelectedIndex = 0;
        UpdateFieldVisibility();
    }

    public ConnectionDialog(ConnectionProfile existing) : this()
    {
        _existing = existing;
        _ovpnInline = existing.OvpnInline;
        NameBox.Text = existing.Name;
        ServerBox.Text = existing.Server;
        PortBox.Text = existing.Port?.ToString() ?? "";
        OverrideBox.Text = existing.ServerOverride;

        foreach (ComboBoxItem item in TypeCombo.Items)
            if ((string)item.Tag == existing.Type) { TypeCombo.SelectedItem = item; break; }
        foreach (ComboBoxItem item in ProtoCombo.Items)
            if ((string)item.Tag == existing.Proto) { ProtoCombo.SelectedItem = item; break; }

        // standard behavior: prefill real saved values (PasswordBox shows them masked automatically)
        UserBox.Text = existing.Username;
        PassBox.Password = existing.Password;
        PskBox.Password = existing.Psk;

        if (_ovpnInline.Trim().Length > 0)
            OvpnStatusText.Text = "embedded profile: yes";

        UpdateFieldVisibility();
    }

    private string SelectedType =>
        (TypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "openvpn";

    private void Type_Changed(object sender, SelectionChangedEventArgs e) => UpdateFieldVisibility();

    private void UpdateFieldVisibility()
    {
        if (OvpnPanel is null || PskPanel is null) return; // fires during InitializeComponent
        OvpnPanel.Visibility = SelectedType == "openvpn" ? Visibility.Visible : Visibility.Collapsed;
        PskPanel.Visibility = SelectedType == "l2tp" ? Visibility.Visible : Visibility.Collapsed;
        UpdateCredsEnabled();
    }

    // Cert-auth personal profiles (no auth-user-pass) do not need username/password
    private void UpdateCredsEnabled()
    {
        var certOnly = SelectedType == "openvpn"
            && _ovpnInline.Trim().Length > 0
            && !_ovpnInline.Contains("auth-user-pass");
        UserBox.IsEnabled = !certOnly;
        PassBox.IsEnabled = !certOnly;
    }

    private void ImportOvpn_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "OpenVPN profile (*.ovpn)|*.ovpn|All files (*.*)|*.*" };
        if (ofd.ShowDialog(this) != true) return;

        string norm;
        try { norm = File.ReadAllText(ofd.FileName).Replace("\r\n", "\n"); }
        catch { MessageBox.Show(this, MsgBadOvpn, AppConfig.BrandName); return; }

        var lines = norm.Split('\n');
        var hasClient = lines.Any(l => l.Trim() == "client");
        var hasRemote = lines.Any(l => l.TrimStart().StartsWith("remote "));
        if (!hasClient || !hasRemote)
        {
            MessageBox.Show(this, MsgBadOvpn, AppConfig.BrandName);
            return;
        }
        if (lines.Any(l => l.TrimStart().StartsWith("dev tap")))
        {
            MessageBox.Show(this, MsgTapRejected, AppConfig.BrandName);
            return;
        }

        // Parse first remote line: remote <host> [port] [proto]
        var host = "";
        var port = 0;
        var proto = "";
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (!t.StartsWith("remote ")) continue;
            var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) host = parts[1];
            if (parts.Length >= 3) int.TryParse(parts[2], out port);
            if (parts.Length >= 4) proto = parts[3].ToLowerInvariant();
            break;
        }
        if (proto.Length == 0)
        {
            var protoLine = lines.Select(l => l.Trim()).FirstOrDefault(t => t.StartsWith("proto "));
            if (protoLine is not null)
                proto = protoLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ElementAtOrDefault(1)?.ToLowerInvariant() ?? "";
        }

        // Strip remote/proto (engine re-adds them per endpoint) + unsafe directives
        var kept = lines.Where(l =>
        {
            var t = l.TrimStart();
            return !(t.StartsWith("remote ") || t.StartsWith("proto ")
                || t.StartsWith("up ") || t.StartsWith("down ")
                || t.StartsWith("script-security"));
        });
        _ovpnInline = string.Join(Environment.NewLine, kept);

        // Autofill
        if (NameBox.Text.Trim().Length == 0)
            NameBox.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
        if (host.Length > 0) ServerBox.Text = host;
        if (port > 0) PortBox.Text = port.ToString();
        if (proto.StartsWith("tcp")) SelectProto("tcp");
        else if (proto.StartsWith("udp")) SelectProto("udp");

        OvpnStatusText.Text = "embedded profile: " + Path.GetFileName(ofd.FileName);
        UpdateCredsEnabled();
    }

    private void SelectProto(string proto)
    {
        foreach (ComboBoxItem item in ProtoCombo.Items)
            if ((string)item.Tag == proto) { ProtoCombo.SelectedItem = item; return; }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var server = ServerBox.Text.Trim();
        if (name.Length == 0 || server.Length == 0)
        {
            MessageBox.Show(this, MsgNameRequired, AppConfig.BrandName);
            return;
        }

        int? port = null;
        if (int.TryParse(PortBox.Text.Trim(), out var pv) && pv > 0) port = pv;

        var p = new ConnectionProfile
        {
            Name = name,
            Type = SelectedType,
            Server = server,
            Port = port,
            Proto = (ProtoCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "udp",
            Source = _existing?.Source ?? "custom",
            OvpnInline = SelectedType == "openvpn" ? _ovpnInline : "",
            ServerOverride = SelectedType == "openvpn" ? OverrideBox.Text.Trim() : "",
        };

        // fields are pre-filled with real values in edit mode, so whatever's there gets saved as-is
        p.Username = UserBox.Text.Trim();
        p.Password = PassBox.Password;
        p.Psk = PskBox.Password;

        Result = p;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}