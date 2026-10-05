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
using System.Windows.Input;
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
        // ================= زبان برنامه =================
        private string _pendingLanguage = "";

        private void LangRb_Click(object sender, RoutedEventArgs e)
        {
            var language = sender == LangEnRb ? "en" : "fa";
            if (string.Equals(_config.Language, language, StringComparison.OrdinalIgnoreCase)) return;

            _pendingLanguage = language;
            if (LangRestartOverlay != null)
            {
                LangRestartOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                ApplyLanguageChangeDirectly(_pendingLanguage);
            }
        }

        private void LangModalRestart_Click(object sender, RoutedEventArgs e)
        {
            if (LangRestartOverlay != null) LangRestartOverlay.Visibility = Visibility.Collapsed;
            if (!string.IsNullOrEmpty(_pendingLanguage))
            {
                _config.Language = _pendingLanguage;
                _config.Save();
                Localization.SetLanguage(_pendingLanguage);
                RestartApp();
            }
        }

        private void LangModalCancel_Click(object sender, RoutedEventArgs e)
        {
            if (LangRestartOverlay != null) LangRestartOverlay.Visibility = Visibility.Collapsed;
            // بازگردانی حالت رادیوباتن به زبان فعال در صورت انصراف کاربر
            if (LangEnRb != null) LangEnRb.IsChecked = string.Equals(_config.Language, "en", StringComparison.OrdinalIgnoreCase);
            if (LangFaRb != null) LangFaRb.IsChecked = !string.Equals(_config.Language, "en", StringComparison.OrdinalIgnoreCase);
            _pendingLanguage = "";
        }

        private void LangRestartOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == LangRestartOverlay)
            {
                LangModalCancel_Click(sender, new RoutedEventArgs());
            }
        }

        private void ApplyLanguageChangeDirectly(string language)
        {
            _config.Language = language;
            _config.Save();
            Localization.SetLanguage(language);

            FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            ApplyLocalizationToNamedElements();
            ApplyDashboardBottomWidgetMode();
            ReapplyCurrentStatusText();
            RefreshList();
            RenderRecentServersList();
            UpdateCoreSwitcherCards();
            RenderXrayConnList();
        }

        private void RestartApp()
        {
            try
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
            }
            catch { }
            _reallyExit = true;
            Application.Current.Shutdown();
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

        // ================= فونت برنامه =================
        private void FontRb_Click(object sender, RoutedEventArgs e)
        {
            string font = sender == FontVazirRb ? "vazir" : "segoe";
            ApplyFontChoice(font);
            _config.Font = font;
            _config.Save();
        }

        internal void ApplyFontChoice(string font)
        {
            try
            {
                string familyName = font == "vazir"
                    ? "./Fonts/#Vazirmatn, Vazirmatn, ./Fonts/#Inter, Inter, Segoe UI"
                    : "./Fonts/#Inter, Inter, Segoe UI Variable Text, Segoe UI, ./Fonts/#Vazirmatn, Vazirmatn";

                var ff = new FontFamily(familyName);
                this.FontFamily = ff;

                if (FontSegoeRb != null) FontSegoeRb.IsChecked = font != "vazir";
                if (FontVazirRb != null) FontVazirRb.IsChecked = font == "vazir";

                if (Application.Current != null)
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        try { w.FontFamily = ff; } catch { }
                    }
                }
            }
            catch { }
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

        internal void ApplyThemeChoice(string theme)
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
                try { RenderXrayConnList(); } catch { }
                try { RenderRecentServersList(); } catch { }
                try { UpdateCoreSwitcherCards(); } catch { }
                try { ApplyDashboardBottomWidgetMode(); } catch { }
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
            AppendConnLog("split-tunnel mode = " + tag + " (applies on next connection)");
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
            AppendConnLog("dns mode = " + _config.DnsMode + " (applies on next connection)");
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
            catch (Exception ex) { AppendConnLog("Failed to configure auto-startup: " + ex.Message); }
        }

        private void ShowRecentServersToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _config.ShowRecentServersOnHome = ShowRecentServersToggle.IsChecked == true;
            _config.Save();
            if (RecentServersCard != null)
            {
                RecentServersCard.Visibility = _config.ShowRecentServersOnHome ? Visibility.Visible : Visibility.Collapsed;
                if (SecurityStatusCard != null)
                    SecurityStatusCard.Visibility = _config.ShowRecentServersOnHome ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void DashWidgetChoice_Click(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            if (sender == DashWidgetRecentRb) _config.DashboardBottomWidgetMode = 0;
            else if (sender == DashWidgetSecurityRb) _config.DashboardBottomWidgetMode = 1;
            else if (sender == DashWidgetToolsRb) _config.DashboardBottomWidgetMode = 2;
            _config.Save();
            ApplyDashboardBottomWidgetMode();
        }

        private void SidebarDefaultOpenToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            SetSidebarExpanded(SidebarDefaultOpenToggle.IsChecked == true);
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
            if (SidebarToggleBtn != null) SidebarToggleBtn.ToolTip = T("نمایش/پنهان‌سازی سایدبار منو (☰)");
            if (TitleAccountBadge != null) TitleAccountBadge.ToolTip = T("مشاهده وضعیت حساب و اشتراک");

            // --- Settings Tab ---
            LangLabel.Text        = T("زبان برنامه");
            LangFaRb.Content      = T("فارسی");
            LangEnRb.Content      = T("English");
            ThemeLabel.Text       = T("تم برنامه");
            ThemeDarkRb.Content   = T("تیره");
            ThemeLightRb.Content  = T("روشن");
            ThemeSystemRb.Content = T("سیستم");
            if (FontLabel != null) FontLabel.Text = T("فونت برنامه");
            if (FontSegoeRb != null) FontSegoeRb.Content = T("Inter / Segoe UI (مدرن)");
            if (FontVazirRb != null) FontVazirRb.Content = T("Vazirmatn (وزیرمتن)");
            if (DashboardCustomLabel != null) DashboardCustomLabel.Text = T("سفارشی‌سازی داشبورد");
            if (ShowRecentServersToggle != null) ShowRecentServersToggle.Content = T("نمایش سرورهای اخیر در داشبورد");
            if (SidebarDefaultOpenToggle != null) SidebarDefaultOpenToggle.Content = T("سایدبار منو به صورت باز پیش‌فرض");

            // --- Settings Expander headers ---
            AppearanceExpander.Header = T("🎨 ظاهر برنامه");
            if (DashboardExpander != null) DashboardExpander.Header = T("📊 داشبورد و سایدبار");
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
            if (DrawerNavHotspotText != null) DrawerNavHotspotText.Text = T("📶 VPN Wi-Fi Direct");
            if (DrawerNavToolsText != null) DrawerNavToolsText.Text = T("🧰 جعبه ابزار");
            if (DrawerNavSettingsText != null) DrawerNavSettingsText.Text = T("⚙ تنظیمات");
            if (DrawerMenuPanel != null)
                DrawerMenuPanel.FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;

            // --- Sidebar Navigation ---
            if (NavDashboardText != null) NavDashboardText.Text = T("داشبورد");
            if (SidebarNavDashboardSubtext != null) SidebarNavDashboardSubtext.Text = T("مرکز کنترل");
            if (NavVpnCoreText != null) NavVpnCoreText.Text = T("هسته VPN");
            if (NavXrayCoreText != null) NavXrayCoreText.Text = T("هسته Xray");
            if (NavHotspotText != null) NavHotspotText.Text = T("VPN Wi-Fi Direct");
            if (NavSettingsText != null) NavSettingsText.Text = T("تنظیمات");
            if (NavToolsText != null) NavToolsText.Text = T("ابزارهای شبکه");
            if (NavLogsText != null) NavLogsText.Text = T("لاگ کانکشن‌ها");

            // --- Home Panel: Hero & Telemetry ---
            if (ConnControlLbl != null) ConnControlLbl.Text = T("کنترل اتصال");
            if (DurationLbl != null) DurationLbl.Text = T("مدت زمان اتصال");
            if (CurrentSpeedLbl != null) CurrentSpeedLbl.Text = T("سرعت لحظه‌ای");
            if (LatencyLbl != null) LatencyLbl.Text = T("تاخیر / پینگ");
            if (SessionTrafficLbl != null) SessionTrafficLbl.Text = T("حجم مصرفی سشن");
            if (TunnelIpLbl != null) TunnelIpLbl.Text = T("🌐 آی‌پی تونل");
            if (ServerIpLbl != null) ServerIpLbl.Text = T("🖥️ آی‌پی سرور");
            if (ProtocolLbl != null) ProtocolLbl.Text = T("🔒 پروتکل");
            if (YouLbl != null) YouLbl.Text = T("👤 نام کاربری");
            if (LiveTrafficGraphLbl != null) LiveTrafficGraphLbl.Text = T("📈 نمودار زنده ترافیک");
            if (PrivateIpText != null) PrivateIpText.ToolTip = T("برای کپی کلیک کنید");
            if (ServerIpText != null) ServerIpText.ToolTip = T("برای کپی کلیک کنید");

            // --- Home Panel: Recent Connections Card & Core Selector ---
            if (RecentCardTitle != null) RecentCardTitle.Text = T("⭐ سرورهای منتخب و اخیر");
            if (RecentCardSubtitle != null) RecentCardSubtitle.Text = T("اتصال سریع با یک کلیک به آخرین کانکشن‌ها");
            if (HomeCoreSelectorTitle != null) HomeCoreSelectorTitle.Text = T("⚡ انتخاب هسته اتصال");
            if (HomeCoreSelectorSubtitle != null) HomeCoreSelectorSubtitle.Text = T("دسترسی سریع به سرورهای تفکیک‌شده");
            if (HomeSwitchToVpnBtn != null) HomeSwitchToVpnBtn.Content = T("🛡️ سرورهای VPN Core");
            if (HomeSwitchToXrayBtn != null) HomeSwitchToXrayBtn.Content = T("⚡ سرورهای Xray Core");
            if (XraySwitchToVpnBtn != null) XraySwitchToVpnBtn.Content = T("VPN (سنتی)");
            if (XraySwitchToXrayBtn != null) XraySwitchToXrayBtn.Content = T("Xray (پروکسی)");
            if (CurrentSpeedLbl != null) CurrentSpeedLbl.Text = T("دریافت ↓");
            if (UlSpeedLbl != null) UlSpeedLbl.Text = T("ارسال ↑");
            if (LatencyLbl != null) LatencyLbl.Text = T("پینگ");
            if (SessionTrafficLbl != null) SessionTrafficLbl.Text = T("حجم سشن");
            if (VpnBackHomeText != null) VpnBackHomeText.Text = T("داشبورد");
            if (XrayBackHomeText != null) XrayBackHomeText.Text = T("داشبورد");

            // --- Home Panel: Stacked Dual Subscriptions ---
            if (SubSectionLabel != null) SubSectionLabel.Text = T("🛡️ اشتراک VPN Core");
            if (SubActiveText != null) SubActiveText.Text = T("فعال");
            if (SubEmptyText != null) SubEmptyText.Text = T("برای مشاهده وضعیت اشتراک کلیک کنید");
            if (SubTimeLbl != null) SubTimeLbl.Text = T("زمان باقی‌مانده");
            if (SubDataLbl != null) SubDataLbl.Text = T("حجم باقی‌مانده");
            if (SubRefreshBtn != null) SubRefreshBtn.ToolTip = T("بروزرسانی اشتراک VPN");

            if (HomeXraySubSectionLabel != null) HomeXraySubSectionLabel.Text = T("⚡ اشتراک Xray Core");
            if (HomeXraySubActiveText != null) HomeXraySubActiveText.Text = T("فعال");
            if (HomeXraySubEmptyText != null) HomeXraySubEmptyText.Text = T("برای مشاهده وضعیت اشتراک کلیک کنید");
            if (HomeXraySubTimeLbl != null) HomeXraySubTimeLbl.Text = T("زمان باقی‌مانده");
            if (HomeXraySubDataLbl != null) HomeXraySubDataLbl.Text = T("حجم باقی‌مانده");
            if (HomeXraySubRefreshBtn != null) HomeXraySubRefreshBtn.ToolTip = T("بروزرسانی اشتراک Xray");

            // Refresh recent servers list, Xray card and Core switcher cards with current language
            if (HomeCoreSelectorTitle != null) HomeCoreSelectorTitle.Text = T("⚡ انتخاب هسته اتصال");
            if (HomeCoreSelectorSubtitle != null) HomeCoreSelectorSubtitle.Text = T("دسترسی سریع به سرورهای تفکیک‌شده");
            if (HomeVpnTitleText != null) HomeVpnTitleText.Text = T("هسته VPN");
            if (HomeXrayTitleText != null) HomeXrayTitleText.Text = T("هسته Xray");
            RenderRecentServersList();
            UpdateXraySubscriptionCard();
            UpdateCoreSwitcherCards();

            // --- Home panel dynamic labels ---
            var moreOpen = MoreInfoPanel?.Visibility == Visibility.Visible;
            if (MoreInfoText != null) MoreInfoText.Text = T(moreOpen ? "بستن جزئیات" : "جزئیات بیشتر");
            if (PowerHintText != null)
            {
                PowerHintText.Text = T(_currentPowerState switch
                {
                    "connected" => "برای قطع اتصال کلیک کنید",
                    "connecting" or "reconnecting" => "لطفاً شکیبا باشید...",
                    "error" => "جهت تلاش مجدد کلیک کنید",
                    _ => "جهت اتصال کلیک کنید"
                });
            }
            if (PowerStateText != null)
            {
                PowerStateText.Text = T(_currentPowerState switch
                {
                    "connected" => "متصل و ایمن",
                    "connecting" or "reconnecting" => "در حال اتصال...",
                    "error" => "خطا در اتصال",
                    _ => "خاموش"
                });
            }
            if (GeoIpText != null && (GeoIpText.Text == "آماده اتصال" || GeoIpText.Text == "Ready to connect"))
                GeoIpText.Text = T("آماده اتصال");
            if (ActiveConnText != null && (ActiveConnText.Text.Contains("کانکشن") || ActiveConnText.Text.StartsWith("Select", StringComparison.OrdinalIgnoreCase)))
                ActiveConnText.Text = T("یک کانکشن انتخاب کنید");
            UpdateSidebarAccountCard();
            if (AddBtn != null) AddBtn.ToolTip = T("افزودن کانکشن جدید");
            if (SubSummaryCard != null) SubSummaryCard.ToolTip = T("مشاهده / استعلام وضعیت اشتراک");
            if (ConnSectionLabel != null) ConnSectionLabel.Text = T("کانکشن‌ها");
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

            // --- Bottom widget mode radio buttons ---
            if (DashWidgetRecentRb != null) DashWidgetRecentRb.Content = T("⭐ سرورهای اخیر");
            if (DashWidgetSecurityRb != null) DashWidgetSecurityRb.Content = T("🛡️ کنترل‌های امنیت");
            if (DashWidgetToolsRb != null) DashWidgetToolsRb.Content = T("🧰 ابزارهای شبکه");

            // --- Exit Modal ---
            if (ExitModalTitle != null) ExitModalTitle.Text = T("خروج از برنامه NETFASTVIP");
            if (ExitModalDesc != null) ExitModalDesc.Text = T("آیا از خروج کامل از برنامه اطمینان دارید؟\nبا انتخاب پاکسازی، تمام پروسس‌های VPN و تنظیمات کارت‌های شبکه پاکسازی و به حالت اولیه بازگردانده می‌شوند.");
            if (ExitModalCleanupText != null) ExitModalCleanupText.Text = T("خروج و پاکسازی کامل شبکه");
            if (ExitModalQuickBtn != null) ExitModalQuickBtn.Content = T("⚡ خروج سریع");
            if (ExitModalCancelBtn != null) ExitModalCancelBtn.Content = T("انصراف");

            // --- Xray Manual Add Modal ---
            if (XrayManualAddBtn != null) XrayManualAddBtn.Content = T("＋ افزودن دستی");
            if (XrayManualAddTitle != null) XrayManualAddTitle.Text = T("افزودن دستی کانفیگ Xray");

            // --- Navigation Back Home Buttons ---
            if (VpnBackHomeText != null) VpnBackHomeText.Text = T("داشبورد");
            if (XrayBackHomeText != null) XrayBackHomeText.Text = T("داشبورد");
            if (VpnBackHomeBtn != null) VpnBackHomeBtn.ToolTip = T("بازگشت به داشبورد");
            if (XrayBackHomeBtn != null) XrayBackHomeBtn.ToolTip = T("بازگشت به داشبورد");
            if (HotspotBackHomeBtn != null) HotspotBackHomeBtn.ToolTip = T("بازگشت به داشبورد");
            if (ToolboxBackHomeBtn != null) ToolboxBackHomeBtn.ToolTip = T("بازگشت به داشبورد");
            if (SettingsBackHomeBtn != null) SettingsBackHomeBtn.ToolTip = T("بازگشت به داشبورد");
            if (ConnLogBackHomeBtn != null) ConnLogBackHomeBtn.ToolTip = T("بازگشت به داشبورد");
            if (SpeedTestHomeBtn != null) SpeedTestHomeBtn.ToolTip = T("بازگشت به داشبورد");

            // --- Dashboard Bottom Widget Modes & Tools ---
            if (DashKsTitleText != null) DashKsTitleText.Text = T("کیل‌سوئیچ");
            if (DashStTitleText != null) DashStTitleText.Text = T("اسپلیت تانل");
            if (DashDnsTitleText != null) DashDnsTitleText.Text = T("محافظت DNS");
            if (DashToolFlushText != null) DashToolFlushText.Text = T("فلاش DNS");
            if (DashToolKillText != null) DashToolKillText.Text = T("کیل پروسس");
            if (DashToolSpeedText != null) DashToolSpeedText.Text = T("تست سرعت");
            ApplyDashboardBottomWidgetMode();

            // --- Language Restart In-App Modal ---
            if (LangModalTitle != null) LangModalTitle.Text = T("تغییر زبان برنامه");
            if (LangModalDesc != null) LangModalDesc.Text = T("برای اعمال کامل زبان جدید و تنظیمات چیدمان، پیشنهاد می‌شود برنامه راه‌اندازی مجدد شود.\nاکنون راه‌اندازی مجدد شود؟");
            if (LangModalRestartBtn != null) LangModalRestartBtn.Content = T("✓ راه‌اندازی مجدد");
            if (LangModalCancelBtn != null) LangModalCancelBtn.Content = T("انصراف");

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

