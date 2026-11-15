namespace SurvivalHud.Core;

/// <summary>
/// What the taskbar's notification area looks like, from its buttons' screen positions and
/// class names. On a mirrored (right-to-left) taskbar every X is flipped across the taskbar
/// (x becomes left + right - x) so the numbers read like a normal taskbar; see <see cref="Mirrored"/>.
/// </summary>
/// <param name="TrayLeft">Left edge of the notification area.</param>
/// <param name="ButtonsRight">Right edge of the app buttons, Start and Search, when they end before the tray.</param>
/// <param name="QuickSettingsLeft">Left edge of the network/volume/battery group.</param>
/// <param name="ClockLeft">Left edge of the clock.</param>
/// <param name="ItemLefts">Left edges of the tray buttons other than the clock and Show Desktop, sorted.</param>
public sealed record TrayFacts(int TrayLeft, int? ButtonsRight, int? QuickSettingsLeft, int? ClockLeft, int[] ItemLefts, bool Mirrored);

public static class TrayAnalysis
{
    /// <param name="tray">Notification-area buttons (class names starting "SystemTray.") in screen pixels.</param>
    /// <param name="others">Every other taskbar button (apps, Start, Search, widgets).</param>
    public static TrayFacts? Analyze(IEnumerable<(int L, int R, string Cls)> tray, IEnumerable<(int L, int R)> others,
        int taskbarLeft, int taskbarRight)
    {
        var t = tray.OrderBy(x => x.L).ToList();
        var o = others.ToList();

        // Mirrored taskbar: flip everything so the notification area is on the right again.
        bool mirrored = TrayClasses.IsMirrored(t.ConvertAll(x => x.Cls));
        if (mirrored)
        {
            int sum = taskbarLeft + taskbarRight;
            t = t.Select(x => (sum - x.R, sum - x.L, x.Cls)).OrderBy(x => x.Item1).ToList();
            o = o.ConvertAll(x => (sum - x.R, sum - x.L));
        }

        int trayLeft = int.MaxValue, buttonsRight = int.MinValue;
        foreach (var x in t) if (x.Cls != TrayClasses.ShowDesktop) trayLeft = Math.Min(trayLeft, x.L);
        foreach (var x in o) buttonsRight = Math.Max(buttonsRight, x.R);
        if (trayLeft == int.MaxValue) return null;

        // Quick Settings group (network + volume + battery): volume is the "OmniButtonCenter";
        // the network button directly before it belongs to the same group.
        var classes = t.ConvertAll(x => x.Cls);
        int? qsLeft = null;
        int vol = TrayClasses.VolumeIndex(classes);
        if (vol >= 0)
        {
            qsLeft = t[vol].L;
            if (vol > 0 && t[vol - 1].Cls == "SystemTray.AccentButton" && t[vol - 1].R >= t[vol].L - 8) qsLeft = t[vol - 1].L;
        }
        else if (TrayClasses.QuickSettingsStart(classes) is int qs and >= 0)
            qsLeft = t[qs].L;   // volume button renamed by a Windows update: use the group before the clock

        int clock = TrayClasses.ClockIndex(classes);
        int? clockLeft = clock >= 0 ? t[clock].L : null;
        var lefts = t.Where((x, i) => i != clock && x.Cls != TrayClasses.ShowDesktop).Select(x => x.L).Distinct().ToArray();
        return new TrayFacts(trayLeft, buttonsRight == int.MinValue || buttonsRight > trayLeft ? null : buttonsRight,
            qsLeft, clockLeft, lefts, mirrored);
    }

    /// <summary>
    /// Index of the battery button among tray buttons in screen order, or −1: the button right
    /// after the volume button (before the clock), read in logical order on a mirrored taskbar.
    /// </summary>
    public static int BatteryIndex(IReadOnlyList<string> classesInScreenOrder)
    {
        var classes = classesInScreenOrder.ToList();
        bool mirrored = TrayClasses.IsMirrored(classes);
        if (mirrored) classes.Reverse();
        int volume = TrayClasses.VolumeIndex(classes), clock = TrayClasses.ClockIndex(classes);
        if (volume < 0 || volume + 1 >= classes.Count || (clock >= 0 && volume + 1 >= clock)) return -1;
        int logical = volume + 1;
        return mirrored ? classes.Count - 1 - logical : logical;
    }
}
