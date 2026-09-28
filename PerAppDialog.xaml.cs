using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SmartVpn
{
    public partial class PerAppDialog : Window
    {
        private readonly AppConfig _config;
        private ObservableCollection<string> _apps;

        public PerAppDialog(AppConfig config)
        {
            InitializeComponent();
            _config = config;
            _apps = new ObservableCollection<string>(_config.SplitTunnelList);
            AppList.ItemsSource = _apps;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe",
                Title = "انتخاب برنامه",
                Multiselect = true
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (var fileName in ofd.FileNames)
                {
                    var exeName = Path.GetFileName(fileName).ToLowerInvariant();
                    if (!_apps.Contains(exeName))
                    {
                        _apps.Add(exeName);
                    }
                }
                SaveConfig();
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string exeName)
            {
                _apps.Remove(exeName);
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            _config.SplitTunnelList = _apps.ToList();
            _config.Save();
        }
    }
}
