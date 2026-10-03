using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartVpn.XrayCore
{
    public static class UrlParser
    {
        public static ProxyProfile Parse(string url)
        {
            url = url.Trim();
            if (string.IsNullOrEmpty(url)) throw new ArgumentException("URL is empty");

            if (url.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Split('='))
                               .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

                return new ProxyProfile
                {
                    Protocol = "VLESS",
                    Alias = Uri.UnescapeDataString(uri.Fragment.TrimStart('#')),
                    Address = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 443,
                    UserId = uri.UserInfo,
                    Network = query.ContainsKey("type") ? query["type"] : "tcp",
                    Security = query.ContainsKey("encryption") ? query["encryption"] : "none",
                    Flow = query.ContainsKey("flow") ? query["flow"] : "",
                    Tls = query.ContainsKey("security") ? query["security"] : "none",
                    Sni = query.ContainsKey("sni") ? query["sni"] : "",
                    Alpn = query.ContainsKey("alpn") ? query["alpn"] : "",
                    Fingerprint = query.ContainsKey("fp") ? query["fp"] : "",
                    PublicKey = query.ContainsKey("pbk") ? query["pbk"] : "",
                    ShortId = query.ContainsKey("sid") ? query["sid"] : "",
                    SpiderX = query.ContainsKey("spx") ? query["spx"] : "",
                    Path = query.ContainsKey("path") ? query["path"] : "",
                    Host = query.ContainsKey("host") ? query["host"] : "",
                    ServiceName = query.ContainsKey("serviceName") ? query["serviceName"] : "",
                    FullUrl = url
                };
            }
            else if (url.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
            {
                string b64 = url.Substring(8).Trim();
                int mod4 = b64.Length % 4;
                if (mod4 > 0) b64 += new string('=', 4 - mod4);
                byte[] bytes = Convert.FromBase64String(b64);
                string jsonStr = Encoding.UTF8.GetString(bytes);

                var node = JsonNode.Parse(jsonStr);
                if (node != null)
                {
                    string alias = node["ps"]?.ToString() ?? "VMess Server";
                    string add = node["add"]?.ToString() ?? "";
                    int port = int.TryParse(node["port"]?.ToString(), out int pt) ? pt : 443;
                    string id = node["id"]?.ToString() ?? "";
                    string net = node["net"]?.ToString() ?? "tcp";
                    string tls = node["tls"]?.ToString() ?? "none";
                    string host = node["host"]?.ToString() ?? "";
                    string path = node["path"]?.ToString() ?? "";
                    string sni = node["sni"]?.ToString() ?? host;
                    string fp = node["fp"]?.ToString() ?? "";

                    return new ProxyProfile
                    {
                        Protocol = "VMess",
                        Alias = alias,
                        Address = add,
                        Port = port,
                        UserId = id,
                        Network = net,
                        Tls = tls,
                        Sni = sni,
                        Host = host,
                        Path = path,
                        Fingerprint = fp,
                        FullUrl = url
                    };
                }
            }
            else if (url.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Split('='))
                               .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

                return new ProxyProfile
                {
                    Protocol = "Trojan",
                    Alias = Uri.UnescapeDataString(uri.Fragment.TrimStart('#')),
                    Address = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 443,
                    UserId = uri.UserInfo,
                    Network = query.ContainsKey("type") ? query["type"] : "tcp",
                    Tls = "tls",
                    Sni = query.ContainsKey("sni") ? query["sni"] : (query.ContainsKey("peer") ? query["peer"] : uri.Host),
                    Alpn = query.ContainsKey("alpn") ? query["alpn"] : "",
                    Path = query.ContainsKey("path") ? query["path"] : "",
                    Host = query.ContainsKey("host") ? query["host"] : "",
                    FullUrl = url
                };
            }
            else if (url.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                return new ProxyProfile
                {
                    Protocol = "Shadowsocks",
                    Alias = Uri.UnescapeDataString(uri.Fragment.TrimStart('#')),
                    Address = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 8388,
                    UserId = uri.UserInfo,
                    FullUrl = url
                };
            }
            else if (url.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
                     url.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Split('='))
                               .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

                string sni = query.ContainsKey("sni") ? query["sni"] : (query.ContainsKey("peer") ? query["peer"] : uri.Host);
                string obfs = query.ContainsKey("obfs") ? query["obfs"] : "";
                string obfsPassword = query.ContainsKey("obfs-password") ? query["obfs-password"] : "";
                bool insecure = query.ContainsKey("insecure") && (query["insecure"] == "1" || query["insecure"].Equals("true", StringComparison.OrdinalIgnoreCase));
                string alias = !string.IsNullOrWhiteSpace(uri.Fragment) ? Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) : $"{uri.Host}:{uri.Port}";

                return new ProxyProfile
                {
                    Protocol = "Hysteria2",
                    Alias = alias,
                    Address = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 443,
                    UserId = Uri.UnescapeDataString(uri.UserInfo),
                    Tls = "tls",
                    Sni = sni,
                    Obfs = obfs,
                    ObfsPassword = obfsPassword,
                    Insecure = insecure,
                    FullUrl = url
                };
            }
            else if (url.StartsWith("hysteria://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Split('='))
                               .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

                string sni = query.ContainsKey("sni") ? query["sni"] : (query.ContainsKey("peer") ? query["peer"] : uri.Host);
                string auth = query.ContainsKey("auth") ? query["auth"] : Uri.UnescapeDataString(uri.UserInfo);
                bool insecure = query.ContainsKey("insecure") && (query["insecure"] == "1" || query["insecure"].Equals("true", StringComparison.OrdinalIgnoreCase));
                string alias = !string.IsNullOrWhiteSpace(uri.Fragment) ? Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) : $"{uri.Host}:{uri.Port}";

                return new ProxyProfile
                {
                    Protocol = "Hysteria",
                    Alias = alias,
                    Address = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 443,
                    UserId = auth,
                    Tls = "tls",
                    Sni = sni,
                    Insecure = insecure,
                    FullUrl = url
                };
            }
            else if (url.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Split('='))
                               .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

                string alias = !string.IsNullOrWhiteSpace(uri.Fragment) ? Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) : $"{uri.Host}:{uri.Port}";

                return new ProxyProfile
                {
                    Protocol = "WireGuard",
                    Alias = alias,
                    Address = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 51820,
                    UserId = Uri.UnescapeDataString(uri.UserInfo),
                    PublicKey = query.ContainsKey("publickey") ? query["publickey"] : "",
                    Path = query.ContainsKey("address") ? query["address"] : "10.0.0.2/32",
                    FullUrl = url
                };
            }
            else if (url.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Split('='))
                               .ToDictionary(p => p[0].ToLowerInvariant(), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

                string uuid = "";
                string password = "";
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var userParts = uri.UserInfo.Split(new[] { ':' }, 2);
                    uuid = Uri.UnescapeDataString(userParts[0]);
                    if (userParts.Length > 1) password = Uri.UnescapeDataString(userParts[1]);
                }

                string sni = query.ContainsKey("sni") ? query["sni"] : (query.ContainsKey("peer") ? query["peer"] : uri.Host);
                string alpn = query.ContainsKey("alpn") ? query["alpn"] : "h3";
                string cc = query.ContainsKey("congestion_control") ? query["congestion_control"] : "bbr";
                string udpRelay = query.ContainsKey("udp_relay_mode") ? query["udp_relay_mode"] : "native";
                bool insecure = (query.ContainsKey("insecure") && (query["insecure"] == "1" || query["insecure"].Equals("true", StringComparison.OrdinalIgnoreCase)))
                             || (query.ContainsKey("allow_insecure") && (query["allow_insecure"] == "1" || query["allow_insecure"].Equals("true", StringComparison.OrdinalIgnoreCase)));

                string alias = !string.IsNullOrWhiteSpace(uri.Fragment) ? Uri.UnescapeDataString(uri.Fragment.TrimStart('#')) : $"{uri.Host}:{uri.Port}";

                return new ProxyProfile
                {
                    Protocol = "TUIC",
                    Alias = alias,
                    Address = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 8443,
                    UserId = uuid,
                    Password = password,
                    CongestionControl = cc,
                    UdpRelayMode = udpRelay,
                    Tls = "tls",
                    Sni = sni,
                    Alpn = alpn,
                    Insecure = insecure,
                    FullUrl = url
                };
            }

            throw new NotSupportedException($"Unsupported proxy protocol format in: {url}");
        }
    }
}
