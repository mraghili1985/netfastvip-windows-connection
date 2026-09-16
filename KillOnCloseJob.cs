using System.Runtime.InteropServices;

// Job Object ویندوز با فلگ KILL_ON_JOB_CLOSE:
// هر پروسه‌ای که به این Job اضافه شود، با مرگ اپ (کرش، End Task، هر دلیلی)
// توسط خود ویندوز بلافاصله کشته می‌شود — openvpn.exe هیچ‌وقت یتیم نمی‌ماند.
// هندل Job عمداً هیچ‌وقت بسته نمی‌شود؛ با پایان پروسه اپ، ویندوز آن را می‌بندد و kill فعال می‌شود.
public static class KillOnCloseJob
{
    private static readonly IntPtr JobHandle = Create();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int cbLength);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    private static IntPtr Create()
    {
        try
        {
            var h = CreateJobObject(IntPtr.Zero, null);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            SetInformationJobObject(h, JobObjectExtendedLimitInformation, ref info,
                Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
            return h;
        }
        catch { return IntPtr.Zero; }
    }

    /// پروسه فرزند را به Job می‌بندد — با مرگ اپ، ویندوز خودش آن را می‌کشد
    public static void Add(System.Diagnostics.Process p)
    {
        try
        {
            if (JobHandle != IntPtr.Zero)
                AssignProcessToJobObject(JobHandle, p.Handle);
        }
        catch { }
    }
}
