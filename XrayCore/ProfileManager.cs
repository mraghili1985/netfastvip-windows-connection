using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SmartVpn.XrayCore
{
    public class ProxyProfile : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Protocol { get; set; } = "VLESS";
        
        private string _alias = "";
        public string Alias { get { return _alias; } set { _alias = value; OnPropertyChanged(); } }
        
        private string _address = "";
        public string Address { get { return _address; } set { _address = value; OnPropertyChanged(); } }
        
        private int _port = 443;
        public int Port { get { return _port; } set { _port = value; OnPropertyChanged(); } }
        
        public string UserId { get; set; } = "";
        public string Security { get; set; } = "none";
        public string Network { get; set; } = "tcp";
        public string Flow { get; set; } = "";
        public string Tls { get; set; } = "none";
        public string Sni { get; set; } = "";
        public string Alpn { get; set; } = "";
        public string Fingerprint { get; set; } = "chrome";
        public string PublicKey { get; set; } = "";
        public string ShortId { get; set; } = "";
        public string SpiderX { get; set; } = "";
        public string Path { get; set; } = "/";
        public string Host { get; set; } = "";
        public string ServiceName { get; set; } = "";

        private string _status = "";
        public string Status { get { return _status; } set { _status = value; OnPropertyChanged(); } }
        
        private string _delay = "-";
        public string Delay { get { return _delay; } set { _delay = value; OnPropertyChanged(); } }
        
        public string FullUrl { get; set; } = "";
        public string Obfs { get; set; } = "";
        public string ObfsPassword { get; set; } = "";
        public bool Insecure { get; set; } = false;
        public string Password { get; set; } = "";
        public string CongestionControl { get; set; } = "bbr";
        public string UdpRelayMode { get; set; } = "native";
        public string GroupId { get; set; } = "";
        public string GroupName { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public static class ProfileManager
    {
        private static readonly string FilePath = "profiles.json";

        public static List<ProxyProfile> LoadProfiles()
        {
            if (!File.Exists(FilePath)) return new List<ProxyProfile>();
            try { return JsonSerializer.Deserialize<List<ProxyProfile>>(File.ReadAllText(FilePath)) ?? new List<ProxyProfile>(); }
            catch { return new List<ProxyProfile>(); }
        }

        public static void SaveProfiles(IEnumerable<ProxyProfile> profiles)
        {
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }
    }
}

