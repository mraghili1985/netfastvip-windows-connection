public enum ConnState { Idle, Probing, Connecting, Connected, Failed, Disconnected }

public interface IConnectionProvider
{
    string Type { get; }        // ikev2, sstp, openvpn-udp-1197, ...
    string Kind { get; }        // vpn | proxy
    bool IsEnabled { get; }

    // بعد از تلاش ناموفق: true یعنی یوزر/پس رد شد (پایان اشتراک یا اشتباه)
    bool AuthFailed { get; }

    // بعد از اتصال موفق: IP خصوصی که از سرور گرفتیم (اگر قابل تشخیص بود)
    string? LocalIp { get; }

    Task<bool> ProbeAsync(string host, CancellationToken ct);
    Task<bool> ConnectAsync(string host, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task<bool> IsAliveAsync(CancellationToken ct);
}