using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace SmartVpn;

/// <summary>
/// Lightweight UI localization. Persian is the default; English is applied after restart.
/// Technical names such as OpenVPN, WireGuard, IP and DNS intentionally remain unchanged.
/// </summary>
public static class Localization
{
    private static readonly Dictionary<string, string> FaToEn = new(StringComparer.Ordinal)
    {
        ["فارسی"] = "Persian", ["English"] = "English", ["زبان برنامه"] = "Application language",
        ["🎨 ظاهر برنامه"] = "🎨 Appearance", ["⚙ رفتار اتصال"] = "⚙ Connection behavior", ["🌐 DNS"] = "🌐 DNS",
        ["خطای سرور: "] = "Server error: ",
        ["آماده اتصال"] = "Ready to connect", ["در حال اتصال..."] = "Connecting...", ["متصل"] = "Connected",
        ["تلاش مجدد برای اتصال..."] = "Reconnecting...", ["احراز هویت ناموفق بود"] = "Authentication failed",
        ["کانکشن‌ها"] = "Connections", ["همه"] = "All", ["＋ Add"] = "＋ Add", ["🎫 اشتراک من"] = "🎫 My subscription",
        ["فعال"] = "Active", ["برای مشاهده وضعیت اشتراک کلیک کنید"] = "Click to view subscription status",
        ["⏳ زمان باقی‌مانده"] = "⏳ Time remaining", ["📦 حجم باقی‌مانده"] = "📦 Data remaining",
        ["🧰 جعبه‌ابزار و عیب‌یابی شبکه"] = "🧰 Network tools & diagnostics", ["تست سرعت اتصال"] = "Connection speed test",
        ["تمدید IP"] = "Renew IP", ["پاک‌سازی DNS"] = "Flush DNS", ["غیرفعال‌کردن پراکسی"] = "Disable proxy",
        ["بستن پروسه‌های گیرکرده"] = "Kill stuck processes", ["تست نشتی DNS"] = "DNS leak test", ["ریست آداپتور"] = "Reset adapter",
        ["🏓 پینگ / Traceroute"] = "🏓 Ping / Traceroute", ["📜 گزارش"] = "📜 Logs", ["تنظیمات"] = "Settings",
        ["ابزارها و گزارش"] = "Tools & logs", ["گزارش کانکشن‌ها"] = "Connection logs", ["🎨 تم برنامه"] = "🎨 Appearance",
        ["⚙ رفتار"] = "⚙ Connection behavior", ["⏱ قطع خودکار در بی‌استفادگی"] = "⏱ Idle disconnect",
        ["دقیقه بی‌استفادگی"] = "minutes idle", ["🔀 اسپلیت تانل (همه کانکشن‌ها)"] = "🔀 Split tunneling (all connections)",
        ["🌐 DNS دلخواه"] = "🌐 DNS", ["📦 پکیج و پیکربندی"] = "📦 Package & configuration",
        ["📶 گزارش کانکشن‌ها"] = "📶 Connection logs", ["ℹ درباره برنامه و اشتراک"] = "ℹ About & subscription",
        ["تیره"] = "Dark", ["روشن"] = "Light", ["سیستم"] = "System",
        ["اتصال هوشمند (امتحان خودکار همه کانکشن‌ها)"] = "Smart connection (try all connections automatically)",
        ["مخفی‌شدن در کنار ساعت به‌جای بستن"] = "Minimize to tray instead of closing", ["اجرای خودکار با ویندوز"] = "Start with Windows",
        ["همیشه متصل"] = "Always connected", ["قطع بعد از"] = "Disconnect after", ["غیرفعال — همه ترافیک از VPN"] = "Off — all traffic through VPN",
        ["Deny — همه از VPN به‌جز موارد لیست"] = "Deny — everything through VPN except listed items",
        ["Allow — فقط موارد لیست از VPN"] = "Allow — only listed items through VPN", ["خودکار"] = "Automatic", ["دلخواه"] = "Custom",
        ["🔧 به‌روزرسانی base.ovpn از فایل"] = "🔧 Update base.ovpn from file", ["📥 ایمپورت پکیج / فایل ovpn"] = "📥 Import package / ovpn file",
        ["📤 اکسپورت / بک‌اپ کانکشن‌ها"] = "📤 Export / backup connections", ["🗑 پاکسازی"] = "🗑 Clear",
        ["روشن کردن"] = "Start", ["توقف"] = "Stop", ["راه‌اندازی مجدد"] = "Restart", ["بازگشت"] = "Back",
        ["VPN WIFI-Direct"] = "VPN WIFI-Direct", ["کانکشن VPN فعال:"] = "Active VPN connection:",
        ["کارت شبکه اینترنت:"] = "Internet adapter:", ["در حال بررسی..."] = "Checking...", ["منتظر اتصال پایدار VPN..."] = "Waiting for a stable VPN connection...",
        ["📱 اسکن برای اتصال سریع"] = "📱 Scan for quick connection",
        ["نام"] = "Name", ["نوع"] = "Type", ["آدرس سرور"] = "Server address", ["پورت (اختیاری)"] = "Port (optional)",
        ["پروتکل"] = "Protocol", ["نام کاربری"] = "Username", ["رمز عبور"] = "Password", ["تأیید"] = "OK", ["انصراف"] = "Cancel",
        ["کلید مشترک (PSK)"] = "Pre-shared key (PSK)", ["یا فیلدها را دستی پر کنید"] = "Or fill in the fields manually",
        ["DNS  (اختیاری)"] = "DNS (optional)", ["MTU  (اختیاری)"] = "MTU (optional)", ["Preshared Key  (اختیاری)"] = "Preshared Key (optional)",
        ["Allowed IPs"] = "Allowed IPs", ["Keepalive (ثانیه — اختیاری)"] = "Keepalive (seconds — optional)",
        ["پارامترهای اضافی (اختیاری)"] = "Additional parameters (optional)", ["📂 انتخاب فایل .ovpn"] = "📂 Choose .ovpn file",
        ["📂 ایمپورت از فایل .conf (اختیاری)"] = "📂 Import from .conf file (optional)", ["استعلام زنده وضعیت اشتراک از سرور"] = "Live subscription status from server",
        ["پسورد"] = "Password", ["وضعیت"] = "Status", ["سرویس"] = "Service", ["انقضا"] = "Expires", ["زمان باقی"] = "Time left",
        ["ترافیک"] = "Traffic", ["اولین ورود"] = "First login", ["وضعیت اتصال"] = "Connection status", ["نیاز به تمدید دارید؟"] = "Need to renew?",
        ["خرید / تمدید اشتراک"] = "Buy / renew subscription", ["📊 استعلام وضعیت"] = "📊 Check status", ["ذخیره یوزر/پس برای همه کانکشن‌های رسمی"] = "Save credentials for all official connections",
        ["وضعیت اشتراک"] = "Subscription status", ["Click to Connect"] = "Click to connect", ["Details"] = "Details", ["User"] = "User", ["Session"] = "Session", ["Tunnel IP"] = "Tunnel IP", ["Server IP"] = "Server IP", ["Protocol"] = "Protocol", ["Ping"] = "Ping",
        ["برای کپی کلیک کنید"] = "Click to copy", ["افزودن کانکشن جدید"] = "Add a new connection", ["مشاهده / استعلام وضعیت اشتراک"] = "View / check subscription status",
        ["بروزرسانی وضعیت اشتراک"] = "Refresh subscription status", ["بستن جزئیات"] = "Hide details", ["نمایش جزئیات"] = "Show details",
        ["کارت شبکه اینترنت (VPN) یافت نشد."] = "VPN internet adapter was not found.", ["اخطار"] = "Warning",
        ["صفحه اصلی"] = "Home", ["باز کردن پنجره Hotspot"] = "Open Hotspot window", ["روشن/خاموش اتصال"] = "Toggle connection",
        ["برای قطع اتصال کلیک کنید"] = "Click to disconnect",
        ["Server Override (اختیاری)"] = "Server Override (optional)", ["───  Interface  ───"] = "─── Interface ───", ["───  Peer  ───"] = "─── Peer ───",
        ["Address  *  (e.g. 10.0.0.2/32)"] = "Address * (e.g. 10.0.0.2/32)", ["AmneziaWG"] = "AmneziaWG",
        ["  — افزودن فیلدهای obfuscation"] = " — add obfuscation fields", ["───  AmneziaWG Obfuscation  ───"] = "─── AmneziaWG Obfuscation ───",
        ["Private Key  *"] = "Private Key *", ["Public Key  *"] = "Public Key *", ["Endpoint  *  (host:port)"] = "Endpoint * (host:port)",
        ["Kill Switch"] = "Kill Switch",
        
        // --- موارد جدید اضافه شده برای دیالوگ‌ها و پیام‌ها ---
        ["نسخه "] = "Version ",
        ["💳 اشتراک"] = "💳 Subscription",
        ["بستن"] = "Close",
        ["باشه"] = "OK",
        ["بله"] = "Yes",
        ["خیر"] = "No",
        ["کانکشن"] = "Connection",
        ["فایل .ovpn معتبر نیست."] = "Invalid .ovpn file.",
        ["پروفایل TAP پشتیبانی نمی‌شود."] = "TAP profile is not supported.",
        ["نام الزامی است."] = "Name is required.",
        ["کانفیگ: بارگذاری شده ✔"] = "Config: Loaded ✔",
        ["کانفیگ بارگذاری شد: "] = "Config loaded: ",
        ["فایل .conf معتبر نیست."] = "Invalid .conf file.",
        ["فایل .conf معتبر نیست (باید شامل [Interface] و [Peer] باشد)."] = "Invalid .conf file (must contain [Interface] and [Peer]).",
        ["لطفاً Private Key، Address، Public Key و Endpoint را پر کنید."] = "Please fill in Private Key, Address, Public Key, and Endpoint.",
        ["نام و آدرس سرور الزامی است."] = "Name and server address are required.",
        ["انتخاب فایل .conf"] = "Select .conf file",
        ["نام کاربری و پسورد را وارد کنید."] = "Please enter username and password.",
        ["در حال استعلام از پورتال..."] = "Checking portal...",
        ["ارتباط با سرور برقرار نشد. اینترنت خود را بررسی کنید و دوباره تلاش کنید."] = "Connection failed. Check your internet and try again.",
        ["نام کاربری یا پسورد اشتباه است."] = "Incorrect username or password.",
        ["فعال ✅"] = "Active ✅",
        ["منقضی ❌"] = "Expired ❌",
        ["آنلاین ✅"] = "Online ✅",
        ["آفلاین ⚫"] = "Offline ⚫",
        ["⏳ در حال استعلام..."] = "⏳ Checking...",
        ["گیگ"] = "GB",
        ["نامحدود"] = "Unlimited",
        ["عدم اتصال"] = "Disconnected",
        ["قطع شده"] = "Disconnected",
        ["اتصال VPN"] = "VPN connection",
        ["پیدا نشد"] = "Not found",
        ["خطا در روشن شدن هات‌اسپات!"] = "Failed to start hotspot!",
        ["کارت شبکه Wi-Fi Direct یافت نشد."] = "Wi-Fi Direct adapter not found.",
        ["خطا در برقراری شیرینگ!"] = "Failed to share connection!",
        ["✅ هات‌اسپات فعال شد و ترافیک در جریان است."] = "✅ Hotspot is active and traffic is flowing.",
        ["هات‌اسپات متوقف شد."] = "Hotspot stopped.",
        ["در حال راه‌اندازی مجدد..."] = "Restarting...",
        ["راه‌اندازی مجدد ناموفق بود."] = "Restart failed.",
        ["خطا: "] = "Error: ",
        ["هیچ دستگاهی متصل نیست."] = "No devices connected.",
        ["در حال بررسی دستگاه‌های متصل..."] = "Checking connected devices...",
        ["امکان دریافت فهرست دستگاه‌ها وجود ندارد."] = "Cannot retrieve device list.",
        ["اتصال VPN قطع شد. هات‌اسپات متوقف گردید."] = "VPN disconnected. Hotspot stopped.",
        ["برای استفاده از هات‌اسپات ابتدا به VPN متصل شوید."] = "Connect to VPN first to use hotspot.",
        ["در حال توقف هات‌اسپات..."] = "Stopping hotspot...",
        ["در حال راه‌اندازی مجدد هات‌اسپات..."] = "Restarting hotspot...",
        ["مرحله ۱: راه‌اندازی هات‌اسپات..."] = "Step 1: Starting hotspot...",
        ["مرحله ۲: تثبیت شبکه وای‌فای دایرکت... "] = "Step 2: Stabilizing Wi-Fi Direct... ",
        ["مرحله ۳: برقراری پل ارتباطی با "] = "Step 3: Bridging with ",
        ["✅ شبکه تثبیت شد. آماده راه‌اندازی هات‌اسپات."] = "✅ Network stabilized. Ready to start hotspot.",
        ["⏳ در حال تثبیت شبکه و درایورهای مجازی... "] = "⏳ Stabilizing network and virtual drivers... ",
        ["هیچ کانکشنی برای اتصال وجود ندارد."] = "No connection available to connect.",
        ["نام کاربری یا رمز عبور اشتباه است — یا ممکن است اشتراک شما به پایان رسیده باشد."] = "Incorrect username/password, or your subscription has expired.",
        ["بررسی اشتراک"] = "Check subscription",
        ["برای اتصال از طریق SSTP یا IKEv2 لازم است گواهی امنیتی NETFASTVIP یک‌بار روی ویندوز نصب شود. نصب شود؟"] = "SSTP/IKEv2 requires the security certificate to be installed. Install now?",
        ["نصب گواهی"] = "Install certificate",
        ["حالا نه"] = "Not now",
        ["بدون نصب گواهی، اتصال SSTP / IKEv2 ممکن نیست."] = "SSTP/IKEv2 connection is not possible without the certificate.",
        ["گواهی CA با موفقیت نصب شد."] = "CA certificate installed successfully.",
        ["نصب گواهی CA انجام نشد — کانکشن‌های SSTP/IKEv2 از این اتصال کنار گذاشته شدند."] = "CA installation failed — SSTP/IKEv2 connections excluded.",
        ["نصب گواهی CA رد شد — کانکشن‌های SSTP/IKEv2 از این اتصال کنار گذاشته شدند."] = "CA installation skipped — SSTP/IKEv2 connections excluded.",
        ["فایل base.ovpn موجود نیست."] = "base.ovpn file is missing.",
        ["این کانکشن حذف شود؟"] = "Delete this connection?",
        ["ایمپورت انجام شد."] = "Import completed.",
        ["پکیج اکسپورت شد."] = "Package exported.",
        ["ورود اطلاعات کاربری"] = "Enter credentials",
        ["اتصال برقرار شد"] = "Connection established",
        ["اتصال مجدد برقرار شد"] = "Reconnection established",
        ["اتصال قطع شد — تلاش مجدد..."] = "Connection lost — reconnecting...",
        ["اتصال قطع شد."] = "Disconnected.",
        ["هنگام اتصال نمی‌توان کانکشن‌ها را ویرایش یا حذف کرد. ابتدا اتصال را قطع کنید."] = "Cannot edit or delete connections while connected. Please disconnect first.",
        ["بک‌اپ کانکشن‌ها چطور ساخته شود؟"] = "How should the backup be created?",
        ["همراه یوزر/پسورد"] = "With credentials",
        ["بدون یوزر/پسورد"] = "Without credentials",
        ["به‌دلیل بی‌استفادگی، اتصال قطع شد."] = "Disconnected due to inactivity.",
        ["قطع خودکار پس از "] = "Auto disconnected after ",
        ["نمایش برنامه"] = "Show application",
        ["خروج"] = "Exit",
        ["برنامه کنار ساعت باز است — برای خروج کامل، روی آیکون راست‌کلیک کنید و «خروج» را بزنید."] = "App is running in the tray. Right-click the icon and select Exit to close completely.",
        ["IP خروجی تغییر کرد: "] = "Exit IP changed: ",
        ["اتصال VPN فعال است — با خروج از برنامه قطع می‌شود. خارج شوید؟"] = "VPN connection is active — it will be disconnected upon exit. Are you sure?",
        ["برای اعمال زبان جدید، برنامه باید دوباره اجرا شود. اکنون راه‌اندازی مجدد شود؟"] = "The application must be restarted to apply the new language. Restart now?",
        ["دستگاه‌های متصل: "] = "Connected devices: ",

        // --- Hotspot status strings ---
        ["آماده راه‌اندازی هات‌اسپات."] = "Hotspot ready.",
        ["✅ شبکه تثبیت شد. در حال شناسایی خودکار کارت‌ها..."] = "✅ Network stabilized. Detecting adapters...",
        ["مرحله ۱: راه‌اندازی هات‌اسپات..."] = "Step 1: Starting hotspot...",
        ["مرحله ۲: تثبیت شبکه وای‌فای دایرکت... "] = "Step 2: Stabilizing Wi-Fi Direct... ",
        ["در حال آزادسازی کارت‌ها و توقف هات‌اسپات..."] = "Releasing adapters and stopping hotspot...",
        ["آماده راه‌اندازی مجدد هات‌اسپات."] = "Ready to restart hotspot.",
        ["در انتظار روشن شدن هات‌اسپات..."] = "Waiting for hotspot to start...",

        // --- ConnectionDialog ---
        ["embedded profile: yes"] = "embedded profile: yes",
        ["embedded profile: "] = "embedded profile: ",
        ["کانفیگ: بارگذاری شده ✔"] = "Config: Loaded ✔",

        // --- MainWindow dynamic ---
        ["برای قطع اتصال کلیک کنید"] = "Click to disconnect",
        ["Click to Disconnect"] = "برای قطع اتصال کلیک کنید",

        // --- Hotspot / Tools additional ---
        ["وضعیت VPN: متصل نیست (غیرفعال)"] = "VPN status: not connected (inactive)",
        ["آخرین به‌روزرسانی: "] = "Last updated: ",
        ["نامشخص"] = "Unknown",
        ["Last Handshake"] = "Last Handshake",
        ["User"] = "User",

        // --- WireGuard type detection labels ---
        ["نوع شناسایی‌شده: AmneziaWG"] = "Detected type: AmneziaWG",
        ["نوع شناسایی‌شده: WireGuard"] = "Detected type: WireGuard",
        ["نوع: AmneziaWG"] = "Type: AmneziaWG",
        ["نوع: WireGuard"] = "Type: WireGuard",

        // --- MainWindow.xaml.cs / HotspotWindow ---
        ["قطع شده"] = "Disconnected",
        ["اتصال VPN"] = "VPN connection",
        ["پیدا نشد"] = "Not found",
        ["در حال ایجاد کارت شبکه..."] = "Creating network adapter...",
        ["ساخته نشد"] = "Not created",
        ["ایجاد"] = "Create",
        ["کارت شبکه VPN متصل یافت نشد"] = "VPN network adapter not found",
        ["کارت شبکه Wi-Fi Direct ساخته نشد"] = "Wi-Fi Direct adapter not created",
        ["وضعیت VPN: متصل"] = "VPN status: connected",
        ["ثانیه"] = "sec",
        ["مرحله ۲: تثبیت شبکه وای‌فای دایرکت... "] = "Step 2: Stabilizing Wi-Fi Direct... ",
        ["مرحله ۳: برقراری پل ارتباطی با "] = "Step 3: Bridging with ",
        ["⏳ در حال تثبیت شبکه و درایورهای مجازی... "] = "⏳ Stabilizing network and virtual drivers... ",

        // --- MainWindow.Tools.cs ---
        ["منقضی"] = "Expired",
        ["رو به اتمام"] = "Expiring soon",
        ["منقضی شده ❌"] = "Expired ❌",
        ["گیگ"] = "GB",
        ["ساعت"] = "hour(s)",
        ["{0} روز و {1} ساعت"] = "{0} day(s) and {1} hour(s)",

        // --- CredentialsDialog ---
        ["نام کاربری و پسورد اجباری‌ان."] = "Username and password are required.",
        ["وارد کردن PSK اجباری است."] = "PSK is required.",

        // --- ConnectionDialog ---
        ["انتخاب فایل .conf"] = "Select .conf file",
        ["کانفیگ بارگذاری شد: "] = "Config loaded: ",

        // --- UpdateChecker ---
        ["نسخه جدید {0} منتشر شده است (نسخه فعلی شما: {1}).{2}\nصفحه دانلود باز شود؟"] =
            "New version {0} is available (your version: {1}).{2}\nOpen download page?",

        // --- FirstRunSetup (از طریق AskDialog.T پردازش می‌شوند) ---
        ["برنامه از داخل ZIP یا پوشه موقت اجرا شده است.\n"] = "The app is running from a ZIP or temp folder.\n",
        ["ابتدا آن را در یک پوشه ثابت (مثل C:\\NETFASTVIP) استخراج کنید و از آنجا اجرا کنید."] =
            "Please extract it to a permanent folder (e.g. C:\\NETFASTVIP) and run it from there.",
        ["پوشه فعلی قابل نوشتن نیست — تنظیمات ذخیره نمی‌شود.\n"] = "Current folder is read-only — settings won't be saved.\n",
        ["پوشه برنامه را به مسیری مثل C:\\NETFASTVIP یا داخل پوشه کاربری منتقل کنید."] =
            "Move the app folder to a writable location such as C:\\NETFASTVIP.",
        ["نصب خودکار و بی‌صداست. الان نصب شود؟"] = "Installation is automatic and silent. Install now?",

        // --- HeaderTitle ---
        ["ابزارها و گزارش"] = "Tools & logs",
        ["گزارش کانکشن‌ها"] = "Connection logs",

        // --- Hotspot inline ---
        ["✅ شبکه تثبیت شد. آماده راه‌اندازی هات‌اسپات."] = "✅ Network stabilized. Ready to start hotspot.",
        ["✅ هات‌اسپات فعال شد و ترافیک در جریان است."] = "✅ Hotspot active and traffic is flowing.",
        ["در حال توقف هات‌اسپات..."] = "Stopping hotspot...",
        ["در حال راه‌اندازی مجدد هات‌اسپات..."] = "Restarting hotspot...",
        ["در حال بررسی دستگاه‌های متصل..."] = "Checking connected devices...",
        ["امکان دریافت فهرست دستگاه‌ها وجود ندارد."] = "Cannot retrieve device list.",
        ["عدم اتصال"] = "Not connected",

        // --- Ping / Traceroute buttons ---
        ["⏹ توقف پینگ"] = "⏹ Stop ping",
        ["🏓 شروع پینگ"] = "🏓 Start ping",
        ["⏹ توقف Traceroute"] = "⏹ Stop traceroute",
        ["🛰 شروع Traceroute"] = "🛰 Start traceroute",

        // --- Connections list ---
        ["نمایش {0} مورد دیگر"] = "Show {0} more",
        ["🗑 پاکسازی"] = "🗑 Clear",
        ["اخطار"] = "Warning",
        ["اخطار امنیتی"] = "Security warning",
        ["خطا"] = "Error",
        ["خطای اشتراک‌گذاری"] = "Sharing error",
        ["ابتدا به VPN متصل شوید."] = "Please connect to VPN first.",
        ["کارت شبکه اینترنت (VPN) یافت نشد."] = "VPN network adapter not found.",
        ["فایل .conf معتبر نیست."] = "Invalid .conf file.",
        ["فایل .conf معتبر نیست (باید شامل [Interface] و [Peer] باشد)."] = "Invalid .conf file (must contain [Interface] and [Peer]).",
        ["لطفاً Private Key، Address، Public Key و Endpoint را پر کنید."] = "Please fill in Private Key, Address, Public Key, and Endpoint.",
        ["نام و آدرس سرور الزامی است."] = "Name and server address are required.",
        ["ℹ درباره برنامه و اشتراک"] = "ℹ About & subscription",

        // --- MoreInfo button ---
        ["بستن جزئیات"] = "Hide details",
        ["نمایش جزئیات"] = "Show details",
    };

    private static readonly Dictionary<string, string> EnToFa = BuildReverse();

    private static Dictionary<string, string> BuildReverse()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in FaToEn)
            if (!result.ContainsKey(pair.Value)) result[pair.Value] = pair.Key;
        return result;
    }
    private static readonly Dictionary<Window, DispatcherTimer?> Watches = new();

    public static string CurrentLanguage { get; private set; } = "fa";
    public static bool IsEnglish => CurrentLanguage == "en";

    public static void SetLanguage(string? language) => CurrentLanguage = language?.Equals("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "fa";

    public static string T(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // ۱. اول متن را به فارسی نرمال‌سازی کن (اگر قبلاً ترجمه شده بود برگردان)
        var fa = EnToFa.TryGetValue(text, out var back) ? back : text;

        // ۲. اگر فارسی خواستیم، همان نسخه فارسی کافی است
        if (!IsEnglish) return fa;

        // ۳. اگر انگلیسی خواستیم، از فارسی به انگلیسی ترجمه کن
        if (FaToEn.TryGetValue(fa, out var en)) return en;

        // ۴. Prefix matching برای پیام‌های داینامیک مثل «خطا: ...»
        foreach (var pair in FaToEn.OrderByDescending(x => x.Key.Length))
        {
            if (pair.Key.EndsWith(" ", StringComparison.Ordinal) && fa.StartsWith(pair.Key, StringComparison.Ordinal))
                return pair.Value + fa[pair.Key.Length..];
        }

        return fa; // اگر ترجمه‌ای نبود، متن فارسی را برگردان
    }

    public static void Watch(Window window)
    {
        if (Watches.ContainsKey(window)) return;
        Apply(window);
        // تایمر حذف شد — Apply فقط موقع تغییر زبان (ApplyToAllWindows) صدا زده می‌شود
        // نگه‌داری window برای ApplyToAllWindows
        Watches[window] = null!;
        window.Closed += (_, _) => Watches.Remove(window);
    }

    public static void ApplyToAllWindows()
    {
        foreach (Window window in Application.Current.Windows) Apply(window);
    }

    private static void Apply(DependencyObject root)
    {
        try
        {
            if (root is Window window)
            {
                window.FlowDirection = IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
                if (!string.IsNullOrWhiteSpace(window.Title)) window.Title = T(window.Title);
            }
            if (root is TextBlock textBlock)
            {
                if (textBlock.Inlines.Count == 0 && !string.IsNullOrEmpty(textBlock.Text))
                    textBlock.Text = T(textBlock.Text);
                else
                    foreach (var run in textBlock.Inlines.OfType<Run>())
                        if (!string.IsNullOrEmpty(run.Text)) run.Text = T(run.Text);
            }
            if (root is ContentControl content && content.Content is string text && text.Length > 0)
                content.Content = T(text);
            if (root is HeaderedContentControl header && header.Header is string headerText && headerText.Length > 0)
                header.Header = T(headerText);
            if (root is FrameworkElement element && element.ToolTip is string tip && tip.Length > 0)
                element.ToolTip = T(tip);
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                Apply(child);
        }
        catch { /* هر خطایی در ترجمه بی‌صدا رد می‌شود تا برنامه crash نکند */ }
    }
}