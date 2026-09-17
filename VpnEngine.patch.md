# Patch برای VpnEngine.cs

## تغییر لازم در CreateProvider

خط فعلی:
```csharp
if (p.Type == "wireguard" || p.Type == "amneziawg")
    return new WireGuardProvider(p.WireGuardConf, isAmnezia: p.Type == "amneziawg");
```

عوضش کن با:
```csharp
if (p.Type == "wireguard" || p.Type == "amneziawg")
    return new WireGuardProvider(p.WireGuardConf); // نوع از روی conf auto-detect می‌شود
```

همین یه خط — بقیه VpnEngine.cs دست نخوره.
