using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartVpn
{
    public class KumaStatus
    {
        public bool IsUp { get; set; }
        public int Ping { get; set; }
    }

    public static class UptimeKumaClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private const string BaseUrl = "http://91.223.116.173:3000";
        private const string Slug = "netfastvip";

        // Stores the latest fetched status (MonitorName -> Status)
        public static Dictionary<string, KumaStatus> LastStatus { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public static async Task UpdateStatusAsync()
        {
            var result = new Dictionary<string, KumaStatus>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var pageJson = await _http.GetStringAsync($"{BaseUrl}/api/status-page/{Slug}");
                using var pageDoc = JsonDocument.Parse(pageJson);
                
                var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var groupList = pageDoc.RootElement.GetProperty("publicGroupList");
                foreach (var group in groupList.EnumerateArray())
                {
                    var monitorList = group.GetProperty("monitorList");
                    foreach (var mon in monitorList.EnumerateArray())
                    {
                        var id = mon.GetProperty("id").GetInt32().ToString();
                        var name = mon.GetProperty("name").GetString();
                        if (!string.IsNullOrEmpty(name)) nameToId[name] = id;
                    }
                }

                var hbJson = await _http.GetStringAsync($"{BaseUrl}/api/status-page/heartbeat/{Slug}");
                using var hbDoc = JsonDocument.Parse(hbJson);
                var hbList = hbDoc.RootElement.GetProperty("heartbeatList");

                foreach (var kvp in nameToId)
                {
                    var name = kvp.Key;
                    var id = kvp.Value;

                    if (hbList.TryGetProperty(id, out var beats) && beats.ValueKind == JsonValueKind.Array && beats.GetArrayLength() > 0)
                    {
                        var lastBeat = beats.EnumerateArray().Last();
                        var status = lastBeat.GetProperty("status").GetInt32();
                        var ping = lastBeat.GetProperty("ping").GetDouble();
                        result[name] = new KumaStatus { IsUp = status == 1, Ping = (int)ping };
                    }
                }
                LastStatus = result;
            }
            catch { }
        }

        public static KumaStatus? GetStatusForProfile(string profileName)
        {
            if (LastStatus == null || LastStatus.Count == 0) return null;
            foreach (var kvp in LastStatus)
            {
                if (profileName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }
            return null;
        }
    }
}
