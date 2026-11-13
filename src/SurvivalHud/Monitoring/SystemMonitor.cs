using SurvivalHud.Core;
using SurvivalHud.Native;

namespace SurvivalHud.Monitoring;

/// <summary>One snapshot of the three readings. Null means "not available right now".</summary>
public readonly record struct Metrics(
    int? BatteryPercent, bool HasBattery, bool OnAc, bool Charging,
    double? CpuPercent, double? MemFreePercent, ulong MemTotalBytes, ulong MemAvailableBytes);

/// <summary>Reads battery, CPU and memory through three cheap kernel32 calls. No WMI, no performance counters.</summary>
public sealed class SystemMonitor
{
    private readonly CpuUsageCalculator _cpu = new(window: 4);

    public Metrics Sample()
    {
        // Battery ---------------------------------------------------------------
        int? battery = null;
        bool hasBattery = false, onAc = false, charging = false;
        if (Win32.GetSystemPowerStatus(out var ps))
        {
            const byte NoBattery = 128, Charging = 8, Unknown = 255;
            hasBattery = ps.BatteryFlag != Unknown && (ps.BatteryFlag & NoBattery) == 0;
            onAc = ps.ACLineStatus == 1;
            if (hasBattery)
            {
                charging = (ps.BatteryFlag & Charging) != 0;
                if (ps.BatteryLifePercent <= 100) battery = ps.BatteryLifePercent;
            }
        }

        // CPU -------------------------------------------------------------------
        double? cpu = Win32.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user)
            ? _cpu.Next(idle, kernel, user)
            : _cpu.Smoothed;

        // Memory ----------------------------------------------------------------
        double? memFree = null;
        ulong total = 0, avail = 0;
        var ms = new Win32.MEMORYSTATUSEX { dwLength = 64 };
        if (Win32.GlobalMemoryStatusEx(ref ms) && ms.ullTotalPhys > 0)
        {
            total = ms.ullTotalPhys;
            avail = ms.ullAvailPhys;
            memFree = FillMapping.ArmorFromMemory(avail, total);
        }

        return new Metrics(battery, hasBattery, onAc, charging, cpu, memFree, total, avail);
    }
}
