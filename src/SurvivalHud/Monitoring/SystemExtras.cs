using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SurvivalHud.Monitoring;

/// <summary>Small extra readings for the game-style popup's hotbar. Each is one cheap system call; none need admin rights.</summary>
public static class SystemExtras
{
    /// <summary>Running processes and threads, from <c>GetPerformanceInfo</c>.</summary>
    public static (int Processes, int Threads)? ProcessCounts()
    {
        var pi = new PERFORMANCE_INFORMATION { cb = Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        return K32GetPerformanceInfo(ref pi, pi.cb) ? ((int)pi.ProcessCount, (int)pi.ThreadCount) : null;
    }

    /// <summary>Free and total bytes on the Windows drive.</summary>
    public static (double Free, double Total)? SystemDrive()
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
            return (d.AvailableFreeSpace, d.TotalSize);
        }
        catch { return null; }
    }

    /// <summary>
    /// Battery health: the full-charge capacity as a share of the design capacity (mWh), read
    /// from the battery driver with the documented IOCTLs. Null on desktops or when the driver
    /// reports relative units only.
    /// </summary>
    public static (double Percent, double FullWh, double DesignWh)? BatteryHealth()
    {
        try
        {
            foreach (string path in BatteryPaths())
            {
                using var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h.IsInvalid) continue;
                uint wait = 0;
                if (!DeviceIoControl(h, 0x294040 /* QUERY_TAG */, ref wait, 4, out uint tag, 4, out _, IntPtr.Zero) || tag == 0) continue;
                var q = new BATTERY_QUERY_INFORMATION { BatteryTag = tag, InformationLevel = 0 };
                if (!DeviceIoControl(h, 0x294044 /* QUERY_INFORMATION */, ref q, Marshal.SizeOf<BATTERY_QUERY_INFORMATION>(),
                        out BATTERY_INFORMATION info, Marshal.SizeOf<BATTERY_INFORMATION>(), out _, IntPtr.Zero)) continue;
                if ((info.Capabilities & 0x40000000) != 0 || info.DesignedCapacity == 0) continue;   // relative units only
                double full = info.FullChargedCapacity / 1000.0, design = info.DesignedCapacity / 1000.0;
                return (full / design * 100, full, design);
            }
        }
        catch { /* no battery driver */ }
        return null;
    }

    private static List<string> BatteryPaths()
    {
        var list = new List<string>();
        Guid cls = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");   // GUID_DEVCLASS_BATTERY
        IntPtr set = SetupDiGetClassDevs(ref cls, IntPtr.Zero, IntPtr.Zero, 0x12 /* PRESENT | DEVICEINTERFACE */);
        if (set == new IntPtr(-1)) return list;
        try
        {
            for (uint i = 0; i < 8; i++)
            {
                var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref cls, i, ref did)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, size, out _, IntPtr.Zero))
                        list.Add(Marshal.PtrToStringUni(buf + 4)!);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public int cb;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
            KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BATTERY_QUERY_INFORMATION { public uint BatteryTag; public int InformationLevel; public int AtRate; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BATTERY_INFORMATION
    {
        public uint Capabilities; public byte Technology, R1, R2, R3; public uint Chemistry;
        public uint DesignedCapacity, FullChargedCapacity, DefaultAlert1, DefaultAlert2, CriticalBias, CycleCount;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("kernel32.dll")] private static extern bool K32GetPerformanceInfo(ref PERFORMANCE_INFORMATION pi, int size);
    [DllImport("setupapi.dll")] private static extern IntPtr SetupDiGetClassDevs(ref Guid cls, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid cls, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref uint input, int inSize, out uint output, int outSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll")]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref BATTERY_QUERY_INFORMATION input, int inSize, out BATTERY_INFORMATION output, int outSize, out int returned, IntPtr overlapped);
}
