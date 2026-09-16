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
}
