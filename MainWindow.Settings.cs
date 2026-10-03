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

                private void BtnManagePerApp_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PerAppDialog(_config) { Owner = this };
            dlg.ShowDialog();
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
            "adguard" => ("94.140.14.14", "94.140.15.15"),
            "adguard_family" => ("94.140.14.15", "94.140.15.16"),
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

            // --- Window Title & System Controls ---
            TitleCloseButton.ToolTip = T("بستن");
            TitleMinimizeButton.ToolTip = T("کمینه‌سازی");
            HamburgerBtn.ToolTip = T("منوی اصلی");

            // --- Settings Tab ---
            LangLabel.Text        = T("زبان برنامه");
            LangFaRb.Content      = T("فارسی");
            LangEnRb.Content      = T("English");
            ThemeLabel.Text       = T("تم برنامه");
            ThemeDarkRb.Content   = T("تیره");
            ThemeLightRb.Content  = T("روشن");
            ThemeSystemRb.Content = T("سیستم");

            // --- Settings Expander headers ---
            AppearanceExpander.Header = T("🎨 ظاهر برنامه");
            BehaviorExpander.Header   = T("⚙ رفتار اتصال");
            IdleExpander.Header       = T("⏱ قطع خودکار در بی‌استفادگی");
            SplitExpander.Header      = T("تونل برنامه‌ها — Per-App (مخصوص Xray / پروکسی)");
            DnsExpander.Header        = T("🌐 DNS");
            PackageExpander.Header    = T("📦 پکیج و پیکربندی");

            // --- Settings RadioButton / CheckBox labels ---
            SmartToggle.Content   = T("اتصال هوشمند (امتحان خودکار همه کانکشن‌ها)");
            TrayToggle.Content    = T("مخفی‌شدن در کنار ساعت به‌جای بستن");
            StartupToggle.Content = T("اجرای خودکار با ویندوز");
            IdleOffRb.Content     = T("همیشه متصل");
            IdleOnRb.Content      = T("قطع بعد از");
            IdleMinutesLabel.Text = T("دقیقه بی‌استفادگی");
            DnsAutoRb.Content     = T("خودکار");
            DnsCustomRb.Content   = T("دلخواه");
            DnsAdguardRb.Content  = T("AdGuard — 94.140.14.14 (مسدودساز تبلیغات)");
            DnsAdguardFamilyRb.Content = T("AdGuard Family — 94.140.14.15 (امنیت خانواده)");

            StOffRb.Content       = T("تونل کل سیستم (همه برنامه‌ها)");
            StAllowRb.Content     = T("فقط برنامه‌های لیست (Allow)");
            StDenyRb.Content      = T("همه به جز برنامه‌های لیست (Bypass)");
            BtnManagePerApp.Content = T("⚙️ مدیریت لیست برنامه‌ها");

            // --- Buttons in settings ---
            UpdateBaseBtn.Content = T("🔧 به‌روزرسانی base.ovpn از فایل");
            ImportBtn.Content     = T("📥 ایمپورت پکیج / فایل ovpn");
            ExportBtn.Content     = T("📤 اکسپورت / بک‌اپ کانکشن‌ها");
            AboutBtn.Content      = T("ℹ درباره برنامه و اشتراک");
            ClearConnLogBtn.Content = T("🗑 پاکسازی");

            // --- Hamburger Drawer ---
            if (DrawerTitleText != null) DrawerTitleText.Text = T("منوی اصلی");
            if (DrawerNavHomeText != null) DrawerNavHomeText.Text = T("🏠 داشبورد اصلی");
            if (DrawerNavXrayText != null) DrawerNavXrayText.Text = T("🚀 سرویس Xray");
            if (DrawerNavLogsText != null) DrawerNavLogsText.Text = T("📄 لاگ اتصال");
            if (DrawerNavHotspotText != null) DrawerNavHotspotText.Text = T("🛜 مدیریت Hotspot");
            if (DrawerNavToolsText != null) DrawerNavToolsText.Text = T("🧰 جعبه ابزار");
            if (DrawerNavSettingsText != null) DrawerNavSettingsText.Text = T("⚙ تنظیمات");
            if (DrawerMenuPanel != null)
                DrawerMenuPanel.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

            // --- Segmented Control (VPN / Xray) ---
            if (HomeSwitchToVpnBtn != null) HomeSwitchToVpnBtn.Content = T("VPN (سنتی)");
            if (HomeSwitchToXrayBtn != null) HomeSwitchToXrayBtn.Content = T("Xray (پروکسی)");
            if (XraySwitchToVpnBtn != null) XraySwitchToVpnBtn.Content = T("VPN (سنتی)");
            if (XraySwitchToXrayBtn != null) XraySwitchToXrayBtn.Content = T("Xray (پروکسی)");

            // --- Home panel dynamic labels ---
            var moreOpen = MoreInfoPanel?.Visibility == Visibility.Visible;
            MoreInfoText.Text  = T(moreOpen ? "بستن جزئیات" : "نمایش جزئیات");
            PowerHintText.Text = T(_currentStatusKey == TxtConnected
                ? "برای قطع اتصال کلیک کنید" : "Click to Connect");
            AddBtn.ToolTip = T("افزودن کانکشن جدید");
            SubRefreshBtn.ToolTip = T("بروزرسانی وضعیت اشتراک");
            SubSummaryCard.ToolTip = T("مشاهده / استعلام وضعیت اشتراک");
            SubEmptyText.Text   = T("برای مشاهده وضعیت اشتراک کلیک کنید");
            SubSectionLabel.Text = T("🎫 اشتراک من");
            SubActiveText.Text  = T("فعال");
            SubTimeLbl.Text     = T("⏳ زمان باقی‌مانده");
            SubDataLbl.Text     = T("📦 حجم باقی‌مانده");
            ConnSectionLabel.Text = T("کانکشن‌ها");
            ProtocolLbl.Text    = T("Protocol");
            YouLbl.Text         = T("User");
            PrivateIpText.ToolTip = T("برای کپی کلیک کنید");
            ServerIpText.ToolTip  = T("برای کپی کلیک کنید");
            if (ActiveConnSummaryPanel != null)
                ActiveConnSummaryPanel.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            if (SubSummaryPanel != null)
                SubSummaryPanel.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            if (SubUpdatedText != null)
                SubUpdatedText.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

            // --- Xray Core Panel ---
            if (XrayConnSectionLabel != null) XrayConnSectionLabel.Text = T("کانکشن‌ها");
            if (XrayAddSubBtn != null)
            {
                XrayAddSubBtn.Content = T("＋ Add Subscription");
                XrayAddSubBtn.ToolTip = T("افزودن لینک سابسکریپشن جدید");
            }
            if (XrayUpdateSubBtn != null)
            {
                XrayUpdateSubBtn.ToolTip = T("بروزرسانی وضعیت اشتراک و سرورها");
            }
            if (XrayUpdateSubLabel != null) XrayUpdateSubLabel.Text = T(" بروزرسانی");
            if (XrayPingAllBtn != null)
            {
                XrayPingAllBtn.ToolTip = T("تست پینگ همه سرورها");
            }
            if (XrayPingAllLabel != null) XrayPingAllLabel.Text = T(" پینگ همه");
            if (XrayClearServersBtn != null)
            {
                XrayClearServersBtn.ToolTip = T("حذف سرورها");
            }
            if (XraySubSectionLabel != null) XraySubSectionLabel.Text = T("🎫 اشتراک من");
            if (XraySubActiveText != null) XraySubActiveText.Text = T("فعال");
            if (XraySubTimeLbl != null) XraySubTimeLbl.Text = T("⏳ زمان باقی‌مانده");
            if (XraySubDataLbl != null) XraySubDataLbl.Text = T("📦 حجم باقی‌مانده");
            if (XraySubRefreshBtn != null) XraySubRefreshBtn.ToolTip = T("بروزرسانی وضعیت اشتراک");
            if (XrayMoreInfoText != null)
            {
                var xrayMoreOpen = XrayMoreInfoPanel?.Visibility == Visibility.Visible;
                XrayMoreInfoText.Text = T(xrayMoreOpen ? "بستن جزئیات" : "جزئیات اتصال");
            }
            if (XrayPowerHintText != null)
                XrayPowerHintText.Text = T(_xrayIsConnected ? "برای قطع اتصال کلیک کنید" : "Click to Connect");
            if (XrayTxtActiveName != null && (XrayTxtActiveName.Text == "یک کانکشن انتخاب کنید" || XrayTxtActiveName.Text == "Select a connection" || XrayTxtActiveName.Text == "یک سرور انتخاب کنید" || XrayTxtActiveName.Text == "Select a server"))
                XrayTxtActiveName.Text = T("یک کانکشن انتخاب کنید");
            if (XrayTxtActiveGeoIP != null && (XrayTxtActiveGeoIP.Text == "آماده اتصال" || XrayTxtActiveGeoIP.Text == "Ready to connect"))
                XrayTxtActiveGeoIP.Text = T("آماده اتصال");
            if (XrayStatusText != null)
                XrayStatusText.Text = T(_xrayIsConnected ? "متصل" : "آماده اتصال");
            if (XrayPrivateIpText != null) XrayPrivateIpText.ToolTip = T("برای کپی کلیک کنید");
            if (XrayServerIpText != null) XrayServerIpText.ToolTip = T("برای کپی کلیک کنید");
            if (XrayActiveConnSummaryPanel != null)
                XrayActiveConnSummaryPanel.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

            // --- In-App Sub & Message Modals ---
            if (XraySubTitleText != null) XraySubTitleText.Text = T("🔗 افزودن لینک اشتراک Xray");
            if (XraySubNameHintText != null) XraySubNameHintText.Text = T("نام گروه / اشتراک (اختیاری):");
            if (XraySubHintText != null) XraySubHintText.Text = T("لینک سابسکریپشن یا کانکشن خود را وارد نمایید:");
            if (XraySubPasteBtn != null)
            {
                XraySubPasteBtn.Content = T("📋 چسباندن");
                XraySubPasteBtn.ToolTip = T("چسباندن از کلیپ‌بورد");
            }
            if (XraySubSubmitBtn != null) XraySubSubmitBtn.Content = T("ثبت و دریافت");
            if (XraySubCancelBtn != null) XraySubCancelBtn.Content = T("انصراف");
            if (XraySubModalStack != null)
                XraySubModalStack.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            if (InAppMsgTitle != null && (InAppMsgTitle.Text == "پیام سیستم" || InAppMsgTitle.Text == "System message"))
                InAppMsgTitle.Text = T("پیام سیستم");
            if (InAppMsgOkBtn != null) InAppMsgOkBtn.Content = T("باشه");
            if (InAppMessageModalStack != null)
                InAppMessageModalStack.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            if (XrayShareTitleText != null) XrayShareTitleText.Text = T("📱 اشتراک‌گذاری کانکشن");
            if (XrayShareHintText != null) XrayShareHintText.Text = T("اسکن با دوربین یا اپ‌های موبایل (v2rayNG, Streisand, sing-box...)");
            if (XrayShareCopyBtn != null) XrayShareCopyBtn.Content = T("📋 کپی لینک کانکشن");
            if (XrayShareCloseBtn != null) XrayShareCloseBtn.Content = T("بستن");
            if (XrayShareModalStack != null)
                XrayShareModalStack.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

            // --- Toolbox buttons & labels ---
            ToolboxTitle.Text     = T("🧰 جعبه‌ابزار و عیب‌یابی شبکه");
            SpeedTestBtn.ToolTip  = T("اندازه‌گیری پینگ، جیتر و سرعت دانلود/آپلود");
            SpeedTestLbl.Text     = T("تست سرعت اتصال");
            RenewIpBtn.ToolTip    = T("وقتی شبکه وصل است ولی اینترنت ندارید");
            RenewIpLbl.Text       = T("تمدید IP");
            FlushDnsBtn.ToolTip   = T("وقتی بعد از تغییر VPN سایت‌ها باز نمی‌شوند");
            FlushDnsLbl.Text      = T("پاک‌سازی DNS");
            ProxyOffBtn.ToolTip   = T("وقتی پراکسی قدیمی، مرورگر را خراب کرده است");
            DisableProxyLbl.Text  = T("غیرفعال‌کردن پراکسی");
            KillProcBtn.ToolTip   = T("وقتی اتصال قبلی گیر کرده و قطع نمی‌شود");
            KillProcLbl.Text      = T("بستن پروسه‌های گیرکرده");
            DnsLeakTestBtn.ToolTip= T("بررسی اینکه DNS شما هم از داخل تانل رد می‌شود یا نشت دارد");
            DnsLeakLbl.Text       = T("تست نشتی DNS");
            ResetAdapterBtn.ToolTip = T("غیرفعال و فعال‌کردن کارت شبکه");
            ResetAdapterLbl.Text  = T("ریست آداپتور");
            PingTraceLbl.Text     = T("🏓 پینگ / Traceroute");
            ToolTargetBox.ToolTip = T("هدف دلخواه (IP یا دامنه)");
            PingToggleBtn.Content       = T(_pingProc is { HasExited: false }
                ? "⏹ توقف پینگ" : "🏓 شروع پینگ");
            TracerouteToggleBtn.Content = T(_tracertProc is { HasExited: false }
                ? "⏹ توقف Traceroute" : "🛰 شروع Traceroute");
            LogSectionLbl.Text    = T("📜 گزارش");
            ClearLogBtn.Content   = T("🗑 پاک‌سازی");

            // --- ConnLog panel ---
            ConnLogSectionLbl.Text = T("📶 گزارش کانکشن‌ها");

            // --- Hotspot panel ---
            HsVpnLabel.Text     = T("کانکشن VPN فعال:");
            HsAdapterLabel.Text = T("کارت شبکه اینترنت:");
            QrScanLbl.Text      = T("📱 اسکن برای اتصال سریع");
            BtnHsStart.Content   = T("روشن کردن");
            BtnHsStop.Content    = T("توقف");
            BtnHsRestart.Content = T("راه‌اندازی مجدد");
            if (BtnOpenNcpa != null) BtnOpenNcpa.Content = T("باز کردن تنظیمات شبکه ویندوز (ncpa.cpl) 🔗");
            if (HsStatusText != null) HsStatusText.Text = T(HsStatusText.Text);
            if (HsClientsCountText != null) HsClientsCountText.Text = T(HsClientsCountText.Text);

            // --- Mini Widget & Stubs ---
            HeaderPowerBtn.ToolTip = T("روشن/خاموش اتصال");
            HeaderSettingsBtn.ToolTip = T("تنظیمات");
            if (MiniPowerBtn != null) MiniPowerBtn.ToolTip = T("روشن/خاموش اتصال");
            if (MiniAlwaysOnTopMenuItem != null) MiniAlwaysOnTopMenuItem.Header = T("همیشه بالا");
            if (MiniRestoreBtn != null) MiniRestoreBtn.ToolTip = T("بازگشت به حالت عادی");
            if (MiniStatusText != null) MiniStatusText.Text = T(MiniStatusText.Text);

            // --- FilterDropdown label ---
            FilterDropdownLabel.Text = _connFilter.Length == 0
                ? T("همه")
                : FilterDropdownLabel.Text;
        }

        // ================= درباره =================
        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog(_config.TelegramUrl, _config.PanelUrl, _config.SupportUrl) { Owner = this }.ShowDialog();
        }
    }
}

