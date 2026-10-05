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
        ["وضعیت اشتراک"] = "Subscription status", ["Click to Connect"] = "Click to connect", ["جزئیات"] = "Details", ["کاربر"] = "User", ["سشن"] = "Session", ["IP تونل"] = "Tunnel IP", ["IP سرور"] = "Server IP", ["پروتکل"] = "Protocol", ["پینگ"] = "Ping",
        ["برای کپی کلیک کنید"] = "Click to copy", ["افزودن کانکشن جدید"] = "Add a new connection", ["مشاهده / استعلام وضعیت اشتراک"] = "View / check subscription status",
        ["بروزرسانی وضعیت اشتراک"] = "Refresh subscription status", ["بستن جزئیات"] = "Hide details", ["نمایش جزئیات"] = "Show details",
        ["کارت شبکه اینترنت (VPN) یافت نشد."] = "VPN internet adapter was not found.", ["اخطار"] = "Warning",
        ["صفحه اصلی"] = "Home", ["باز کردن پنجره Hotspot"] = "Open Hotspot window", ["Click To Connect"] = "Click To Connect",
        ["Click to disconnect"] = "Click to disconnect",
        ["Server Override (اختیاری)"] = "Server Override (optional)", ["───  Interface  ───"] = "─── Interface ───", ["───  Peer  ───"] = "─── Peer ───",
        ["Address  *  (e.g. 10.0.0.2/32)"] = "Address * (e.g. 10.0.0.2/32)", ["AmneziaWG"] = "AmneziaWG",
        ["  — افزودن فیلدهای obfuscation"] = " — add obfuscation fields", ["───  AmneziaWG Obfuscation  ───"] = "─── AmneziaWG Obfuscation ───",
        ["Private Key  *"] = "Private Key *", ["Public Key  *"] = "Public Key *", ["Endpoint  *  (host:port)"] = "Endpoint * (host:port)",
        ["Kill Switch"] = "Kill Switch",
        
        // --- موارد جدید اضافه شده برای دیالوگ‌ها و پیام‌ها ---
        ["نسخه "] = "Version ",
        ["💳 اشتراک"] = "💳 Subscription",
        ["بستن"] = "Close",
        ["تست کامل"] = "Full Test", ["⬇ دانلود"] = "⬇ Download", ["⬆ آپلود"] = "⬆ Upload",
        ["موتور تست:"] = "Test engine:", ["پینگ"] = "Ping", ["جیتر"] = "Jitter",
        ["دانلود"] = "Download", ["آپلود"] = "Upload", ["تست سرعت — {0}"] = "Speed test — {0}",
        ["تست از طریق: {0} — متصل به VPN"] = "Test via: {0} — connected to VPN",
        ["تست بدون VPN — اینترنت مستقیم شما"] = "Test without VPN — direct internet",
        ["برای شروع، یکی از دو تست را انتخاب کنید"] = "Choose one of the two tests to start",
        ["🏓 در حال اندازه‌گیری پینگ و جیتر…"] = "🏓 Measuring ping and jitter…",
        ["⬇ در حال تست دانلود…"] = "⬇ Testing download…",
        ["⬆ در حال تست آپلود…"] = "⬆ Testing upload…",
        ["✓ تست کامل شد"] = "✓ Test completed", ["تست لغو شد"] = "Test canceled",
        ["خطا در تست — اتصال اینترنت را بررسی کنید"] = "Test failed — check your internet connection",
        ["خطا در اتصال به سرور M-Lab — ممکن است در شبکه فعلی مسدود باشد؛ Cloudflare را امتحان کنید"] = "Could not connect to the M-Lab server — it may be blocked on this network; try Cloudflare",
        ["عالی"] = "Excellent", ["خوب"] = "Good", ["متوسط"] = "Fair", ["ضعیف"] = "Poor",
        ["لغو"] = "Cancel",
        ["⚠️ هر فاز حداکثر ۱۰ ثانیه اجرا می‌شود و از حجم اشتراک شما مصرف می‌کند — نتیجه، بالاترین سرعت ثبت‌شده در همین بازه است"] = "⚠️ Each phase runs for up to 10 seconds and uses your subscription traffic — the result is the highest speed recorded during this period",
        ["⚠️ هر فاز حداکثر ۱۰ ثانیه اجرا می‌شود و از حجم اشتراک شما مصرف می‌کند — نتیجه، میانگین سرعت واقعی در همین بازه است"] = "⚠️ Each phase runs for up to 10 seconds and uses your subscription traffic — the result is the average speed recorded during this period",
        ["تست از طریق: {0} — متصل به Xray"] = "Test via: {0} — connected to Xray",
        ["خطا در دریافت سرور Speedtest — لطفاً Cloudflare را امتحان کنید"] = "Could not resolve Speedtest server — please try Cloudflare",
        ["Speedtest.net (Ookla)"] = "Speedtest.net (Ookla)",
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
        ["دستگاه‌های متصل: ۰"] = "Connected devices: 0",

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

        ["فونت برنامه"] = "Application font",
        ["Inter / Segoe UI (مدرن)"] = "Inter / Segoe UI (Modern)",
        ["Segoe UI (ویندوز ۱۱)"] = "Segoe UI (Windows 11)",
        ["Vazirmatn (وزیرمتن)"] = "Vazirmatn",

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
        ["بستن جزئیات"] = "Less",
        ["جزئیات بیشتر"] = "More",
        ["نمایش جزئیات"] = "More",

        // --- Settings & Per-App additions ---
        ["تم برنامه"] = "App theme",
        ["تونل برنامه‌ها — Per-App (مخصوص Xray / پروکسی)"] = "Per-App Tunneling (Xray / Proxy)",
        ["تونل کل سیستم (همه برنامه‌ها)"] = "Full system tunnel (all apps)",
        ["فقط برنامه‌های لیست (Allow)"] = "Only listed apps (Allow)",
        ["همه به جز برنامه‌های لیست (Bypass)"] = "All except listed apps (Bypass)",
        ["⚙️ مدیریت لیست برنامه‌ها"] = "⚙️ Manage apps list",
        ["مدیریت لیست برنامه‌ها"] = "Manage apps list",
        ["AdGuard — 94.140.14.14 (مسدودساز تبلیغات)"] = "AdGuard — 94.140.14.14 (Ad blocker)",
        ["AdGuard Family — 94.140.14.15 (امنیت خانواده)"] = "AdGuard Family — 94.140.14.15 (Family protection)",
        ["مدیریت برنامه‌ها"] = "App Management",
        ["مدیریت برنامه‌های تونل (Per-App)"] = "Per-App Tunnel Management",
        ["برنامه‌های زیر بر اساس حالت انتخابی شما تفکیک می‌شوند:"] = "The following apps will be tunneled or bypassed based on your selected mode:",
        ["➕ افزودن برنامه جدید (.exe)"] = "➕ Add New App (.exe)",
        ["انتخاب برنامه"] = "Select Application",
        ["کلیک برای فعال یا غیرفعال کردن"] = "Click to enable or disable",
        ["حذف از لیست"] = "Remove from list",
        ["غیرفعال"] = "Disabled",

        // --- Hamburger Drawer ---
        ["منوی اصلی"] = "Main Menu",
        ["🏠 داشبورد اصلی"] = "🏠 Main Dashboard",
        ["داشبورد اصلی"] = "Main Dashboard",
        ["🚀 سرویس Xray"] = "🚀 Xray Service",
        ["سرویس Xray"] = "Xray Service",
        ["📄 لاگ اتصال"] = "📄 Connection Log",
        ["لاگ اتصال"] = "Connection Log",
        ["🛜 مدیریت Hotspot"] = "🛜 Hotspot Management",
        ["مدیریت Hotspot"] = "Hotspot Management",
        ["🧰 جعبه ابزار"] = "🧰 Toolbox",
        ["جعبه ابزار"] = "Toolbox",
        ["⚙ تنظیمات"] = "⚙ Settings",

        // --- Xray Panel & Subscriptions ---
        ["VPN (سنتی)"] = "VPN (Traditional)",
        ["Xray (پروکسی)"] = "Xray (Proxy)",
        ["＋ افزودن ساب"] = "＋ Add Subscription",
        ["🔄 بروزرسانی"] = "🔄 Update Servers",
        ["⚡ پینگ سرورها"] = "⚡ Ping Servers",
        ["🗑️ پاک کردن سرورها"] = "🗑️ Clear Servers",
        ["اشتراک من"] = "My Subscription",
        ["حجم باقی‌مانده"] = "Data Remaining",
        ["زمان باقی‌مانده"] = "Time Remaining",
        ["جزئیات اتصال"] = "Connection Details",
        ["یک کانکشن انتخاب کنید"] = "Select a connection",
        ["یک سرور انتخاب کنید"] = "Select a server",
        ["در حال شناسایی موقعیت..."] = "Detecting location...",
        ["در حال برقراری ارتباط با هسته..."] = "Connecting to core...",
        ["اتصال به سرور"] = "Connect to server",
        ["تست پینگ"] = "Test ping",
        ["ویرایش کانکشن"] = "Edit connection",
        ["حذف کانکشن"] = "Delete connection",
        ["حذف سرور انتخاب‌شده یا پاک کردن همه سرورها"] = "Delete selected server or clear all servers",
        ["تست پینگ همه سرورها"] = "Ping all servers",
        ["بروزرسانی وضعیت اشتراک و سرورها"] = "Update subscription and servers",
        ["افزودن لینک اشتراک یا کانکشن جدید"] = "Add subscription link or new connection",

        ["اشتراک‌گذاری (کد QR و لینک)"] = "Share (QR Code & Link)",
        ["📱 اشتراک‌گذاری کانکشن"] = "📱 Share Connection",
        ["اسکن با دوربین یا اپ‌های موبایل (v2rayNG, Streisand, sing-box...)"] = "Scan with camera or mobile apps (v2rayNG, Streisand, sing-box...)",
        ["📋 کپی لینک کانکشن"] = "📋 Copy Connection Link",
        ["Hysteria2"] = "Hysteria2",
        ["Hysteria"] = "Hysteria",
        ["TUIC"] = "TUIC",
        ["نام گروه / اشتراک (اختیاری):"] = "Group / subscription name (optional):",
        ["＋ Add Subscription"] = "＋ Add Subscription",
        [" بروزرسانی"] = " Update",
        [" پینگ همه"] = " Ping all",
        ["بروزرسانی این اشتراک"] = "Update this subscription",
        ["تست پینگ این گروه"] = "Ping this group",
        ["حذف این گروه اشتراک"] = "Delete this subscription group",
        ["آیا از حذف گروه «{0}» و تمام کانکشن‌های آن اطمینان دارید؟"] = "Are you sure you want to delete group \"{0}\" and all its connections?",
        ["آیا از پاک کردن تمام سرورها و گروه‌های اشتراک اطمینان دارید؟"] = "Are you sure you want to delete all servers and subscription groups?",
        ["کانکشن‌های من"] = "My Connections",

        // --- In-App Sub & Message Modals ---
        ["🔗 افزودن لینک اشتراک Xray"] = "🔗 Add Xray Subscription Link",
        ["لینک سابسکریپشن یا کانکشن خود را وارد نمایید:"] = "Enter your subscription or connection link:",
        ["📋 چسباندن"] = "📋 Paste",
        ["چسباندن از کلیپ‌بورد"] = "Paste from clipboard",
        ["ثبت و دریافت"] = "Submit & fetch",
        ["پیام سیستم"] = "System message",
        ["راهنمای اتصال"] = "Connection guide",
        ["خطای اتصال"] = "Connection error",
        ["پاک کردن تمام سرورها"] = "Clear all servers",
        ["حذف سرور انتخاب‌شده"] = "Delete selected server",
        ["بله، حذف همه"] = "Yes, delete all",
        ["تمام سرورها با موفقیت حذف شدند."] = "All servers cleared successfully.",
        ["حذف سرورها"] = "Clear servers",
        ["هیچ سروری در لیست وجود ندارد."] = "No servers in list.",
        ["امکان حذف کانکشنی که در حال حاضر متصل است وجود ندارد.\nابتدا اتصال را قطع کنید."] = "Cannot delete an active connected server.\nPlease disconnect first.",
        ["امکان حذف کانکشن فعال وجود ندارد."] = "Cannot delete active connection.",
        ["لطفاً یک کانکشن از لیست انتخاب کنید یا از دکمه «افزودن ساب» استفاده نمایید."] = "Please select a connection from the list or use the Add Subscription button.",

        // --- Tooltips & Windows controls ---
        ["همیشه بالا"] = "Always on top",
        ["بازگشت به حالت عادی"] = "Restore to normal",
        ["کمینه‌سازی"] = "Minimize",
        ["باز کردن تنظیمات شبکه ویندوز (ncpa.cpl) 🔗"] = "Open Windows network connections (ncpa.cpl) 🔗",
        ["هدف دلخواه (IP یا دامنه)"] = "Target (IP or domain)",
        ["اندازه‌گیری پینگ، جیتر و سرعت دانلود/آپلود"] = "Measure ping, jitter, download/upload speed",
        ["وقتی شبکه وصل است ولی اینترنت ندارید"] = "When connected but no internet",
        ["وقتی بعد از تغییر VPN سایت‌ها باز نمی‌شوند"] = "When websites don't open after VPN change",
        ["وقتی پراکسی قدیمی، مرورگر را خراب کرده است"] = "When old proxy broke browser",
        ["وقتی اتصال قبلی گیر کرده و قطع نمی‌شود"] = "When previous connection is stuck",
        ["بررسی اینکه DNS شما هم از داخل تانل رد می‌شود یا نشت دارد"] = "Check if your DNS is leaking",
        ["غیرفعال و فعال‌کردن کارت شبکه"] = "Disable and re-enable network adapter",
        ["برای اتصال کلیک کنید"] = "Click to connect",
        
        // --- Dashboard & Navigation (Fluent Glassmorphism) ---
        ["داشبورد"] = "Dashboard",
        ["مرکز کنترل"] = "Control Center",
        ["هسته VPN"] = "VPN Core",
        ["هسته Xray"] = "Xray Core",
        ["تست سرعت"] = "Speedtest",
        ["ابزارهای شبکه"] = "Network Tools",
        ["لاگ کانکشن‌ها"] = "Connection Logs",
        ["کنترل اتصال"] = "Connection Control",
        ["مدت زمان اتصال"] = "Duration",
        ["سرعت لحظه‌ای"] = "Current Speed",
        ["تاخیر / پینگ"] = "Latency / Ping",
        ["حجم مصرفی سشن"] = "Session Traffic",
        ["🌐 آی‌پی تونل"] = "🌐 Tunnel IP",
        ["🖥️ آی‌پی سرور"] = "🖥️ Server IP",
        ["🔒 پروتکل"] = "🔒 Protocol",
        ["👤 نام کاربری"] = "👤 Username",
        ["📈 نمودار زنده ترافیک"] = "📈 Live Traffic Graph",
        ["نمودار زنده ترافیک"] = "Live Traffic Graph",
        ["⭐ سرورهای منتخب و اخیر"] = "⭐ Favorite & Recent Servers",
        ["اتصال سریع با یک کلیک به آخرین کانکشن‌ها"] = "1-Click quick connect to recent connections",
        ["🛡️ سرورهای VPN Core"] = "🛡️ VPN Core Servers",
        ["⚡ سرورهای Xray Core"] = "⚡ Xray Core Servers",
        ["🛡️ اشتراک VPN Core"] = "🛡️ VPN Core Subscription",
        ["⚡ اشتراک Xray Core"] = "⚡ Xray Core Subscription",
        ["بروزرسانی اشتراک VPN"] = "Update VPN Subscription",
        ["بروزرسانی اشتراک Xray"] = "Update Xray Subscription",
        ["زمان باقی‌مانده"] = "Time Remaining",
        ["حجم باقی‌مانده"] = "Data Remaining",
        ["هنوز سروری اضافه نشده است"] = "No servers added yet",
        ["هنوز سروری انتخاب نشده است"] = "No server selected yet",
        ["انتخاب"] = "Select",
        ["📡 لاگ ابزارها و پینگ/تریس"] = "📡 Diagnostics & Tools Log",
        ["روشن"] = "Light",
        ["خاموش"] = "OFF",
        ["متصل و ایمن"] = "Connected & Secure",
        ["شروع اتصال"] = "Connect",
        ["قطع اتصال"] = "Disconnect",
        ["مشاهده جزئیات بیشتر ⌵"] = "More Details ⌵",
        ["بستن جزئیات ▴"] = "Hide Details ▴",
        ["مشخصات و جزئیات شبکه"] = "Network Details",
        ["جهت اتصال کلیک کنید"] = "Click to connect",
        ["جهت قطع اتصال کلیک کنید"] = "Click to disconnect",
        ["منوی ناوبری (سایدبار)"] = "Sidebar Menu",
        ["سایدبار منو"] = "Sidebar Menu",
        ["سفارشی‌سازی داشبورد"] = "Dashboard Customization",
        ["نمایش سرورهای اخیر در داشبورد"] = "Show Recent Servers on Dashboard",
        ["نمایش بخش سرورهای اخیر در داشبورد"] = "Show Recent Servers on Dashboard",
        ["باز بودن پیش‌فرض سایدبار هنگام اجرای برنامه"] = "Expand Sidebar by default on launch",
        ["📊 داشبورد و سایدبار"] = "📊 Dashboard & Sidebar",
        ["● فعال و متصل"] = "● Connected",
        ["ورود →"] = "Open →",
        ["داشبورد"] = "Dashboard",
        ["نمایش جزئیات فنی شبکه"] = "Show Technical Network Details",
        ["خطا در اتصال"] = "Connection Error",
        ["جهت تلاش مجدد کلیک کنید"] = "Click to retry",
        ["لطفاً شکیبا باشید..."] = "Please wait...",
        ["شروع نشده ⏳"] = "Not Started ⏳",
        ["رو به اتمام"] = "Expiring Soon",
        ["کاربر: "] = "User: ",
        ["کاربر متصل"] = "Connected User",
        ["وضعیت"] = "Status",
        ["زمان"] = "Time",
        ["حجم"] = "Data",
        ["حساب کاربری"] = "User Account",
        ["مشاهده وضعیت حساب و اشتراک"] = "View Account & Subscription Status",
        ["📈 نمودار زنده ترافیک (دانلود / آپلود)"] = "📈 Live Traffic Graph (Download / Upload)",
        ["⚡ انتخاب هسته اتصال"] = "⚡ Connection Core",
        ["انتخاب هسته اتصال"] = "Connection Core",
        ["دسترسی سریع به سرورهای تفکیک‌شده"] = "Quick access to separated servers",
        ["اتصال سریع با یک کلیک به آخرین کانکشن‌ها"] = "1-Click quick connect to recent servers",
        ["یک کانکشن انتخاب کنید"] = "Select a Connection",
        ["دریافت سرورها از پنل"] = "Sync servers from portal",
        ["اشتراک فعال ✅"] = "Active Subscription ✅",
        ["ورود به حساب"] = "Sign In / Account",
        ["ارسال ↑"] = "Upload ↑",
        ["دریافت ↓"] = "Download ↓",
        ["سرعت دریافت ↓"] = "Download ↓",
        ["سرعت دریافت"] = "Download Speed",
        ["سرعت ارسال"] = "Upload Speed",
        ["حجم سشن"] = "Session Data",
        ["More"] = "More",
        ["● متصل - VPN"] = "● Connected (VPN)",
        ["● متصل - Xray"] = "● Connected (Xray)",
        ["★ Favorite & Recent Servers"] = "★ Favorite & Recent Servers",
        ["⭐ سرورهای منتخب و اخیر"] = "★ Favorite & Recent Servers",
        ["اتصال سریع با یک کلیک به آخرین کانکشن‌ها"] = "1-Click quick connect to recent connections",
        ["مشاهده وضعیت"] = "View Status",
        ["⌂ داشبورد"] = "⌂ Dashboard",
        ["کاربر"] = "User",
        ["نمایش / پنهان کردن منو"] = "Toggle sidebar menu",
        ["نمایش / پنهان کردن جزئیات اتصال"] = "Toggle connection details",
        ["VPN Wi-Fi Direct"] = "VPN Wi-Fi Direct",
        ["اشتراک VPN Wi-Fi Direct"] = "VPN Wi-Fi Direct",
        ["اتصال SSH و پاورشل"] = "SSH & PowerShell Connect",
        ["اتصال SSH"] = "SSH Connect",
        ["هاست / IP"] = "Host / IP",
        ["پورت"] = "Port",
        ["نام کاربری"] = "Username",
        ["کنترل‌های سریع امنیتی"] = "Quick Security Controls",
        ["ابزارهای سریع شبکه"] = "Quick Network Tools",
        ["سرورهای منتخب و اخیر"] = "Favorite & Recent Servers",
        ["کیل‌سوئیچ"] = "Kill Switch",
        ["اسپلیت تانل"] = "Split Tunnel",
        ["محافظت DNS"] = "DNS Protection",
        ["فلاش DNS"] = "Flush DNS",
        ["کیل پروسس‌ها"] = "Kill Processes",
        ["کیل پروسس"] = "Kill Procs",
        ["انجام شد!"] = "Done!",
        ["تست سرعت"] = "Speed Test",
        ["کل ترافیک"] = "All Traffic",
        ["هوشمند"] = "Smart",
        ["غیرفعال"] = "Disabled",
        ["خروج و پاکسازی شبکه"] = "Exit & Full Network Cleanup",
        ["خروج سریع"] = "Quick Exit",
        ["آیا از خروج کامل از برنامه اطمینان دارید؟"] = "Are you sure you want to exit the application?",
        ["تمام پروسس‌های VPN و تنظیمات کارت‌های شبکه پاکسازی و ریست خواهند شد."] = "All active VPN network adapters and processes will be cleaned up.",
        ["افزودن دستی کانفیگ Xray"] = "Add Xray Config Manually",
        ["افزودن دستی"] = "Add Manually",
        ["نوع پروتکل"] = "Protocol",
        ["آدرس سرور یا دامنه"] = "Server Address / Domain",
        ["UUID یا رمز عبور"] = "UUID / Password",
        ["نوع انتقال (Transport)"] = "Transport Type",
        ["هاست / SNI"] = "Host / SNI",
        ["مسیر (Path)"] = "Path",
        ["افزودن و ذخیره"] = "Add & Save",
        ["● انتخاب‌شده"] = "● Selected",
        ["انتخاب‌شده"] = "Selected",
        ["خروج از برنامه NETFASTVIP"] = "Exit NETFASTVIP",
        ["خروج از برنامه {0}"] = "Exit {0}",
        ["خروج و پاکسازی کامل شبکه"] = "Exit & Full Network Cleanup",
        ["⚡ خروج سریع"] = "⚡ Quick Exit",
        ["انصراف"] = "Cancel",
        ["آیا از خروج کامل از برنامه اطمینان دارید؟\nبا انتخاب پاکسازی، تمام پروسس‌های VPN و تنظیمات کارت‌های شبکه پاکسازی و به حالت اولیه بازگردانده می‌شوند."] = "Are you sure you want to exit?\nSelecting cleanup will terminate all VPN processes and reset network adapters.",
        ["＋ افزودن دستی"] = "＋ Add Manually",
        ["💻 اتصال SSH"] = "💻 SSH Connect",
        ["⭐ سرورهای اخیر"] = "★ Recent Servers",
        ["🛡️ کنترل‌های امنیت"] = "🛡️ Security Controls",
        ["🧰 ابزارهای شبکه"] = "🧰 Network Tools",
        ["📶 VPN Wi-Fi Direct"] = "📶 VPN Wi-Fi Direct",
        ["پورت (پیش‌فرض 22)"] = "Port (Default 22)",
        ["نام کاربری (پیش‌فرض root)"] = "Username (Default root)",
        ["آدرس سرور / هاست:"] = "Server Address / Host:",
        ["پورت:"] = "Port:",
        ["UUID / رمز عبور (Password):"] = "UUID / Password:",
        ["نام کانکشن (اختیاری):"] = "Connection Name (Optional):",
        ["ثبت و ذخیره کانفیگ"] = "Submit & Save Config",
        ["سرورها"] = "Servers",
        ["امنیت"] = "Security",
        ["ابزارها"] = "Tools",
        ["وضعیت کیل‌سوئیچ، اسپلیت تانل و DNS"] = "Kill switch, split tunnel & DNS status",
        ["فلاش DNS، بستن پروسس‌ها و تست سرعت"] = "Flush DNS, kill processes & speed test",
        ["اتصال سریع با یک کلیک به آخرین کانکشن‌ها"] = "1-Click quick connect to recent connections",
        ["تغییر ویجت: سرورها / امنیت / ابزارها"] = "Switch widget: Servers / Security / Tools",
        ["تغییر زبان برنامه"] = "Application Language",
        ["برای اعمال کامل زبان جدید و تنظیمات چیدمان، پیشنهاد می‌شود برنامه راه‌اندازی مجدد شود.\nاکنون راه‌اندازی مجدد شود؟"] = "To fully apply the new language and layout direction, an application restart is recommended.\nRestart now?",
        ["✓ راه‌اندازی مجدد"] = "✓ Restart Now",
    
        // --- Automatically Added Localizations ---
        ["📋 کپی لاگ"] = "📋 Copy Log",
        ["📂 فایل لاگ"] = "📂 Log File",
        ["🗑 پاک‌سازی"] = "🗑 Clear",
        ["برای اجرای تست پینگ یا تریسرورت، آدرس مقصد را وارد کرده و دکمه را بزنید..."] = "To run ping or traceroute, enter target address and press the button...",
        ["لاگ در کلیپ‌بورد کپی شد"] = "Log copied to clipboard",
        ["لاگ کانکشن‌ها در کلیپ‌بورد کپی شد"] = "Connection logs copied to clipboard",
        ["✓ کپی شد!"] = "✓ Copied!",
        ["سنجش زنده پینگ، جیتر، سرعت دانلود و آپلود"] = "Live measurement of ping, jitter, download & upload speed",
        ["موتور:"] = "Engine:",
        ["📍 اتصال: "] = "📍 Connection: ",
        ["سرور مقصد تست (Test Server)"] = "Target Test Server",
        ["⚠️ هر فاز حداکثر تا ۱۰ ثانیه اجرا می‌شود و از حجم اشتراک استفاده می‌کند."] = "⚠️ Each phase runs for up to 10s and consumes subscription data.",
        ["⚡ تست کامل (Full Test)"] = "⚡ Full Test",
        ["⬇ فقط دانلود"] = "⬇ Download Only",
        ["⬆ فقط آپلود"] = "⬆ Upload Only",
        ["⏹ توقف"] = "⏹ Stop",
        ["آماده شروع تست"] = "Ready to start test",
        ["در حال انتخاب سرور..."] = "Selecting server...",
        ["اجرای تست سرعت"] = "Run Speed Test",
        ["شروع پس از اتصال ⏳"] = "Starts after connection ⏳",
        ["در زمان اتصال فعال، امکان بستن پروسس‌ها وجود ندارد"] = "Disabled while connection is active",
        ["بستن تمام پروسس‌های گیرکرده و ریست آداپتورها"] = "Terminate frozen processes and reset adapters",
        ["کیل‌سوئیچ امنیتی (قطع خودکار اینترنت هنگام قطعی VPN)"] = "Security kill switch (auto-block internet on VPN drop)",
        ["پاکسازی کش DNS سیستم"] = "Flush system DNS cache",
        ["تفکیک ترافیک برنامه‌ها (اسپلیت تانل)"] = "App traffic separation (Split Tunnel)",
        ["محافظت از نشت DNS"] = "DNS Leak Protection",
        ["برای ورود به پنل و دریافت خودکار سرورها کلیک کنید"] = "Click to log in and auto-fetch servers",
        ["مشاهده وضعیت اشتراک، ترافیک و ورود/خروج"] = "View subscription status, traffic and login/logout",
        ["وضعیت حساب کاربری"] = "User Account Status",
        ["🧰 ابزارهای شبکه (Network Toolbox)"] = "🧰 Network Tools",
        ["📡 لاگ ابزارها و پینگ/تریس (Diagnostics Log)"] = "📡 Diagnostics & Ping/Trace Log",
        ["📶 لاگ سیستم و کانکشن‌ها"] = "📶 System & Connection Logs",
        ["بازگشت به ابزارها"] = "Back to Tools",
        ["بازگشت به داشبورد"] = "Back to Dashboard",
        ["بازگشت به لیست کانکشن‌ها"] = "Back to Connections List",
        ["کانال تلگرام (Telegram Channel)"] = "Telegram Channel",
        ["مخزن گیت‌هاب (GitHub Repository)"] = "GitHub Repository",
        ["تایل پایینی داشبورد (Bottom Dashboard Slot):"] = "Bottom Dashboard Slot:",
        ["سایدبار منو به صورت باز پیش‌فرض"] = "Sidebar drawer open by default",
        ["نمایش/پنهان‌سازی سایدبار منو (☰)"] = "Toggle sidebar menu (☰)",
        ["روشن/خاموش اتصال"] = "Toggle Connection",
        ["متصل شد"] = "Connected",
        ["وضعیت اتصال: متصل (Xray)"] = "Connection status: Connected (Xray)",
        ["آماده"] = "Ready",
        ["در حال شناسایی..."] = "Detecting...",
        ["دریافت و بارگذاری"] = "Download & Load",
        ["کلید مشترک (Pre-shared Key / PSK)"] = "Pre-shared Key (PSK)",
        ["یا فیلدهای زیر را دستی تکمیل کنید"] = "Or fill the fields below manually",
        ["مشخصات و تنظیمات اتصال VPN را وارد نمایید"] = "Enter VPN connection details & parameters",
        ["موقعیت و آی‌پی شما (برای بروزرسانی کلیک کنید)"] = "Your Location & IP (click to refresh)",
        ["افزودن دستی سرور VLESS / VMess / Trojan"] = "Manually add VLESS / VMess / Trojan server",
        ["دریافت کانکشن OpenVPN از لینک (Add from URL)"] = "Add OpenVPN connection from URL",
        ["افزودن کانکشن جدید (Add Connection)"] = "Add New Connection",
        ["＋ دستی"] = "＋ Manual",
        ["✓ افزودن و ذخیره"] = "✓ Add & Save",
        ["انصراف (Cancel)"] = "Cancel",
        ["💾 ذخیره کانکشن (Save)"] = "💾 Save Connection",
        ["🌐 دریافت از لینک URL"] = "🌐 Import from URL",
        ["🌐 دریافت کانکشن OpenVPN از لینک"] = "🌐 Add OpenVPN from URL",
        ["پینگ همه"] = "Ping All",
        ["بروزرسانی"] = "Update",
        ["DNS (اختیاری)"] = "DNS (optional)",
        ["Keepalive (ثانیه)"] = "Keepalive (seconds)",
        ["MTU (اختیاری)"] = "MTU (optional)",
        ["Preshared Key (اختیاری)"] = "Preshared Key (optional)",
        ["SNI / هاست جعلی:"] = "SNI / Fake Host:",
        ["آدرس سرور (Server Address) *"] = "Server Address *",
        ["رمز عبور (Password)"] = "Password",
        ["لینک مستقیم فایل کانفیگ (.ovpn):"] = "Direct link to .ovpn config:",
        ["مسیر (Path):"] = "Path:",
        ["مشخصات و پارامترهای اتصال VPN را وارد نمایید"] = "Enter VPN connection parameters",
        ["نام سرور (Alias):"] = "Server Name (Alias):",
        ["نام کاربری (Username)"] = "Username",
        ["نام کانکشن *"] = "Connection Name *",
        ["نوع انتقال:"] = "Transport Type:",
        ["نوع پروتکل *"] = "Protocol Type *",
        ["پروتکل OpenVPN"] = "OpenVPN Protocol",
        ["پروتکل:"] = "Protocol:",
        ["— پارامترهای ضد فیلترینگ پیشرفته (Jc, S, H)"] = "— Advanced Censorship Bypass Parameters (Jc, S, H)",
        ["─── پارامترهای Junk و Obfuscation ───"] = "─── Junk and Obfuscation Parameters ───",
        ["⚙ تنظیمات و پیکربندی (Settings)"] = "⚙ Settings & Configuration",
        ["💳 ورود به حساب / اشتراک"] = "💳 Login / Subscription",
        ["افزودن لینک سابسکریپشن جدید"] = "Add New Subscription Link",
        ["دستگاه‌های متصل:"] = "Connected Devices:",
        ["هات‌اسپات در حال آماده‌سازی است"] = "Hotspot is preparing...",
        ["هات‌اسپات فعال است"] = "Hotspot is active",
        ["هات‌اسپات روشن شد! (نیاز به اشتراک‌گذاری دستی در صورت قطعی اینترنت)"] = "Hotspot started! (Manual sharing needed if offline)",
        ["🚀 روشن کردن هات‌اسپات"] = "🚀 Start Hotspot",
        ["⏹ توقف و قطع"] = "⏹ Stop & Disconnect",
        ["● متصل"] = "● Connected",
        ["✅ هات‌اسپات با موفقیت فعال شد."] = "✅ Hotspot started successfully.",
        ["کانفیگ بارگذاری شد:"] = "Config loaded:",
        ["کیل‌سوئیچ غیرفعال شد."] = "Kill switch disabled.",
        ["کیل‌سوئیچ فعال شد."] = "Kill switch enabled.",
        ["پروفایل تعبیه‌شده لود گردید ✔"] = "Embedded profile loaded ✔",
        ["پروفایل تعبیه‌شده موجود است ✔"] = "Embedded profile available ✔",
        ["برای استفاده از هات‌اسپات ابتدا به VPN یا Xray متصل شوید."] = "Please connect to VPN or Xray first to use Hotspot.",
        ["بستن تمام سرویس‌ها، ریست آداپتورها و بازگردانی DNS"] = "Stop all services, reset adapters and restore DNS",
        ["💾 ذخیره"] = "💾 Save",
        ["📍 اتصال:"] = "📍 Connection:",
        ["🚀 تست سرعت اتصال"] = "🚀 Speed Test",
        ["خروج و پاکسازی کامل شبکه"] = "Exit & Full Network Cleanup",
        ["آیا از خروج کامل از برنامه اطمینان دارید؟\nبا انتخاب پاکسازی، تمام پروسس‌های VPN و تنظیمات کارت‌های شبکه پاکسازی و به حالت اولیه بازگردانده می‌شوند."] = "Are you sure you want to exit?\nSelecting cleanup will terminate all VPN processes and reset network adapters.",
        ["برای اعمال کامل زبان جدید و تنظیمات چیدمان، برنامه نیاز به راه‌اندازی مجدد دارد.\nاکنون راه‌اندازی مجدد شود؟"] = "To fully apply the new language and layout direction, an application restart is required.\nRestart now?",
        ["آخرین بروزرسانی:"] = "Last updated:",
        ["آخرین به‌روزرسانی:"] = "Last updated:",
        ["تمام پروسس‌های VPN متوقف و تنظیمات بازنشانی شدند."] = "All VPN processes terminated and adapters reset.",
        ["خطا در شیرینگ خودکار. لطفاً دستی انجام دهید."] = "Automatic sharing failed. Please configure manually.",
        ["خطا:"] = "Error:",
        ["مرحله ۲: تثبیت شبکه وای‌فای دایرکت..."] = "Phase 2: Stabilizing Wi-Fi Direct network...",
        ["مرحله ۳: برقراری پل ارتباطی با"] = "Phase 3: Bridging connection with",
        ["ویرایش کانکشن: {0}"] = "Edit Connection: {0}",
        ["پروتکل {0} — ویرایش و به‌روزرسانی پارامترها"] = "Protocol {0} — Edit and update parameters",
        ["⏳ در حال تثبیت شبکه و درایورهای مجازی..."] = "⏳ Stabilizing network & virtual drivers...",
        ["برای شروع، یکی از گزینه‌های تست را انتخاب کنید"] = "Select a test option to start",
        ["تست متوقف شد"] = "Test canceled",
        ["✓ تست با موفقیت کامل شد"] = "✓ Test completed successfully",
        ["خطا در تست — اتصال اینترنت را بررسی کنید"] = "Test error — check your internet connection",
        ["در حال یافتن نزدیک‌ترین سرور…"] = "Locating nearest test server...",
        ["عالی"] = "Great",
        ["خوب"] = "Good",
        ["متوسط"] = "Moderate",
        ["ضعیف"] = "Poor",
        ["تست بدون VPN — اینترنت مستقیم شما"] = "Direct Internet (No VPN)",
        ["در حال شناسایی موقعیت..."] = "Detecting location...",
        ["یافت نشد"] = "Not found",
        ["امکان دریافت موقعیت وجود ندارد"] = "Location unavailable",
        ["در حال یافتن نزدیک‌ترین سرور Ookla..."] = "Locating nearest Ookla server...",
        ["بستن"] = "Close",
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