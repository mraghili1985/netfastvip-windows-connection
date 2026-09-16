using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SmartVpn;

// راه‌انداز اولین اجرا — مخصوص نسخه پورتابل (ZIP)
// چیزی در رجیستری یا Program Files نمی‌نویسد؛ همه‌چیز کنار EXE می‌ماند.
// کارها: ۱) هشدار اجرا از مسیر موقت/داخل ZIP  ۲) بررسی مجوز نوشتن کنار EXE  ۳) پیشنهاد میان‌بر دسکتاپ (فقط بار اول)
public static class FirstRunSetup
{
    // ---- متن‌های فارسی ----
    private const string MsgRunFromTemp =
        "برنامه از داخل ZIP یا پوشه موقت اجرا شده است.\n" +
        "ابتدا آن را در یک پوشه ثابت (مثل C:\\NETFASTVIP) استخراج کنید و از آنجا اجرا کنید؛ وگرنه تنظیمات ذخیره نمی‌شود.";
    private const string MsgNotWritable =
        "پوشه فعلی قابل نوشتن نیست — تنظیمات ذخیره نمی‌شود.\n" +
        "پوشه برنامه را به مسیری مثل C:\\NETFASTVIP یا داخل پوشه کاربری منتقل کنید.";
    private const string MsgAskShortcutFmt = "میان‌بر {0} روی دسکتاپ ساخته شود؟";
    private const string MsgAskOvpnInstall =
        "برای کانکشن‌های OpenVPN باید موتور آن یک‌بار نصب شود.\n" +
        "نصب خودکار و بی‌صداست. الان نصب شود؟";
    private const string MsgOvpnInstallOk = "نصب OpenVPN با موفقیت انجام شد.";
    private const string MsgOvpnInstallFail =
        "نصب خودکار OpenVPN ناموفق بود — فایل ستاپ کنار برنامه را دستی نصب کنید؛\n" +
        "بقیه پروتکل‌ها بدون آن کار می‌کنند.";

    private const string MarkerFile = "firstrun.done";

    public static void Run(Window? owner = null)
    {
        string appDir = AppContext.BaseDirectory;

        // ۱) اجرا از مسیر موقت (کاربر EXE را مستقیم از داخل ZIP باز کرده)
        if (IsUnderTempPath(appDir))
        {
            AskDialog.Info(owner!, MsgRunFromTemp);
            return; // مارکر نمی‌نویسیم تا بعد از جابه‌جایی دوباره بررسی شود
        }

        // ۲) مجوز نوشتن کنار EXE (مثلاً اگر دستی داخل Program Files کپی شده باشد)
        if (!IsWritable(appDir))
        {
            AskDialog.Info(owner!, MsgNotWritable);
            return;
        }

        // ۳) بقیه کارها فقط بار اول
        string marker = Path.Combine(appDir, MarkerFile);
        if (File.Exists(marker)) return;

        if (AskDialog.Confirm(owner!, string.Format(MsgAskShortcutFmt, AppConfig.BrandName)))
            TryCreateDesktopShortcut(appDir);

        // نصب بی‌صدای OpenVPN اگر نصب نیست و فایل ستاپ رسمی همراه برنامه هست
        TryInstallOpenVpn(appDir, owner);

        try { File.WriteAllText(marker, "done " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")); }
        catch { }
    }

    private static bool IsUnderTempPath(string dir)
    {
        try
        {
            string temp = Path.GetTempPath().TrimEnd('\\');
            return dir.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".writetest.tmp");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // نصب بی‌صدای OpenVPN از فایل ستاپ رسمی همراه برنامه (فقط بار اول و با اجازه کاربر)
    // msiexec /qn هیچ پنجره‌ای نشان نمی‌دهد؛ درایور ovpn-dco هم همراه آن نصب می‌شود.
    // نیاز به ادمین دارد که مانیفست برنامه تأمین می‌کند.
    private static void TryInstallOpenVpn(string appDir, Window? owner)
    {
        try
        {
            if (OpenVpnProvider.FindExe() != null) return; // نصب است یا نسخه پورتابل کنار برنامه هست

            string? msi = null;
            foreach (var dir in new[] { appDir, Path.Combine(appDir, "openvpn"), AppPaths.Root, Path.Combine(AppPaths.Root, "openvpn") })
            {
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir, "openvpn-install*.msi");
                if (files.Length > 0) { msi = files[0]; break; }
            }
            if (msi is null) return; // فایل ستاپ همراه برنامه نیست — کاری نمی‌کنیم

            if (!AskDialog.Confirm(owner!, MsgAskOvpnInstall)) return;

            var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{msi}\" /qn /norestart")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();

            AskDialog.Info(owner!,
                OpenVpnProvider.FindExe() != null ? MsgOvpnInstallOk : MsgOvpnInstallFail);
        }
        catch { }
    }

    // ساخت میان‌بر دسکتاپ با COM ویندوز — بدون هیچ وابستگی خارجی
    private static void TryCreateDesktopShortcut(string appDir)
    {
        try
        {
            string exe = Environment.ProcessPath ?? Path.Combine(appDir, "NETFASTVIP.exe");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            // نام میان‌بر از برند config.json
            dynamic lnk = shell.CreateShortcut(Path.Combine(desktop, AppConfig.BrandName + ".lnk"));
            lnk.TargetPath = exe;
            lnk.WorkingDirectory = appDir;
            lnk.IconLocation = exe + ",0";
            lnk.Save();
        }
        catch { }
    }
}
