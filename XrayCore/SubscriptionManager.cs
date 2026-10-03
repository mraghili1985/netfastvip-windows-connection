using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmartVpn.XrayCore
{
    public static class SubscriptionManager
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<(List<ProxyProfile>? Proxies, UserAccountInfo? UserInfo, string? SuggestedTitle, string? Error)> FetchSubscriptionAsync(string url)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "v2rayN/6.23; sing-box/1.14.2");

                var response = await _http.SendAsync(request);
                response.EnsureSuccessStatusCode();

                // 1. Suggested title from headers if present
                string? suggestedTitle = null;
                if (response.Headers.TryGetValues("profile-title", out var titleVals))
                {
                    var raw = titleVals.FirstOrDefault() ?? "";
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        try
                        {
                            suggestedTitle = raw.StartsWith("base64:")
                                ? Encoding.UTF8.GetString(Convert.FromBase64String(raw.Substring(7)))
                                : Uri.UnescapeDataString(raw);
                        }
                        catch { suggestedTitle = raw; }
                    }
                }

                // 2. Parse UserInfo Header
                UserAccountInfo userInfo = new UserAccountInfo();
                string infoStr = "";
                string[] headerKeys = { "subscription-userinfo", "user-info", "sub-userinfo", "x-user-info" };

                foreach (var k in headerKeys)
                {
                    if (response.Headers.TryGetValues(k, out var vals) || response.Content.Headers.TryGetValues(k, out vals))
                    {
                        infoStr = vals.FirstOrDefault() ?? "";
                        if (!string.IsNullOrWhiteSpace(infoStr)) break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(infoStr))
                {
                    var parts = infoStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        var kv = part.Trim().Split('=');
                        if (kv.Length == 2 && long.TryParse(kv[1], out long val))
                        {
                            switch (kv[0].ToLowerInvariant())
                            {
                                case "upload": userInfo.Upload = val; break;
                                case "download": userInfo.Download = val; break;
                                case "total": userInfo.Total = val; break;
                                case "expire": userInfo.ExpireDateTimestamp = val; break;
                            }
                        }
                    }
                }

                // 3. Parse Body (Base64 or Plain Text)
                string body = await response.Content.ReadAsStringAsync();
                body = body.Trim();

                string decodedText = "";
                try
                {
                    int mod4 = body.Length % 4;
                    if (mod4 > 0) body += new string('=', 4 - mod4);

                    byte[] data = Convert.FromBase64String(body);
                    decodedText = Encoding.UTF8.GetString(data);
                }
                catch
                {
                    decodedText = body;
                }

                var lines = decodedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                List<ProxyProfile> proxies = new List<ProxyProfile>();

                foreach (var line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("hysteria://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            proxies.Add(UrlParser.Parse(t));
                        }
                        catch { /* skip unsupported or malformed links */ }
                    }
                }

                // 4. Fallback parsing for subscription info if header was missing (Common in Iranian X-UI / Marzban panels)
                if (userInfo.Total == 0)
                {
                    foreach (var p in proxies)
                    {
                        if (string.IsNullOrWhiteSpace(p.Alias)) continue;

                        var mTraffic = Regex.Match(p.Alias, @"(\d+(?:\.\d+)?)\s*(TB|GB|MB|KB|T|G|M|K)B?", RegexOptions.IgnoreCase);
                        var mDays = Regex.Match(p.Alias, @"(\d+)\s*(?:D|Days?|روز|d)", RegexOptions.IgnoreCase);

                        if (mTraffic.Success && userInfo.Total == 0)
                        {
                            if (double.TryParse(mTraffic.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                            {
                                string unit = mTraffic.Groups[2].Value.ToUpperInvariant();
                                long mult = 1024L * 1024L * 1024L;
                                if (unit.StartsWith("T")) mult = 1024L * 1024L * 1024L * 1024L;
                                else if (unit.StartsWith("M")) mult = 1024L * 1024L;
                                else if (unit.StartsWith("K")) mult = 1024L;
                                userInfo.Total = (long)(val * mult);
                                userInfo.Upload = 0;
                                userInfo.Download = 0;
                            }
                        }

                        if (mDays.Success && userInfo.ExpireDateTimestamp == 0)
                        {
                            if (int.TryParse(mDays.Groups[1].Value, out int days))
                            {
                                userInfo.ExpireDateTimestamp = DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds();
                            }
                        }

                        if (userInfo.Total > 0 && userInfo.ExpireDateTimestamp > 0)
                            break;
                    }
                }

                return (proxies, userInfo, suggestedTitle, null);
            }
            catch (Exception ex)
            {
                return (null, null, null, ex.Message);
            }
        }
    }
}
