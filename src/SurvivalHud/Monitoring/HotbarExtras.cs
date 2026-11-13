using System.Runtime.InteropServices;

namespace SurvivalHud.Monitoring;

/// <summary>Volume of the default speakers through Windows Core Audio. Read-only.</summary>
public static class VolumeReader
{
    /// <summary>Master volume 0–100 and the mute state, or null when there is no output device.</summary>
    public static (int Percent, bool Muted)? Read()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out IMMDevice device) != 0) return null;
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out object o) != 0) return null;
            var vol = (IAudioEndpointVolume)o;
            if (vol.GetMasterVolumeLevelScalar(out float level) != 0) return null;
            vol.GetMute(out bool muted);
            return ((int)Math.Round(level * 100), muted);
        }
        catch { return null; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}

/// <summary>
/// The program using the most CPU, like Task Manager's top row. Process times come from one
/// kernel snapshot (<c>NtQuerySystemInformation</c>), so no process has to be opened and
/// protected ones are included. Processes with the same name are added together (all of a
/// browser's processes count as one). The first call only takes the baseline.
/// </summary>
public sealed class TopProcessMonitor
{
    private Dictionary<IntPtr, (long Time, string Name)> _last = new();
    private long _lastTicks;

    public (string Name, double Percent)? Sample()
    {
        var now = new Dictionary<IntPtr, (long, string)>();
        if (!Snapshot(now)) return null;
        long ticks = Environment.TickCount64;
        (string, double)? top = null;
        if (_last.Count > 0 && ticks > _lastTicks)
        {
            double budget = (ticks - _lastTicks) * 10_000.0 * Environment.ProcessorCount;   // 100-ns units across all cores
            var byName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var (pid, (time, name)) in now)
                if (pid != IntPtr.Zero && _last.TryGetValue(pid, out var prev) && time >= prev.Time)
                    byName[name] = byName.GetValueOrDefault(name) + (time - prev.Time);
            foreach (var (name, delta) in byName)
                if (top is not { } t || delta > t.Item2) top = (name, delta);
            if (top is { } best) top = (best.Item1, Math.Clamp(best.Item2 / budget * 100, 0, 100));
        }
        _last = now;
        _lastTicks = ticks;
        return top;
    }

    private static bool Snapshot(Dictionary<IntPtr, (long, string)> into)
    {
        int size = 512 * 1024;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int status = NtQuerySystemInformation(5 /* SystemProcessInformation */, buf, size, out int needed);
                if (status == unchecked((int)0xC0000004)) { size = Math.Max(size * 2, needed + 65536); continue; }   // buffer too small
                if (status != 0) return false;
                long offset = 0;
                while (true)
                {
                    IntPtr e = buf + (int)offset;
                    long user = Marshal.ReadInt64(e + 40), kernel = Marshal.ReadInt64(e + 48);
                    ushort nameLen = (ushort)Marshal.ReadInt16(e + 56);
                    IntPtr namePtr = Marshal.ReadIntPtr(e + 64);
                    IntPtr pid = Marshal.ReadIntPtr(e + 80);
                    string name = nameLen > 0 && namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr, nameLen / 2) : "System Idle";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                    into[pid] = (user + kernel, name);
                    int next = Marshal.ReadInt32(e);
                    if (next == 0) break;
                    offset += next;
                }
                return true;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return false;
    }

    [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);
}

/// <summary>Drives other than the Windows drive: internal disks and USB sticks that are ready.</summary>
public static class OtherDrives
{
    public static List<(string Name, double Free, double Total)> Read()
    {
        var list = new List<(string, double, double)>();
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                if (string.Equals(d.Name, system, StringComparison.OrdinalIgnoreCase)) continue;
                try { if (d.IsReady) list.Add((d.Name.TrimEnd('\\'), d.AvailableFreeSpace, d.TotalSize)); }
                catch { /* card reader without a card, locked BitLocker drive… */ }
            }
        }
        catch { }
        return list;
    }
}
