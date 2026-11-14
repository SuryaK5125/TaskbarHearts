using System.IO.Compression;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurvivalHud.Core;
using SurvivalHud.Settings;

namespace SurvivalHud.Textures;

public sealed record InstalledVersion(string Name, string JarPath, DateTime ReleaseTime);

/// <summary>
/// Loads the genuine HUD sprites from a Minecraft Java Edition JAR (or a resource pack /
/// folder) that is already on this PC. Nothing is copied or redistributed: the textures are
/// decoded into memory each time the app starts. Three layouts are understood:
///   • 1.20.2+  : assets/minecraft/textures/gui/sprites/hud/…  (one PNG per sprite)
///   • 1.6–1.20.1: assets/minecraft/textures/gui/icons.png      (256×256 atlas)
///   • ≤ 1.5    : gui/icons.png                                 (same atlas, older path)
/// </summary>
public static class MinecraftTextureLoader
{
    private static readonly string[] SpritePrefixes =
    {
        "assets/minecraft/textures/gui/sprites/hud/", "textures/gui/sprites/hud/", "gui/sprites/hud/", "sprites/hud/", "hud/", "",
    };

    private static readonly string[] AtlasPaths =
    {
        "assets/minecraft/textures/gui/icons.png", "textures/gui/icons.png", "gui/icons.png", "icons.png",
    };

    public static string VersionsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "versions");

    /// <summary>Installed versions that have a JAR, newest release first.</summary>
    public static List<InstalledVersion> FindInstalledVersions()
    {
        var list = new List<InstalledVersion>();
        try
        {
            if (!Directory.Exists(VersionsDirectory)) return list;
            foreach (string dir in Directory.EnumerateDirectories(VersionsDirectory))
            {
                string name = Path.GetFileName(dir);
                string jar = Path.Combine(dir, name + ".jar");
                if (!File.Exists(jar)) continue;
                list.Add(new InstalledVersion(name, jar, ReadReleaseTime(Path.Combine(dir, name + ".json"), jar)));
            }
        }
        catch (Exception ex) { Log.Write("Scanning Minecraft versions failed: " + ex.Message); }
        list.Sort((a, b) => b.ReleaseTime.CompareTo(a.ReleaseTime));
        return list;
    }

    private static DateTime ReadReleaseTime(string jsonPath, string jarPath, int depth = 0)
    {
        try
        {
            if (File.Exists(jsonPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                // Mod loaders (OptiFine, Forge, Fabric) write their install date as releaseTime;
                // rank them by the game version they are built on instead.
                if (doc.RootElement.TryGetProperty("inheritsFrom", out var parent) && parent.GetString() is { Length: > 0 } baseName
                    && baseName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                {
                    string baseJson = Path.Combine(VersionsDirectory, baseName, baseName + ".json");
                    if (depth < 3 && File.Exists(baseJson)) return ReadReleaseTime(baseJson, jarPath, depth + 1);
                }
                if (doc.RootElement.TryGetProperty("releaseTime", out var t) && t.TryGetDateTime(out var dt))
                    return dt.ToUniversalTime();
            }
        }
        catch { /* fall through to the file date */ }
        return File.GetLastWriteTimeUtc(jarPath);
    }

    /// <summary>
    /// Resolves the configured source to a sprite set, falling back to the built-in art.
    /// <paramref name="note"/> explains a fallback so the settings window can show it.
    /// </summary>
    public static SpriteSet Resolve(string source, out string? note)
    {
        note = null;
        if (source == AppSettings.TexturesBuiltIn) return FallbackSprites.Create();

        if (source == AppSettings.TexturesAuto)
        {
            foreach (var v in FindInstalledVersions())
            {
                var set = TryLoad(v.JarPath, "Minecraft " + v.Name, out _);
                if (set != null) return set;
            }
            note = "No usable Minecraft Java Edition installation was found; using the built-in pixel art.";
            return FallbackSprites.Create();
        }

        string label = Directory.Exists(source)
            ? "Folder " + Path.GetFileName(source.TrimEnd('\\', '/'))
            : Path.GetFileName(source);
        var custom = TryLoad(source, label, out string? error);
        if (custom != null) return custom;
        note = $"{error} Using the built-in pixel art.";
        return FallbackSprites.Create();
    }

    /// <summary>Loads from a .jar/.zip file or a folder. Returns null (with a reason) when the sprites are not there.</summary>
    public static SpriteSet? TryLoad(string path, string label, out string? error)
    {
        error = null;
        try
        {
            if (Directory.Exists(path))
            {
                return WithSource(Build(rel =>
                {
                    string full = Path.Combine(path, rel.Replace('/', Path.DirectorySeparatorChar));
                    return File.Exists(full) ? File.ReadAllBytes(full) : null;
                }, label, SpritePrefixes, AtlasPaths, out error), path);
            }

            if (!File.Exists(path)) { error = "The texture source no longer exists."; return null; }

            using var zip = ZipFile.OpenRead(path);
            return WithSource(Build(rel =>
            {
                var entry = zip.GetEntry(rel);
                if (entry == null || entry.Length > 8 * 1024 * 1024) return null;
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }, label, SpritePrefixes[..1], AtlasPaths, out error), path);
        }
        catch (Exception ex)
        {
            error = "Could not read the texture source (" + ex.Message + ").";
            return null;
        }
    }

    private static SpriteSet? WithSource(SpriteSet? set, string path)
    {
        if (set != null) set.ItemSource = path;
        return set;
    }

    private static SpriteSet? Build(Func<string, byte[]?> open, string label, string[] spritePrefixes, string[] atlasPaths, out string? error)
    {
        error = null;
        Sprite? Png(string rel) => open(rel) is byte[] data ? Decode(data) : null;

        // Modern layout: individual sprites.
        foreach (string p in spritePrefixes)
        {
            Sprite? hc = Png(p + "heart/container.png"), hf = Png(p + "heart/full.png");
            if (hc == null || hf == null) continue;
            Sprite? fe = Png(p + "food_empty.png"), ff = Png(p + "food_full.png");
            Sprite? ae = Png(p + "armor_empty.png"), af = Png(p + "armor_full.png");
            if (fe == null || ff == null || ae == null || af == null) continue;
            return new SpriteSet(
                new StatSprites(hc, hf, Png(p + "heart/half.png")),
                new StatSprites(fe, ff, Png(p + "food_half.png")),
                new StatSprites(ae, af, Png(p + "armor_half.png")),
                label)
            {
                Game = LoadGame(Png, Png(p + "hotbar.png")),
                Font = LoadFont(Png),
                XpBackground = Png(p + "experience_bar_background.png"),
                XpProgress = Png(p + "experience_bar_progress.png"),
            };
        }

        // Legacy layout: 256×256 atlas (resource packs may scale it up by a whole factor).
        foreach (string p in atlasPaths)
        {
            Sprite? atlas = Png(p);
            if (atlas == null || atlas.Width < 256 || atlas.Width % 256 != 0 || atlas.Height < atlas.Width / 256 * 36) continue;
            int k = atlas.Width / 256;
            Sprite Cut(int x, int y) => atlas.Crop(x * k, y * k, 9 * k, 9 * k);
            var set = new SpriteSet(
                new StatSprites(Cut(16, 0), Cut(52, 0), Cut(61, 0)),
                new StatSprites(Cut(16, 27), Cut(52, 27), Cut(61, 27)),
                new StatSprites(Cut(16, 9), Cut(34, 9), Cut(25, 9)),
                label)
            {
                Game = LoadGame(Png, WidgetsHotbar(Png)),
                Font = LoadFont(Png),
                XpBackground = atlas.Crop(0, 64 * k, 182 * k, 5 * k),
                XpProgress = atlas.Crop(0, 69 * k, 182 * k, 5 * k),
            };
            if (set.Heart.Full.IsEmpty || set.Food.Full.IsEmpty || set.Armor.Full.IsEmpty) continue;
            return set;
        }

        error = "No Minecraft HUD sprites were found in that location.";
        return null;
    }

    internal static readonly string[] ItemFolders =
    {
        "assets/minecraft/textures/item/", "textures/item/", "item/", "assets/minecraft/textures/items/", "textures/items/",
    };

    /// <summary>Hotbar plus the three item icons used by the game-style popup.</summary>
    private static GameSprites LoadGame(Func<string, Sprite?> png, Sprite? hotbar)
    {
        Sprite? Item(string name)
        {
            foreach (string f in ItemFolders)
                if (png(f + name + ".png") is { } s) return s;
            return null;
        }
        return new GameSprites(hotbar, Item("sugar"), Item("firework_rocket"), Item("diamond_pickaxe"),
            Item("clock_00"), Item("elytra"), Item("redstone"),
            Item("bundle"), Item("flint_and_steel"), Item("music_disc_cat"));
    }

    /// <summary>The game's ASCII font: textures/font/ascii.png since 1.6, font/default.png before.</summary>
    private static Sprite? LoadFont(Func<string, Sprite?> png) =>
        png("assets/minecraft/textures/font/ascii.png") ?? png("textures/font/ascii.png") ?? png("font/default.png");

    /// <summary>Pre-1.20.2 layout: the hotbar is the top-left 182×22 of gui/widgets.png (scaled by whole factors in packs).</summary>
    private static Sprite? WidgetsHotbar(Func<string, Sprite?> png)
    {
        foreach (string p in new[] { "assets/minecraft/textures/gui/widgets.png", "textures/gui/widgets.png", "gui/widgets.png" })
            if (png(p) is { } w && w.Width >= 256 && w.Width % 256 == 0)
            {
                int k = w.Width / 256;
                return w.Crop(0, 0, 182 * k, 22 * k);
            }
        return null;
    }

    internal static Sprite? Decode(byte[] png)
    {
        try
        {
            using var ms = new MemoryStream(png);
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (frame.PixelWidth is < 1 or > 4096 || frame.PixelHeight is < 1 or > 4096) return null;
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var px = new uint[bgra.PixelWidth * bgra.PixelHeight];
            bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
            return new Sprite(bgra.PixelWidth, bgra.PixelHeight, px);
        }
        catch { return null; }
    }
}
