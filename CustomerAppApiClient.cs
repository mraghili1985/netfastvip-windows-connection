using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVpn;

// ==========================================
// مدل‌های داده API اپ مشتری (25-Customer-App-API)
// ==========================================

public sealed class CustomerDevice
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("platform")] public string Platform { get; set; } = "windows";
    [JsonPropertyName("appVersion")] public string? AppVersion { get; set; }
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("lastSeenAt")] public DateTime? LastSeenAt { get; set; }
    [JsonPropertyName("current")] public bool Current { get; set; }
}

public sealed class CustomerAccount
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = ""; // "customer" | "radius"
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("fullName")] public string? FullName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("type")] public int? Type { get; set; }
    [JsonPropertyName("serviceState")] public string? ServiceState { get; set; }
    [JsonPropertyName("canConnect")] public bool CanConnect { get; set; }
}

public sealed class CustomerLoginResponse
{
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("accessExpiresIn")] public int AccessExpiresIn { get; set; }
    [JsonPropertyName("refreshToken")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("refreshExpiresAt")] public DateTime? RefreshExpiresAt { get; set; }
    [JsonPropertyName("device")] public CustomerDevice? Device { get; set; }
    [JsonPropertyName("account")] public CustomerAccount? Account { get; set; }
}

public sealed class CustomerErrorResponse
{
    [JsonPropertyName("error")] public string Error { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("details")] public JsonElement? Details { get; set; }
}

public sealed class CustomerServiceLocation
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public sealed class CustomerService
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("base")] public string Base { get; set; } = "";
    [JsonPropertyName("protocols")] public List<string> Protocols { get; set; } = new();
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("displayNo")] public object? DisplayNo { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("canConnect")] public bool CanConnect { get; set; }
    [JsonPropertyName("online")] public bool Online { get; set; }
    [JsonPropertyName("packageName")] public string? PackageName { get; set; }
    [JsonPropertyName("expireAt")] public DateTime? ExpireAt { get; set; }
    [JsonPropertyName("firstConnectedAt")] public DateTime? FirstConnectedAt { get; set; }
    [JsonPropertyName("totalTrafficBytes")] public long? TotalTrafficBytes { get; set; }
    [JsonPropertyName("usedTrafficBytes")] public long? UsedTrafficBytes { get; set; }
    [JsonPropertyName("serverLabel")] public string? ServerLabel { get; set; }
    [JsonPropertyName("location")] public CustomerServiceLocation? Location { get; set; }
}

public sealed class CustomerServicesResponse
{
    [JsonPropertyName("services")] public List<CustomerService> Services { get; set; } = new();
}

public sealed class RadiusProfileItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public sealed class RadiusConnectionData
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
    [JsonPropertyName("profiles")] public List<RadiusProfileItem> Profiles { get; set; } = new();
}

public sealed class WireGuardLocationItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
    [JsonPropertyName("available")] public bool Available { get; set; }
}

public sealed class WireGuardConnectionData
{
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("subscriptionUrl")] public string? SubscriptionUrl { get; set; }
    [JsonPropertyName("locationId")] public string? LocationId { get; set; }
    [JsonPropertyName("locations")] public List<WireGuardLocationItem> Locations { get; set; } = new();
    [JsonPropertyName("locationWaitSeconds")] public int LocationWaitSeconds { get; set; }
    [JsonPropertyName("configPerLocation")] public bool ConfigPerLocation { get; set; }
}

public sealed class V2RayExtraItem
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("server")] public string? Server { get; set; }
    [JsonPropertyName("link")] public string? Link { get; set; }
    [JsonPropertyName("config")] public string? Config { get; set; }
}

public sealed class V2RayConnectionData
{
    [JsonPropertyName("subscriptionUrl")] public string? SubscriptionUrl { get; set; }
    [JsonPropertyName("links")] public List<string> Links { get; set; } = new();
    [JsonPropertyName("extras")] public List<V2RayExtraItem> Extras { get; set; } = new();
    [JsonPropertyName("endedReason")] public string? EndedReason { get; set; }
}

public sealed class ServiceConnectionResponse
{
    [JsonPropertyName("service")] public CustomerService? Service { get; set; }
    [JsonPropertyName("radius")] public RadiusConnectionData? Radius { get; set; }
    [JsonPropertyName("wireguard")] public WireGuardConnectionData? WireGuard { get; set; }
    [JsonPropertyName("v2ray")] public V2RayConnectionData? V2Ray { get; set; }
}

public sealed class CustomerMeResponse
{
    [JsonPropertyName("account")] public CustomerAccount? Account { get; set; }
    [JsonPropertyName("deviceLimit")] public int DeviceLimit { get; set; }
    [JsonPropertyName("devices")] public List<CustomerDevice> Devices { get; set; } = new();
}

public sealed class StartOtpResponse
{
    [JsonPropertyName("verificationId")] public string VerificationId { get; set; } = "";
    [JsonPropertyName("channel")] public string Channel { get; set; } = "";
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    [JsonPropertyName("expiresAt")] public DateTime? ExpiresAt { get; set; }
    [JsonPropertyName("resendAfterSeconds")] public int ResendAfterSeconds { get; set; }
    [JsonPropertyName("botUrl")] public string? BotUrl { get; set; }
}

public sealed class CustomerAppException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public CustomerAppException(int statusCode, string errorCode, string message) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public sealed class CustomerDeviceLimitException : Exception
{
    public int Limit { get; }
    public List<CustomerDevice> Devices { get; }
    public CustomerDeviceLimitException(string message, int limit, List<CustomerDevice> devices) : base(message)
    {
        Limit = limit;
        Devices = devices;
    }
}

// ==========================================
// کلاینت ارتباطی API اپ مشتری
// ==========================================

public sealed class CustomerAppApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    public string BaseUrl { get; private set; }

    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public CustomerAccount? CurrentAccount { get; set; }
    public CustomerDevice? CurrentDevice { get; set; }

    public CustomerAppApiClient(string? baseUrl = null, HttpClient? http = null)
    {
        BaseUrl = (baseUrl ?? AppConfig.Load().PortalApiUrl).TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    }

    private static object CreateDevicePayload()
    {
        return new
        {
            name = Environment.MachineName,
            platform = "windows",
            appVersion = AppConfig.BrandVersion
        };
    }

    // ===== ورود اکانت RADIUS =====
    public async Task<CustomerLoginResponse> LoginRadiusAsync(string username, string password, string? replaceDeviceId = null, CancellationToken ct = default)
    {
        var bodyObj = new Dictionary<string, object?>
        {
            ["mode"] = "radius",
            ["username"] = username,
            ["password"] = password,
            ["device"] = CreateDevicePayload(),
            ["appVersion"] = AppConfig.BrandVersion
        };
        if (!string.IsNullOrWhiteSpace(replaceDeviceId))
            bodyObj["replaceDeviceId"] = replaceDeviceId;

        return await SendLoginRequestAsync($"{BaseUrl}/api/app/login", bodyObj, ct);
    }

    // ===== ورود اکانت مشتری (شماره موبایل یا ایمیل) =====
    public async Task<CustomerLoginResponse> LoginCustomerAsync(string identifier, string password, string? replaceDeviceId = null, CancellationToken ct = default)
    {
        var bodyObj = new Dictionary<string, object?>
        {
            ["mode"] = "customer",
            ["identifier"] = identifier,
            ["password"] = password,
            ["device"] = CreateDevicePayload(),
            ["appVersion"] = AppConfig.BrandVersion
        };
        if (!string.IsNullOrWhiteSpace(replaceDeviceId))
            bodyObj["replaceDeviceId"] = replaceDeviceId;

        return await SendLoginRequestAsync($"{BaseUrl}/api/app/login", bodyObj, ct);
    }

    // ===== درخواست کد ورود یکبارمصرف (OTP) =====
    public async Task<StartOtpResponse> StartOtpAsync(string? phone, string? email, string? channel = null, CancellationToken ct = default)
    {
        var bodyObj = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(phone)) bodyObj["phone"] = phone;
        if (!string.IsNullOrWhiteSpace(email)) bodyObj["email"] = email;
        if (!string.IsNullOrWhiteSpace(channel)) bodyObj["channel"] = channel;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/customer/login/code/start")
        {
            Content = new StringContent(JsonSerializer.Serialize(bodyObj, JsonOpts), Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, ct);
        var respStr = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            HandleErrorResponse((int)resp.StatusCode, respStr);

        return JsonSerializer.Deserialize<StartOtpResponse>(respStr, JsonOpts)
            ?? throw new CustomerAppException((int)resp.StatusCode, "PARSE_ERROR", "خطا در پردازش پاسخ کد یکبارمصرف");
    }

    // ===== تأیید کد ورود یکبارمصرف (OTP Confirm) =====
    public async Task<CustomerLoginResponse> ConfirmOtpAsync(string verificationId, string code, string? replaceDeviceId = null, CancellationToken ct = default)
    {
        var bodyObj = new Dictionary<string, object?>
        {
            ["verificationId"] = verificationId,
            ["code"] = code,
            ["device"] = CreateDevicePayload(),
            ["appVersion"] = AppConfig.BrandVersion
        };
        if (!string.IsNullOrWhiteSpace(replaceDeviceId))
            bodyObj["replaceDeviceId"] = replaceDeviceId;

        return await SendLoginRequestAsync($"{BaseUrl}/api/app/login/code/confirm", bodyObj, ct);
    }

    private async Task<CustomerLoginResponse> SendLoginRequestAsync(string url, object bodyObj, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(bodyObj, JsonOpts), Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, ct);
        var respStr = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            HandleErrorResponse((int)resp.StatusCode, respStr);

        var result = JsonSerializer.Deserialize<CustomerLoginResponse>(respStr, JsonOpts)
            ?? throw new CustomerAppException((int)resp.StatusCode, "PARSE_ERROR", "پاسخ ورود نامعتبر است");

        ApplyLoginResult(result);
        return result;
    }

    private void ApplyLoginResult(CustomerLoginResponse res)
    {
        AccessToken = res.AccessToken;
        RefreshToken = res.RefreshToken;
        TokenExpiresAt = DateTime.UtcNow.AddSeconds(res.AccessExpiresIn > 0 ? res.AccessExpiresIn : 3600);
        CurrentAccount = res.Account;
        CurrentDevice = res.Device;

        // ذخیره توکن‌ها و سشن در کانفیگ پایدار برنامه
        try
        {
            var cfg = AppConfig.Load();
            cfg.CustomerAccessToken = AccessToken;
            cfg.CustomerRefreshToken = RefreshToken;
            cfg.CustomerTokenExpiresAt = TokenExpiresAt;
            if (res.Device != null) cfg.CustomerDeviceId = res.Device.Id;
            if (res.Account != null)
            {
                cfg.CustomerLoginMode = res.Account.Kind;
                cfg.CustomerUsername = res.Account.Username ?? res.Account.Name;
            }
            cfg.Save();
        }
        catch { }
    }

    // ===== تمدید نشست (Refresh Token) =====
    public async Task<CustomerLoginResponse> RefreshTokenAsync(CancellationToken ct = default)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(RefreshToken))
                throw new CustomerAppException(401, "UNAUTHORIZED", "نشست کاربری نامعتبر است؛ لطفاً دوباره وارد شوید.");

            var bodyObj = new
            {
                refreshToken = RefreshToken,
                device = new { appVersion = AppConfig.BrandVersion }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/app/refresh")
            {
                Content = new StringContent(JsonSerializer.Serialize(bodyObj, JsonOpts), Encoding.UTF8, "application/json")
            };
            using var resp = await _http.SendAsync(req, ct);
            var respStr = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    ClearSession();
                }
                HandleErrorResponse((int)resp.StatusCode, respStr);
            }

            var result = JsonSerializer.Deserialize<CustomerLoginResponse>(respStr, JsonOpts)
                ?? throw new CustomerAppException((int)resp.StatusCode, "PARSE_ERROR", "پاسخ تمدید نشست نامعتبر است");

            ApplyLoginResult(result);
            return result;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void ClearSession()
    {
        AccessToken = null;
        RefreshToken = null;
        TokenExpiresAt = null;
        CurrentAccount = null;
        CurrentDevice = null;
        try
        {
            var cfg = AppConfig.Load();
            cfg.CustomerAccessToken = null;
            cfg.CustomerRefreshToken = null;
            cfg.CustomerTokenExpiresAt = null;
            cfg.Save();
        }
        catch { }
    }

    private async Task EnsureAuthorizedAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            var cfg = AppConfig.Load();
            if (!string.IsNullOrWhiteSpace(cfg.CustomerRefreshToken))
            {
                RefreshToken = cfg.CustomerRefreshToken;
                AccessToken = cfg.CustomerAccessToken;
                TokenExpiresAt = cfg.CustomerTokenExpiresAt;
            }
        }

        if (string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(RefreshToken))
        {
            await RefreshTokenAsync(ct);
            return;
        }

        if (TokenExpiresAt.HasValue && DateTime.UtcNow >= TokenExpiresAt.Value.AddSeconds(-30))
        {
            if (!string.IsNullOrWhiteSpace(RefreshToken))
            {
                await RefreshTokenAsync(ct);
            }
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        await EnsureAuthorizedAsync(ct);

        var req = requestFactory();
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        var resp = await _http.SendAsync(req, ct);

        // در صورت انقضای توکن در حین کار (401)، یک بار خودکار رفرش کرده و درخواست را تکرار کن
        if (resp.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(RefreshToken))
        {
            resp.Dispose();
            await RefreshTokenAsync(ct);

            var retryReq = requestFactory();
            retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            return await _http.SendAsync(retryReq, ct);
        }

        return resp;
    }

    // ===== دریافت لیست سرویس‌های کاربر =====
    public async Task<List<CustomerService>> GetServicesAsync(CancellationToken ct = default)
    {
        using var resp = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/app/services"), ct);
        var respStr = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            HandleErrorResponse((int)resp.StatusCode, respStr);

        var data = JsonSerializer.Deserialize<CustomerServicesResponse>(respStr, JsonOpts);
        return data?.Services ?? new List<CustomerService>();
    }

    // ===== دریافت کانفیگ کامل اتصال یک سرویس =====
    public async Task<ServiceConnectionResponse> GetServiceConnectionAsync(string serviceId, CancellationToken ct = default)
    {
        using var resp = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/app/services/{serviceId}/connection"), ct);
        var respStr = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            HandleErrorResponse((int)resp.StatusCode, respStr);

        return JsonSerializer.Deserialize<ServiceConnectionResponse>(respStr, JsonOpts)
            ?? throw new CustomerAppException((int)resp.StatusCode, "PARSE_ERROR", "اطلاعات اتصال سرویس دریافت نشد");
    }

    // ===== تغییر لوکیشن WireGuard =====
    public async Task<ServiceConnectionResponse> SwitchWireGuardLocationAsync(string serviceId, string locationId, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { locationId }, JsonOpts);
        using var resp = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/app/services/{serviceId}/location")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }, ct);

        var respStr = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            HandleErrorResponse((int)resp.StatusCode, respStr);

        return JsonSerializer.Deserialize<ServiceConnectionResponse>(respStr, JsonOpts)
            ?? throw new CustomerAppException((int)resp.StatusCode, "PARSE_ERROR", "پاسخ تغییر لوکیشن نامعتبر است");
    }

    // ===== اطلاعات کاربر و دستگاه‌های فعال =====
    public async Task<CustomerMeResponse> GetMeAsync(CancellationToken ct = default)
    {
        using var resp = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/app/me"), ct);
        var respStr = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            HandleErrorResponse((int)resp.StatusCode, respStr);

        return JsonSerializer.Deserialize<CustomerMeResponse>(respStr, JsonOpts)
            ?? throw new CustomerAppException((int)resp.StatusCode, "PARSE_ERROR", "اطلاعات حساب کاربری دریافت نشد");
    }

    // ===== خروج از این دستگاه =====
    public async Task<bool> LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/app/logout"), ct);
            ClearSession();
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            ClearSession();
            return false;
        }
    }

    // ===== حذف یک دستگاه دیگر کاربر =====
    public async Task<bool> DeleteDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        using var resp = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/api/app/devices/{deviceId}"), ct);
        var respStr = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            HandleErrorResponse((int)resp.StatusCode, respStr);

        return true;
    }

    // ===== مدیریت خطاها و سقف دستگاه‌ها بر اساس داکیومنت =====
    private static void HandleErrorResponse(int statusCode, string rawJson)
    {
        try
        {
            var err = JsonSerializer.Deserialize<CustomerErrorResponse>(rawJson, JsonOpts);
            if (err != null)
            {
                if (err.Error == "DEVICE_LIMIT" && err.Details.HasValue)
                {
                    var details = err.Details.Value;
                    int limit = details.TryGetProperty("limit", out var lp) ? lp.GetInt32() : 3;
                    var devices = new List<CustomerDevice>();
                    if (details.TryGetProperty("devices", out var devArr) && devArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in devArr.EnumerateArray())
                        {
                            var parsedDev = JsonSerializer.Deserialize<CustomerDevice>(d.GetRawText(), JsonOpts);
                            if (parsedDev != null) devices.Add(parsedDev);
                        }
                    }
                    throw new CustomerDeviceLimitException(err.Message ?? "سقف دستگاه‌های متصل پر است.", limit, devices);
                }

                throw new CustomerAppException(statusCode, err.Error ?? "ERROR", !string.IsNullOrWhiteSpace(err.Message) ? err.Message : rawJson);
            }
        }
        catch (CustomerDeviceLimitException) { throw; }
        catch (CustomerAppException) { throw; }
        catch { }

        throw new CustomerAppException(statusCode, "HTTP_ERROR", $"خطای سرور: {statusCode}");
    }

    // ===== تبدیل شیء CustomerService به PortalDashboard جهت هماهنگی با ویجت‌های موجود داشبورد =====
    public static PortalDashboard ConvertToPortalDashboard(CustomerService s)
    {
        var expired = string.Equals(s.Status, "expired", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(s.Status, "suspended", StringComparison.OrdinalIgnoreCase);

        int remainingDays;
        if (s.ExpireAt.HasValue)
        {
            var diff = (s.ExpireAt.Value.ToLocalTime() - DateTime.Now).TotalDays;
            remainingDays = (int)Math.Max(0, Math.Ceiling(diff));
            if (diff <= 0) expired = true;
        }
        else
        {
            // اگر هنوز تاریخ انقضا ثبت نشده (شروع پس از اتصال یا بدون انقضا)، نباید 0 باشد تا با منقضی اشتباه نشود
            remainingDays = -1;
        }

        long totalMb = (s.TotalTrafficBytes ?? 0) / (1024 * 1024);
        long usedMb = (s.UsedTrafficBytes ?? 0) / (1024 * 1024);
        long remainingMb = totalMb > 0 ? Math.Max(0, totalMb - usedMb) : 0;
        int usagePercent = totalMb > 0 ? (int)Math.Clamp((usedMb * 100.0) / totalMb, 0, 100) : 0;

        return new PortalDashboard
        {
            CanRenewSelf = true,
            Account = new PortalAccount
            {
                Id = s.Id,
                Username = s.Username,
                Status = s.Status,
                ExpireAt = s.ExpireAt,
                RemainingDays = remainingDays,
                FirstLoginAt = s.FirstConnectedAt,
            },
            Package = new PortalPackage
            {
                Id = s.Id,
                Name = s.PackageName ?? s.Name,
                TrafficMb = totalMb,
                DurationDays = remainingDays
            },
            Traffic = new PortalTraffic
            {
                TotalBytes = s.TotalTrafficBytes ?? 0,
                UsedBytes = s.UsedTrafficBytes ?? 0,
                RemainingBytes = totalMb > 0 ? (s.TotalTrafficBytes!.Value - (s.UsedTrafficBytes ?? 0)) : 0,
                TotalMb = totalMb,
                UsedMb = usedMb,
                RemainingMb = remainingMb,
                UsagePercent = usagePercent
            }
        };
    }
}
