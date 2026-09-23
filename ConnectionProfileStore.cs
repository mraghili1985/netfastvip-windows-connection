using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

public static class ConnectionProfileStore
{
    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "Data", "connection-profile.json");

    public static List<ConnectionProfile> Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return [];

        try
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            };
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(File.ReadAllText(path), opts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<ConnectionProfile> profiles, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(profiles, opts));
    }
}
