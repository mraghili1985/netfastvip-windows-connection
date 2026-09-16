using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// =====================================================================================
// Kill Switch واقعی و بدون نشت با استفاده مستقیم از Windows Filtering Platform (WFP)
// از طریق fwpuclnt.dll — همان لایه‌ای که WireGuard، Mullvad و اکثر کلاینت‌های حرفه‌ای
// VPN برای Kill Switch استفاده می‌کنند. این پیاده‌سازی به «جدول روتینگ» وابسته نیست،
// بلکه مستقیماً در مسیر تصمیم‌گیری شبکه‌ی ویندوز (ALE - Application Layer Enforcement)
// فیلتر نصب می‌کند، پس با قطع ناگهانی کارت شبکه یا سوییچ اینترفیس هم نشت رخ نمی‌دهد.
//
// منطق:
//   1) یک Session با پرچم FWPM_SESSION_FLAG_DYNAMIC باز می‌شود => اگر پروسه به هر دلیل
//      (کرش / Kill از Task Manager / حذف ناگهانی) از بین برود، خود ویندوز تمام
//      فیلترهای این Session را خودکار پاک می‌کند و اینترنت کاربر قفل ابدی نمی‌شود.
//   2) یک SubLayer اختصاصی ساخته می‌شود تا فیلترها مستقل از بقیه سیستم باشند.
//   3) همه‌ی فیلترها در یک Transaction اضافه می‌شوند: یا همه با موفقیت ثبت می‌شوند،
//      یا (در صورت خطا در هر مرحله) هیچ‌کدام اعمال نمی‌شوند — تا هرگز حالت نیمه‌کاره
//      «بلاک فعال بدون استثناها» رخ ندهد.
//   4) روی لایه‌ی ALE_AUTH_CONNECT_V4 یک فیلتر BLOCK بدون شرط (کم‌اختصاصی‌ترین) و چند
//      فیلتر PERMIT مشخص‌تر اضافه می‌شود. WFP به‌صورت خودکار به فیلترهای مشخص‌تر
//      (شرط بیشتر) وزن بالاتر می‌دهد، پس PERMIT ها همیشه بر BLOCK اولویت دارند.
//   5) چون این اپ از IPv6 پشتیبانی نمی‌کند (مثل SplitTunnel که فقط IPv4 است)، تمام
//      ترافیک IPv6 خروجی هم به‌طور کامل بلاک می‌شود تا مسیر نشتی از آن طرف هم بسته باشد.
//
// نیازمندی: این کد باید با دسترسی Administrator اجرا شود (WFP engine بدون دسترسی
// ادمین باز نمی‌شود). اگر برنامه از قبل برای اجرای OpenVPN/تغییر route نیاز به ادمین
// دارد (که ظاهراً دارد)، این نیاز از قبل برطرف است؛ در غیر این صورت باید یک
// app.manifest با requestedExecutionLevel=requireAdministrator به پروژه اضافه شود.
// =====================================================================================
public static class KillSwitch
{
    public static event Action<string>? Log;

    // توسط کاربر از طریق کلید تنظیمات روشن/خاموش می‌شود. مقدار اولیه‌ی آن در استارتاپ برنامه
    // از AppConfig.KillSwitchEnabled (داخل config.json) خوانده می‌شود، پس بین اجراهای برنامه حفظ می‌شود.
    public static bool Enabled { get; set; }

    private static IntPtr _engine = IntPtr.Zero;
    private static readonly Guid _sessionKey = Guid.NewGuid();
    private static readonly Guid _subLayerKey = Guid.NewGuid();
    private static readonly List<Guid> _filterKeys = [];
    private static readonly object _gate = new();

    public static bool IsActive => _engine != IntPtr.Zero;

    // فراخوانی بعد از اتصال موفق VPN.
    //   serverIp/serverPort: آی‌پی و پورت واقعی سرور VPN (برای استثنای اول).
    //   tunnelInterfaceIndex: Interface Index آداپتور مجازی تانل (TAP/TUN/Wintun) —
    //     همان مقداری که NetworkInterface.GetIPProperties().GetIPv4Properties().Index می‌دهد.
    public static Task<bool> EnableAsync(string? serverIp, int? serverPort, int? tunnelInterfaceIndex)
        => Task.Run(() => EnableCore(serverIp, serverPort, tunnelInterfaceIndex));

    // فراخوانی روی قطع اتصال / خاموش کردن برنامه — پاک‌سازی کامل فیلترها و بستن Session.
    public static Task DisableAsync() => Task.Run(DisableCore);

    private static bool EnableCore(string? serverIp, int? serverPort, int? tunnelInterfaceIndex)
    {
        lock (_gate)
        {
            // اگر از قبل فعال بود، اول کامل تمیز کن تا فیلترهای تکراری/قدیمی نماند.
            if (_engine != IntPtr.Zero) DisableCore();

            var session = new FWPM_SESSION0
            {
                displayData = new FWPM_DISPLAY_DATA0 { name = "NetFastVIP KillSwitch", description = "Leak-proof outbound block" },
                flags = FWPM_SESSION_FLAG_DYNAMIC,
            };

            var rc = FwpmEngineOpen0(null, RPC_C_AUTHN_WINNT, IntPtr.Zero, ref session, out _engine);
            if (rc != 0)
            {
                Log?.Invoke($"killswitch: FwpmEngineOpen0 failed (0x{rc:X}) — آیا برنامه با دسترسی Administrator اجرا شده؟");
                _engine = IntPtr.Zero;
                return false;
            }

            var txnRc = FwpmTransactionBegin0(_engine, 0);
            if (txnRc != 0)
            {
                Log?.Invoke($"killswitch: FwpmTransactionBegin0 failed (0x{txnRc:X})");
                FwpmEngineClose0(_engine); _engine = IntPtr.Zero;
                return false;
            }

            try
            {
                var subLayer = new FWPM_SUBLAYER0
                {
                    subLayerKey = _subLayerKey,
                    displayData = new FWPM_DISPLAY_DATA0 { name = "NetFastVIP KillSwitch SubLayer", description = "" },
                    weight = 0xFFFF,
                };
                var subRc = FwpmSubLayerAdd0(_engine, ref subLayer, IntPtr.Zero);
                if (subRc != 0) throw new InvalidOperationException($"FwpmSubLayerAdd0 failed (0x{subRc:X})");

                // 1) بلاک همه‌ی ترافیک IPv4 خروجی (بدون هیچ شرطی -> کم‌اختصاصی‌ترین -> کمترین وزن خودکار)
                AddFilter("Block all IPv4 outbound", LAYER_ALE_AUTH_CONNECT_V4, [], ACTION_BLOCK);

                // 2) بلاک کامل IPv6 خروجی (این اپ IPv6 را تانل نمی‌کند، پس برای جلوگیری از نشت کامل بسته می‌شود)
                AddFilter("Block all IPv6 outbound", LAYER_ALE_AUTH_CONNECT_V6, [], ACTION_BLOCK);

                // 3) استثنا: Loopback (127.0.0.1) برای اپ‌های محلی
                AddFilter("Permit loopback", LAYER_ALE_AUTH_CONNECT_V4,
                    [Condition(COND_IP_LOCAL_ADDRESS, MATCH_EQUAL, FwpValueUInt32(IPv4ToUInt32("127.0.0.1")))],
                    ACTION_PERMIT);

                // 4) استثنا: آی‌پی/پورت واقعی سرور VPN (از طریق کارت شبکه فیزیکی)
                if (!string.IsNullOrWhiteSpace(serverIp) && IPAddress.TryParse(serverIp, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var conditions = new List<FWPM_FILTER_CONDITION0>
                    {
                        Condition(COND_IP_REMOTE_ADDRESS, MATCH_EQUAL, FwpValueUInt32(IPv4ToUInt32(serverIp))),
                    };
                    if (serverPort is > 0 and <= 65535)
                        conditions.Add(Condition(COND_IP_REMOTE_PORT, MATCH_EQUAL, FwpValueUInt16((ushort)serverPort.Value)));
                    AddFilter("Permit VPN server endpoint", LAYER_ALE_AUTH_CONNECT_V4, conditions, ACTION_PERMIT);
                }
                else
                {
                    Log?.Invoke("killswitch: هشدار — آی‌پی سرور مشخص نیست، استثنای سرور اضافه نشد (ممکن است پس از فعال شدن Kill Switch اتصال جدید سرور برقرار نشود)");
                }

                // 5) استثنا: آداپتور مجازی تانل (۱۰۰٪ ترافیک ورودی/خروجی روی آن آزاد است)
                ulong? tunnelLuid = null;
                if (tunnelInterfaceIndex is int idx and >= 0)
                {
                    var rcLuid = ConvertInterfaceIndexToLuid((uint)idx, out var luid);
                    if (rcLuid == 0)
                    {
                        tunnelLuid = luid;
                        AddFilter("Permit VPN tunnel adapter", LAYER_ALE_AUTH_CONNECT_V4,
                            [Condition(COND_IP_LOCAL_INTERFACE, MATCH_EQUAL, FwpValueUInt64(luid))],
                            ACTION_PERMIT);
                    }
                    else
                    {
                        Log?.Invoke($"killswitch: ConvertInterfaceIndexToLuid failed (0x{rcLuid:X}) — استثنای آداپتور تانل اضافه نشد");
                    }
                }
                else
                {
                    Log?.Invoke("killswitch: هشدار — Interface Index آداپتور تانل مشخص نیست، استثنای تانل اضافه نشد");
                }

                // 6) استثنا: DHCP خروجی محلی (UDP از پورت 68 — برای تمدید IP لوکال)
                AddFilter("Permit local DHCP", LAYER_ALE_AUTH_CONNECT_V4,
                    [
                        Condition(COND_IP_PROTOCOL, MATCH_EQUAL, FwpValueUInt8(17 /* UDP */)),
                        Condition(COND_IP_LOCAL_PORT, MATCH_EQUAL, FwpValueUInt16(68)),
                    ],
                    ACTION_PERMIT);

                // 7) DNS Leak Protection — پورت 53 (UDP و TCP) فقط از طریق آداپتور تانل و لوپ‌بک اجازه‌ی
                //    خروج دارد؛ هر درخواست DNS که سعی کند از کارت شبکه‌ی فیزیکی (یا هر اینترفیس دیگری
                //    غیر از تانل) خارج شود صریحاً بلاک می‌شود. این یک لایه‌ی دفاعی صریح و اضافه است؛ چون
                //    مدل پایه‌ی «بلاک همه به‌جز استثناها» از قبل چنین ترافیکی را (که هیچ استثنایی پوشش‌اش
                //    نمی‌دهد) بلاک می‌کند، اما با یک قانون مشخص و قابل‌ردیابی، حتی اگر بعداً یک Permit
                //    عمومی و نامناسب اضافه شود هم امکان نشت DNS باز نمی‌شود (چون این فیلتر با تعداد
                //    شرط بیشتر، وزن بالاتری خودکار می‌گیرد و مقدم بر آن Permit اجرا می‌شود).
                //    شرط IP_LOCAL_ADDRESS != 127.0.0.1 هم اضافه شده تا یک ریزالور DNS محلی احتمالی
                //    (مثل Pi-hole/کلاینت DNS-over-HTTPS محلی روی 127.0.0.1) دچار مشکل نشود.
                if (tunnelLuid is ulong tLuid)
                {
                    foreach (var (protoNum, protoLabel) in new[] { (17u /* UDP */, "UDP"), (6u /* TCP */, "TCP") })
                    {
                        AddFilter($"Block DNS ({protoLabel} 53) outside tunnel", LAYER_ALE_AUTH_CONNECT_V4,
                            [
                                Condition(COND_IP_REMOTE_PORT, MATCH_EQUAL, FwpValueUInt16(53)),
                                Condition(COND_IP_PROTOCOL, MATCH_EQUAL, FwpValueUInt8((byte)protoNum)),
                                Condition(COND_IP_LOCAL_INTERFACE, MATCH_NOT_EQUAL, FwpValueUInt64(tLuid)),
                                Condition(COND_IP_LOCAL_ADDRESS, MATCH_NOT_EQUAL, FwpValueUInt32(IPv4ToUInt32("127.0.0.1"))),
                            ],
                            ACTION_BLOCK);
                    }
                }
                else
                {
                    // اگر Interface Index تانل مشخص نیست، بدون امکان استثنا کردن آن، محافظت DNS را
                    // به‌طور کلی (روی همه‌ی اینترفیس‌ها جز لوپ‌بک) اعمال می‌کنیم — ایمن‌تر است حتی اگر
                    // موقتاً DNS خود تانل را هم مسدود کند تا اینکه ریسک نشت را بپذیریم.
                    Log?.Invoke("killswitch: هشدار — Interface تانل نامعلوم است؛ محافظت DNS به‌صورت سراسری (غیر از لوپ‌بک) اعمال می‌شود");
                    foreach (var (protoNum, protoLabel) in new[] { (17u /* UDP */, "UDP"), (6u /* TCP */, "TCP") })
                    {
                        AddFilter($"Block DNS ({protoLabel} 53) fallback", LAYER_ALE_AUTH_CONNECT_V4,
                            [
                                Condition(COND_IP_REMOTE_PORT, MATCH_EQUAL, FwpValueUInt16(53)),
                                Condition(COND_IP_PROTOCOL, MATCH_EQUAL, FwpValueUInt8((byte)protoNum)),
                                Condition(COND_IP_LOCAL_ADDRESS, MATCH_NOT_EQUAL, FwpValueUInt32(IPv4ToUInt32("127.0.0.1"))),
                            ],
                            ACTION_BLOCK);
                    }
                }

                var commitRc = FwpmTransactionCommit0(_engine);
                if (commitRc != 0) throw new InvalidOperationException($"FwpmTransactionCommit0 failed (0x{commitRc:X})");

                Log?.Invoke("killswitch: فعال شد — تمام ترافیک خروجی جز استثناهای مجاز مسدود است");
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke("killswitch: فعال‌سازی ناموفق بود، هیچ فیلتری اعمال نشد -> " + ex.Message);
                try { FwpmTransactionAbort0(_engine); } catch { }
                try { FwpmEngineClose0(_engine); } catch { }
                _engine = IntPtr.Zero;
                _filterKeys.Clear();
                return false;
            }
        }
    }

    private static void DisableCore()
    {
        lock (_gate)
        {
            if (_engine == IntPtr.Zero) return;
            try
            {
                // بستن کامل Session به‌تنهایی تمام فیلترها و SubLayer را هم پاک می‌کند،
                // اما برای شفافیت و لا�� تمیز، هر فیلتر را هم صریحاً حذف می‌کنیم.
                foreach (var key in _filterKeys)
                {
                    var k = key;
                    try { FwpmFilterDeleteByKey0(_engine, ref k); } catch { }
                }
                _filterKeys.Clear();
                var slKey = _subLayerKey;
                try { FwpmSubLayerDeleteByKey0(_engine, ref slKey); } catch { }
            }
            finally
            {
                try { FwpmEngineClose0(_engine); } catch { }
                _engine = IntPtr.Zero;
                Log?.Invoke("killswitch: غیرفعال شد — فیلترها پاک شدند");
            }
        }
    }

    // ------------------------------------------------------------------------------
    // کمک‌کننده‌ها برای ساخت فیلتر
    // ------------------------------------------------------------------------------
    private static void AddFilter(string name, Guid layerKey, List<FWPM_FILTER_CONDITION0> conditions, uint actionType)
    {
        var filterKey = Guid.NewGuid();
        var condArrayPtr = IntPtr.Zero;
        try
        {
            int condCount = conditions.Count;
            if (condCount > 0)
            {
                int size = Marshal.SizeOf<FWPM_FILTER_CONDITION0>();
                condArrayPtr = Marshal.AllocHGlobal(size * condCount);
                for (int i = 0; i < condCount; i++)
                    Marshal.StructureToPtr(conditions[i], IntPtr.Add(condArrayPtr, i * size), false);
            }

            var filter = new FWPM_FILTER0
            {
                filterKey = filterKey,
                displayData = new FWPM_DISPLAY_DATA0 { name = "NetFastVIP: " + name, description = "" },
                layerKey = layerKey,
                subLayerKey = _subLayerKey,
                weight = new FWP_VALUE0 { type = FWP_EMPTY }, // وزن خودکار بر اساس تعداد شرط‌ها (اختصاصی‌تر = اولویت بالاتر)
                numFilterConditions = (uint)condCount,
                filterCondition = condArrayPtr,
                action = new FWPM_ACTION0 { type = actionType },
            };

            var rc = FwpmFilterAdd0(_engine, ref filter, IntPtr.Zero, out _);
            if (rc != 0) throw new InvalidOperationException($"FwpmFilterAdd0('{name}') failed (0x{rc:X})");
            _filterKeys.Add(filterKey);
        }
        finally
        {
            if (condArrayPtr != IntPtr.Zero) Marshal.FreeHGlobal(condArrayPtr);
        }
    }

    private static FWPM_FILTER_CONDITION0 Condition(Guid fieldKey, uint matchType, FWP_VALUE0 value) => new()
    {
        fieldKey = fieldKey,
        matchType = matchType,
        conditionValue = value,
    };

    private static uint IPv4ToUInt32(string ipText)
    {
        var bytes = IPAddress.Parse(ipText).GetAddressBytes(); // network order: a.b.c.d -> [a,b,c,d]
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static FWP_VALUE0 FwpValueUInt8(byte v) => new() { type = FWP_UINT8, value = (IntPtr)v };
    private static FWP_VALUE0 FwpValueUInt16(ushort v) => new() { type = FWP_UINT16, value = (IntPtr)v };
    private static FWP_VALUE0 FwpValueUInt32(uint v) => new() { type = FWP_UINT32, value = (IntPtr)v };
    private static FWP_VALUE0 FwpValueUInt64(ulong v)
    {
        // اعداد ۶۴ بیتی در این ساختار به‌صورت غیرمستقیم (پوینتر) ذخیره می‌شوند، نه به‌صورت مستقیم
        var p = Marshal.AllocHGlobal(sizeof(ulong));
        Marshal.WriteInt64(p, unchecked((long)v));
        return new FWP_VALUE0 { type = FWP_UINT64, value = p };
        // یادداشت: این حافظه عمداً آزاد نمی‌شود؛ عمر آن به اندازه‌ی خود فیلتر (تا زمان Disable) کافی است
        // و نشتی حافظه‌ی ناچیز آن در مقیاس یک اتصال VPN اهمیتی ندارد.
    }

    // ------------------------------------------------------------------------------
    // P/Invoke — Fwpuclnt.dll (Windows Filtering Platform) و iphlpapi.dll
    // ------------------------------------------------------------------------------
    private const uint RPC_C_AUTHN_WINNT = 10;
    private const uint FWPM_SESSION_FLAG_DYNAMIC = 0x00000001;

    private const uint FWP_EMPTY = 0;
    private const uint FWP_UINT8 = 1;
    private const uint FWP_UINT16 = 2;
    private const uint FWP_UINT32 = 3;
    private const uint FWP_UINT64 = 4;

    private const uint MATCH_EQUAL = 0; // FWP_MATCH_EQUAL
    private const uint MATCH_NOT_EQUAL = 10; // FWP_MATCH_NOT_EQUAL

    private const uint ACTION_FLAG_TERMINATING = 0x00001000;
    private const uint ACTION_BLOCK = 0x00000001 | ACTION_FLAG_TERMINATING;  // FWP_ACTION_BLOCK
    private const uint ACTION_PERMIT = 0x00000002 | ACTION_FLAG_TERMINATING; // FWP_ACTION_PERMIT

    // GUID های استاندارد و رسمی مایکروسافت (fwpmu.h) — لایه‌ها و شرط‌های ALE
    private static readonly Guid LAYER_ALE_AUTH_CONNECT_V4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    private static readonly Guid LAYER_ALE_AUTH_CONNECT_V6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private static readonly Guid COND_IP_LOCAL_ADDRESS = new("b235ae9c-1d64-49b8-a44c-5ff3d9095045");
    private static readonly Guid COND_IP_REMOTE_ADDRESS = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    private static readonly Guid COND_IP_LOCAL_PORT = new("0c1ba1af-5765-453f-af22-a8f791ac775b");
    private static readonly Guid COND_IP_REMOTE_PORT = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    private static readonly Guid COND_IP_PROTOCOL = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    private static readonly Guid COND_IP_LOCAL_INTERFACE = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FWPM_DISPLAY_DATA0
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string name;
        [MarshalAs(UnmanagedType.LPWStr)] public string description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_BYTE_BLOB { public uint size; public IntPtr data; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_SESSION0
    {
        public Guid sessionKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public uint txnWaitTimeoutInMSec;
        public uint processId;
        public IntPtr sid;
        [MarshalAs(UnmanagedType.LPWStr)] public string? username;
        public int kernelMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_SUBLAYER0
    {
        public Guid subLayerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public ushort weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWP_VALUE0
    {
        public uint type;
        public IntPtr value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_FILTER_CONDITION0
    {
        public Guid fieldKey;
        public uint matchType;
        public FWP_VALUE0 conditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_ACTION0
    {
        public uint type;
        public Guid guid; // filterType/calloutKey - برای BLOCK/PERMIT استفاده نمی‌شود
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FWPM_FILTER0
    {
        public Guid filterKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid layerKey;
        public Guid subLayerKey;
        public FWP_VALUE0 weight;
        public uint numFilterConditions;
        public IntPtr filterCondition;
        public FWPM_ACTION0 action;
        public Guid providerContextKey; // union با rawContext (UINT64) - استفاده نمی‌شود، صفر می‌ماند
        public IntPtr reserved;
        public ulong filterId;
        public FWP_VALUE0 effectiveWeight;
    }

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity, ref FWPM_SESSION0 session, out IntPtr engineHandle);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, ref FWPM_SUBLAYER0 subLayer, IntPtr sd);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmSubLayerDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmFilterAdd0(IntPtr engineHandle, ref FWPM_FILTER0 filter, IntPtr sd, out ulong id);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmFilterDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

    [DllImport("Fwpuclnt.dll", ExactSpelling = true)]
    private static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int ConvertInterfaceIndexToLuid(uint interfaceIndex, out ulong interfaceLuid);
}
