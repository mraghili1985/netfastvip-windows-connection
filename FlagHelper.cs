using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SmartVpn
{
    public static class FlagHelper
    {
        private static readonly Dictionary<string, BitmapImage> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly HashSet<string> Downloading = new(StringComparer.OrdinalIgnoreCase);

        public static event Action<string, BitmapImage>? FlagDownloaded;

        private static readonly Dictionary<string, string> CountryNameToCode = new(StringComparer.OrdinalIgnoreCase)
        {
            ["UNITED KINGDOM"] = "gb", ["GREAT BRITAIN"] = "gb", ["ENGLAND"] = "gb", ["LONDON"] = "gb", ["UK"] = "gb", ["GB"] = "gb",
            ["UNITED STATES"] = "us", ["USA"] = "us", ["AMERICA"] = "us", ["US"] = "us",
            ["GERMANY"] = "de", ["DEUTSCHLAND"] = "de", ["FRANKFURT"] = "de", ["BERLIN"] = "de", ["DE"] = "de",
            ["NETHERLANDS"] = "nl", ["HOLLAND"] = "nl", ["AMSTERDAM"] = "nl", ["NL"] = "nl",
            ["FRANCE"] = "fr", ["PARIS"] = "fr", ["FR"] = "fr",
            ["TURKEY"] = "tr", ["TURKIYE"] = "tr", ["ISTANBUL"] = "tr", ["TR"] = "tr",
            ["FINLAND"] = "fi", ["HELSINKI"] = "fi", ["FI"] = "fi",
            ["CANADA"] = "ca", ["TORONTO"] = "ca", ["CA"] = "ca",
            ["RUSSIA"] = "ru", ["MOSCOW"] = "ru", ["RU"] = "ru",
            ["POLAND"] = "pl", ["WARSAW"] = "pl", ["PL"] = "pl",
            ["SWEDEN"] = "se", ["STOCKHOLM"] = "se", ["SE"] = "se",
            ["SINGAPORE"] = "sg", ["SG"] = "sg",
            ["IRAN"] = "ir", ["TEHRAN"] = "ir", ["IR"] = "ir",
            ["ITALY"] = "it", ["ROME"] = "it", ["MILAN"] = "it", ["IT"] = "it",
            ["SPAIN"] = "es", ["MADRID"] = "es", ["ES"] = "es",
            ["SWITZERLAND"] = "ch", ["ZURICH"] = "ch", ["CH"] = "ch",
            ["JAPAN"] = "jp", ["TOKYO"] = "jp", ["JP"] = "jp",
            ["AUSTRALIA"] = "au", ["SYDNEY"] = "au", ["AU"] = "au",
            ["AUSTRIA"] = "at", ["VIENNA"] = "at", ["AT"] = "at",
            ["ROMANIA"] = "ro", ["BUCHAREST"] = "ro", ["RO"] = "ro",
            ["INDIA"] = "in", ["MUMBAI"] = "in", ["IN"] = "in",
            ["BRAZIL"] = "br", ["SAO PAULO"] = "br", ["BR"] = "br",
            ["UKRAINE"] = "ua", ["KYIV"] = "ua", ["UA"] = "ua",
            ["NORWAY"] = "no", ["OSLO"] = "no", ["NO"] = "no",
            ["DENMARK"] = "dk", ["COPENHAGEN"] = "dk", ["DK"] = "dk",
            ["SOUTH KOREA"] = "kr", ["KOREA"] = "kr", ["SEOUL"] = "kr", ["KR"] = "kr",
            ["HONG KONG"] = "hk", ["HK"] = "hk",
            ["UNITED ARAB EMIRATES"] = "ae", ["UAE"] = "ae", ["DUBAI"] = "ae", ["AE"] = "ae",
            ["CZECH REPUBLIC"] = "cz", ["CZECHIA"] = "cz", ["PRAGUE"] = "cz", ["CZ"] = "cz",
            ["BULGARIA"] = "bg", ["SOFIA"] = "bg", ["BG"] = "bg",
            ["BELGIUM"] = "be", ["BRUSSELS"] = "be", ["BE"] = "be",
            ["IRELAND"] = "ie", ["DUBLIN"] = "ie", ["IE"] = "ie",
            ["LITHUANIA"] = "lt", ["VILNIUS"] = "lt", ["LT"] = "lt",
            ["ESTONIA"] = "ee", ["TALLINN"] = "ee", ["EE"] = "ee",
            ["LATVIA"] = "lv", ["RIGA"] = "lv", ["LV"] = "lv",
            ["PORTUGAL"] = "pt", ["LISBON"] = "pt", ["PT"] = "pt",
        };

        public static string? DetectCountryCode(string? name, string? countryText = null)
        {
            if (!string.IsNullOrWhiteSpace(countryText))
            {
                var ct = countryText.Trim();
                if (ct.Length == 2 && CountryNameToCode.ContainsKey(ct))
                    return ct.ToLowerInvariant();

                foreach (var kvp in CountryNameToCode)
                {
                    if (ct.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kvp.Value;
                }
            }

            if (string.IsNullOrWhiteSpace(name)) return null;

            // Check unicode regional indicator pairs (e.g. 🇬🇧)
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsSurrogatePair(name, i))
                {
                    int first = char.ConvertToUtf32(name, i);
                    if (first >= 0x1F1E6 && first <= 0x1F1FF && i + 3 < name.Length && char.IsSurrogatePair(name, i + 2))
                    {
                        int second = char.ConvertToUtf32(name, i + 2);
                        if (second >= 0x1F1E6 && second <= 0x1F1FF)
                        {
                            char c1 = (char)('a' + (first - 0x1F1E6));
                            char c2 = (char)('a' + (second - 0x1F1E6));
                            return $"{c1}{c2}";
                        }
                    }
                }
            }

            // Check bracketed codes: [US], [GB], (DE), - NL -, | TR |
            var mBracket = Regex.Match(name, @"[\[\(\-\|\s]([A-Za-z]{2})[\]\)\-\|\s]");
            if (mBracket.Success)
            {
                var candidate = mBracket.Groups[1].Value.ToUpperInvariant();
                if (CountryNameToCode.TryGetValue(candidate, out var code))
                    return code;
            }

            // Check country name keywords in alias/name
            var upper = name.ToUpperInvariant();
            foreach (var kvp in CountryNameToCode)
            {
                if (kvp.Key.Length > 2 && upper.Contains(kvp.Key))
                    return kvp.Value;
            }

            return null;
        }

        public static BitmapImage? GetFlagBitmap(string? countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode)) return null;
            countryCode = countryCode.ToLowerInvariant().Trim();

            lock (Cache)
            {
                if (Cache.TryGetValue(countryCode, out var cached))
                    return cached;
            }

            string flagsDir = Path.Combine(AppContext.BaseDirectory, "Data", "flags");
            string flagFile = Path.Combine(flagsDir, $"{countryCode}.png");

            if (!File.Exists(flagFile))
            {
                // Try source / project Data directory
                string altDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "flags");
                string altFile = Path.Combine(altDir, $"{countryCode}.png");
                if (File.Exists(altFile))
                    flagFile = altFile;
            }

            if (File.Exists(flagFile))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(flagFile, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 80;
                    bi.EndInit();
                    bi.Freeze();

                    lock (Cache) { Cache[countryCode] = bi; }
                    return bi;
                }
                catch { }
            }

            // If not found locally, trigger background download
            _ = DownloadFlagAsync(countryCode);
            return null;
        }

        private static async Task DownloadFlagAsync(string code)
        {
            lock (Downloading)
            {
                if (Downloading.Contains(code)) return;
                Downloading.Add(code);
            }

            try
            {
                string flagsDir = Path.Combine(AppContext.BaseDirectory, "Data", "flags");
                Directory.CreateDirectory(flagsDir);
                string flagFile = Path.Combine(flagsDir, $"{code}.png");

                byte[] data = await Http.GetByteArrayAsync($"https://flagcdn.com/w80/{code}.png");
                if (data.Length > 0)
                {
                    await File.WriteAllBytesAsync(flagFile, data);

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.StreamSource = new MemoryStream(data);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.DecodePixelWidth = 80;
                        bi.EndInit();
                        bi.Freeze();

                        lock (Cache) { Cache[code] = bi; }
                        FlagDownloaded?.Invoke(code, bi);
                    });
                }
            }
            catch { }
            finally
            {
                lock (Downloading) { Downloading.Remove(code); }
            }
        }

        public static string GetProtocolIcon(string? protocol)
        {
            var p = (protocol ?? "").ToLowerInvariant();
            if (p.Contains("wireguard") || p.Contains("amnezia")) return "🛡️";
            if (p.Contains("openvpn")) return "🔒";
            return "⚡";
        }

        public static void ApplyFlagOrIcon(Image? img, TextBlock? txt, string? name, string? protocol, string? countryText = null)
        {
            var code = DetectCountryCode(name, countryText);
            var bmp = GetFlagBitmap(code);

            if (bmp != null && img != null)
            {
                img.Source = bmp;
                img.Visibility = Visibility.Visible;
                if (txt != null) txt.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (img != null) img.Visibility = Visibility.Collapsed;
                if (txt != null)
                {
                    txt.Visibility = Visibility.Visible;
                    txt.Text = GetProtocolIcon(protocol);
                }
            }
        }
    }
}
