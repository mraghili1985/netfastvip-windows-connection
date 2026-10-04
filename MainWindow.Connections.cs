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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SmartVpn
{
    public partial class MainWindow : Window
    {
        // ================= کانکشن‌ها =================
        // اسکرول‌بار عمودی لیست کانکشن‌ها را مخفی می‌کند (بدون تغییر XAML)
        private void HideConnListScrollBar()
        {
            try
            {
                DependencyObject? p = ConnList;
                while (p != null && p is not ScrollViewer)
                    p = LogicalTreeHelper.GetParent(p);
                if (p is ScrollViewer sv)
                    sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            }
            catch { }
        }

        // کانکشن متصل‌شده به صدر لیست می‌رود — همین مکانیسم باعث می‌شود لیست به مرور زمان به ترتیب استفاده‌ی واقعی
        // کاربر مرتب بماند و بین بستن/باز کردن برنامه هم حفظ شود (LastConnectedName هم همینجا ذخیره می‌شود)
        private void MoveToTop(string name)
        {
            _config.LastConnectedName = name;
            var idx = _config.Connections.FindIndex(c => c.Name == name);
            if (idx > 0)
            {
                var item = _config.Connections[idx];
                _config.Connections.RemoveAt(idx);
                _config.Connections.Insert(0, item);
            }
            _config.Save();
            RefreshList();
        }
        // فیلتر نوع کانکشن روی صفحه اصلی — رشته خالی یعنی نمایش همه
        private string _connFilter = "";
        // آیا لیست کانکشن‌ها به‌صورت کامل باز شده (دکمه «نمایش بیشتر» زده شده)؛ حداکثر تعداد پیش‌فرض نمایش = ۵
        private bool _connListExpanded;
        private const int ConnListVisibleCap = 5;
        // پاپ‌آپ منوی اقدامات ردیفی که هم اکنون باز است — قبل از رفرش دوباره لیست باید بسته شود، ورنه رها/معلق روی صفحه می‌ماند
        private Popup? _openRowMenuPopup;

        // کش پینگ سرورهای VPN Core به همراه وضعیت و مقدار تأخیر
        internal static readonly Dictionary<string, (int ping, bool isUp, string text)> _vpnPingCache = new(StringComparer.OrdinalIgnoreCase);

        // پاپ‌آپ منوی آبشاری فیلتر نوع کانکشن — باز/بسته با کلیک روی دکمه بالای لیست (همون الگوی منوی سه‌نقطه ردیف‌ها)
        private Popup? _openFilterPopup;

        // ترتیب نمایش پروتکل‌ها در منوی آبشاری — طبق اصل «هر مدل کانکشنی داریم باید توی لیست باشه»، این فقط ترتیب نمایش است؛
        // فیلتر واقعی پایین‌تر (AvailableConnTypes) فقط پروتکل‌هایی را نشان می‌دهد که حداقل یک کانکشن از آن نوع واقعاً وجود دارد
        // (مثلاً IKEv2 تا کانکشنی از این نوع نساخته‌ایم در منو دیده نمی‌شود؛ به محض ساختنش خودکار اضافه می‌شود)
        private static readonly string[] ProtocolOrder =
        {
            "openvpn",
            "wireguard",
            "amneziawg",
            "l2tp",
            "pptp",
            "sstp",
            "ikev2"
        };

        private List<string> AvailableConnTypes() =>
            ProtocolOrder.Where(t => _config.Connections.Any(c => string.Equals(c.Type, t, StringComparison.OrdinalIgnoreCase))).ToList();

        private void FilterDropdownBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_openFilterPopup != null)
            {
                _openFilterPopup.IsOpen = false;
                _openFilterPopup = null;
                return;
            }
            if (_openRowMenuPopup != null)
            {
                _openRowMenuPopup.IsOpen = false;
                _openRowMenuPopup = null;
            }

            var light = IsLightTheme();
            var popup = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                PlacementTarget = FilterDropdownBtn,
                Placement = PlacementMode.Bottom,
            };
            var stack = new StackPanel { Orientation = Orientation.Vertical, Width = 150 };
            var border = new Border
            {
                Background = new SolidColorBrush(light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1B, 0x24, 0x38)),
                BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x35, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x45, 0x94, 0xA3, 0xB8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = 0.30 },
                Child = stack,
            };
            popup.Child = border;
            popup.Closed += (_, __) => { if (_openFilterPopup == popup) _openFilterPopup = null; };

            var textNormal = new SolidColorBrush(light ? Color.FromRgb(0x0F, 0x17, 0x2A) : Color.FromRgb(0xE2, 0xE8, 0xF0));
            var accentBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));

            Button MakeItem(string tag, string label)
            {
                var isSelected = string.Equals(tag, _connFilter, StringComparison.OrdinalIgnoreCase);
                var item = new Button
                {
                    Content = label,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 8, 12, 8),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = isSelected ? accentBrush : textNormal,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = 11.5,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                item.Click += (_, __) =>
                {
                    popup.IsOpen = false;
                    _connFilter = tag;
                    _connListExpanded = false;
                    RefreshList();
                };
                return item;
            }

            stack.Children.Add(MakeItem("", Localization.T("همه")));
            foreach (var t in AvailableConnTypes())
                stack.Children.Add(MakeItem(t, CategoryLabel(t)));

            popup.IsOpen = true;
            _openFilterPopup = popup;
        }

        // برچسب دکمه‌ی فیلتر آبشاری را با فیلتر فعلی هماهنگ می‌کند
        private void UpdateFilterChips()
        {
            try
            {
                // اگر فیلتر روی نوعی مانده که دیگر هیچ کانکشنی از آن نیست (مثلاً آخرین کانکشن آن نوع حذف شده)، به «همه» برمی‌گردد
                if (_connFilter.Length > 0 && !AvailableConnTypes().Contains(_connFilter.ToLowerInvariant()))
                    _connFilter = "";
                FilterDropdownLabel.Text = _connFilter.Length == 0 ? Localization.T("همه") : CategoryLabel(_connFilter);
            }
            catch { }
        }

        private void RefreshList()
        {
            // منوی اقدامات ردیفی که باز مانده قبل از بازسازی ردیف‌ها باید بسته شود
            // ورنه ردیفی که تولیدکرده از درخت محو می‌شود اما خودش هرگز بسته نمی‌شود
            // و رها/معلق روی صفحه باقی می‌ماند (همان باگی که منوی جدا از کارت روی پنجره دیگر دیده می‌شد)
            if (_openRowMenuPopup != null)
            {
                _openRowMenuPopup.IsOpen = false;
                _openRowMenuPopup = null;
            }
            UpdateFilterChips();
            ConnList.Children.Clear();

            // انتخاب پیش‌فرض: اگر چیزی انتخاب نشده یا حذف شده، اولین کانکشن
            if (_config.Connections.Count > 0 &&
                (_selectedName == null || _config.Connections.All(c => c.Name != _selectedName)))
                _selectedName = _config.Connections[0].Name;

            var filtered = _config.Connections
                .Where(c => _connFilter.Length == 0 || string.Equals(c.Type, _connFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var toRender = filtered;
            if (!_connListExpanded && filtered.Count > ConnListVisibleCap)
            {
                // کانکشن وصل/انتخاب‌شده همیشه دیده بماند، حتی اگر بیرون از ۵ تای اول لیست فیلترشده باشد
                var pinnedName = _connectedName ?? _selectedName;
                var pinnedIndex = pinnedName != null ? filtered.FindIndex(c => c.Name == pinnedName) : -1;
                List<int> indices;
                if (pinnedIndex >= ConnListVisibleCap)
                {
                    indices = Enumerable.Range(0, ConnListVisibleCap - 1).ToList();
                    indices.Add(pinnedIndex);
                    indices.Sort();
                }
                else
                {
                    indices = Enumerable.Range(0, ConnListVisibleCap).ToList();
                }
                toRender = indices.Select(i => filtered[i]).ToList();
            }

            foreach (var c in toRender)
                ConnList.Children.Add(MakeRowBtn(c));

            if (filtered.Count > ConnListVisibleCap)
                ConnList.Children.Add(MakeMoreLessRow(_connListExpanded, filtered.Count - toRender.Count));
        }

        // ردیف «نمایش بیشتر / بستن» زیر لیست کانکشن‌ها — وقتی تعداد از سقف نمایش (۵) بیشتر باشد
        private UIElement MakeMoreLessRow(bool expanded, int hiddenCount)
        {
            var btn = new Button
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 6),
                Padding = new Thickness(8, 4, 8, 4),
                Focusable = false,
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            var subBrush = (Brush)FindResource("SubTextBrush");
            stack.Children.Add(new TextBlock
            {
                Text = expanded ? Localization.T("بستن") : string.Format(Localization.T("نمایش {0} مورد دیگر"), hiddenCount),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = subBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(new TextBlock
            {
                Text = expanded ? "⌃" : "⌄",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(5, 0, 0, 0),
                Foreground = subBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            btn.Content = stack;
            btn.Click += (_, __) =>
            {
                _connListExpanded = !_connListExpanded;
                _config.ConnListExpanded = _connListExpanded; // رفتار باز/بسته لیست بین بستن/باز کردن برنامه حفظ می‌شود
                _config.Save();
                RefreshList();
            };
            return btn;
        }

        // برچسب نمایشی کتگوری/پروتکل کانکشن — برای بج کارت لیست و پیشوند نام در کارت بالای برنامه (طبق طرح تاییدشده: OpenVPN Poland-UDP / L2TP Netherlands)
        private static string CategoryLabel(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "openvpn": return "OpenVPN";
                case "l2tp": return "L2TP";
                case "pptp": return "PPTP";
                case "sstp": return "SSTP";
                case "ikev2": return "IKEv2";
                case "wireguard": return "WireGuard";
                case "amneziawg": return "AmneziaWG";
                default: return (type ?? "").ToUpperInvariant();
            }
        }

        internal static string ExtractFlagOrIcon(string name, string protocol)
        {
            if (string.IsNullOrEmpty(name)) return "⚡";
            for (int i = 0; i < name.Length - 1; i++)
            {
                if (char.IsSurrogatePair(name, i))
                {
                    int codePoint = char.ConvertToUtf32(name, i);
                    if (codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF)
                    {
                        if (i + 3 < name.Length && char.IsSurrogatePair(name, i + 2))
                        {
                            int second = char.ConvertToUtf32(name, i + 2);
                            if (second >= 0x1F1E6 && second <= 0x1F1FF)
                                return name.Substring(i, 4);
                        }
                    }
                }
            }
            var upper = name.ToUpperInvariant();
            if (upper.Contains("GERMAN") || upper.Contains("FRANKFURT") || upper.Contains(" DE ") || upper.Contains("[DE]")) return "🇩🇪";
            if (upper.Contains("NETHERLAND") || upper.Contains("AMSTERDAM") || upper.Contains(" NL ") || upper.Contains("[NL]")) return "🇳🇱";
            if (upper.Contains("UNITED KINGDOM") || upper.Contains("LONDON") || upper.Contains(" GB ") || upper.Contains("[GB]") || upper.Contains("[UK]")) return "🇬🇧";
            if (upper.Contains("UNITED STATES") || upper.Contains("USA") || upper.Contains(" US ") || upper.Contains("[US]")) return "🇺🇸";
            if (upper.Contains("TURKEY") || upper.Contains("ISTANBUL") || upper.Contains(" TR ") || upper.Contains("[TR]")) return "🇹🇷";
            if (upper.Contains("FRANCE") || upper.Contains("PARIS") || upper.Contains(" FR ") || upper.Contains("[FR]")) return "🇫🇷";
            if (upper.Contains("FINLAND") || upper.Contains(" FI ") || upper.Contains("[FI]")) return "🇫🇮";
            if (upper.Contains("CANADA") || upper.Contains(" CA ") || upper.Contains("[CA]")) return "🇨🇦";
            if (upper.Contains("RUSSIA") || upper.Contains("MOSCOW") || upper.Contains(" RU ") || upper.Contains("[RU]")) return "🇷🇺";
            if (upper.Contains("POLAND") || upper.Contains("WARSAW") || upper.Contains(" PL ") || upper.Contains("[PL]")) return "🇵🇱";
            if (upper.Contains("SWEDEN") || upper.Contains("STOCKHOLM") || upper.Contains(" SE ") || upper.Contains("[SE]")) return "🇸🇪";
            if (upper.Contains("SINGAPORE") || upper.Contains(" SG ") || upper.Contains("[SG]")) return "🇸🇬";

            var p = (protocol ?? "").ToLowerInvariant();
            if (p.Contains("wireguard") || p.Contains("amnezia")) return "🛡️";
            if (p.Contains("openvpn")) return "🔒";
            return "⚡";
        }

        // رنگ بج کتگوری برای هر پروتکل — دقیقاً مطابق طرح موکاپ تاییدشده (آبی=OpenVPN، بنفش=SSTP، کهربایی=L2TP، فیروزه‌ای=IKEv2)
        private static (Color bg, Color border, Color fg) CategoryBadgeColors(string type, bool light)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "sstp":
                    return light
                        ? (Color.FromRgb(0xF3, 0xE8, 0xFF), Color.FromRgb(0xD8, 0xB4, 0xFE), Color.FromRgb(0x7E, 0x22, 0xCE))
                        : (Color.FromArgb(0x26, 0xA8, 0x55, 0xF7), Color.FromArgb(0x48, 0xA8, 0x55, 0xF7), Color.FromRgb(0xD8, 0xB4, 0xFE));
                case "l2tp":
                    return light
                        ? (Color.FromRgb(0xFE, 0xF3, 0xC7), Color.FromRgb(0xFC, 0xD3, 0x4D), Color.FromRgb(0xB4, 0x53, 0x09))
                        : (Color.FromArgb(0x26, 0xF5, 0x9E, 0x0B), Color.FromArgb(0x48, 0xF5, 0x9E, 0x0B), Color.FromRgb(0xFC, 0xD3, 0x4D));
                case "ikev2":
                    return light
                        ? (Color.FromRgb(0xCC, 0xFB, 0xF1), Color.FromRgb(0x5E, 0xEA, 0xD4), Color.FromRgb(0x0F, 0x76, 0x6E))
                        : (Color.FromArgb(0x26, 0x2D, 0xD4, 0xBF), Color.FromArgb(0x48, 0x2D, 0xD4, 0xBF), Color.FromRgb(0x5E, 0xEA, 0xD4));
                case "pptp":
                    return light
                        ? (Color.FromRgb(0xE2, 0xE8, 0xF0), Color.FromRgb(0xCB, 0xD5, 0xE1), Color.FromRgb(0x33, 0x41, 0x55))
                        : (Color.FromArgb(0x26, 0x94, 0xA3, 0xB8), Color.FromArgb(0x48, 0x94, 0xA3, 0xB8), Color.FromRgb(0xCB, 0xD5, 0xE1));
                case "wireguard":
                    // سبز — رنگ رسمی WireGuard
                    return light
                        ? (Color.FromRgb(0xDC, 0xFC, 0xE7), Color.FromRgb(0x86, 0xEF, 0xAC), Color.FromRgb(0x16, 0x6D, 0x3B))
                        : (Color.FromArgb(0x26, 0x22, 0xC5, 0x5E), Color.FromArgb(0x48, 0x22, 0xC5, 0x5E), Color.FromRgb(0x86, 0xEF, 0xAC));
                case "amneziawg":
                    // بنفش-آبی — برند AmneziaWG
                    return light
                        ? (Color.FromRgb(0xEE, 0xE6, 0xFF), Color.FromRgb(0xC4, 0xB5, 0xFD), Color.FromRgb(0x5B, 0x21, 0xB6))
                        : (Color.FromArgb(0x26, 0x7C, 0x3A, 0xED), Color.FromArgb(0x48, 0x7C, 0x3A, 0xED), Color.FromRgb(0xC4, 0xB5, 0xFD));
                default:
                    return light
                        ? (Color.FromRgb(0xDB, 0xEA, 0xFE), Color.FromRgb(0x93, 0xC5, 0xFD), Color.FromRgb(0x1D, 0x4E, 0xD8))
                        : (Color.FromArgb(0x24, 0x3B, 0x82, 0xF6), Color.FromArgb(0x38, 0x3B, 0x82, 0xF6), Color.FromRgb(0xBF, 0xDB, 0xFE));
            }
        }

        private UIElement MakeRowBtn(ConnectionProfile c)
        {
            var selected = c.Name == _selectedName;
            // قبلاً فقط _engine.IsRunning بررسی می‌شد که می‌توانست با وضعیت واقعی کارت بالا (که از روی رویداد Stopped/Connected به‌روز می‌شود) هم‌خوان نباشد؛ اکنون هم‌زمان با همان پرچم که کارت بالا/دکمه‌ی پاور را رنگ می‌کند (_currentPowerState) هم‌راستی می‌شود
            var connected = _engine.IsRunning && _currentPowerState == "connected" && c.Name == _connectedName;

            // کارت وصل‌شده فضای بیشتری دور خودش لازم دار������ تا خش سبز (DropShadowEffect) توسط کارت‌های مجاور پوشانده نشود
            var row = new Grid { Margin = connected ? new Thickness(4, 8, 4, 12) : new Thickness(4, 0, 4, 4), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var light = IsLightTheme();

            var accent = connected
                ? (light ? Color.FromRgb(0x16, 0xA3, 0x4A) : Color.FromRgb(0x22, 0xC5, 0x5E))
                : selected ? (light ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromRgb(0x3B, 0x82, 0xF6))
                : (light ? Color.FromRgb(0xCB, 0xD5, 0xE1) : Color.FromRgb(0x47, 0x5A, 0x75));

            // نوار رنگی کنار کارت به جای border دور کارت
            var strip = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(7, 0, 0, 7),
                Background = new SolidColorBrush(accent),
                Opacity = connected || selected ? 1.0 : 0.55,
            };
            Grid.SetColumn(strip, 0);
            row.Children.Add(strip);

            var cardBg = connected
                ? new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xDC, 0xFC, 0xE7) : Color.FromArgb(0x78, 0x13, 0x28, 0x24))
                : selected
                    ? new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xDB, 0xEA, 0xFE) : Color.FromArgb(0x78, 0x12, 0x2B, 0x52))
                    : new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x70, 0x1E, 0x29, 0x3B));

            var card = new Border
            {
                Background = cardBg,
                BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x28, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0x94, 0xA3, 0xB8)),
                BorderThickness = light ? new Thickness(0, 1, 1, 1) : new Thickness(0), // در تم روشن یک حاشیه ظریف برای تفکیک کارت از پس‌زمینه
                CornerRadius = new CornerRadius(0, 8, 8, 0),
                Padding = new Thickness(7, 4, 6, 4),
            };
            Grid.SetColumn(card, 1);

            // خط لایت دور کادر کانکشن وصل‌شده/انتخاب‌شده — دقیقاً مثل مدل موکاپ (نه فقط نوار کناری)
            if (connected)
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
                card.BorderThickness = new Thickness(0, 1.4, 1.4, 1.4);
                card.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x22, 0xC5, 0x5E),
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.5,
                };
            }
            else if (selected)
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                card.BorderThickness = new Thickness(0, 1.2, 1.2, 1.2);
            }

            var contentGrid = new Grid { FlowDirection = FlowDirection.LeftToRight };
            // مهم: والد (Window) FlowDirection=RightToLeft دارد که بدون این ترتیب، ترتیب ستون‌های Grid را می‌چرخاند — همین بود که باعث می‌شد سمت راست/چپ برعکس شود
            // سه ستون طبق طرح تاییدشده: کتگوری (چپ) / نام کانکشن (وسط) / وضعیت+سه‌نقطه (راست)
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // منوی گزینه‌ها (اتصال/ویرایش/حذف) — پاپ‌آپ شناور با ۳ ردیف نوشتاری ��یر هم
            var menuPopup = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            var menuStack = new StackPanel { Orientation = Orientation.Vertical, Width = 160 };
            var menuBorder = new Border
            {
                Background = new SolidColorBrush(light ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1B, 0x24, 0x38)),
                BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x35, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x45, 0x94, 0xA3, 0xB8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = 0.30 },
                Child = menuStack,
            };
            menuPopup.Child = menuBorder;
            // وقتی StaysOpen=false با کلیک بیرون بسته می‌شود، مرجع مشترک هم باید از طریق همین رویداد پاک شود
            menuPopup.Closed += (_, __) =>
            {
                if (_openRowMenuPopup == menuPopup) _openRowMenuPopup = null;
            };

            var menuTextNormal = new SolidColorBrush(light ? Color.FromRgb(0x0F, 0x17, 0x2A) : Color.FromRgb(0xE2, 0xE8, 0xF0));
            var menuTextDanger = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            var menuSepBrush = new SolidColorBrush(light ? Color.FromArgb(0x22, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x28, 0x94, 0xA3, 0xB8));

            Button MakeMenuRow(string icon, string text, Brush fg) => new Button
            {
                Content = $"{icon}   {text}",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Thickness(12, 10, 12, 10),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = fg,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            var menuConnectRow = MakeMenuRow("🔌", connected ? "Disconnect" : "Connect", connected ? menuTextDanger : menuTextNormal);
            menuConnectRow.Click += async (_, __) =>
            {
                MainWindow.LastConnectionType = "vpn";
                menuPopup.IsOpen = false;
                if (connected)
                {
                    await StopManuallyAsync();
                    return;
                }
                if (_engine.IsRunning)
                {
                    if (c.Name == _connectedName) return;
                    var q = string.Format(MsgSwitchConfirm, ActiveConnText.Text, c.Name);
                    if (!AskDialog.Confirm(this, q)) return;
                    await SwitchToConnectionAsync(c);
                    ShowPanel("home");
                    return;
                }
                var qConnect = string.Format(MsgConnectConfirm, c.Name);
                if (!AskDialog.Confirm(this, qConnect)) return;
                _selectedName = c.Name;
                ActiveConnText.Text = $"{CategoryLabel(c.Type)} {c.Name}";
                ServerSubText.Text = c.ServerLine;
                UpdateActiveBadge(c.Name, c.Type);
                RefreshList();
                ConnectSelected();
                ShowPanel("home");
            };
            menuStack.Children.Add(menuConnectRow);
            menuStack.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 2, 6, 2), Background = menuSepBrush });

            var menuPingRow = MakeMenuRow("⚡", "Ping", menuTextNormal);
            menuPingRow.Click += async (_, __) => { menuPopup.IsOpen = false; await PingSingleVpnProfileAsync(c); };
            menuStack.Children.Add(menuPingRow);
            menuStack.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 2, 6, 2), Background = menuSepBrush });

            var menuEditRow = MakeMenuRow("✏", "Edit", menuTextNormal);
            menuEditRow.Click += (_, __) => { menuPopup.IsOpen = false; EditConn(c.Name); };
            menuStack.Children.Add(menuEditRow);
            menuStack.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 2, 6, 2), Background = menuSepBrush });

            var menuDeleteRow = MakeMenuRow("🗑", "Delete", menuTextDanger);
            menuDeleteRow.Click += (_, __) => { menuPopup.IsOpen = false; DeleteConn(c.Name); };
            menuStack.Children.Add(menuDeleteRow);

            var textStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

            var nameText = new TextBlock
            {
                Text = c.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(light ? Color.FromRgb(0x0F, 0x17, 0x2A) : Color.FromRgb(0xF8, 0xFA, 0xFC)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight,
            };
            textStack.Children.Add(nameText);

            var subtitle = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(c.Subtitle) ? c.EffectiveServer : c.Subtitle,
                FontSize = 9,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = new SolidColorBrush(light ? Color.FromRgb(0x47, 0x55, 0x69) : Color.FromRgb(0x94, 0xA3, 0xB8)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight,
            };
            textStack.Children.Add(subtitle);

            // متن/زیرنویس در یک بلوک عمودی وسط قرار می‌گیرد (RowSpan=2 مثل بج/دکمهٔ سه‌ن��طه)؛ قبلاً هر یک توی ردیف جدا بود و همیشه از بالای کادر شروع می‌شد
            Grid.SetColumn(textStack, 1);
            Grid.SetRow(textStack, 0);
            Grid.SetRowSpan(textStack, 2);
            contentGrid.Children.Add(textStack);

            // بج کتگوری (پروتکل) — همیشه نمایش داده می‌شود، جدا از وضعیت
            var catColors = CategoryBadgeColors(c.Type, light);
            var categoryBadge = new Border
            {
                Background = new SolidColorBrush(catColors.bg),
                BorderBrush = new SolidColorBrush(catColors.border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = CategoryLabel(c.Type),
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(catColors.fg),
                    FlowDirection = FlowDirection.LeftToRight,
                },
            };
            Grid.SetColumn(categoryBadge, 0);
            Grid.SetRow(categoryBadge, 0);
            Grid.SetRowSpan(categoryBadge, 2);
            contentGrid.Children.Add(categoryBadge);

            // برچسب وضعیت فقط برای کارت متصل/درحال اتصال نمایش داده می‌شود — کارت‌های idle فقط سه‌نقطه دارند
            var isActiveRow = c.Name == _selectedName;
            string? statusLabel = null;
            if (connected) statusLabel = "CONNECTED";
            else if (isActiveRow && _currentPowerState == "reconnecting") statusLabel = "RECONNECTING";
            else if (isActiveRow && _currentPowerState == "connecting") statusLabel = "CONNECTING";
            Border? statusPill = null;
            if (statusLabel != null)
            {
                statusPill = new Border
                {
                    Background = new SolidColorBrush(connected
                        ? (light ? Color.FromArgb(0xFF, 0xD1, 0xFA, 0xDD) : Color.FromArgb(0x28, 0x22, 0xC5, 0x5E))
                        : (light ? Color.FromArgb(0xFF, 0xDB, 0xEA, 0xFE) : Color.FromArgb(0x24, 0x3B, 0x82, 0xF6))),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(5, 1, 5, 1),
                    BorderBrush = new SolidColorBrush(connected
                        ? (light ? Color.FromRgb(0x16, 0xA3, 0x4A) : Color.FromArgb(0x45, 0x22, 0xC5, 0x5E))
                        : (light ? Color.FromRgb(0x93, 0xC5, 0xFD) : Color.FromArgb(0x38, 0x3B, 0x82, 0xF6))),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                    Child = new TextBlock
                    {
                        Text = statusLabel,
                        FontSize = 8.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(connected
                            ? (light ? Color.FromRgb(0x15, 0x80, 0x3D) : Color.FromRgb(0x86, 0xEF, 0xAC))
                            : (light ? Color.FromRgb(0x1D, 0x4E, 0xD8) : Color.FromRgb(0xBF, 0xDB, 0xFE))),
                        FlowDirection = FlowDirection.LeftToRight,
                    },
                };
            }

            // دکمه سه‌نقطه — همیشه با پس‌زمینه‌ی دایره‌ای ملایم و قابل‌مشاهده، دقیقاً کنار بج و روی سمت راست خود کارت (نه گوشه‌ی مطلق، نه پاپ‌آپ رها روی صفحه)
            var dotsBtn = new Border
            {
                FlowDirection = FlowDirection.LeftToRight,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0),
                Background = new SolidColorBrush(light ? Color.FromArgb(0x14, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Options (Connect / Edit / Delete)",
                Child = new TextBlock
                {
                    Text = "⋯",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -3, 0, 0),
                    Foreground = new SolidColorBrush(light ? Color.FromRgb(0x33, 0x41, 0x55) : Color.FromRgb(0xE2, 0xE8, 0xF0)),
                },
            };
            dotsBtn.MouseEnter += (_, __) => dotsBtn.Background = new SolidColorBrush(light ? Color.FromArgb(0x28, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            dotsBtn.MouseLeave += (_, __) => dotsBtn.Background = new SolidColorBrush(light ? Color.FromArgb(0x14, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            menuPopup.PlacementTarget = dotsBtn;
            menuPopup.Placement = PlacementMode.Bottom;
            dotsBtn.MouseLeftButtonDown += (_, e) => e.Handled = true;
            dotsBtn.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true; // جلوگیری از هایلایت/انتخاب کارت هنگام کلیک روی سه‌نقطه
                if (!menuPopup.IsOpen)
                {
                    // اگر منوی ردیف دیگری باز مانده، ��بل از باز کردن این یکی بسته شود (فقط یک منو همزمان باز بماند)
                    // توجّه: نمی‌شود از ?. روی سمت چپ ک assignment استفاده کرد (در C# تحح کمپایل می‌شود)؛ باید null-check معمولی زد شود
                    if (_openRowMenuPopup != null) _openRowMenuPopup.IsOpen = false;
                    // راست گوشهی بالا با لبهٔ دکمهی سه‌نقطه هم���تراز می‌شود — منو به سمت چپ کارت باز می‌شود، نه بیرون از پنجره
                    menuPopup.HorizontalOffset = dotsBtn.ActualWidth - menuStack.Width;
                    _openRowMenuPopup = menuPopup;
                }
                else
                {
                    _openRowMenuPopup = null;
                }
                menuPopup.IsOpen = !menuPopup.IsOpen;
            };

            // سه‌نقطه همیشه سمت راست کارت است؛ برچسب وضعیت (در صورت وجود) کنار آن می‌آید — کتگوری اینجا نیست، جایش ثابت در ستون چپ (۰) است
            var rowRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            
            // نمایش دائمی پینگ سرور (Ping Pill) با امکان کلیک برای تست سریع
            (int ping, bool isUp, string text) pingData = (-1, false, "");
            if (_vpnPingCache.TryGetValue(c.Name, out var cachedPing))
            {
                pingData = cachedPing;
            }
            else
            {
                var kuma = UptimeKumaClient.GetStatusForProfile(c.Name);
                if (kuma != null)
                {
                    pingData = (kuma.Ping, kuma.IsUp, kuma.IsUp ? $"{kuma.Ping} ms" : "Down");
                }
            }

            Color pingColor;
            string pingText;
            if (pingData.ping > 0)
            {
                pingColor = pingData.ping < 120 ? Color.FromRgb(0x22, 0xC5, 0x5E) :
                            (pingData.ping < 250 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0xEF, 0x44, 0x44));
                pingText = pingData.text;
            }
            else if (pingData.text == "Timeout" || pingData.text == "Down")
            {
                pingColor = Color.FromRgb(0xEF, 0x44, 0x44);
                pingText = pingData.text;
            }
            else if (pingData.text == "Pinging...")
            {
                pingColor = Color.FromRgb(0x3B, 0x82, 0xF6);
                pingText = "...";
            }
            else
            {
                pingColor = light ? Color.FromRgb(0x64, 0x74, 0x8B) : Color.FromRgb(0x94, 0xA3, 0xB8);
                pingText = "⚡ Ping";
            }

            var pingDot = new Border
            {
                Background = new SolidColorBrush(pingColor),
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var pingLabel = new TextBlock
            {
                Text = pingText,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(pingColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            var pingPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x20, pingColor.R, pingColor.G, pingColor.B)),
                BorderBrush = new SolidColorBrush(pingColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "تست پینگ زنده سرور (کلیک کنید)",
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { pingDot, pingLabel }
                }
            };

            pingPill.MouseLeftButtonUp += async (s, e) =>
            {
                e.Handled = true;
                pingLabel.Text = "...";
                pingDot.Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                pingLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                pingPill.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                pingPill.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x3B, 0x82, 0xF6));

                var res = await MeasureVpnProfilePingAsync(c);
                _vpnPingCache[c.Name] = res;

                var col = res.isUp ? (res.ping < 120 ? Color.FromRgb(0x22, 0xC5, 0x5E) : (res.ping < 250 ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0xEF, 0x44, 0x44))) : Color.FromRgb(0xEF, 0x44, 0x44);
                pingDot.Background = new SolidColorBrush(col);
                pingLabel.Foreground = new SolidColorBrush(col);
                pingLabel.Text = res.text;
                pingPill.BorderBrush = new SolidColorBrush(col);
                pingPill.Background = new SolidColorBrush(Color.FromArgb(0x20, col.R, col.G, col.B));
            };

            rowRight.Children.Add(pingPill);
            rowRight.Children.Add(dotsBtn);
            if (statusPill != null) rowRight.Children.Add(statusPill);
            Grid.SetColumn(rowRight, 2);
            Grid.SetRow(rowRight, 0);
            Grid.SetRowSpan(rowRight, 2);
            contentGrid.Children.Add(rowRight);

            if (connected)
            {
                // نوار وضعیت سبز ثابت روی خود کارت — برخلاف درخشای بیرونی کارت که با کارت کناری پوشانده می‌شد، این همیشه داخل محدودهٔ خود کارت رسم می‌شود و هیچ‌وقت پوشیده نمی‌شود
                var statusWrap = new DockPanel();
                var statusBar = new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(0, 6, 0, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
                    Margin = new Thickness(-7, -4, -6, 5),
                };
                DockPanel.SetDock(statusBar, Dock.Top);
                statusWrap.Children.Add(statusBar);
                statusWrap.Children.Add(contentGrid);
                card.Child = statusWrap;
            }
            else
            {
                card.Child = contentGrid;
            }
            row.Children.Add(card);

            if (!connected && !selected)
            {
                row.MouseEnter += (_, __) =>
                {
                    card.Background = new SolidColorBrush(light ? Color.FromArgb(0xFF, 0xEE, 0xF2, 0xF6) : Color.FromArgb(0x95, 0x2A, 0x3A, 0x52));
                    card.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x50, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x50, 0x3B, 0x82, 0xF6));
                    card.BorderThickness = new Thickness(0, 1, 1, 1);
                };
                row.MouseLeave += (_, __) =>
                {
                    card.Background = cardBg;
                    card.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(0x28, 0x0F, 0x17, 0x2A) : Color.FromArgb(0x22, 0x94, 0xA3, 0xB8));
                    card.BorderThickness = light ? new Thickness(0, 1, 1, 1) : new Thickness(0);
                };
            }

            // کلیک تک روی کارت = فقط انتخاب/هایلایت — دبل‌کلیک = سوال تایید و اتصال/سوییچ واقعی
            card.MouseLeftButtonUp += (_, __) =>
            {
                MainWindow.LastConnectionType = "vpn";
                if (_engine.IsRunning)
                {
                    if (c.Name == _connectedName) return;
                    _selectedName = c.Name; // فقط هایلایت می‌شود؛ بدون سوال و بدون قطع اتصال فعلی
                    RefreshList();
                    return;
                }
                _selectedName = c.Name;
                ActiveConnText.Text = $"{CategoryLabel(c.Type)} {c.Name}";
                ServerSubText.Text = c.ServerLine;
                UpdateActiveBadge(c.Name, c.Type);
                RefreshList();
            };
            card.MouseLeftButtonDown += async (_, e) =>
            {
                if (e.ClickCount != 2) return;
                MainWindow.LastConnectionType = "vpn";
                if (_engine.IsRunning)
                {
                    if (c.Name == _connectedName) return;
                    var q = string.Format(MsgSwitchConfirm, ActiveConnText.Text, c.Name);
                    if (!AskDialog.Confirm(this, q)) return;
                    await SwitchToConnectionAsync(c);
                    ShowPanel("home");
                    return;
                }
                var qConnect = string.Format(MsgConnectConfirm, c.Name);
                if (!AskDialog.Confirm(this, qConnect)) return;
                _selectedName = c.Name;
                ActiveConnText.Text = $"{CategoryLabel(c.Type)} {c.Name}";
                ServerSubText.Text = c.ServerLine;
                UpdateActiveBadge(c.Name, c.Type);
                RefreshList();
                ConnectSelected();
                ShowPanel("home");
            };

            return row;
        }

        
        
        private ConnectionProfile? _editingConnProfile;
        private string _formOvpnInline = "";
        private string _formWgConf = "";
        private bool _connFormFiltersApplied;

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            OpenConnForm(null);
        }

        private void EditConn(string name)
        {
            var existing = _config.Connections.FirstOrDefault(c => c.Name == name);
            if (existing == null) return;
            OpenConnForm(existing);
        }

        private void OpenConnForm(ConnectionProfile? existing)
        {
            _editingConnProfile = existing;
            _formOvpnInline = existing?.OvpnInline ?? "";
            _formWgConf = existing?.WireGuardConf ?? "";

            if (!_connFormFiltersApplied)
            {
                AsciiInputFilter.ApplyTo(ConnFormUserBox);
                AsciiInputFilter.ApplyTo(ConnFormPassBox);
                AsciiInputFilter.ApplyTo(ConnFormPskBox);
                AsciiInputFilter.ApplyTo(ConnFormWgPresharedKeyBox);
                _connFormFiltersApplied = true;
            }

            if (existing == null)
            {
                VpnEditTitleText.Text = Localization.T("افزودن کانکشن جدید");
                VpnEditSubtitleText.Text = Localization.T("مشخصات و تنظیمات اتصال VPN را وارد نمایید");

                ConnFormNameBox.Text = "";
                ConnFormServerBox.Text = "";
                ConnFormPortBox.Text = "";
                ConnFormOverrideBox.Text = "";
                ConnFormUserBox.Text = "";
                ConnFormPassBox.Password = "";
                ConnFormPskBox.Text = "";
                ConnFormOvpnStatusText.Text = "";

                ConnFormWgPrivateKeyBox.Text = "";
                ConnFormWgAddressBox.Text = "";
                ConnFormWgDnsBox.Text = "";
                ConnFormWgMtuBox.Text = "";
                ConnFormWgPublicKeyBox.Text = "";
                ConnFormWgPresharedKeyBox.Text = "";
                ConnFormWgEndpointBox.Text = "";
                ConnFormWgAllowedIPsBox.Text = "0.0.0.0/0";
                ConnFormWgKeepaliveBox.Text = "";
                ConnFormAwgCheckBox.IsChecked = false;
                ConnFormWgStatusText.Text = Localization.T("یا فیلدهای زیر را دستی تکمیل کنید");

                ConnFormTypeCombo.SelectedIndex = 0;
            }
            else
            {
                VpnEditTitleText.Text = string.Format(Localization.T("ویرایش کانکشن: {0}"), existing.Name);
                VpnEditSubtitleText.Text = string.Format(Localization.T("پروتکل {0} — ویرایش و به‌روزرسانی پارامترها"), existing.Type);

                ConnFormNameBox.Text = existing.Name;

                foreach (ComboBoxItem item in ConnFormTypeCombo.Items)
                {
                    if (string.Equals((string)item.Tag, existing.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        ConnFormTypeCombo.SelectedItem = item;
                        break;
                    }
                }

                var isWg = existing.Type == "wireguard" || existing.Type == "amneziawg";
                if (isWg)
                {
                    if (!string.IsNullOrWhiteSpace(_formWgConf))
                    {
                        ConnFormWgStatusText.Text = Localization.T("کانفیگ: بارگذاری شده ✔");
                        PopulateFormWgFields(_formWgConf);
                    }
                    else
                    {
                        ConnFormWgStatusText.Text = Localization.T("یا فیلدهای زیر را دستی تکمیل کنید");
                    }
                }
                else
                {
                    ConnFormServerBox.Text = existing.Server;
                    ConnFormPortBox.Text = existing.Port?.ToString() ?? "";
                    ConnFormOverrideBox.Text = existing.ServerOverride;
                    ConnFormUserBox.Text = existing.Username;
                    ConnFormPassBox.Password = existing.Password;
                    ConnFormPskBox.Text = existing.Psk;

                    foreach (ComboBoxItem item in ConnFormProtoCombo.Items)
                    {
                        if (string.Equals((string)item.Tag, existing.Proto, StringComparison.OrdinalIgnoreCase))
                        {
                            ConnFormProtoCombo.SelectedItem = item;
                            break;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(_formOvpnInline))
                        ConnFormOvpnStatusText.Text = Localization.T("پروفایل تعبیه‌شده موجود است ✔");
                    else
                        ConnFormOvpnStatusText.Text = "";
                }
            }

            UpdateConnFormFieldVisibility();

            VpnListView.Visibility = Visibility.Collapsed;
            VpnEditView.Visibility = Visibility.Visible;
        }

        private void CloseConnForm()
        {
            _editingConnProfile = null;
            VpnEditView.Visibility = Visibility.Collapsed;
            VpnListView.Visibility = Visibility.Visible;
        }

        private void ConnFormCancel_Click(object sender, RoutedEventArgs e)
        {
            CloseConnForm();
        }

        private void ConnFormType_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateConnFormFieldVisibility();
        }

        private void UpdateConnFormFieldVisibility()
        {
            if (ConnFormStandardPanel == null || ConnFormWgPanel == null || ConnFormTypeCombo == null) return;

            var selType = (ConnFormTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "openvpn";
            var isWg = selType == "wireguard" || selType == "amneziawg";

            ConnFormStandardPanel.Visibility = isWg ? Visibility.Collapsed : Visibility.Visible;
            ConnFormWgPanel.Visibility = isWg ? Visibility.Visible : Visibility.Collapsed;

            if (!isWg)
            {
                ConnFormOvpnPanel.Visibility = selType == "openvpn" ? Visibility.Visible : Visibility.Collapsed;
                ConnFormPskPanel.Visibility = selType == "l2tp" ? Visibility.Visible : Visibility.Collapsed;
                UpdateConnFormCredsEnabled(selType);
            }
        }

        private void UpdateConnFormCredsEnabled(string selType)
        {
            var certOnly = selType == "openvpn"
                && _formOvpnInline.Trim().Length > 0
                && !_formOvpnInline.Contains("auth-user-pass");
            ConnFormUserBox.IsEnabled = !certOnly;
            ConnFormPassBox.IsEnabled = !certOnly;
        }

        private void ConnFormAwgCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (ConnFormAwgPanel == null) return;
            ConnFormAwgPanel.Visibility = ConnFormAwgCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ConnFormImportOvpn_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Filter = "OpenVPN profile (*.ovpn)|*.ovpn|All files (*.*)|*.*" };
            if (ofd.ShowDialog(this) != true) return;

            string norm;
            try { norm = File.ReadAllText(ofd.FileName).Replace("\r\n", "\n"); }
            catch { AskDialog.Info(this, "فایل .ovpn معتبر نیست."); return; }

            var lines = norm.Split('\n');
            if (!lines.Any(l => l.Trim() == "client") || !lines.Any(l => l.TrimStart().StartsWith("remote ")))
            { AskDialog.Info(this, "فایل .ovpn معتبر نیست."); return; }

            if (lines.Any(l => l.TrimStart().StartsWith("dev tap")))
            { AskDialog.Info(this, "پروفایل TAP پشتیبانی نمی‌شود."); return; }

            var host = ""; var port = 0; var proto = "";
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (!t.StartsWith("remote ")) continue;
                var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) host = parts[1];
                if (parts.Length >= 3) int.TryParse(parts[2], out port);
                if (parts.Length >= 4) proto = parts[3].ToLowerInvariant();
                break;
            }
            if (proto.Length == 0)
            {
                var pl = lines.Select(l => l.Trim()).FirstOrDefault(t => t.StartsWith("proto "));
                if (pl != null)
                    proto = pl.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1)?.ToLowerInvariant() ?? "";
            }

            var kept = lines.Where(l =>
            {
                var t = l.TrimStart();
                return !(t.StartsWith("remote ") || t.StartsWith("proto ")
                    || t.StartsWith("up ") || t.StartsWith("down ")
                    || t.StartsWith("script-security"));
            });
            _formOvpnInline = string.Join(Environment.NewLine, kept);

            if (ConnFormNameBox.Text.Trim().Length == 0)
                ConnFormNameBox.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
            if (host.Length > 0) ConnFormServerBox.Text = host;
            if (port > 0) ConnFormPortBox.Text = port.ToString();

            if (proto.StartsWith("tcp"))
            {
                foreach (ComboBoxItem item in ConnFormProtoCombo.Items)
                    if ((string)item.Tag == "tcp") { ConnFormProtoCombo.SelectedItem = item; break; }
            }
            else if (proto.StartsWith("udp"))
            {
                foreach (ComboBoxItem item in ConnFormProtoCombo.Items)
                    if ((string)item.Tag == "udp") { ConnFormProtoCombo.SelectedItem = item; break; }
            }

            ConnFormOvpnStatusText.Text = Localization.T("پروفایل تعبیه‌شده: ") + Path.GetFileName(ofd.FileName);
            UpdateConnFormCredsEnabled("openvpn");
        }

        private void ConnFormImportWgConf_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "WireGuard / AmneziaWG config (*.conf)|*.conf|All files (*.*)|*.*",
                Title = Localization.T("انتخاب فایل .conf")
            };
            if (ofd.ShowDialog(this) != true) return;

            string conf;
            try { conf = File.ReadAllText(ofd.FileName).Replace("\r\n", "\n"); }
            catch { AskDialog.Info(this, "فایل .conf معتبر نیست."); return; }

            if (!conf.Contains("[Interface]") || !conf.Contains("[Peer]"))
            {
                AskDialog.Info(this, "فایل .conf معتبر نیست (باید شامل [Interface] و [Peer] باشد).");
                return;
            }

            _formWgConf = conf;
            if (ConnFormNameBox.Text.Trim().Length == 0)
                ConnFormNameBox.Text = Path.GetFileNameWithoutExtension(ofd.FileName);

            PopulateFormWgFields(conf);
            ConnFormWgStatusText.Text = Localization.T("کانفیگ بارگذاری شد: ") + Path.GetFileName(ofd.FileName) + " ✔";
        }

        private void PopulateFormWgFields(string conf)
        {
            var iface = ParseWgSection(conf, "[Interface]");
            var peer = ParseWgSection(conf, "[Peer]");

            ConnFormWgPrivateKeyBox.Text = iface.GetValueOrDefault("privatekey", "");
            ConnFormWgAddressBox.Text = iface.GetValueOrDefault("address", "");
            ConnFormWgDnsBox.Text = iface.GetValueOrDefault("dns", "");
            ConnFormWgMtuBox.Text = iface.GetValueOrDefault("mtu", "");

            ConnFormWgEndpointBox.Text = peer.GetValueOrDefault("endpoint", "");
            ConnFormWgPublicKeyBox.Text = peer.GetValueOrDefault("publickey", "");
            ConnFormWgPresharedKeyBox.Text = peer.GetValueOrDefault("presharedkey", "");
            ConnFormWgAllowedIPsBox.Text = peer.GetValueOrDefault("allowedips", "0.0.0.0/0");
            ConnFormWgKeepaliveBox.Text = peer.GetValueOrDefault("persistentkeepalive", "");

            var isAmnezia = iface.ContainsKey("jc") || iface.ContainsKey("h1") ||
                            iface.ContainsKey("jmin") || iface.ContainsKey("headerprotectionkey") ||
                            conf.Contains("# amw", StringComparison.OrdinalIgnoreCase);

            ConnFormAwgCheckBox.IsChecked = isAmnezia;
            ConnFormAwgPanel.Visibility = isAmnezia ? Visibility.Visible : Visibility.Collapsed;

            if (isAmnezia)
            {
                ConnFormAwgJcBox.Text = iface.GetValueOrDefault("jc", "—");
                ConnFormAwgJminBox.Text = iface.GetValueOrDefault("jmin", "—");
                ConnFormAwgJmaxBox.Text = iface.GetValueOrDefault("jmax", "—");
                ConnFormAwgS1Box.Text = iface.GetValueOrDefault("s1", "—");
                ConnFormAwgS2Box.Text = iface.GetValueOrDefault("s2", "—");
                ConnFormAwgS3Box.Text = iface.GetValueOrDefault("s3", "—");
                ConnFormAwgS4Box.Text = iface.GetValueOrDefault("s4", "—");
                ConnFormAwgH1Box.Text = iface.GetValueOrDefault("h1", "—");
                ConnFormAwgH2Box.Text = iface.GetValueOrDefault("h2", "—");
                ConnFormAwgH3Box.Text = iface.GetValueOrDefault("h3", "—");
                ConnFormAwgH4Box.Text = iface.GetValueOrDefault("h4", "—");

                var knownKeys = new HashSet<string>
                {
                    "privatekey","address","dns","mtu",
                    "jc","jmin","jmax","s1","s2","s3","s4",
                    "h1","h2","h3","h4"
                };
                var extras = new System.Text.StringBuilder();
                foreach (var kv in iface)
                    if (!knownKeys.Contains(kv.Key))
                        extras.AppendLine($"{kv.Key} = {kv.Value}");

                ConnFormAwgExtraBox.Text = extras.ToString().Trim();
            }
        }

        private static Dictionary<string, string> ParseWgSection(string conf, string header)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inSection = false;

            foreach (var rawLine in conf.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('#')) continue;

                if (line.Equals(header, StringComparison.OrdinalIgnoreCase))
                { inSection = true; continue; }

                if (line.StartsWith('[') && inSection) break;

                if (inSection && line.Contains('='))
                {
                    var eq = line.IndexOf('=');
                    var key = line[..eq].Trim().ToLowerInvariant();
                    var val = line[(eq + 1)..].Trim();
                    result[key] = val;
                }
            }
            return result;
        }

        private string BuildFormWgConf()
        {
            var priv = ConnFormWgPrivateKeyBox.Text.Trim();
            var addr = ConnFormWgAddressBox.Text.Trim();
            var pubKey = ConnFormWgPublicKeyBox.Text.Trim();
            var endpoint = ConnFormWgEndpointBox.Text.Trim();

            static bool Missing(string value) => string.IsNullOrWhiteSpace(value) || value == "—";

            if (Missing(priv) || Missing(addr) || Missing(pubKey) || Missing(endpoint))
                return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Interface]");
            sb.AppendLine($"PrivateKey = {priv}");
            sb.AppendLine($"Address = {addr}");
            var dns = ConnFormWgDnsBox.Text.Trim();
            if (dns.Length > 0 && dns != "—") sb.AppendLine($"DNS = {dns}");
            var mtu = ConnFormWgMtuBox.Text.Trim();
            if (mtu.Length > 0 && mtu != "—") sb.AppendLine($"MTU = {mtu}");
            if (ConnFormAwgCheckBox.IsChecked == true)
            {
                void A(string k, string v) { if (v.Length > 0 && v != "—") sb.AppendLine($"{k} = {v}"); }
                A("Jc", ConnFormAwgJcBox.Text.Trim());
                A("Jmin", ConnFormAwgJminBox.Text.Trim());
                A("Jmax", ConnFormAwgJmaxBox.Text.Trim());
                A("S1", ConnFormAwgS1Box.Text.Trim());
                A("S2", ConnFormAwgS2Box.Text.Trim());
                A("S3", ConnFormAwgS3Box.Text.Trim());
                A("S4", ConnFormAwgS4Box.Text.Trim());
                A("H1", ConnFormAwgH1Box.Text.Trim());
                A("H2", ConnFormAwgH2Box.Text.Trim());
                A("H3", ConnFormAwgH3Box.Text.Trim());
                A("H4", ConnFormAwgH4Box.Text.Trim());
                var extra = ConnFormAwgExtraBox.Text.Trim();
                if (extra.Length > 0) sb.AppendLine(extra);
            }
            sb.AppendLine();
            sb.AppendLine("[Peer]");
            sb.AppendLine($"PublicKey = {pubKey}");
            var presharedKey = ConnFormWgPresharedKeyBox.Text.Trim();
            if (presharedKey.Length > 0 && presharedKey != "—")
                sb.AppendLine($"PresharedKey = {presharedKey}");

            sb.AppendLine($"Endpoint = {endpoint}");
            var ips = ConnFormWgAllowedIPsBox.Text.Trim();
            sb.AppendLine($"AllowedIPs = {(ips.Length > 0 && ips != "—" ? ips : "0.0.0.0/0")}");
            var kpa = ConnFormWgKeepaliveBox.Text.Trim();
            if (kpa.Length > 0 && kpa != "—") sb.AppendLine($"PersistentKeepalive = {kpa}");
            return sb.ToString();
        }

        private void ConnFormSave_Click(object sender, RoutedEventArgs e)
        {
            var name = ConnFormNameBox.Text.Trim();
            if (name.Length == 0)
            {
                AskDialog.Info(this, "نام الزامی است.");
                return;
            }

            var selType = (ConnFormTypeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "openvpn";
            var isWg = selType == "wireguard" || selType == "amneziawg";

            ConnectionProfile result;

            if (isWg)
            {
                var built = BuildFormWgConf();
                if (string.IsNullOrWhiteSpace(_formWgConf))
                    _formWgConf = built;
                else if (ConnFormAwgCheckBox.IsChecked == true && !_formWgConf.Contains("Jc =") && !_formWgConf.Contains("jc =") && built.Length > 0)
                    _formWgConf = built;

                if (_formWgConf.Trim().Length == 0)
                {
                    AskDialog.Info(this, "لطفاً Private Key، Address، Public Key و Endpoint را پر کنید.");
                    return;
                }

                var ep = ConnFormWgEndpointBox.Text.Trim();
                var server = "";
                int? port = null;
                if (ep != "—" && ep.Length > 0)
                {
                    var lastColon = ep.LastIndexOf(':');
                    if (lastColon > 0)
                    {
                        server = ep[..lastColon];
                        if (int.TryParse(ep[(lastColon + 1)..], out var pv) && pv > 0) port = pv;
                    }
                    else server = ep;
                }

                var realType = ConnFormAwgPanel.Visibility == Visibility.Visible ? "amneziawg" : "wireguard";

                result = new ConnectionProfile
                {
                    Name = name,
                    Type = realType,
                    Server = server,
                    Port = port,
                    Proto = "udp",
                    Source = _editingConnProfile?.Source ?? "custom",
                    WireGuardConf = _formWgConf,
                    OvpnInline = "",
                    Username = "",
                    Password = "",
                    Psk = "",
                };
            }
            else
            {
                var server = ConnFormServerBox.Text.Trim();
                if (server.Length == 0)
                {
                    AskDialog.Info(this, "نام و آدرس سرور الزامی است.");
                    return;
                }

                int? port = null;
                if (int.TryParse(ConnFormPortBox.Text.Trim(), out var pv) && pv > 0) port = pv;

                result = new ConnectionProfile
                {
                    Name = name,
                    Type = selType,
                    Server = server,
                    Port = port,
                    Proto = (ConnFormProtoCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "udp",
                    Source = _editingConnProfile?.Source ?? "custom",
                    OvpnInline = selType == "openvpn" ? _formOvpnInline : "",
                    ServerOverride = selType == "openvpn" ? ConnFormOverrideBox.Text.Trim() : "",
                    WireGuardConf = "",
                    Username = ConnFormUserBox.Text.Trim(),
                    Password = ConnFormPassBox.Password,
                    Psk = ConnFormPskBox.Text,
                };
            }

            if (_editingConnProfile == null)
            {
                _config.Connections.Add(result);
            }
            else
            {
                var idx = _config.Connections.IndexOf(_editingConnProfile);
                if (idx >= 0) _config.Connections[idx] = result;
            }

            _config.Save();
            _selectedName = result.Name;
            RefreshList();
            CloseConnForm();
        }

        private void DeleteConn(string name)
        {
            var existing = _config.Connections.FirstOrDefault(c => c.Name == name);
            if (existing == null) return;
            if (!AskDialog.Confirm(this, MsgDeleteConfirm))
                return;
            _config.Connections.Remove(existing);
            _config.Save();
            RefreshList();
        }


        // ================= پکیج/پیکربندی (فرمت رسمی: JSON) =================
        private void UpdateBase_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "OpenVPN config (*.ovpn)|*.ovpn" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var text = SanitizeOvpn(File.ReadAllText(dlg.FileName));
                    Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
                    var dest = Path.Combine(AppContext.BaseDirectory, "Data", "base.ovpn");
                    File.WriteAllText(dest, text);
                    _config.BaseOvpn = text;
                    _config.Save();
                    AppendConnLog("base.ovpn به‌روز شد.");
                }
                catch (Exception ex) { AppendConnLog("خطا در به‌روزرسانی base.ovpn: " + ex.Message); }
            }
        }

        private static string SanitizeOvpn(string content)
        {
            var s = content.Replace("\r\n", "\n");
            // VpnEngine اینا رو خودش اضافه می‌کنه — از inline strip می‌شن
            s = Regex.Replace(s, @"^remote\s+.*\n?",       "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^proto\s+.*\n?",        "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^port\s+.*\n?",         "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^auth-user-pass\s*\n?", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^mute\s+\d+\s*\n?",   "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        // ================= درگ‌اند‌دراپ فایل روی پنجره =================
        private const string MsgWgDetected = "فایل وایرگارد (.conf) شناسایی شد — پشتیبانی WireGuard به‌زودی اضافه می‌شود.";
        private const string MsgDropUnsupportedFmt = "فرمت فایل «{0}» پشتیبانی نمی‌شود (فقط ovpn / json / pkg).";

        // برنامه با دسترسی ادمین اجرا می‌شود و ویندوز (UIPI) مسیر عادی درگ‌اند‌دراپ WPF را بلاک می‌کند؛
        // فیلتر پیام‌ها را باز کرده و فایل‌ها را از مسیر قدیمی WM_DROPFILES می‌گیریم
        private const uint WmDropFiles = 0x0233;
        private const uint WmCopyData = 0x004A;
        private const uint WmCopyGlobalData = 0x0049;
        private const uint MsgFltAllow = 1;

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);
        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);
        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr hDrop);

        private void EnableElevatedDragDrop()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                ChangeWindowMessageFilterEx(hwnd, WmDropFiles, MsgFltAllow, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hwnd, WmCopyGlobalData, MsgFltAllow, IntPtr.Zero);
                ChangeWindowMessageFilterEx(hwnd, WmCopyData, MsgFltAllow, IntPtr.Zero);
                DragAcceptFiles(hwnd, true);
                HwndSource.FromHwnd(hwnd)?.AddHook(DropFilesHook);
            }
            catch { }
        }

        private IntPtr DropFilesHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != (int)WmDropFiles) return IntPtr.Zero;
            try
            {
                var count = DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
                var files = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    var len = DragQueryFile(wParam, i, null, 0);
                    var sb = new StringBuilder((int)len + 1);
                    if (DragQueryFile(wParam, i, sb, (uint)sb.Capacity) > 0) files.Add(sb.ToString());
                }
                if (files.Count > 0) HandleDroppedFiles(files.ToArray());
            }
            catch { }
            finally { DragFinish(wParam); }
            handled = true;
            return IntPtr.Zero;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            HandleDroppedFiles(files);
        }

        private void HandleDroppedFiles(string[] files)
        {
            var imported = 0;
            foreach (var f in files)
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                switch (ext)
                {
                    case ".ovpn":   // کانفیگ OpenVPN ← کانکشن سفارشی جدید
                    case ".json":   // پکیج رسمی
                    case ".pkg":
                        if (ImportPackage(f)) imported++;
                        break;
                    case ".conf":   // وایرگارد — فعلاً فقط تشخیص؛ افزودن کانکشن در فاز WireGuard
                        AppendConnLog(MsgWgDetected);
                        AskDialog.Info(this, MsgWgDetected);
                        break;
                    default:
                        AppendConnLog(string.Format(MsgDropUnsupportedFmt, Path.GetFileName(f)));
                        break;
                }
            }
            if (imported > 0)
            {
                RefreshList();
                AskDialog.Info(this, MsgImported);
            }
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "NETFASTVIP package / OpenVPN config|*.json;*.pkg;*.ovpn|JSON package (*.json)|*.json|OpenVPN config (*.ovpn)|*.ovpn"
            };
            if (dlg.ShowDialog() == true)
            {
                var ok = ImportPackage(dlg.FileName);
                RefreshList();
                if (ok) AskDialog.Info(this, MsgImported);
            }
        }

        private bool ImportPackage(string path)
        {
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".ovpn")
                {
                    var rawOvpn = File.ReadAllText(path);
                    var content = SanitizeOvpn(rawOvpn);
                    var name    = Path.GetFileNameWithoutExtension(path);

                    var remMatch = Regex.Match(rawOvpn, @"^remote\s+(\S+)\s+(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    var server   = remMatch.Success ? remMatch.Groups[1].Value : "";
                    var port     = remMatch.Success ? int.Parse(remMatch.Groups[2].Value) : 1194;
                    var proto    = Regex.IsMatch(rawOvpn, @"^proto\s+tcp", RegexOptions.Multiline | RegexOptions.IgnoreCase) ? "tcp" : "udp";

                    _config.Connections.Add(new ConnectionProfile
                    {
                        Name       = name,
                        Type       = "openvpn",
                        Source     = "custom",
                        Server     = server,
                        Port       = port,
                        Proto      = proto,
                        OvpnInline = content,
                    });
                    _config.Save();
                    AppendConnLog("ایمپورت فایل ovpn: " + name);
                    return true;
                }

                // json (رسمی) یا pkg (قدیمی) — هر دو محتوای JSON دا��ند
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var pkg = JsonSerializer.Deserialize<PackageFile>(json, opts);
                if (pkg != null)
                {
                    _config.ApplyOfficialPackage(pkg);
                    _config.Save();
                    AppendConnLog("پکیج رسمی ایمپورت شد (نسخه " + pkg.PackageVersion + ").");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                AppendConnLog("خطا در ایمپورت: " + ex.Message);
                return false;
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            // انتخاب کاربر: بک‌اپ همراه یوزر/پسو��د یا بدون آن
            var choice = AskDialog.Choose(this, MsgExportChoice, BtnExportWithCreds, BtnExportNoCreds);
            if (choice == -1) return;
            var withCreds = choice == 0;

            var dlg = new SaveFileDialog { Filter = "NETFASTVIP package (JSON)|*.json", FileName = "netfastvip-package.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var pkg = new PackageFile
                    {
                        PackageVersion = _config.PackageVersion + 1,
                        Name = "NETFASTVIP",
                        BaseOvpn = _config.BaseOvpn,
                        // همه کانکشن‌ها (ساخته‌دست خودت هم) وارد پکیج می‌شوند —
                        // یوزر/پس فقط با انتخاب کاربر، PSK و ovpn داخلی حفظ
                        Connections = _config.Connections
                            .Select(c => new ConnectionProfile
                            {
                                Name = c.Name,
                                Type = c.Type,
                                Server = c.Server,
                                Port = c.Port,
                                Proto = c.Proto,
                                Psk = c.Psk,
                                Username = withCreds ? c.Username : "",
                                Password = withCreds ? c.Password : "",
                                OvpnInline = c.OvpnInline,
                                Source = "official",
                            })
                            .ToList(),
                    };
                    var opts = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(pkg, opts));
                    AppendConnLog((withCreds ? "بک‌اپ همراه یوزر/پسورد ساخته شد: " : "پکیج بدون یوزر/پسورد ساخته شد: ") + dlg.FileName);
                    AskDialog.Info(this, MsgExported);
                }
                catch (Exception ex) { AppendConnLog("خطا در اکسپورت: " + ex.Message); }
            }
        }

        // ================= تست پینگ سرورهای VPN Core =================
        private async void VpnPingAll_Click(object sender, RoutedEventArgs e)
        {
            await PingAllVpnProfilesAsync();
        }

        public async Task PingAllVpnProfilesAsync()
        {
            var profiles = _config.Connections.ToList();
            if (profiles.Count == 0) return;

            if (VpnPingAllBtn != null)
            {
                VpnPingAllBtn.IsEnabled = false;
            }

            try
            {
                foreach (var p in profiles)
                {
                    _vpnPingCache[p.Name] = (-1, false, "Pinging...");
                }
                RefreshList();

                var tasks = profiles.Select(async p =>
                {
                    var res = await MeasureVpnProfilePingAsync(p);
                    _vpnPingCache[p.Name] = res;
                });

                await Task.WhenAll(tasks);
                RefreshList();
            }
            catch { }
            finally
            {
                if (VpnPingAllBtn != null)
                {
                    VpnPingAllBtn.IsEnabled = true;
                }
            }
        }

        public async Task PingSingleVpnProfileAsync(ConnectionProfile c)
        {
            _vpnPingCache[c.Name] = (-1, false, "Pinging...");
            RefreshList();
            var res = await MeasureVpnProfilePingAsync(c);
            _vpnPingCache[c.Name] = res;
            RefreshList();
        }

        private static async Task<(int ping, bool isUp, string text)> MeasureVpnProfilePingAsync(ConnectionProfile c)
        {
            string host = "";
            int port = 0;

            if (c.Type.Equals("wireguard", StringComparison.OrdinalIgnoreCase) ||
                c.Type.Equals("amneziawg", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(c.WireGuardConf))
                {
                    var (wgHost, wgPort) = WireGuardProvider.ParseEndpoint(c.WireGuardConf);
                    if (!string.IsNullOrWhiteSpace(wgHost)) host = wgHost;
                    if (wgPort.HasValue) port = wgPort.Value;
                }
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                host = !string.IsNullOrWhiteSpace(c.ServerOverride) ? c.ServerOverride : c.Server;
            }

            if (port == 0 && c.Port.HasValue)
            {
                port = c.Port.Value;
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                return (-1, false, "—");
            }

            host = host.Trim();
            if (host.Contains("://"))
            {
                try { host = new Uri(host).Host; } catch { }
            }
            if (host.Contains(':'))
            {
                var parts = host.Split(':');
                host = parts[0];
                if (port == 0 && parts.Length > 1 && int.TryParse(parts[1], out var parsedPort))
                {
                    port = parsedPort;
                }
            }

            // 1. Try TCP connect first if port is known
            if (port > 0)
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(host, port);
                    var delayTask = Task.Delay(2000);
                    var completed = await Task.WhenAny(connectTask, delayTask);
                    sw.Stop();

                    if (completed == connectTask && !connectTask.IsFaulted)
                    {
                        int rtt = Math.Max(1, (int)sw.ElapsedMilliseconds);
                        return (rtt, true, $"{rtt} ms");
                    }
                }
                catch { }
            }

            // 2. Try ICMP Ping
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync(host, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    int rtt = Math.Max(1, (int)reply.RoundtripTime);
                    return (rtt, true, $"{rtt} ms");
                }
            }
            catch { }

            // 3. Fallback to Uptime Kuma if available
            var kuma = UptimeKumaClient.GetStatusForProfile(c.Name);
            if (kuma != null && kuma.IsUp)
            {
                return (kuma.Ping, true, $"{kuma.Ping} ms");
            }

            return (-1, false, "Timeout");
        }

    }
}



