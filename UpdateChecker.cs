using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace SmartVpn;

// بررسی نسخه جدید از روی هاست — مدل «فقط اطلاع‌رسانی» (بدون دانلود خودکار)
// version.json روی هاست: { "Version": "1.0.7", "DownloadUrl": "https://...zip", "Notes": "" }
// اگر اینترنت یا سرور در دسترس نباشد کاملاً بی‌صدا رد می‌شود.
public static class UpdateChecker
{
    // ---- متن‌های فارسی ----
    private const string MsgNewVersionFmt =
        "نسخه جدید {0} منتشر شده است (نسخه فعلی شما: {1}).{2}\nصفحه دانلود باز شود؟";

    private sealed class VersionInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    public static async Task CheckAsync(Window owner)
    {
        try
        {
            var url = AppConfig.Load().UpdateUrl.Trim();
            if (url.Length == 0) return; // بررسی آپدیت خاموش است

            await Task.Delay(3000); // اول پنجره اصلی کامل بالا بیاید

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // پارامتر زمان برای دور زدن کش سرور
            var sep = url.Contains('?') ? "&" : "?";
            var json = await http.GetStringAsync(url + sep + "t=" + DateTime.UtcNow.Ticks);

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var info = JsonSerializer.Deserialize<VersionInfo>(json, opts);
            if (info is null || info.Version.Trim().Length == 0) return;

            if (!Version.TryParse(info.Version.Trim().TrimStart('v', 'V'), out var latest)) return;
            var cur = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (cur is null || latest <= cur) return; // نسخه جدیدتری نیست

            var notes = info.Notes.Trim().Length == 0 ? "" : "\n" + info.Notes.Trim();
            var curText = $"{cur.Major}.{cur.Minor}.{cur.Build}";
            if (!AskDialog.Confirm(owner, string.Format(MsgNewVersionFmt, info.Version.Trim(), curText, notes)))
                return;

            var dl = info.DownloadUrl.Trim().Length == 0 ? url : info.DownloadUrl.Trim();
            Process.Start(new ProcessStartInfo(dl) { UseShellExecute = true });
        }
        catch { } // هر خطایی (شبکه، JSON خراب، ...) بی‌صدا رد می‌شود
    }
}
