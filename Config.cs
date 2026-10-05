using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class AppConfig
{
    public bool SmartSwitch { get; set; }
    public CredentialsConfig? Credentials { get; set; } // legacy, migrated on load
    [JsonIgnore]
    public List<ConnectionProfile> Connections { get; set; } = [];
    public long TotalDownloadBytes { get; set; }
    public long TotalUploadBytes { get; set; }

    // --- New in step 11.6 ---
    public string Theme { get; set; } = "system"; // dark | light | system (پیش‌فرض: تم سیستم)
    public string Language { get; set; } = "fa"; // fa | en
    public string Font { get; set; } = "segoe"; // segoe | vazir
	public string SplitTunnelMode { get; set; } = "off"; // off | deny | allow
	public List<string> SplitTunnelList { get; set; } = [];
    public string BaseOvpn { get; set; } = "";    // base.ovpn content kept inside config (self-healing)
    public int PackageVersion { get; set; }       // version of last imported official package
    public bool MinimizeToTray { get; set; }
    public bool KillSwitchEnabled { get; set; } = false; // Kill Switch (WFP) — پیش‌فرض خاموش (از تنظیمات قابل روشن‌شدن است)

    // آخرین کانکشنی که واقعاً وصل شده — بعد از بستن/باز کردن برنامه همین انتخاب‌شده نمایش داده می‌شود
    public string? LastConnectedName { get; set; }
    public List<string> RecentConnections { get; set; } = [];

    public void AddRecentConnection(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        RecentConnections.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        RecentConnections.Insert(0, name);
        if (RecentConnections.Count > 10)
            RecentConnections = RecentConnections.Take(10).ToList();
        try { Save(); } catch { }
    }
    // آیا لیست کانکشن‌ها (دکمه «نمایش بیشتر») باز مانده — رفتار آخرین باری که کاربر انتخاب کرده حفظ می‌شود
    public bool ConnListExpanded { get; set; }
    public bool SidebarExpanded { get; set; } = false; // باز یا بسته بودن سایدبار منو (پیش‌فرض بسته)
    public bool HomeDetailsExpanded { get; set; } = true; // باز یا بسته بودن تایل جزئیات شبکه (پیش‌فرض باز)
    public bool ShowRecentServersOnHome { get; set; } = true; // نمایش بخش سرورهای اخیر در داشبورد
    public int DashboardBottomWidgetMode { get; set; } = 0; // 0 = Recent Servers, 1 = Quick Security Controls, 2 = Quick Diagnostics & Tools

    // --- New: custom DNS (برای قابلیت DNS دلخواه در تنظیمات) ---
    public string DnsMode { get; set; } = "auto";      // auto | cloudflare | google | custom
    public string DnsPrimary { get; set; } = "";
    public string DnsSecondary { get; set; } = "";

    // ===== تنظیمات پنل اصلی پروداکشن (NETFASTVIP) - محفوظ برای بازگردانی =====
    public const string DefaultProductionPortalApiUrl = "https://panel.netfast.vip/";
    public const string DefaultProductionPanelUrl = "https://panel.netfast.vip/portal/login";
    public const string DefaultProductionConnectionsUrl = "https://dl.netfast.vip/connections.json";
    public const string DefaultProductionUpdateUrl = "https://dl.netfast.vip/version.json";
    public const string DefaultProductionTelegramUrl = "https://t.me/netfastvip";
    public const string DefaultProductionSupportUrl = "https://t.me/nfv_sup";

    // ===== تنظیمات پنل تستی اپ مشتری (v2moon.shop) =====
    public const string DefaultTestCustomerApiUrl = "https://panel.v2moon.shop";
    public const string DefaultTestPanelUrl = "https://panel.v2moon.shop";

    // آدرس کانال تلگرام — از config.json قابل تغییر است، بدون نیاز به بیلد مجدد
    public string TelegramUrl { get; set; } = DefaultProductionTelegramUrl;

    // آدرس ریپازیتوری یا لینک گیت‌هاب برنامه — قابل تغییر از config.json
    public const string DefaultProductionGithubUrl = "https://github.com/mraghili1985/netfastvip-windows-connection";
    public string GithubUrl { get; set; } = DefaultProductionGithubUrl;

    // لینک پنل کاربری/اکانتینگ برای بررسی اشتراک (دکمه در پنجره درباره)
    public string PanelUrl { get; set; } = DefaultTestPanelUrl;

    // لینک پشتیبانی تلگرام (دکمه Support در پنجره درباره)
    public string SupportUrl { get; set; } = DefaultProductionSupportUrl;

    // آدرس فایل version.json روی هاست برای بررسی نسخه جدید — خالی یعنی بررسی خاموش (UpdateChecker.cs)
    public string UpdateUrl { get; set; } = DefaultProductionUpdateUrl;

    // آدرس فایل connections.json روی هاست برای دریافت خودکار سرورها/base.ovpn/گواهی CA — خالی یعنی بررسی خاموش (ConnectionsUpdateChecker.cs)
    public string ConnectionsUpdateUrl { get; set; } = DefaultProductionConnectionsUrl;

    // محتوای گواهی CA به‌صورت Base64 — داخل کانفیگ نگه داشته می‌شود (self-healing مثل BaseOvpn)
    public string Ca { get; set; } = "";

    // آدرس پایه پورتال و API اپ — اکنون روی سرور تستی تنظیم شده و با SwitchToProductionPanel به پروداکشن برمی‌گردد
    public string PortalApiUrl { get; set; } = DefaultTestCustomerApiUrl;

    // نشست و احراز هویت اپ مشتری (25-Customer-App-API)
    public string? CustomerAccessToken { get; set; }
    public string? CustomerRefreshToken { get; set; }
    public DateTime? CustomerTokenExpiresAt { get; set; }
    public string? CustomerDeviceId { get; set; }
    public string? CustomerUsername { get; set; }

    [JsonIgnore]
    public string? CustomerPassword { get; set; }
    [JsonPropertyName("CustomerPassword")]
    public string? StoredCustomerPassword
    {
        get => string.IsNullOrWhiteSpace(CustomerPassword) ? null : CredentialProtector.Protect(CustomerPassword);
        set => CustomerPassword = string.IsNullOrWhiteSpace(value) ? null : CredentialProtector.Unprotect(value);
    }

    public string? CustomerPackageName { get; set; }
    public string? CustomerRemainingTime { get; set; }
    public string? CustomerRemainingTraffic { get; set; }
    public string? CustomerStatus { get; set; }
    public string? CustomerLoginMode { get; set; }

    // متدهای سوییچ بین محیط تستی و پروداکشن
    public void SwitchToProductionPanel()
    {
        PortalApiUrl = DefaultProductionPortalApiUrl;
        PanelUrl = DefaultProductionPanelUrl;
        ConnectionsUpdateUrl = DefaultProductionConnectionsUrl;
        UpdateUrl = DefaultProductionUpdateUrl;
        Save();
    }

    public void SwitchToTestPanel()
    {
        PortalApiUrl = DefaultTestCustomerApiUrl;
        PanelUrl = DefaultTestPanelUrl;
        Save();
    }

    // قطع خودکار پس از این تعداد دقیقه بی‌استفادگی — ۰ یعنی همیشه متصل
    public int IdleDisconnectMinutes { get; set; }

    // --- برندینگ (وایت‌لیبل) — از config.json بدون بیلد مجدد قابل تغییر ---
    // نام نمایشی برنامه: عنوان پنجره‌ها، آیکون تری، دیالوگ‌ها و میان‌بر دسکتاپ
    public string AppName { get; set; } = "NETFASTVIP";

    // نسخه نمایشی در پنجره «درباره» — خالی یعنی خودکار از نسخه خود EXE
    public string AppVersion { get; set; } = "";

    // دسترسی سریع برای پنجره‌هایی که config را لود نمی‌کنند (یک بار خوانده و کش می‌شود)
    private static string? _brandName;
    private static string? _brandVersion;
    public static string BrandName { get { EnsureBrand(); return _brandName!; } }
    public static string BrandVersion { get { EnsureBrand(); return _brandVersion!; } }
    private static void EnsureBrand()
    {
        if (_brandName is not null) return;
        string name = "NETFASTVIP", ver = "";
        try
        {
            var cfg = Load();
            if (!string.IsNullOrWhiteSpace(cfg.AppName)) name = cfg.AppName.Trim();
            ver = cfg.AppVersion.Trim();
        }
        catch { }
        if (ver.Length == 0)
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            ver = v is null ? "—" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
        _brandName = name;
        _brandVersion = ver;
    }

    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "Data", "app-config.json");

    public static string LegacyPath =>
        Path.Combine(AppContext.BaseDirectory, "Data", "config.json");

    public static AppConfig Load(string? path = null)
    {
        var requestedPath = path;
        path ??= DefaultPath;
        var sourcePath = File.Exists(path) ? path : requestedPath is null ? LegacyPath : path;
        AppConfig cfg;
        string? legacyConnectionsJson = null;
        if (!File.Exists(sourcePath))
        {
            cfg = new AppConfig();
        }
        else
        {
            var raw = File.ReadAllText(sourcePath);
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            };
            cfg = JsonSerializer.Deserialize<AppConfig>(raw, opts)
                  ?? new AppConfig();

            try
            {
                using var document = JsonDocument.Parse(raw);
                if (document.RootElement.TryGetProperty("Connections", out var connections))
                    legacyConnectionsJson = connections.GetRawText();
            }
            catch { }
        }

        var profilePathExists = File.Exists(ConnectionProfileStore.DefaultPath);
        cfg.Connections = profilePathExists
            ? ConnectionProfileStore.Load()
            : legacyConnectionsJson is null
                ? []
                : JsonSerializer.Deserialize<List<ConnectionProfile>>(legacyConnectionsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        // Migration 1: old global credentials -> per-connection (from step 10.8)
        if (cfg.Credentials is { } legacy)
        {
            foreach (var c in cfg.Connections)
            {
                if (c.Username.Length == 0 && legacy.Username.Length > 0)
                {
                    c.Username = legacy.Username;
                    c.Password = legacy.Password;
                }
                if (c.Type == "l2tp" && c.Psk.Length == 0 && legacy.L2tpPsk.Length > 0)
                    c.Psk = legacy.L2tpPsk;
            }
            cfg.Credentials = null;
        }

        // Migration 2: profiles saved before Source existed -> custom
        foreach (var c in cfg.Connections)
            if (string.IsNullOrWhiteSpace(c.Source))
                c.Source = "custom";

        // Older configs did not contain the host URL, so keep automatic server updates enabled.
        if (string.IsNullOrWhiteSpace(cfg.ConnectionsUpdateUrl))
            cfg.ConnectionsUpdateUrl = "https://dl.netfast.vip/connections.json";

        // One-time migration: preserve the old file, then write the split format.
        if (requestedPath is null && !profilePathExists && legacyConnectionsJson is not null)
        {
            try { cfg.Save(); } catch { }
        }

        return cfg;
    }

    public void Save(string? path = null)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
        File.WriteAllText(path ?? DefaultPath, JsonSerializer.Serialize(this, opts));
        ConnectionProfileStore.Save(Connections);
    }

    // Merge an official package: add/update official connections by name,
    // keep user credentials, never touch custom profiles.
    // هر سرور رسمی (Source == "official") که دیگر داخل پکیج جدید نیست هم حذف می‌شود —
    // قبلاً اگر از روی هاست یک سرور حذف می‌شد، اینجا همیشه توی لیست کانکشن‌های کاربر می‌ماند (باگ).
    // پروفایل سفارشی (Source == "custom") هرگز دست نمی‌خورند.
    public void ApplyOfficialPackage(PackageFile pkg)
    {
        foreach (var incoming in pkg.Connections)
        {
            incoming.Source = "official";
            var existing = Connections.FirstOrDefault(c => c.Name == incoming.Name);
            if (existing is null)
            {
                Connections.Add(incoming);
                continue;
            }
            existing.Source = "official";
            existing.Type = incoming.Type;
            existing.Server = incoming.Server;
            existing.Port = incoming.Port;
            existing.Proto = incoming.Proto;
            if (incoming.Psk.Length > 0) existing.Psk = incoming.Psk;
            // existing.Username / existing.Password stay untouched
        }

        // حذف سرورهای رسمی قدیمی که دیگر توی پکیج جدید نیستند (یعنی از هاست حذف شده‌اند)
        var incomingNames = new HashSet<string>(pkg.Connections.Select(c => c.Name));
        Connections.RemoveAll(c => c.Source == "official" && !incomingNames.Contains(c.Name));

        if (pkg.BaseOvpn.Trim().Length > 0) BaseOvpn = pkg.BaseOvpn;
        if (pkg.Ca.Trim().Length > 0) Ca = pkg.Ca;
        if (pkg.PackageVersion > PackageVersion) PackageVersion = pkg.PackageVersion;
    }

    // Make sure base.ovpn exists on disk; rebuild it from config when missing.
    // Also adopts an existing file into config the first time (anti-loss).
    public bool EnsureBaseOvpnFile()
    {
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "base.ovpn");
        if (File.Exists(path))
        {
            if (BaseOvpn.Trim().Length == 0)
                BaseOvpn = File.ReadAllText(path);
            return true;
        }
        if (BaseOvpn.Trim().Length == 0) return false;
        File.WriteAllText(path, BaseOvpn);
        return true;
    }

    // مطمئن می‌شود ca.crt کنار exe وجود دارد و آخرین نسخه‌ی دریافتی از هاست را دارد.
    // مثل EnsureBaseOvpnFile: اگر Ca خالی باشد ولی فایل روی دیسک باشد، همان فایل پذیرفته می‌شود (anti-loss).
    // نصب واقعی در Trusted Root همچنان فقط توسط CaInstaller و با اجازه‌ی کاربر هنگام اتصال SSTP/IKEv2 انجام می‌شود؛
    // این متد فقط فایل روی دیسک را به‌روز نگه می‌دارد تا CaInstaller.IsInstalled() تشخیص اختلاف بدهد.
    public bool EnsureCaFile()
    {
        var path = CaInstaller.CaPath;
        if (Ca.Trim().Length == 0)
        {
            if (File.Exists(path))
            {
                try { Ca = Convert.ToBase64String(File.ReadAllBytes(path)); } catch { }
            }
            return File.Exists(path);
        }
        try
        {
            var bytes = Convert.FromBase64String(Ca.Trim());
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == bytes.Length && existing.SequenceEqual(bytes))
                    return true; // بدون تغییر — نیازی به نصب مجدد نیست
            }
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch { return false; }
    }
}

public sealed class ConnectionProfile
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "openvpn"; // openvpn | l2tp | pptp | sstp | ikev2
    public string Server { get; set; } = "";
    public int? Port { get; set; }
    public string Proto { get; set; } = "udp";    // openvpn only: udp | tcp
    [JsonIgnore]
    public string Psk { get; set; } = "";         // l2tp only
    [JsonPropertyName("Psk")]
    public string StoredPsk { get => CredentialProtector.Protect(Psk); set => Psk = CredentialProtector.Unprotect(value); }

    [JsonIgnore]
    public string Username { get; set; } = "";
    [JsonPropertyName("Username")]
    public string StoredUsername { get => CredentialProtector.Protect(Username); set => Username = CredentialProtector.Unprotect(value); }

    [JsonIgnore]
    public string Password { get; set; } = "";
    [JsonPropertyName("Password")]
    public string StoredPassword { get => CredentialProtector.Protect(Password); set => Password = CredentialProtector.Unprotect(value); }

    // --- New in step 11.6 ---
    public string Source { get; set; } = "custom"; // official | custom
    [JsonIgnore]
    public string OvpnInline { get; set; } = "";   // personal .ovpn content (custom openvpn only)
    [JsonPropertyName("OvpnInline")]
    public string StoredOvpnInline { get => CredentialProtector.Protect(OvpnInline); set => OvpnInline = CredentialProtector.Unprotect(value); }

    // Server Override — در صورت تنظیم، اتصال حتماً به این آدرس انجام می‌شود (مثل OpenVPN Connect)
    public string ServerOverride { get; set; } = "";

    // ===== WireGuard / AmneziaWG =====
    // محتوای فایل .conf — برای type=wireguard و type=amneziawg
    [JsonIgnore]
    public string WireGuardConf { get; set; } = "";
    [JsonPropertyName("WireGuardConf")]
    public string StoredWireGuardConf { get => CredentialProtector.Protect(WireGuardConf); set => WireGuardConf = CredentialProtector.Unprotect(value); }

    [JsonIgnore]
    public string EffectiveServer =>
        string.IsNullOrWhiteSpace(ServerOverride) ? Server : ServerOverride.Trim();

    [JsonIgnore] public bool IsOfficial => Source == "official";
    [JsonIgnore] public bool HasInlineOvpn => OvpnInline.Trim().Length > 0;
    [JsonIgnore] public bool HasWireGuardConf => WireGuardConf.Trim().Length > 0;

    // Cert-only personal profiles (no auth-user-pass) must not trigger the credentials popup.
    [JsonIgnore]
    public bool NeedsCredentials =>
        Type != "openvpn" || !HasInlineOvpn || OvpnInline.Contains("auth-user-pass");

    [JsonIgnore]
    public string Subtitle
    {
        get
        {
            // نمایش زیر نام کانکشن: آدرس + پورت (بدون پروتکل — نام کانکشن خودش گویاست)
            var server = Server.Length > 0 ? Server : "-";
            var port = Port is int p ? $"   Port: {p}" : "";
            return $"{server}{port}";
        }
    }

    // فقط آدرس سرور بدون پورت — برای کارت اینفوی بالا (پورت به کادر Protocol منتقل شد تا خلوت‌تر باشد)
    [JsonIgnore]
    public string ServerLine => Server.Length > 0 ? Server : "-";

    public ConnectionProfile CloneWithCreds(string username, string password) => new()
    {
        Name = Name,
        Type = Type,
        Server = Server,
        Port = Port,
        Proto = Proto,
        Psk = Psk,
        Username = username,
        Password = password,
        Source = Source,
        OvpnInline = OvpnInline,
        WireGuardConf = WireGuardConf,
    };
}

public sealed class CredentialsConfig
{
    [JsonIgnore]
    public string Username { get; set; } = "";
    [JsonPropertyName("Username")]
    public string StoredUsername { get => CredentialProtector.Protect(Username); set => Username = CredentialProtector.Unprotect(value); }

    [JsonIgnore]
    public string Password { get; set; } = "";
    [JsonPropertyName("Password")]
    public string StoredPassword { get => CredentialProtector.Protect(Password); set => Password = CredentialProtector.Unprotect(value); }

    [JsonIgnore]
    public string L2tpPsk { get; set; } = "";
    [JsonPropertyName("L2tpPsk")]
    public string StoredL2tpPsk { get => CredentialProtector.Protect(L2tpPsk); set => L2tpPsk = CredentialProtector.Unprotect(value); }
}

public sealed class ProtocolConfig
{
    public string Type { get; set; } = "";
    public string Kind { get; set; } = "vpn";
    public bool Enabled { get; set; }
    public int? Port { get; set; }
    public string? Proto { get; set; }
    public string? Core { get; set; }
    public bool Warn { get; set; }
}

public sealed class OpenVpnEndpoint
{
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public string Proto { get; set; } = "tcp"; // tcp | udp
}

public sealed class PackageFile
{
    public int PackageVersion { get; set; } = 1;
    public string Name { get; set; } = "NFV";
    public List<ConnectionProfile> Connections { get; set; } = [];
    public string BaseOvpn { get; set; } = "";
    public string Ca { get; set; } = ""; // گواهی CA به‌صورت Base64 — اختیاری (فقط برای SSTP/IKEv2)
}
