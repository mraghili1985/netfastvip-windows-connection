using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartVpn.XrayCore
{
    public class SubscriptionGroup : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        private string _name = "اشتراک";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _url = "";
        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        private DateTime? _lastUpdated;
        public DateTime? LastUpdated
        {
            get => _lastUpdated;
            set { _lastUpdated = value; OnPropertyChanged(); }
        }

        private string _dataRemaining = "Unlimited";
        public string DataRemaining
        {
            get => _dataRemaining;
            set { _dataRemaining = value; OnPropertyChanged(); }
        }

        private string _totalData = "";
        public string TotalData
        {
            get => _totalData;
            set { _totalData = value; OnPropertyChanged(); }
        }

        private string _daysRemaining = "Unlimited";
        public string DaysRemaining
        {
            get => _daysRemaining;
            set { _daysRemaining = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ProxyProfile> Profiles { get; set; } = new ObservableCollection<ProxyProfile>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
