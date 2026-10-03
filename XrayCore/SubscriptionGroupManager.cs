using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SmartVpn.XrayCore
{
    public static class SubscriptionGroupManager
    {
        private static readonly string GroupsFilePath = "subscriptions.json";
        private static readonly string SubUrlFilePath = "sub_url.txt";

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static void SaveGroups(IEnumerable<SubscriptionGroup> groups)
        {
            try
            {
                var list = groups.ToList();
                string json = JsonSerializer.Serialize(list, JsonOpts);
                File.WriteAllText(GroupsFilePath, json);

                // Flat profiles fallback for legacy consumers
                var allProfiles = list.SelectMany(g => g.Profiles).ToList();
                ProfileManager.SaveProfiles(allProfiles);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save subscription groups: {ex.Message}");
            }
        }

        public static List<SubscriptionGroup> LoadGroups()
        {
            try
            {
                if (File.Exists(GroupsFilePath))
                {
                    string json = File.ReadAllText(GroupsFilePath);
                    var groups = JsonSerializer.Deserialize<List<SubscriptionGroup>>(json, JsonOpts);
                    if (groups != null && groups.Count > 0)
                    {
                        return groups;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load subscription groups: {ex.Message}");
            }

            // Migration from legacy profiles.json + sub_url.txt
            var legacyProfiles = ProfileManager.LoadProfiles();
            string legacyUrl = "";
            try
            {
                if (File.Exists(SubUrlFilePath))
                {
                    legacyUrl = File.ReadAllText(SubUrlFilePath).Trim();
                }
            }
            catch { }

            var defaultGroup = new SubscriptionGroup
            {
                Name = "اشتراک ۱",
                Url = legacyUrl,
                IsExpanded = true
            };

            foreach (var p in legacyProfiles)
            {
                p.GroupId = defaultGroup.Id;
                p.GroupName = defaultGroup.Name;
                defaultGroup.Profiles.Add(p);
            }

            var migrated = new List<SubscriptionGroup> { defaultGroup };
            if (legacyProfiles.Count > 0 || !string.IsNullOrWhiteSpace(legacyUrl))
            {
                SaveGroups(migrated);
            }
            return migrated;
        }
    }
}
