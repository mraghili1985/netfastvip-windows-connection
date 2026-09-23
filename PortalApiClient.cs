using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVpn;

// ---------- مدل‌های پاسخ پورتال ----------

public sealed class PortalUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("serviceState")] public string ServiceState { get; set; } = "";
    [JsonPropertyName("canConnect")] public bool CanConnect { get; set; }
}

public sealed class PortalLoginResult
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("user")] public PortalUser? User { get; set; }
}

public sealed class PortalAccount
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("firstLoginAt")] public DateTime? FirstLoginAt { get; set; }
    [JsonPropertyName("expireAt")] public DateTime? ExpireAt { get; set; }
    [JsonPropertyName("remainingDays")] public int RemainingDays { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}

public sealed class PortalPackage
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("trafficMb")] public long TrafficMb { get; set; }
    [JsonPropertyName("durationDays")] public int DurationDays { get; set; }
}

public sealed class PortalTraffic
{
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("usedBytes")] public long UsedBytes { get; set; }
    [JsonPropertyName("remainingBytes")] public long RemainingBytes { get; set; }
    [JsonPropertyName("totalMb")] public long TotalMb { get; set; }
    [JsonPropertyName("usedMb")] public long UsedMb { get; set; }
    [JsonPropertyName("remainingMb")] public long RemainingMb { get; set; }
    [JsonPropertyName("usagePercent")] public int UsagePercent { get; set; }
}

public sealed class PortalDashboard
{
    [JsonPropertyName("canRenewSelf")] public bool CanRenewSelf { get; set; }
    [JsonPropertyName("account")] public PortalAccount Account { get; set; } = new();
    [JsonPropertyName("package")] public PortalPackage Package { get; set; } = new();
    [JsonPropertyName("traffic")] public PortalTraffic Traffic { get; set; } = new();
}

// ---------- مدل پروفایل اتصال پورتال ----------

public sealed class PortalConnectionProfile
{
    [JsonPropertyName("label")]    public string Label    { get; set; } = "";
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = "";
    [JsonPropertyName("kind")]     public string Kind     { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("fileKind")] public string FileKind { get; set; } = "";
    [JsonPropertyName("format")]   public string Format   { get; set; } = "";
    [JsonPropertyName("url")]      public string Url      { get; set; } = "";

    /// <summary>آیا این پروفایل روی ویندوز قابل استفاده است</summary>
    [JsonIgnore]
    public bool IsWindowsCompatible =>
        Format == "ovpn" || Format == "pbk";
}

public sealed class PortalConnectionsResult
{
    [JsonPropertyName("success")]  public bool Success  { get; set; }
    [JsonPropertyName("count")]    public int  Count    { get; set; }
    [JsonPropertyName("profiles")] public List<PortalConnectionProfile> Profiles { get; set; } = new();
}

// ---------- خطای API ----------

public sealed class PortalApiException : Exception
{
    public int StatusCode { get; }
    public PortalApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

// ---------- کلاینت ----------

public sealed class PortalApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private string _username = "";
    private string _password = "";
    private string? _token;

    public PortalApiClient(string baseUrl = "http://panel.netfast.vip", HttpClient? http = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    // ===== Auth =====

    public async Task<PortalUser> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        _username = username;
        _password = password;
        return await LoginInternalAsync(ct);
    }

    private async Task<PortalUser> LoginInternalAsync(CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { username = _username, password = _password });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{_baseUrl}/api/auth/user-login", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new PortalApiException((int)resp.StatusCode, body);

        var result = JsonSerializer.Deserialize<PortalLoginResult>(body, JsonOpts)
            ?? throw new PortalApiException(0, "پاسخ لاگین پورتال نامعتبر بود");
        if (string.IsNullOrEmpty(result.Token))
            throw new PortalApiException(0, "پاسخ لاگین پورتال توکن نداشت");

        _token = result.Token;
        return result.User ?? new PortalUser { Username = _username };
    }

    // ===== Dashboard =====

    public async Task<PortalDashboard> GetDashboardAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_token)) await LoginInternalAsync(ct);
        var dashboard = await TryGetDashboardOnceAsync(ct);
        if (dashboard != null) return dashboard;
        await LoginInternalAsync(ct);
        dashboard = await TryGetDashboardOnceAsync(ct);
        if (dashboard != null) return dashboard;
        throw new PortalApiException(401, "دریافت اطلاعات داشبورد پورتال ناموفق بود");
    }

    private async Task<PortalDashboard?> TryGetDashboardOnceAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/portal/dashboard");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PortalDashboard>(body, JsonOpts);
    }

    // ===== Connection Profiles =====

    /// <summary>
    /// لیست پروفایل‌های اتصال اختصاصی کاربر را از پورتال دریافت می‌کند.
    /// برای ویندوز: format=="ovpn" (OpenVPN) یا format=="pbk" (L2TP).
    /// format=="mobileconfig" را فیلتر کنید (فقط iOS/Mac).
    /// </summary>
    public async Task<PortalConnectionsResult> GetConnectionProfilesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_token)) await LoginInternalAsync(ct);

        var result = await TryGetConnectionProfilesOnceAsync(ct);
        if (result != null) return result;

        // توکن منقضی — دوباره لاگین و تلاش مجدد
        await LoginInternalAsync(ct);
        result = await TryGetConnectionProfilesOnceAsync(ct);
        return result ?? throw new PortalApiException(401, "دریافت پروفایل‌های اتصال ناموفق بود");
    }

    private async Task<PortalConnectionsResult?> TryGetConnectionProfilesOnceAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/connection-info/public");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PortalConnectionsResult>(body, JsonOpts);
    }

    /// <summary>
    /// محتوی فایل اتصال (.ovpn یا .pbk) را با احراز دانلود می‌کند.
    /// </summary>
    public async Task<string> DownloadProfileFileAsync(string fileUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_token)) await LoginInternalAsync(ct);

        using var req = new HttpRequestMessage(HttpMethod.Get, fileUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new PortalApiException((int)resp.StatusCode, $"دانلود فایل ناموفق: {fileUrl}");
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
