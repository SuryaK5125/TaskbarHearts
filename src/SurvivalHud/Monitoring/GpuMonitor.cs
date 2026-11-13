using System.Runtime.InteropServices;

namespace SurvivalHud.Monitoring;

/// <summary>
/// GPU utilisation the way Task Manager shows it: the Windows "GPU Engine" performance counters
/// are summed per engine (across all processes) and the busiest engine is the GPU's figure.
/// Utilisation is a rate, so the first sample after opening returns null.
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000, PDH_MORE_DATA = 0x800007D2;
    private IntPtr _query, _counter;
    private bool _unavailable;

    public double? Sample()
    {
        if (_unavailable) return null;
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0
                    || PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _counter) != 0)
                {
                    Dispose();
                    _unavailable = true;
                    return null;
                }
                PdhCollectQueryData(_query);
                return null;
            }
            if (PdhCollectQueryData(_query) != 0) return null;

            uint size = 0;
            uint status = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return status == 0 ? 0 : null;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf) != 0) return null;
                var perEngine = new Dictionary<string, double>();
                int itemSize = IntPtr.Size == 8 ? 24 : 16;   // { LPWSTR name; { DWORD status; double value } }
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buf + i * itemSize;
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                    if (Marshal.ReadInt32(item + IntPtr.Size) != 0) continue;   // CStatus: invalid data
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                    // "pid_1234_luid_0x…_0x…_phys_0_eng_3_engtype_3D" → one engine = "luid_…_eng_3"
                    int a = name.IndexOf("luid_", StringComparison.Ordinal), b = name.IndexOf("_engtype_", StringComparison.Ordinal);
                    if (a < 0 || b <= a) continue;
                    string engine = name[a..b];
                    perEngine[engine] = perEngine.GetValueOrDefault(engine) + v;
                }
                double max = 0;
                foreach (double v in perEngine.Values) max = Math.Max(max, v);
                return Math.Clamp(max, 0, 100);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (DllNotFoundException) { _unavailable = true; return null; }
    }

    /// <summary>Closes the counter query; the next <see cref="Sample"/> starts a new one.</summary>
    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = _counter = IntPtr.Zero;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
