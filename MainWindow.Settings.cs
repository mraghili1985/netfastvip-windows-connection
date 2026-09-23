using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        // ================= زبان برنامه =================
        private void LangRb_Click(object sender, RoutedEventArgs e)
        {
            var language = sender == LangEnRb ? "en" : "fa";
            if (string.Equals(_config.Language, language, StringComparison.OrdinalIgnoreCase)) return;

            _config.Language = language;
            _config.Save();
            Localization.SetLanguage(language);

            // اول FlowDirection پنجره اصلی را اعمال کن تا بیدی پرانتزها درست شود
            FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

            // اعمال مستقیم روی همه المان‌های named — چون پنل‌های Collapsed از LogicalTreeHelper مخفی‌اند
            ApplyLocalizationToNamedElements();
            ReapplyCurrentStatusText();
            RefreshList();
        }

        // ================= تم =================
        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            var next = _config.Theme == "dark" ? "light" : "dark";
            ApplyThemeChoice(next);
            _config.Theme = next;
            _config.Save();
        }

        private void ThemeRb_Click(object sender, RoutedEventArgs e)
        {
            string theme = sender == ThemeDarkRb ? "dark" : sender == ThemeLightRb ? "light" : "system";
            ApplyThemeChoice(theme);
            _config.Theme = theme;
            _config.Save();
        }

        private void RefreshThemeSurfaces()
        {
            // The window uses custom transparent chrome, so explicitly rebind
            // its surfaces whenever the application theme changes.
            WindowSurface.SetResourceReference(Border.BackgroundProperty, "BgBrush");
            WindowSurface.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
            AppTitleBar.SetResourceReference(Border.BackgroundProperty, "BgBrush");
            AppTitleBar.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        }

        private void ApplyThemeChoice(string theme)
        {
            try
            {
                App.ApplyTheme(theme);
                RefreshThemeSurfaces();
                ApplyDarkTitleBar();
                
                ThemeDarkRb.IsChecked = theme == "dark";
                ThemeLightRb.IsChecked = theme == "light";
                ThemeSystemRb.IsChecked = theme == "system";

                try { RefreshList(); } catch { }
            }
            catch 
            { 
                // جلوگیری از کرش مخفی در صورت عدم تطابق ریسورس‌های تم
            }
        }

        // ================= رفتار/اسپلیت =================
        private void SmartToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _config.SmartSwitch = SmartToggle.IsChecked == true;
            _config.Save();
        }

        private void TrayToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _config.MinimizeToTray = TrayToggle.IsChecked == true;
            _config.Save();
        }

        // وضعیت این توگل در KillSwitchEnabled داخل config.json ذخیره و بین اجراهای برنامه حفظ می‌شود.
        private void KillSwitchToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            KillSwitch.Enabled = KillSwitchToggle.IsChecked == true;
            _config.KillSwitchEnabled = KillSwitch.Enabled;
            _config.Save();
            if (!KillSwitch.Enabled)
            {
                // اگر در حال اتصال خاموشش کرده، فوراً بلاک فعال را هم پاک کن.
                _ = KillSwitch.DisableAsync();
            }
            else if (_engine.IsRunning)
            {
                // اگر از قبل وصل بوده و کاربر همین الان فعالش کرد، بلاک را همین الان برای اتصال جاری اعمال کن.
                var target = _config.Connections.FirstOrDefault(c => c.Name == _selectedName) ?? _config.Connections.FirstOrDefault();
                if (target != null) _ = ApplyKillSwitchAsync(target, _tunnelLocalIp);
            }
        }

        private void IdleRb_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            SaveIdleSetting();
        }

        private void IdleMinutes_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            SaveIdleSetting();
        }

        private void SaveIdleSetting()
        {
            if (IdleOnRb.IsChecked == true)
            {
                // اعداد فارسی هم پذیرفته می‌شوند
                var raw = (IdleMinutesBox.Text ?? "").Trim();
                var sb = new System.Text.StringBuilder();
                foreach (var ch in raw)
                {
                    if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
                    else sb.Append(ch);
                }
                if (!int.TryParse(sb.ToString(), out var m) || m < 1) { m = 30; IdleMinutesBox.Text = "30"; }
                _config.IdleDisconnectMinutes = m;
            }
            else
            {
                _config.IdleDisconnectMinutes = 0;
            }
            _idleMinutes = 0;
            _idleWindowBytes = 0;
            _config.Save();
        }

        private void StMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            var tag = (string)((RadioButton)sender).Tag;
            _config.SplitTunnelMode = tag;
            _config.Save();
            AppendConnLog("split-tunnel mode = " + tag + " (روی اتصال بعدی اعمال می‌شود)");
        }

        private void StList_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            var lines = StListBox.Text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            _config.SplitTunnelList = lines;
            _config.Save();
        }

        // ================= DNS دلخواه =================
        private void DnsRb_Checked(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _config.DnsMode = (string)((RadioButton)sender).Tag;
            _config.Save();
            AppendConnLog("dns mode = " + _config.DnsMode + " (روی اتصال بعدی اعمال می‌شود)");
        }

        private void DnsBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _config.DnsPrimary = DnsPrimaryBox.Text.Trim();
            _config.DnsSecondary = DnsSecondaryBox.Text.Trim();
            _config.Save();
        }

        private (string primary, string secondary)? ResolveDnsChoice() => _config.DnsMode switch
        {
            "cloudflare" => ("1.1.1.1", "1.0.0.1"),
            "google" => ("8.8.8.8", "8.8.4.4"),
            "custom" when !string.IsNullOrWhiteSpace(_config.DnsPrimary) => (_config.DnsPrimary.Trim(), _config.DnsSecondary.Trim()),
            _ => null,
        };

        private void InitStartupToggle()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                var val = key?.GetValue(RunValueName) as string;
                StartupToggle.IsChecked = !string.IsNullOrEmpty(val);
            }
            catch { }
        }

        private void StartupToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return;
                if (StartupToggle.IsChecked == true)
                {
                    var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    key.SetValue(RunValueName, "\"" + exe + "\"");
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception ex) { AppendConnLog("تنظیم اجرای خودکار ناموفق بود: " + ex.Message); }
        }

        // ================= اعمال ترجمه روی المان‌های named =================
        // LogicalTreeHelper به پنل‌های Collapsed نفوذ نمی‌کند؛ اینجا مستقیم ست می‌شود
        private void ApplyLocalizationToNamedElements()
        {
            var T = Localization.T;

            // --- Labels بدون x:Name (اضافه شد) ---
            LangLabel.Text        = T("زبان برنامه");
            ThemeLabel.Text       = T("تم برنامه");
            IdleMinutesLabel.Text = T("دقیقه بی‌استفادگی");

            // --- Settings Expander headers ---
            AppearanceExpander.Header = T("🎨 ظاهر برنامه");
            BehaviorExpander.Header   = T("⚙ رفتار اتصال");
            IdleExpander.Header       = T("⏱ قطع خودکار در بی‌استفادگی");
            SplitExpander.Header      = T("🔀 اسپلیت تانل (همه کانکشن‌ها)");
            PackageExpander.Header    = T("📦 پکیج و پیکربندی");

            // --- Settings RadioButton / CheckBox labels ---
            ThemeDarkRb.Content   = T("تیره");
            ThemeLightRb.Content  = T("روشن");
            ThemeSystemRb.Content = T("سیستم");
            SmartToggle.Content   = T("اتصال هوشمند (امتحان خودکار همه کانکشن‌ها)");
            TrayToggle.Content    = T("مخفی‌شدن در کنار ساعت به‌جای بستن");
            StartupToggle.Content = T("اجرای خودکار با ویندوز");
            IdleOffRb.Content     = T("همیشه متصل");
            IdleOnRb.Content      = T("قطع بعد از");
            DnsAutoRb.Content     = T("خودکار");
            DnsCustomRb.Content   = T("دلخواه");
            StOffRb.Content       = T("غیرفعال — همه ترافیک از VPN");
            StDenyRb.Content      = T("Deny — همه از VPN به‌جز موارد لیست");
            StAllowRb.Content     = T("Allow — فقط موارد لیست از VPN");

            // --- Buttons in settings ---
            UpdateBaseBtn.Content = T("🔧 به‌روزرسانی base.ovpn از فایل");
            ImportBtn.Content     = T("📥 ایمپورت پکیج / فایل ovpn");
            ExportBtn.Content     = T("📤 اکسپورت / بک‌اپ کانکشن‌ها");

            // --- Home panel dynamic labels ---
            var moreOpen = MoreInfoPanel?.Visibility == Visibility.Visible;
            MoreInfoText.Text  = T(moreOpen ? "بستن جزئیات" : "نمایش جزئیات");
            PowerHintText.Text = T(_currentStatusKey == TxtConnected
                ? "برای قطع اتصال کلیک کنید" : "روشن/خاموش اتصال");

            // --- Toolbox buttons ---
            PingToggleBtn.Content       = T(_pingProc is { HasExited: false }
                ? "⏹ توقف پینگ" : "🏓 شروع پینگ");
            TracerouteToggleBtn.Content = T(_tracertProc is { HasExited: false }
                ? "⏹ توقف Traceroute" : "🛰 شروع Traceroute");

            // --- Subscription badge ---
            SubEmptyText.Text   = T("برای مشاهده وضعیت اشتراک کلیک کنید");
            SubSectionLabel.Text = T("🎫 اشتراک من");
            SubTimeLbl.Text     = T("⏳ زمان باقی‌مانده");
            SubDataLbl.Text     = T("📦 حجم باقی‌مانده");

            // --- Connections panel ---
            ConnSectionLabel.Text = T("کانکشن‌ها");

            // --- Toolbox panel ---
            ToolboxTitle.Text     = T("🧰 جعبه‌ابزار و عیب‌یابی شبکه");
            SpeedTestLbl.Text     = T("تست سرعت اتصال");
            RenewIpLbl.Text       = T("تمدید IP");
            FlushDnsLbl.Text      = T("پاک‌سازی DNS");
            DisableProxyLbl.Text  = T("غیرفعال‌کردن پراکسی");
            KillProcLbl.Text      = T("بستن پروسه‌های گیرکرده");
            DnsLeakLbl.Text       = T("تست نشتی DNS");
            ResetAdapterLbl.Text  = T("ریست آداپتور");
            PingTraceLbl.Text     = T("🏓 پینگ / Traceroute");
            LogSectionLbl.Text    = T("📜 گزارش");

            // --- ConnLog panel ---
            ConnLogSectionLbl.Text = T("📶 گزارش کانکشن‌ها");

            // --- Hotspot panel ---
            HsVpnLabel.Text     = T("کانکشن VPN فعال:");
            HsAdapterLabel.Text = T("کارت شبکه اینترنت:");
            QrScanLbl.Text      = T("📱 اسکن برای اتصال سریع");

            // --- Hotspot buttons ---
            BtnHsStart.Content   = T("روشن کردن");
            BtnHsStop.Content    = T("توقف");
            BtnHsRestart.Content = T("راه‌اندازی مجدد");

            // --- Settings About button ---
            AboutBtn.Content = T("ℹ درباره برنامه و اشتراک");

            // --- Settings Clear log button ---
            ClearConnLogBtn.Content = T("🗑 پاکسازی");

            // --- NavBar ToolTips ---
            HeaderPowerBtn.ToolTip = T("روشن/خاموش اتصال");
            HeaderSettingsBtn.ToolTip = T("تنظیمات");
            if (MiniPowerBtn != null) MiniPowerBtn.ToolTip = T("روشن/خاموش اتصال");

            // --- Info card labels (Protocol label in HomePanel) ---
            ProtocolLbl.Text = T("Protocol");

            // --- FilterDropdown label ---
            FilterDropdownLabel.Text = _connFilter.Length == 0
                ? T("همه")
                : FilterDropdownLabel.Text; // مقدار داینامیک — موقع باز کردن dropdown به‌روز می‌شه
        }

        // ================= درباره =================
        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog(_config.TelegramUrl, _config.PanelUrl, _config.SupportUrl) { Owner = this }.ShowDialog();
        }
    }
}