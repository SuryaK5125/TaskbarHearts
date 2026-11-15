namespace SurvivalHud.Core;

/// <summary>
/// Recognises the Windows 11 notification-area buttons by their UI Automation class names, which
/// are the same in every display language. Windows has renamed some of them across updates (the
/// clock was "SystemTray.OmniButton" and is "SystemTray.OmniButtonLeft" on newer builds), so the
/// rules here match the family rather than one exact name.
/// </summary>
public static class TrayClasses
{
    public const string Volume = "SystemTray.OmniButtonCenter";
    public const string ShowDesktop = "SystemTray.ShowDesktopButton";
    private const string OmniPrefix = "SystemTray.OmniButton";

    /// <summary>
    /// Index of the clock button among tray buttons sorted left to right, or −1. The clock is the
    /// rightmost "OmniButton…" that isn't the volume button; failing that, the last button before
    /// Show Desktop, as long as it is to the right of the volume button.
    /// </summary>
    public static int ClockIndex(IReadOnlyList<string> classes)
    {
        for (int i = classes.Count - 1; i >= 0; i--)
            if (classes[i].StartsWith(OmniPrefix, StringComparison.Ordinal) && classes[i] != Volume) return i;
        int volume = VolumeIndex(classes);
        if (volume < 0) return -1;
        for (int i = classes.Count - 1; i > volume + 1; i--)
            if (classes[i] != ShowDesktop) return i;
        return -1;
    }

    public const string Normal = "SystemTray.NormalButton";

    /// <summary>
    /// True when the taskbar is mirrored (right-to-left display languages such as Arabic and
    /// Hebrew): the notification area is on the left, so in screen order the clock comes before
    /// the volume button, or Show Desktop is the leftmost button. <paramref name="classes"/> are
    /// in screen order, left to right.
    /// </summary>
    public static bool IsMirrored(IReadOnlyList<string> classes)
    {
        if (classes.Count < 2) return false;
        int volume = VolumeIndex(classes);
        int clock = -1;
        for (int i = 0; i < classes.Count; i++)
            if (classes[i].StartsWith(OmniPrefix, StringComparison.Ordinal) && classes[i] != Volume) { clock = i; break; }
        if (volume >= 0 && clock >= 0) return clock < volume;
        return classes[0] == ShowDesktop;
    }

    /// <summary>
    /// First button of the Quick Settings group (network, volume, battery), or −1. Normally the
    /// volume button, or the network button right before it. If Windows ever renames the volume
    /// button, falls back to the run of non-ordinary buttons just before the clock (ordinary app
    /// tray icons, the input indicator and the overflow arrow are all "NormalButton").
    /// </summary>
    public static int QuickSettingsStart(IReadOnlyList<string> classes)
    {
        int volume = VolumeIndex(classes);
        if (volume >= 0) return volume > 0 && classes[volume - 1] == "SystemTray.AccentButton" ? volume - 1 : volume;
        int clock = ClockIndex(classes);
        if (clock <= 0) return -1;
        int start = clock;
        while (start - 1 >= 0 && classes[start - 1] != Normal && classes[start - 1] != ShowDesktop) start--;
        return start < clock ? start : -1;
    }

    public static int VolumeIndex(IReadOnlyList<string> classes)
    {
        for (int i = 0; i < classes.Count; i++)
            if (classes[i] == Volume) return i;
        return -1;
    }
}
