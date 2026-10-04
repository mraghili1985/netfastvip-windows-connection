using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVpn;

/// <summary>
/// سرویس sync پروفایل‌های اتصال از پورتال به کانکشن‌های برنامه
///
/// جریان کار:
///   1. پس از لاگین موفق در SubscriptionDialog ، SyncFromPortalAsync صدا بزن
///   2. لیست پروفایل‌ها از /api/connection-info/public دریافت می‌شود
///   3. فقط فرمت‌های ویندوز (ovpn + pbk) پردازش می‌شون
///   4. فایل هر پروفایل دانلود و parse می‌شود
///   5. VpnEngine از طریق HasInlineOvpn فایل رو روی دیسک می‌نویسه و استفاده می‌کنه
///
/// نکته: فایل‌های .ovpn پورتال همان فرمت base.ovpn هستند — فقط خطوط
/// remote و proto حذف می‌شن تا VpnEngine اینا رو از Server/Port/Proto بسازه دوباره اضافه کنه.
/// </summary>
public static class PortalSyncService
{
    public sealed class SyncResult
    {
        public int Added   { get; init; }
        public int Updated { get; init; }
        public int Removed { get; init; }
        public List<string> Errors { get; init; } = new();
    }

    /// <summary>
    /// سینک اصلی — پروفایل‌های پورتال را با config برنامه مرتب می‌کند.
    /// کانکشن‌های قدیمی (Source="official") دست‌نخورده می‌مانن.
    /// </summary>
    public static async Task<SyncResult> SyncFromPortalAsync(
        PortalApiClient client,
        AppConfig config,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var added   = 0;
        var updated = 0;
        var errors  = new List<string>();

        // 1. دریافت لیست پروفایل‌ها
        PortalConnectionsResult result;
        try   { result = await client.GetConnectionProfilesAsync(ct); }
        catch (Exception ex) { return new SyncResult { Errors = { $"خطا در دریافت لیست: {ex.Message}" } }; }

        // 2. فقط فرمت‌های ویندوز (ovpn + pbk)
        var windowsProfiles = result.Profiles
            .Where(p => p.IsWindowsCompatible ||
                        (string.Equals(p.Protocol, "sstp", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(p.Kind,     "link", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var removed = config.Connections.RemoveAll(c =>
            string.Equals(c.Source, "portal",   StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Source, "official", StringComparison.OrdinalIgnoreCase));

        foreach (var profile in windowsProfiles)
        {
            ct.ThrowIfCancellationRequested();
            string fileContent = string.Empty;
            // link-kind (SSTP) و pbk با description — نیازی به دانلود فایل نیست
            bool needsDownload = !string.Equals(profile.Kind, "link", StringComparison.OrdinalIgnoreCase)
                              && !HasDescriptionData(profile.Description);
            if (needsDownload)
            {
                try   { fileContent = await client.DownloadProfileFileAsync(profile.Url, ct); }
                catch (Exception ex) { errors.Add($"{profile.Label}: {ex.Message}"); continue; }
            }
            ConnectionProfile conn;
            try   { conn = BuildConnectionProfile(profile, fileContent, username, password); }
            catch (Exception ex) { errors.Add($"{profile.Label} (parse): {ex.Message}"); continue; }
            config.Connections.Add(conn);
            added++;
        }

        return new SyncResult { Added = added, Updated = updated, Removed = removed, Errors = errors };
    }

    /// <summary>
    /// سینک کانکشن‌ها از API جدید اپ مشتری (25-Customer-App-API)
    /// شامل پروفایل‌های OpenVPN، کانفیگ‌های WireGuard و لینک‌های ساب‌اسکریپشن V2Ray.
    /// </summary>
    public static async Task<SyncResult> SyncFromCustomerAppAsync(
        CustomerAppApiClient client,
        AppConfig config,
        CancellationToken ct = default)
    {
        var added = 0;
        var updated = 0;
        var errors = new List<string>();

        List<CustomerService> services;
        try
        {
            services = await client.GetServicesAsync(ct);
        }
        catch (Exception ex)
        {
            return new SyncResult { Errors = { $"خطا در دریافت لیست سرویس‌ها: {ex.Message}" } };
        }

        // حذف کانکشن‌های قبلی پورتال
        var removed = config.Connections.RemoveAll(c =>
            string.Equals(c.Source, "portal", StringComparison.OrdinalIgnoreCase));

        foreach (var svc in services)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var connData = await client.GetServiceConnectionAsync(svc.Id, ct);

                // ۱. کانکشن‌های OpenVPN
                if (connData.Radius?.Profiles != null)
                {
                    foreach (var p in connData.Radius.Profiles)
                    {
                        var prof = BuildFromRadiusProfile(p, connData.Radius.Username, connData.Radius.Password, svc.Name);
                        config.Connections.Add(prof);
                        added++;
                    }
                }

                // ۲. کانکشن WireGuard
                if (connData.WireGuard != null && !string.IsNullOrWhiteSpace(connData.WireGuard.Text))
                {
                    var wgProf = BuildFromWireGuardData(connData.WireGuard, svc.Name);
                    config.Connections.Add(wgProf);
                    added++;
                }

                // ۳. ساب‌اسکریپشن V2Ray
                var v2Sub = connData.V2Ray?.SubscriptionUrl ?? connData.WireGuard?.SubscriptionUrl;
                if (!string.IsNullOrWhiteSpace(v2Sub))
                {
                    try
                    {
                        var subUrl = v2Sub.Trim();
                        var (proxies, userInfo, suggestedTitle, err) = await SmartVpn.XrayCore.SubscriptionManager.FetchSubscriptionAsync(subUrl);
                        if (proxies != null && proxies.Count > 0)
                        {
                            var existingGroups = SmartVpn.XrayCore.SubscriptionGroupManager.LoadGroups();
                            var group = existingGroups.FirstOrDefault(g => string.Equals(g.Url, subUrl, StringComparison.OrdinalIgnoreCase));
                            if (group == null)
                            {
                                group = new SmartVpn.XrayCore.SubscriptionGroup
                                {
                                    Name = !string.IsNullOrWhiteSpace(suggestedTitle) ? suggestedTitle : $"اشتراک {svc.Name}",
                                    Url = subUrl
                                };
                                existingGroups.Add(group);
                            }
                            group.Profiles.Clear();
                            foreach (var p in proxies)
                            {
                                p.GroupId = group.Id;
                                p.GroupName = group.Name;
                                group.Profiles.Add(p);
                            }
                            if (userInfo != null)
                            {
                                group.DataRemaining = userInfo.GetRemainingDataString();
                                group.TotalData = userInfo.GetTotalDataString();
                                group.DaysRemaining = userInfo.GetExpireString();
                            }
                            group.LastUpdated = DateTime.Now;
                            SmartVpn.XrayCore.SubscriptionGroupManager.SaveGroups(existingGroups);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{svc.Name}: {ex.Message}");
            }
        }

        return new SyncResult { Added = added, Updated = updated, Removed = removed, Errors = errors };
    }

    private static ConnectionProfile BuildFromRadiusProfile(RadiusProfileItem p, string username, string password, string serviceName)
    {
        var remoteMatch = Regex.Match(p.Text,
            @"^remote\s+(\S+)\s+(\d+)(?:\s+(udp|tcp))?",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var server = remoteMatch.Success ? remoteMatch.Groups[1].Value : "";
        var port = remoteMatch.Success && int.TryParse(remoteMatch.Groups[2].Value, out var pt) ? (int?)pt : null;
        var proto = remoteMatch.Success && remoteMatch.Groups[3].Success ? remoteMatch.Groups[3].Value.ToLower() : "udp";
        if (Regex.IsMatch(p.Text, @"^proto\s+tcp", RegexOptions.Multiline | RegexOptions.IgnoreCase)) proto = "tcp";

        var nameMatch = Regex.Match(p.Text, @"setenv\s+FRIENDLY_NAME\s+""?([^""\r\n]+)""?", RegexOptions.IgnoreCase);
        var label = nameMatch.Success
            ? nameMatch.Groups[1].Value.Trim()
            : (!string.IsNullOrWhiteSpace(p.Label) ? p.Label : (!string.IsNullOrWhiteSpace(p.FileName) ? p.FileName : "OpenVPN"));

        return new ConnectionProfile
        {
            Name = label,
            Type = "openvpn",
            Server = server,
            Port = port,
            Proto = proto,
            OvpnInline = StripRemoteAndProto(p.Text),
            Username = username,
            Password = password,
            Psk = "",
            WireGuardConf = "",
            Source = "portal"
        };
    }

    private static ConnectionProfile BuildFromWireGuardData(WireGuardConnectionData wg, string serviceName)
    {
        var epMatch = Regex.Match(wg.Text, @"Endpoint\s*=\s*([^:\r\n]+)(?::(\d+))?", RegexOptions.IgnoreCase);
        var server = epMatch.Success ? epMatch.Groups[1].Value.Trim() : "";
        var port = epMatch.Success && int.TryParse(epMatch.Groups[2].Value, out var pt) ? (int?)pt : null;

        var name = !string.IsNullOrWhiteSpace(wg.FileName)
            ? wg.FileName.Replace(".conf", "", StringComparison.OrdinalIgnoreCase)
            : $"WireGuard - {serviceName}";

        return new ConnectionProfile
        {
            Name = name,
            Type = "wireguard",
            Server = server,
            Port = port,
            Proto = "udp",
            OvpnInline = "",
            WireGuardConf = wg.Text,
            Username = "",
            Password = "",
            Psk = "",
            Source = "portal"
        };
    }

    // ===== تبدیل PortalConnectionProfile به ConnectionProfile =====

    private static ConnectionProfile BuildConnectionProfile(
        PortalConnectionProfile portal,
        string fileContent,
        string username,
        string password)
    {
        // SSTP: kind="link", format="other" — url is "host:port"
        if (string.Equals(portal.Kind, "link", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(portal.Protocol, "sstp", StringComparison.OrdinalIgnoreCase))
            return BuildFromLink(portal, username, password);

        return portal.Format switch
        {
            "ovpn" => BuildFromOvpn(portal, fileContent, username, password),
            "pbk"  => BuildFromPbk(portal,  fileContent, username, password),
            _      => throw new NotSupportedException($"فرمت ناپشتیبانی: {portal.Format}")
        };
    }

    // ===== OpenVPN (.ovpn) =====

    private static ConnectionProfile BuildFromOvpn(
        PortalConnectionProfile portal,
        string ovpnContent,
        string username,
        string password)
    {
        // remote <host> <port> [udp|tcp] — برای پر کردن Server/Port/Proto
        var remoteMatch = Regex.Match(ovpnContent,
            @"^remote\s+(\S+)\s+(\d+)(?:\s+(udp|tcp))?",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var server = remoteMatch.Success ? remoteMatch.Groups[1].Value : "";
        var port   = remoteMatch.Success && int.TryParse(remoteMatch.Groups[2].Value, out var p) ? (int?)p : null;
        var proto  = remoteMatch.Success && remoteMatch.Groups[3].Success
            ? remoteMatch.Groups[3].Value.ToLower()
            : "udp";

        // برخی .ovpn از proto در خط جداگانه استفاده می‌کنن
        if (Regex.IsMatch(ovpnContent, @"^proto\s+tcp", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            proto = "tcp";

        return new ConnectionProfile
        {
            Name          = "NFV-" + portal.Label,
            Type          = "openvpn",
            Server        = server,
            Port          = port,
            Proto         = proto,
            // remote و proto حذف می‌شن — VpnEngine.CreateProvider اینا رو از Server/Port/Proto
            // به طور خودکار دوباره اضافه می‌کنه (OpenVpnProvider.ConnectAsync)
            OvpnInline    = StripRemoteAndProto(ovpnContent),
            Username      = username,
            Password      = password,
            Psk           = "",
            WireGuardConf = "",
            Source        = "portal",
        };
    }

    // ===== L2TP (.pbk = Windows Phone Book) =====

    private static ConnectionProfile BuildFromPbk(
        PortalConnectionProfile portal,
        string pbkContent,
        string username,
        string password)
    {
        string server, psk;
        if (HasDescriptionData(portal.Description))
        {
            // description: "server:hostname\r\nsecret:psk" (literal or real line endings)
            var srvM = Regex.Match(portal.Description!, @"server:([^\r\n\\]+)", RegexOptions.IgnoreCase);
            var secM = Regex.Match(portal.Description!, @"secret:([^\r\n\\]+)", RegexOptions.IgnoreCase);
            server = srvM.Success ? srvM.Groups[1].Value.Trim() : "";
            psk    = secM.Success ? secM.Groups[1].Value.Trim() : "";
        }
        else
        {
            var srvM = Regex.Match(pbkContent, @"PhoneNumber=([^\r\n]+)", RegexOptions.IgnoreCase);
            var secM = Regex.Match(pbkContent, @"IpsecPSK=([^\r\n]+)",    RegexOptions.IgnoreCase);
            server = srvM.Success ? srvM.Groups[1].Value.Trim() : "";
            psk    = secM.Success ? secM.Groups[1].Value.Trim() : "";
        }

        return new ConnectionProfile
        {
            Name          = "NFV-" + portal.Label,
            Type          = "l2tp",
            Server        = server,
            Port          = null,
            Proto         = "udp",
            OvpnInline    = "",
            WireGuardConf = "",
            Username      = username,
            Password      = password,
            Psk           = psk,
            Source        = "portal",
        };
    }

    private static bool HasDescriptionData(string? desc) =>
        !string.IsNullOrWhiteSpace(desc) && desc.Contains("server:", StringComparison.OrdinalIgnoreCase);

    // ===== SSTP (kind="link") — url is "host:port" =====
    private static ConnectionProfile BuildFromLink(
        PortalConnectionProfile portal,
        string username,
        string password)
    {
        // url format: "hostname:port" e.g. "nfst.netfast.vip:443"
        var url = portal.Url ?? string.Empty;
        var lastColon = url.LastIndexOf(':');
        string host; int? port = null;
        if (lastColon > 0 && int.TryParse(url.Substring(lastColon + 1), out var p))
        {
            host = url.Substring(0, lastColon);
            port = p;
        }
        else
        {
            host = url;
            port = 443;
        }
        return new ConnectionProfile
        {
            Name          = "NFV-" + portal.Label,
            Type          = "sstp",
            Server        = host,
            Port          = port,
            Proto         = "tcp",
            OvpnInline    = string.Empty,
            WireGuardConf = string.Empty,
            Username      = username,
            Password      = password,
            Psk           = string.Empty,
            Source        = "portal",
        };
    }

        // ===== Helper: حذف remote/proto/auth از .ovpn =====
    //
    // VpnEngine.CreateProvider این خطوط رو از ConnectionProfile.Server/Port/Proto
    // دوباره به اول فایل اضافه می‌کنه — پس duplicate نشه.
    private static string StripRemoteAndProto(string raw)
    {
        raw = raw.Replace("\r\n", "\n");
        // remote ... را حذف کن (همه خطوط)
        raw = Regex.Replace(raw,
            @"^remote\s+.*",
            "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // proto ... را حذف کن
        raw = Regex.Replace(raw,
            @"^proto\s+.*",
            "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // بلوک <auth-user-pass> ... </auth-user-pass> inline را حذف کن
        raw = Regex.Replace(raw,
            @"<auth-user-pass>[\s\S]*?</auth-user-pass>",
            "", RegexOptions.IgnoreCase);

        // auth-user-pass با فایل خارجی — حذف نام فایل، بقیه دستور حفظ شود
        raw = Regex.Replace(raw,
            @"^auth-user-pass[^\r\n]*\r?\n?",
            "", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // خطوط خالی اضافی
        raw = Regex.Replace(raw, @"\n{3,}", "\n\n");

        return raw.Trim();
    }
}
