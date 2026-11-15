using SurvivalHud.Core;
using System.Text.Json;
using Microsoft.Win32;

namespace SurvivalHud.Settings;

/// <summary>User preferences, stored as JSON in %LOCALAPPDATA%\TaskbarHearts\settings.json.</summary>
public sealed class AppSettings
{
    public const string ModeOverlay = "overlay", ModeTray = "tray";
    public const string TexturesAuto = "auto", TexturesBuiltIn = "builtin";
    public const string LayoutCompact = "compact", LayoutBars = "bars", LayoutBarsSlot = "barsslot";
    public const string PopupList = "list", PopupGame = "game";
    public const string RowsArmorHearts = "armor-hearts", RowsHearts = "hearts",
        RowsHeartsHunger = "hearts-hunger", RowsArmorHunger = "armor-hunger";

    // Appearance
    public double CompactScale { get; set; } = 2;      // logical pixels per sprite pixel
    public double IconSpacing { get; set; } = 2;       // logical pixels between compact icons
    public double HudScale { get; set; } = 2;
    public string TextureSource { get; set; } = TexturesAuto; // "auto", "builtin", or a .jar/.zip/folder path
    public bool ChargingIndicator { get; set; } = true;
    public bool LightPopup { get; set; }

    /// <summary>Popup follows the Windows light/dark setting (default). When off, <see cref="LightPopup"/> decides.</summary>
    public bool PopupFollowsTheme { get; set; } = true;
    public bool CoverNativeBattery { get; set; } = true;

    // Behaviour
    public int HoverDelayMs { get; set; } = 200;
    public int CloseDelayMs { get; set; } = 100;
    public bool PinEnabled { get; set; } = true;
    public bool Pinned { get; set; }

    /// <summary>Whether the three HUD icons are shown at all (toggled from the app's tray icon).</summary>
    public bool HudVisible { get; set; } = true;

    // Taskbar
    public string Mode { get; set; } = ModeOverlay;
    public string TargetDisplay { get; set; } = "";    // monitor device name; empty = primary

    /// <summary>Offset (logical px) from the detected native battery slot.</summary>
    public double SlotOffsetX { get; set; }
    public double SlotOffsetY { get; set; }

    /// <summary>Used when the slot cannot be detected: offset (logical px) of the overlay's right edge from the taskbar's right edge.</summary>
    public double EdgeOffsetX { get; set; } = -88;
    public double EdgeOffsetY { get; set; }

    /// <summary>Overlay layout: "compact" (heart + hunger + armor) or "bars" (10 hearts over 10 armor, always visible).</summary>
    public string HudLayout { get; set; } = LayoutCompact;

    /// <summary>Hover popup style: "list" (labelled rows with exact numbers) or "game" (laid out like Minecraft's HUD, with a hotbar).</summary>
    public string PopupStyle { get; set; } = PopupList;

    /// <summary>Which bars the Survival Bars layouts show (top row first). Default: armor over hearts, as in the game.</summary>
    public string BarsRows { get; set; } = RowsArmorHearts;

    /// <summary>Which reading each bar shows: "battery", "cpu", "ram" or "gpu".</summary>
    public string HeartsStat { get; set; } = "battery";
    public string HungerStat { get; set; } = "cpu";
    public string ArmorStat { get; set; } = "ram";
    public string XpStat { get; set; } = "gpu";

    [System.Text.Json.Serialization.JsonIgnore]
    public BarMap Map => new(BarMap.Parse(HeartsStat, Stat.Battery), BarMap.Parse(HungerStat, Stat.Cpu),
        BarMap.Parse(ArmorStat, Stat.Ram), BarMap.Parse(XpStat, Stat.Gpu));

    /// <summary>Minecraft-style warnings: hearts shake at low battery, hunger jitters when the CPU is maxed out.</summary>
    public bool Animations { get; set; } = true;

    /// <summary>Show the items below in the game-style popup's hotbar instead of PC readings (purely decorative).</summary>
    public bool CustomHotbar { get; set; }

    /// <summary>Item texture names for the custom hotbar's slots, left to right, at most nine; "" is an empty slot.</summary>
    public List<string> HotbarItems { get; set; } = new();

    /// <summary>Offset (logical px) of the Survival Bars from their default place left of the notification area.</summary>
    public double BarsOffsetX { get; set; }
    public double BarsOffsetY { get; set; }

    // ---- persistence --------------------------------------------------------------------
    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarHearts");

    /// <summary>Folder used before the app was renamed; its settings are copied over once.</summary>
    private static string OldDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinecraftSurvivalHud");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            // First start after the rename: bring the old settings along.
            string old = Path.Combine(OldDirectory, "settings.json");
            if (!File.Exists(FilePath) && File.Exists(old))
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.Copy(old, FilePath);
            }
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                if (s != null) { s.Sanitize(); return s; }
            }
        }
        catch (Exception ex) { Log.Write("Could not read settings, using defaults: " + ex.Message); }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Sanitize();
            System.IO.Directory.CreateDirectory(Directory);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) { Log.Write("Could not save settings: " + ex.Message); }
    }

    public void Sanitize()
    {
        static double C(double v, double lo, double hi, double def) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;
        CompactScale = C(CompactScale, 1, 4, 2);
        IconSpacing = C(IconSpacing, 0, 12, 2);
        HudScale = C(HudScale, 1, 5, 2);
        HoverDelayMs = Math.Clamp(HoverDelayMs, 0, 2000);
        HotbarItems = HotbarLayout.ToList(HotbarLayout.FromList(HotbarItems));
        CloseDelayMs = Math.Clamp(CloseDelayMs, 0, 5000);
        SlotOffsetX = C(SlotOffsetX, -4000, 4000, 0);
        SlotOffsetY = C(SlotOffsetY, -200, 200, 0);
        EdgeOffsetX = C(EdgeOffsetX, -8000, 0, -88);
        EdgeOffsetY = C(EdgeOffsetY, -200, 200, 0);
        if (Mode != ModeTray) Mode = ModeOverlay;
        if (HudLayout != LayoutBars && HudLayout != LayoutBarsSlot) HudLayout = LayoutCompact;
        if (PopupStyle != PopupGame) PopupStyle = PopupList;
        if (BarsRows is not (RowsArmorHearts or RowsHearts or RowsHeartsHunger or RowsArmorHunger)) BarsRows = RowsArmorHearts;
        var map = Map;
        (HeartsStat, HungerStat, ArmorStat, XpStat) = (BarMap.Key(map.Hearts), BarMap.Key(map.Hunger), BarMap.Key(map.Armor), BarMap.Key(map.Xp));
        BarsOffsetX = C(BarsOffsetX, -8000, 8000, 0);
        BarsOffsetY = C(BarsOffsetY, -200, 200, 0);
        TextureSource = string.IsNullOrWhiteSpace(TextureSource) ? TexturesAuto : TextureSource;
        TargetDisplay ??= "";
    }
}

/// <summary>"Start with Windows" through the per-user Run key. No administrator rights needed.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarHearts", OldValueName = "MinecraftSurvivalHud";

    /// <summary>After the rename: an old "start with Windows" entry is replaced by one for this exe.</summary>
    public static void MigrateFromOldName()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (key.GetValue(OldValueName) is string)
            {
                key.DeleteValue(OldValueName, throwOnMissingValue: false);
                key.SetValue(ValueName, Command);
            }
            // An entry for this exe without the background argument: add it, so sign-in stays quiet.
            else if (key.GetValue(ValueName) is string v && IsEnabled && v != Command) key.SetValue(ValueName, Command);
        }
        catch (Exception ex) { Log.Write("Could not migrate the startup entry: " + ex.Message); }
    }

    /// <summary>Argument used by the startup entry: start in the tray without opening the window.</summary>
    public const string BackgroundArg = "--background";

    private static string Command => $"\"{Environment.ProcessPath}\" {BackgroundArg}";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                // Entries from older versions have no argument; they still count (and are rewritten on start).
                return key?.GetValue(ValueName) is string v
                    && v.StartsWith($"\"{Environment.ProcessPath}\"", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Write("Could not change the startup entry: " + ex.Message); }
    }
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(AppSettings.Directory);
                string path = Path.Combine(AppSettings.Directory, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never take the app down */ }
    }
}
