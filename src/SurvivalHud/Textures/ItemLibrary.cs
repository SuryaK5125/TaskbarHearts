using System.IO.Compression;
using SurvivalHud.Core;
using SurvivalHud.Settings;

namespace SurvivalHud.Textures;

/// <summary>
/// Every item texture in the texture source (Minecraft JAR, resource pack or folder), for the
/// purely decorative custom hotbar. Items are identified by their texture name, e.g.
/// "diamond_sword". Without Minecraft only the app's own pixel-art items are available.
/// Nothing is cached here: callers keep just the sprites they show.
/// </summary>
public static class ItemLibrary
{
    /// <summary>The app's own pixel art, used when there is no Minecraft install.</summary>
    private static readonly (string Id, Func<Sprite> Art)[] BuiltIn =
    {
        ("sugar", FallbackSprites.Sugar), ("firework_rocket", FallbackSprites.FireworkRocket),
        ("diamond_pickaxe", FallbackSprites.DiamondPickaxe), ("clock", FallbackSprites.Clock),
        ("elytra", FallbackSprites.Elytra), ("redstone", FallbackSprites.Redstone),
        ("bundle", FallbackSprites.Bundle), ("flint_and_steel", FallbackSprites.FlintAndSteel),
        ("music_disc_cat", FallbackSprites.MusicDisc),
    };

    public static bool IsItemTexture(string id) => ItemNames.IsItemTexture(id);
    public static string Pretty(string id) => ItemNames.Pretty(id);

    /// <summary>All item ids in the source, sorted by name; the built-in items when there is none.</summary>
    public static List<string> List(string? source)
    {
        var names = new List<string>();
        if (source != null)
        {
            try { names.AddRange(EnumerateItemFiles(source).Select(n => n[..^4])); }
            catch (Exception ex) { Log.Write("Listing items failed: " + ex.Message); }
        }
        var ids = ItemNames.Filter(names);
        return ids.Count > 0 ? ids : BuiltIn.Select(b => b.Id).ToList();
    }

    /// <summary>
    /// Loads the given items (opening the JAR once). Missing items fall back to the built-in art
    /// when there is one with the same name; otherwise they are left out of the result.
    /// </summary>
    public static Dictionary<string, Sprite> Load(string? source, IEnumerable<string> ids)
    {
        var result = new Dictionary<string, Sprite>();
        var wanted = ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
        if (source != null && wanted.Count > 0)
        {
            try { LoadFromSource(source, wanted, result); }
            catch (Exception ex) { Log.Write("Loading items failed: " + ex.Message); }
        }
        foreach (string id in wanted)
        {
            if (result.ContainsKey(id)) continue;
            string bare = id.EndsWith("_00") ? id[..^3] : id;
            foreach (var b in BuiltIn)
                if (b.Id == bare) { result[id] = b.Art(); break; }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateItemFiles(string source)
    {
        if (Directory.Exists(source))
        {
            foreach (string folder in MinecraftTextureLoader.ItemFolders)
            {
                string dir = Path.Combine(source, folder.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                var files = Directory.EnumerateFiles(dir, "*.png").Select(Path.GetFileName).OfType<string>().ToList();
                if (files.Count > 0) return files;
            }
            return Array.Empty<string>();
        }
        if (!File.Exists(source)) return Array.Empty<string>();
        using var zip = ZipFile.OpenRead(source);
        foreach (string folder in MinecraftTextureLoader.ItemFolders)
        {
            var files = zip.Entries
                .Where(e => e.FullName.StartsWith(folder, StringComparison.Ordinal) && e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.IndexOf('/', folder.Length) < 0)
                .Select(e => e.Name).ToList();
            if (files.Count > 0) return files;
        }
        return Array.Empty<string>();
    }

    private static void LoadFromSource(string source, List<string> ids, Dictionary<string, Sprite> into)
    {
        if (Directory.Exists(source))
        {
            foreach (string id in ids)
                foreach (string folder in MinecraftTextureLoader.ItemFolders)
                {
                    string file = Path.Combine(source, folder.Replace('/', Path.DirectorySeparatorChar), id + ".png");
                    if (File.Exists(file) && new FileInfo(file).Length < 1024 * 1024
                        && Square(MinecraftTextureLoader.Decode(File.ReadAllBytes(file))) is { } s) { into[id] = s; break; }
                }
            return;
        }
        if (!File.Exists(source)) return;
        using var zip = ZipFile.OpenRead(source);
        foreach (string id in ids)
            foreach (string folder in MinecraftTextureLoader.ItemFolders)
            {
                var entry = zip.GetEntry(folder + id + ".png");
                if (entry == null || entry.Length > 1024 * 1024) continue;
                using var st = entry.Open();
                using var ms = new MemoryStream();
                st.CopyTo(ms);
                if (Square(MinecraftTextureLoader.Decode(ms.ToArray())) is { } s) { into[id] = s; break; }
            }
    }

    /// <summary>Animated textures are vertical strips of frames; keep the first square frame.</summary>
    private static Sprite? Square(Sprite? s) =>
        s is null || s.Width > 256 ? null : s.Height > s.Width ? s.Crop(0, 0, s.Width, s.Width) : s;
}
