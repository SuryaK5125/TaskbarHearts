using System.Text.RegularExpressions;

namespace SurvivalHud.Core;

/// <summary>Which item textures make sense in the custom hotbar, and their readable names.</summary>
public static partial class ItemNames
{
    [GeneratedRegex(@"_(\d\d)$")] private static partial Regex FrameSuffix();

    /// <summary>
    /// False for textures that are not items you would hold on their own: tint overlays,
    /// bow/crossbow pull stages and the empty-slot outlines used by the smithing table.
    /// </summary>
    public static bool IsItemTexture(string id) =>
        id.Length > 0 && !id.Contains("_overlay") && !id.Contains("_pulling_") && !id.StartsWith("empty_");

    /// <summary>
    /// The listable items, sorted: <see cref="IsItemTexture"/>, and of animated items
    /// (clock_00…clock_63, compass_00…) only the first frame. "music_disc_11" stays, because
    /// there is no "music_disc_00".
    /// </summary>
    public static List<string> Filter(IEnumerable<string> ids)
    {
        var all = new HashSet<string>(ids.Where(IsItemTexture), StringComparer.Ordinal);
        return all.Where(id =>
        {
            var m = FrameSuffix().Match(id);
            return !m.Success || m.Groups[1].Value == "00" || !all.Contains(id[..m.Index] + "_00");
        }).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    /// <summary>"diamond_sword" → "Diamond Sword"; animation frame numbers are dropped ("clock_00" → "Clock").</summary>
    public static string Pretty(string id)
    {
        string s = id.EndsWith("_00") ? id[..^3] : id;
        return string.Join(' ', s.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => i > 0 && SmallWords.Contains(w) ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static readonly string[] SingleSuffixes =
    {
        "_sword", "_pickaxe", "_axe", "_shovel", "_hoe", "_helmet", "_chestplate", "_leggings", "_boots",
        "_horse_armor", "_boat", "_raft", "_minecart", "_bucket", "_stew", "_soup", "_bundle", "_banner_pattern",
        "_on_a_stick", "_harness", "_spear",
    };

    private static readonly HashSet<string> SingleItems = new(StringComparer.Ordinal)
    {
        "bow", "crossbow", "crossbow_standby", "trident", "shield", "elytra", "fishing_rod", "flint_and_steel", "shears",
        "brush", "mace", "spyglass", "totem_of_undying", "potion", "splash_potion", "lingering_potion", "saddle",
        "minecart", "enchanted_book", "written_book", "writable_book", "cake", "bundle", "wolf_armor", "debug_stick",
        "knowledge_book", "goat_horn", "turtle_helmet", "carrot_on_a_stick", "warped_fungus_on_a_stick",
    };

    private static readonly HashSet<string> SixteenItems = new(StringComparer.Ordinal)
    {
        "ender_pearl", "snowball", "egg", "blue_egg", "brown_egg", "bucket", "honey_bottle", "armor_stand",
    };

    /// <summary>
    /// How many fit in one stack in the game: 1 for tools, weapons, armour, potions, filled
    /// buckets, music discs and the like; 16 for ender pearls, snowballs, eggs, empty buckets,
    /// signs, honey bottles and armour stands; 64 for everything else.
    /// </summary>
    public static int MaxStack(string id)
    {
        string s = id.EndsWith("_00") ? id[..^3] : id;
        if (SingleItems.Contains(s) || s.StartsWith("music_disc_") || SingleSuffixes.Any(s.EndsWith)) return 1;
        if (SixteenItems.Contains(s) || s.EndsWith("_sign")) return 16;
        return 64;
    }

    /// <summary>Kept lower-case mid-name, like the game does ("Totem of Undying", "Banner Pattern on a Stick").</summary>
    private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal) { "of", "and", "the", "on", "a", "with", "in", "o" };
}
