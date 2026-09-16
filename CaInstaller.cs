using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;

// نصب گواهی CA در Trusted Root — فقط با اجازه کاربر در اولین اتصال sstp/ikev2
// (نصب سایلنت در اینستالر حذف شد — هم برای اعتماد کاربر هم حساسیت آنتی‌ویروس‌ها)
public static class CaInstaller
{
    // فایل ca.crt کنار exe برنامه قرار می‌گیرد (اینستالر فقط کپی می‌کند، نصب نمی‌کند)
    public static string CaPath => Path.Combine(AppPaths.Root, "ca.crt");

    public static bool Exists => File.Exists(CaPath);

    // آیا همین گواهی (با همین اثر انگشت) قبلاً در Trusted Root سیستم نصب شده؟
    // نکته: سرویس‌های SSTP و IKEv2 ویندوز فقط LocalMachine\Root را می‌بینند، نه CurrentUser را
    public static bool IsInstalled()
    {
        try
        {
            if (!Exists) return false;
            var cert = X509CertificateLoader.LoadCertificateFromFile(CaPath);
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false).Count > 0;
        }
        catch { return false; }
    }

    // نصب گواهی — اول تلاش مستقیم (اگر برنامه Administrator باشد)،
    // وگرنه certutil با UAC — یعنی خود ویندوز هم از کاربر تأیید می‌گیرد
    public static bool TryInstall(Action<string>? log = null)
    {
        try
        {
            if (!Exists) return false;

            try
            {
                var cert = X509CertificateLoader.LoadCertificateFromFile(CaPath);
                using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);
                store.Add(cert);
                return true;
            }
            catch
            {
                // دسترسی مستقیم نبود — certutil با درخواست UAC
                var psi = new ProcessStartInfo("certutil.exe", $"-addstore Root \"{CaPath}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(60000);
                return IsInstalled();
            }
        }
        catch (Exception ex)
        {
            // لغو UAC توسط کاربر هم به همین‌جا می‌رسد — شکست نرم، بدون کرش
            log?.Invoke("CA install error: " + ex.Message);
            return false;
        }
    }
}
