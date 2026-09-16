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
        // ================= تم =================
        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            var next = _config.Theme == "dark" ? "light" : "dark";
            ApplyThemeChoice(next);
            _config.Theme = next;
            _config.Save();
        }

        private void ThemeRb_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressTheme || !_ready) return;
            string theme = sender == ThemeDarkRb ? "dark" : sender == ThemeLightRb ? "light" : "system";
            ApplyThemeChoice(theme);
            _config.Theme = theme;
            _config.Save();
        }

        private void ApplyThemeChoice(string theme)
        {
            _suppressTheme = true;
            try
            {
                App.ApplyTheme(theme);
                ThemeDarkRb.IsChecked = theme == "dark";
                ThemeLightRb.IsChecked = theme == "light";
                ThemeSystemRb.IsChecked = theme == "system";

                // کارت‌های لیست کانکشن‌ها رنگ‌شان را توی کد بر اساس IsLightTheme() مستقیم می‌سازند،
                // نه با DynamicResource؛ پس با تغییر تم باید دوباره ساخته شوند وگرنه با رنگ تم قبلی
                // (مثلاً نوشته‌ی روشن روی پس‌زمینه‌ی تم روشن) باقی می‌مانند و خوانده نمی‌شوند.
                try { RefreshList(); } catch { }
            }
            finally { _suppressTheme = false; }
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



        // ================= درباره =================
        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog(_config.TelegramUrl, _config.PanelUrl, _config.SupportUrl) { Owner = this }.ShowDialog();
        }
    }
}
