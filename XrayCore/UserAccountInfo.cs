using System;
using System.Globalization;

namespace SmartVpn.XrayCore
{
    public class UserAccountInfo
    {
        public long Upload { get; set; }
        public long Download { get; set; }
        public long Total { get; set; }
        public long ExpireDateTimestamp { get; set; }
        
        public string GetRemainingDataString()
        {
            if (Total == 0) return "Unlimited";
            long remaining = Total - (Upload + Download);
            if (remaining < 0) remaining = 0;
            return FormatBytes(remaining) + " / " + FormatBytes(Total);
        }
        
        public string GetTotalDataString() => Total == 0 ? "Unlimited" : FormatBytes(Total);
        
        public string GetExpireString()
        {
            if (ExpireDateTimestamp == 0) return "Unlimited";
            var expireDate = DateTimeOffset.FromUnixTimeSeconds(ExpireDateTimestamp).ToLocalTime();
            var days = (expireDate - DateTimeOffset.Now).Days;
            if (days < 0) return "Expired";
            
            // Format normally without culture messing it up
            return $"{days} Days";
        }

        private string FormatBytes(long bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < suffix.Length - 1)
            {
                dblSByte /= 1024;
                i++;
            }
            return dblSByte.ToString("0.##", CultureInfo.InvariantCulture) + " " + suffix[i];
        }
    }
}

