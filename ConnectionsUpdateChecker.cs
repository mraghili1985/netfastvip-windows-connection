using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace SmartVpn;

// دریافت پکیج کانکشن‌ها از روی هاست (لیست سرورها + base.ovpn + گواهی CA) و اعمال آن.
// برخلاف نسخه‌ی قبلی، این‌جا هیچ‌چیزی بی‌صدا اعمال نمی‌شود: قبل از ذخیره، خلاصه‌ی دقیق
// تغییرات (کانکشن‌های جدید/به‌روزشده، base.ovpn، گواهی CA) با همان دیالوگ داخلی هم‌تم برنامه (AskDialog) به کاربر نشان داده می‌شود.
// اگر اینترنت/سرور در دسترس نباشد یا خطایی رخ دهد، کاملاً بی‌صدا رد می‌شود (مثل UpdateChecker).
public static class ConnectionsUpdateChecker
{
    // مهم: cfg باید همان نمونه‌ی به‌اشتراک‌گذاشته‌شده‌ی _config در MainWindow باشد، نه یک AppConfig.Load()
    // جدا و تازه. اگر اینجا نمونه‌ی جدا لود و Save می‌شد، هر _config.Save() دیگری که در همان اجرا
    // از جای دیگر برنامه (تنظیمات، لیست کانکشن‌ها، آمار و...) با نمونه‌ی قدیمی‌تر صدا زده می‌شود،
    // بی‌صدا آپدیت را روی دیسک رول‌بک می‌کرد — همین باعث می‌شد آپدیت گاهی تا ۲ بار بستن برنامه اعمال نشود.
    public static async Task CheckAsync(Window owner, AppConfig cfg)
    {
        try
        {
            var url = cfg.ConnectionsUpdateUrl.Trim();
            if (url.Length == 0) return; // بررسی خاموش است

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // پارامتر زمان برای دور زدن کش سرور/CDN
            var sep = url.Contains('?') ? "&" : "?";
            var json = await http.GetStringAsync(url + sep + "t=" + DateTime.UtcNow.Ticks);

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var pkg = JsonSerializer.Deserialize<PackageFile>(json, opts);
            if (pkg is null) return;
            if (pkg.PackageVersion <= cfg.PackageVersion) return; // نسخه‌ی جدیدتری نیست

            // --- قبل از اعمال، دقیقاً مشخص کن چه چیزی تغییر می‌کند تا به کاربر نشان داده شود ---
            var addedNames = new List<string>();
            var updatedNames = new List<string>();
            foreach (var incoming in pkg.Connections)
            {
                var existing = cfg.Connections.FirstOrDefault(c => c.Name == incoming.Name);
                if (existing is null)
                {
                    addedNames.Add(incoming.Name);
                }
                else if (existing.Type != incoming.Type || existing.Server != incoming.Server ||
                         existing.Port != incoming.Port || existing.Proto != incoming.Proto ||
                         (incoming.Psk.Length > 0 && existing.Psk != incoming.Psk))
                {
                    updatedNames.Add(incoming.Name);
                }
            }
            // سرورهای رسمی‌ای که قبلاً داشتیم ولی دیگه توی پکیج جدید نیستند — یعنی از روی هاست حذف شده‌اند
            var incomingNamesForRemoval = new HashSet<string>(pkg.Connections.Select(c => c.Name));
            var removedNames = cfg.Connections
                .Where(c => c.Source == "official" && !incomingNamesForRemoval.Contains(c.Name))
                .Select(c => c.Name)
                .ToList();
            var baseOvpnChanged = pkg.BaseOvpn.Trim().Length > 0 && pkg.BaseOvpn != cfg.BaseOvpn;
            var caChanged = pkg.Ca.Trim().Length > 0 && pkg.Ca != cfg.Ca;

            if (addedNames.Count == 0 && updatedNames.Count == 0 && removedNames.Count == 0 && !baseOvpnChanged && !caChanged)
            {
                // هیچ محتوایی برای نشان دادن نیست — فقط شماره نسخه را هم‌تراز کن، بدون دیالوگ
                cfg.PackageVersion = pkg.PackageVersion;
                cfg.Save();
                return;
            }

            // --- اعمال واقعی ---
            cfg.ApplyOfficialPackage(pkg);   // سرورها + BaseOvpn + Ca را در حافظه به‌روز می‌کند
            cfg.Save();                       // ذخیره در Data/config.json
            cfg.EnsureBaseOvpnFile();         // نوشتن/به‌روزرسانی Data/base.ovpn روی دیسک
            cfg.EnsureCaFile();               // نوشتن/به‌روزرسانی ca.crt کنار exe (نصب واقعی همچنان با CaInstaller و اجازه‌ی کاربر انجام می‌شود)

            // --- نمایش خلاصه‌ی تغییرات به کاربر ---
            var sb = new StringBuilder();
            sb.AppendLine($"پکیج سرورها به نسخه {pkg.PackageVersion} به‌روزرسانی شد:");
            if (addedNames.Count > 0)
                sb.AppendLine($"• {addedNames.Count} کانکشن جدید: {string.Join("، ", addedNames)}");
            if (updatedNames.Count > 0)
                sb.AppendLine($"• {updatedNames.Count} کانکشن به‌روزرسانی شد: {string.Join("، ", updatedNames)}");
            if (removedNames.Count > 0)
                sb.AppendLine($"• {removedNames.Count} کانکشن حذف شد: {string.Join("، ", removedNames)}");
            if (baseOvpnChanged)
                sb.AppendLine("• فایل base.ovpn به‌روزرسانی شد.");
            if (caChanged)
                sb.AppendLine("• گواهی امنیتی (CA) جدید دریافت شد — هنگام اتصال بعدی SSTP/IKEv2 نصبش را تایید کنید.");
            sb.AppendLine();
            sb.AppendLine("لطفاً برنامه را مجدد اجرا کنید تا تمام تفییرات در لیست کانکشن‌ها نمایش داده شود.");

            AskDialog.Info(owner, sb.ToString().TrimEnd());
        }
        catch { } // هر خطایی (شبکه، JSON خراب، ...) بی‌صدا رد می‌شود
    }
}
