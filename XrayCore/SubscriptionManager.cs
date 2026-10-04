using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmartVpn.XrayCore
{
    public static class SubscriptionManager
    {
        private static readonly HttpClient _http;

        static SubscriptionManager()
        {
            var handler = new SocketsHttpHandler
            {
                // نادیده‌گرفتن خطای سرتیفیکیت SSL برای IPها یا سرورهای مرزبان خودامضا
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                },
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                ConnectTimeout = TimeSpan.FromSeconds(10)
            };

            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(18)
            };
        }

        public static async Task<(List<ProxyProfile>? Proxies, UserAccountInfo? UserInfo, string? SuggestedTitle, string? Error)> FetchSubscriptionAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return (null, null, null, "آدرس لینک اشتراک خالی است.");

            // پاک‌سازی کاراکترهای مخفی یونیکد و فاصله‌های احتمالی هنگام کپی
            url = CleanUrl(url);

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            // تولید لیست آدرس‌های احتمالی (آدرس اصلی + حالت‌های خودکار مرزبان مانند /sub/)
            var candidateUrls = GenerateCandidateUrls(url);
            string lastError = "";
            bool saw404 = false;

            foreach (var targetUrl in candidateUrls)
            {
                // تلاش اول: با هدر اختصاصی کلاینت‌های V2Ray (v2rayN / sing-box)
                // تلاش دوم: با هدر مرورگر معمولی (در صورتی که پنل به یوزرایجنت حساس باشد)
                string[] userAgents = { "v2rayN/6.23; sing-box/1.14.2", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" };

                foreach (var ua in userAgents)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                        request.Headers.TryAddWithoutValidation("User-Agent", ua);
                        request.Headers.TryAddWithoutValidation("Accept", "*/*");

                        using var response = await _http.SendAsync(request);

                        if (response.StatusCode == HttpStatusCode.NotFound)
                        {
                            saw404 = true;
                            lastError = "404 Not Found";
                            continue; // تلاش با URL بعدی
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            lastError = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                            continue;
                        }

                        // 1. عنوان پیشنهادی از هدرهای سرور
                        string? suggestedTitle = ExtractTitleFromHeaders(response);

                        // 2. اطلاعات اکانت کاربر از هدرها
                        UserAccountInfo userInfo = ExtractUserInfoFromHeaders(response);

                        // 3. دریافت محتوا
                        string body = await response.Content.ReadAsStringAsync();
                        body = body.Trim();

                        // 4. استخراج کانکشن‌ها از بدنه (Base64، متن ساده یا HTML مرزبان)
                        var proxies = ExtractProxiesFromText(body);

                        // اگر پاسخ HTML بود و هیچ کانکشنی مستقیم پیدا نشد، ممکن است لینک ساب داخل HTML باشد
                        if (proxies.Count == 0 && (body.Contains("<html", StringComparison.OrdinalIgnoreCase) || body.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)))
                        {
                            ExtractUserInfoFromHtml(body, userInfo, ref suggestedTitle);

                            // جستجو برای لینک ساب در کدهای HTML
                            var subHrefMatch = Regex.Match(body, @"(?:href|data-url|data-sub)=""([^""]*\/sub\/[^""]+)""", RegexOptions.IgnoreCase);
                            if (subHrefMatch.Success)
                            {
                                string relOrAbs = subHrefMatch.Groups[1].Value;
                                if (Uri.TryCreate(new Uri(targetUrl), relOrAbs, out var subUri))
                                {
                                    try
                                    {
                                        using var subReq = new HttpRequestMessage(HttpMethod.Get, subUri);
                                        subReq.Headers.TryAddWithoutValidation("User-Agent", "v2rayN/6.23; sing-box/1.14.2");
                                        using var subResp = await _http.SendAsync(subReq);
                                        if (subResp.IsSuccessStatusCode)
                                        {
                                            string subBody = await subResp.Content.ReadAsStringAsync();
                                            proxies = ExtractProxiesFromText(subBody);
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        else if (body.Contains("<html", StringComparison.OrdinalIgnoreCase))
                        {
                            // حتی اگر کانکشن پیدا شد، نام و ترافیک را از صفحه HTML استخراج کن
                            ExtractUserInfoFromHtml(body, userInfo, ref suggestedTitle);
                        }

                        if (proxies.Count > 0)
                        {
                            // 5. استخراج ترافیک و تاریخ از اسم کانکشن‌ها در صورت خالی بودن
                            FallbackUserInfoFromAliases(proxies, userInfo);
                            return (proxies, userInfo, suggestedTitle, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }
            }

            // اگر هیچ پاسخی با کانکشن پیدا نشد، پیام خطای هوشمند و راهنما برگردان
            if (saw404)
            {
                return (null, null, null,
                    "پاسخ ۴۰۴ (یافت نشد) از سرور دریافت شد.\n" +
                    "لطفاً دقت فرمایید:\n" +
                    "۱. مطمئن شوید املای توکن در لینک دقیق است و حرفی کم یا زیاد تایپ نشده است.\n" +
                    "۲. در پنل وب مرزبان، از دکمه کپی لینک ساب‌اسکریپشن یا آیکون اشتراک استفاده نمایید.");
            }

            return (null, null, null,
                string.IsNullOrWhiteSpace(lastError)
                    ? "هیچ کانکشن معتبری در آدرس وارد شده یافت نشد."
                    : $"خطا در اتصال به سرور:\n{lastError}");
        }

        private static string CleanUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
            {
                // حذف کاراکترهای کنترل و کاراکترهای نامرئی
                if (c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF' || c == '\u00A0' || c == '"' || c == '\'')
                    continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static List<string> GenerateCandidateUrls(string originalUrl)
        {
            var list = new List<string> { originalUrl };

            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri))
                return list;

            string hostAndPort = uri.Authority;
            string scheme = uri.Scheme;
            string path = uri.AbsolutePath.Trim('/');
            string query = uri.Query;

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            // اگر در مسیر کلمه sub وجود ندارد (مانند پنل‌های مرزبان)، نسخه‌های رایج ساب را پیشنهاد کن:
            if (!segments.Any(s => s.Equals("sub", StringComparison.OrdinalIgnoreCase)))
            {
                // حالت ۱: افزودن /sub/ به ابتدای مسیر: http://host:port/sub/prefix/token
                list.Add($"{scheme}://{hostAndPort}/sub/{string.Join("/", segments)}{query}");

                // حالت ۲: اگر چند بخش است، بخش آخر معمولاً توکن ساب است: http://host:port/sub/token
                if (segments.Length > 1)
                {
                    list.Add($"{scheme}://{hostAndPort}/sub/{segments[segments.Length - 1]}{query}");
                }

                // حالت ۳: اضافه کردن /sub به انتهای مسیر: http://host:port/prefix/token/sub
                list.Add($"{scheme}://{hostAndPort}/{string.Join("/", segments)}/sub{query}");
            }

            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string? ExtractTitleFromHeaders(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("profile-title", out var titleVals))
            {
                var raw = titleVals.FirstOrDefault() ?? "";
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        return raw.StartsWith("base64:")
                            ? Encoding.UTF8.GetString(Convert.FromBase64String(raw.Substring(7)))
                            : Uri.UnescapeDataString(raw);
                    }
                    catch { return raw; }
                }
            }
            return null;
        }

        private static UserAccountInfo ExtractUserInfoFromHeaders(HttpResponseMessage response)
        {
            var userInfo = new UserAccountInfo();
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

            return userInfo;
        }

        public static List<ProxyProfile> ExtractProxiesFromText(string text)
        {
            var proxies = new List<ProxyProfile>();
            if (string.IsNullOrWhiteSpace(text)) return proxies;

            string decoded = text;
            try
            {
                string clean = text.Trim();
                // اگر متن حاوی کاراکترهای HTML یا پروتکل مستقیم نیست، احتمالاً بیس۶۴ است
                if (!clean.Contains("<") && !clean.Contains("://"))
                {
                    clean = Regex.Replace(clean, @"\s+", "");
                    int mod4 = clean.Length % 4;
                    if (mod4 > 0) clean += new string('=', 4 - mod4);
                    byte[] data = Convert.FromBase64String(clean);
                    decoded = Encoding.UTF8.GetString(data);
                }
            }
            catch { decoded = text; }

            // استخراج کلیه کانکشن‌های شناخته‌شده با ریجکس جامع
            var matches = Regex.Matches(decoded, @"(?i)(?:vless|vmess|trojan|ss|hysteria2|hy2|hysteria|wireguard|tuic)://[^\s""'<>`\\]+");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in matches)
            {
                string raw = m.Value.Trim();
                raw = WebUtility.HtmlDecode(raw);
                raw = raw.TrimEnd(';', ')', ']', '}', ',', '.', '\'', '"');

                if (seen.Add(raw))
                {
                    try
                    {
                        var p = UrlParser.Parse(raw);
                        if (p != null) proxies.Add(p);
                    }
                    catch { /* نادیده‌گرفتن کانکشن‌های ناقص */ }
                }
            }

            return proxies;
        }

        private static void ExtractUserInfoFromHtml(string html, UserAccountInfo userInfo, ref string? suggestedTitle)
        {
            if (string.IsNullOrWhiteSpace(html)) return;

            try
            {
                // استخراج نام کاربر از قالب‌های مرزبان (مانند "fb14qa29i546e2a1 - mamad" -> mamad)
                if (string.IsNullOrWhiteSpace(suggestedTitle))
                {
                    var userMatch = Regex.Match(html, @"(?:Subscription\s+info|User)[^<]*<\/[^>]+>\s*<[^>]+>\s*([a-zA-Z0-9_\-\.]+)\s*-\s*([^<]+)<\/", RegexOptions.IgnoreCase);
                    if (userMatch.Success)
                    {
                        suggestedTitle = userMatch.Groups[2].Value.Trim();
                    }
                    else
                    {
                        var titleMatch = Regex.Match(html, @"<title>([^<]+)<\/title>", RegexOptions.IgnoreCase);
                        if (titleMatch.Success && !titleMatch.Groups[1].Value.Contains("Subscription info", StringComparison.OrdinalIgnoreCase))
                        {
                            suggestedTitle = titleMatch.Groups[1].Value.Trim();
                        }
                    }
                }

                // استخراج ترافیک مصرفی مرزبان: Usage 206.64 MB یا Downloaded 183.15MB
                var dlMatch = Regex.Match(html, @"(?:Usage|Downloaded)[^<]*<\/[^>]+>\s*<[^>]+>\s*([0-9.]+)\s*(MB|GB|TB|KB)B?", RegexOptions.IgnoreCase);
                if (dlMatch.Success && double.TryParse(dlMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double dlVal))
                {
                    string unit = dlMatch.Groups[2].Value.ToUpperInvariant();
                    long mult = 1024L * 1024L;
                    if (unit.StartsWith("G")) mult = 1024L * 1024L * 1024L;
                    else if (unit.StartsWith("T")) mult = 1024L * 1024L * 1024L * 1024L;
                    else if (unit.StartsWith("K")) mult = 1024L;
                    userInfo.Download = (long)(dlVal * mult);
                }

                var ulMatch = Regex.Match(html, @"Uploaded[^<]*<\/[^>]+>\s*<[^>]+>\s*([0-9.]+)\s*(MB|GB|TB|KB)B?", RegexOptions.IgnoreCase);
                if (ulMatch.Success && double.TryParse(ulMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double ulVal))
                {
                    string unit = ulMatch.Groups[2].Value.ToUpperInvariant();
                    long mult = 1024L * 1024L;
                    if (unit.StartsWith("G")) mult = 1024L * 1024L * 1024L;
                    else if (unit.StartsWith("T")) mult = 1024L * 1024L * 1024L * 1024L;
                    else if (unit.StartsWith("K")) mult = 1024L;
                    userInfo.Upload = (long)(ulVal * mult);
                }

                // روزهای باقیمانده: Days left 30
                var daysMatch = Regex.Match(html, @"Days\s+left[^<]*<\/[^>]+>\s*<[^>]+>\s*([0-9]+)", RegexOptions.IgnoreCase);
                if (daysMatch.Success && int.TryParse(daysMatch.Groups[1].Value, out int days))
                {
                    userInfo.ExpireDateTimestamp = DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds();
                }
            }
            catch { }
        }

        private static void FallbackUserInfoFromAliases(List<ProxyProfile> proxies, UserAccountInfo userInfo)
        {
            if (userInfo.Total > 0 && userInfo.ExpireDateTimestamp > 0) return;

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
    }
}
