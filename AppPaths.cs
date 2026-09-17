using System;
using System.IO;

// مسیر پوشه فایل‌های برنامه (نسخه پرتابل: پوشه «NFV Connect» کنار EXE)
// ساختار ZIP: NETFASTVIP.exe + README.txt + NFV Connect\ (شامل ca.crt، base.ovpn، config.json، پوشه openvpn و ...)
// اگر پوشه وجود نداشته باشد (اجرای توسعه یا چیدمان قدیمی)، خودِ پوشه EXE استفاده می‌شود
public static class AppPaths
{
    public const string DataFolderName = "NFV Connect";

    public static readonly string Root = ResolveRoot();

    private static string ResolveRoot()
    {
        try
        {
            var packaged = Path.Combine(AppContext.BaseDirectory, DataFolderName);
            if (Directory.Exists(packaged)) return packaged;
        }
        catch { }
        return AppContext.BaseDirectory;
    }

    public static string In(string relativePath) => Path.Combine(Root, relativePath);

    // ======== WireGuard / AmneziaWG ========
    // مسیر پیش‌فرض نصب WireGuard for Windows
    public static string WireGuardExe =>
        FindExe(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
            Path.Combine(AppContext.BaseDirectory, "wireguard", "wireguard.exe"),  // نسخه portable
            "wireguard.exe"
        );

    // مسیر پیش‌فرض نصب AmneziaWG for Windows
    public static string AmneziaWgExe =>
        FindExe(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AmneziaWG", "AmneziaWG.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amnezia", "AmneziaWG.exe"),
            Path.Combine(AppContext.BaseDirectory, "amneziawg", "AmneziaWG.exe"),  // نسخه portable
            "AmneziaWG.exe"
        );

    private static string FindExe(params string[] candidates)
    {
        foreach (var p in candidates)
            if (File.Exists(p)) return p;
        return candidates[0]; // fallback به اولین مسیر
    }
}
