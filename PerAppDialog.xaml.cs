using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SmartVpn
{
    public class PerAppEntry : INotifyPropertyChanged
    {
        private string _name = "";
        private bool _isActive = true;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBg));
                    OnPropertyChanged(nameof(StatusBorder));
                    OnPropertyChanged(nameof(StatusFg));
                }
            }
        }

        public string StatusText => Localization.T(IsActive ? "فعال" : "غیرفعال");
        public Brush StatusBg => IsActive
            ? new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        public Brush StatusBorder => IsActive
            ? new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69))
            : new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69));
        public Brush StatusFg => IsActive
            ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))
            : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public partial class PerAppDialog : Window
    {
        private readonly AppConfig _config;
        private readonly ObservableCollection<PerAppEntry> _items = new();

        public PerAppDialog(AppConfig config)
        {
            InitializeComponent();
            _config = config;

            FlowDirection = Localization.IsEnglish ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            Title = Localization.T(Title);
            PerAppTitleText.Text = Localization.T("مدیریت برنامه‌های تونل (Per-App)");
            PerAppHintText.Text = Localization.T("برنامه‌های زیر بر اساس حالت انتخابی شما تفکیک می‌شوند:");
            PerAppAddBtn.Content = Localization.T("➕ افزودن برنامه جدید (.exe)");
            Loaded += (_, _) => Localization.Watch(this);

            foreach (var s in _config.SplitTunnelList)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                var trimmed = s.Trim();
                bool active = !trimmed.StartsWith("#") && !trimmed.StartsWith("!");
                string name = trimmed.TrimStart('#', '!').Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(name))
                {
                    _items.Add(new PerAppEntry { Name = name, IsActive = active });
                }
            }

            AppList.ItemsSource = _items;
        }

        private void BtnToggleActive_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PerAppEntry entry)
            {
                entry.IsActive = !entry.IsActive;
                SaveConfig();
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PerAppEntry entry)
            {
                _items.Remove(entry);
                SaveConfig();
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe",
                Title = Localization.T("انتخاب برنامه"),
                Multiselect = true
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (var fileName in ofd.FileNames)
                {
                    var exeName = Path.GetFileName(fileName).ToLowerInvariant();
                    if (!_items.Any(i => i.Name.Equals(exeName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _items.Add(new PerAppEntry { Name = exeName, IsActive = true });
                    }
                }
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            _config.SplitTunnelList = _items
                .Select(i => i.IsActive ? i.Name : $"#{i.Name}")
                .ToList();
            _config.Save();
        }
    }
}
