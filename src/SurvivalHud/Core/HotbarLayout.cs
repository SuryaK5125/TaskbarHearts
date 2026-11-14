namespace SurvivalHud.Core;

/// <summary>
/// The custom hotbar's nine slots, where an empty slot is "". Items stay exactly where they are
/// put; gaps are allowed and never closed up automatically.
/// </summary>
public static class HotbarLayout
{
    public const int Slots = 9;

    /// <summary>Nine slots from saved settings: missing or blank entries become empty slots.</summary>
    public static string[] FromList(IEnumerable<string?>? saved)
    {
        var slots = Enumerable.Repeat("", Slots).ToArray();
        int i = 0;
        foreach (string? id in saved ?? Array.Empty<string?>())
        {
            if (i >= Slots) break;
            slots[i++] = string.IsNullOrWhiteSpace(id) || id.Length >= 100 ? "" : id;
        }
        return slots;
    }

    /// <summary>For saving: the slots with trailing empty ones dropped (gaps in between are kept).</summary>
    public static List<string> ToList(string[] slots)
    {
        int n = slots.Length;
        while (n > 0 && slots[n - 1].Length == 0) n--;
        return slots.Take(n).ToList();
    }

    /// <summary>A slot entry is "id" for one item or "id*count" for a stack, e.g. "ender_pearl*16".</summary>
    public static (string Id, int Count) Parse(string entry)
    {
        int star = entry.LastIndexOf('*');
        if (star > 0 && int.TryParse(entry[(star + 1)..], out int n) && n is > 1 and <= 64) return (entry[..star], n);
        return (star > 0 ? entry[..star] : entry, 1);
    }

    public static string Format(string id, int count) => count > 1 ? $"{id}*{count}" : id;

    /// <summary>
    /// Middle-click, like picking a full stack in creative mode: one item becomes a full stack,
    /// a full stack goes back to one. Items that don't stack (swords, potions…) stay as they are.
    /// Returns false when nothing changed.
    /// </summary>
    public static bool ToggleStack(string[] slots, int slot)
    {
        if (slot < 0 || slot >= slots.Length || slots[slot].Length == 0) return false;
        var (id, count) = Parse(slots[slot]);
        int max = ItemNames.MaxStack(id);
        if (max <= 1) return false;
        slots[slot] = Format(id, count >= max ? 1 : max);
        return true;
    }

    /// <summary>The leftmost empty slot, or −1 when the hotbar is full.</summary>
    public static int FirstFree(string[] slots) => Array.IndexOf(slots, "");

    /// <summary>
    /// Puts an item in a slot. An empty slot just takes it. An occupied slot keeps its item by
    /// sliding it, and its neighbours, one step towards the nearest gap (right first, then left),
    /// so items beyond that gap don't move. When the hotbar is full the item there is replaced.
    /// </summary>
    public static void Place(string[] slots, int target, string id)
    {
        if (target < 0 || target >= slots.Length) return;
        if (slots[target].Length > 0)
        {
            int gap = Array.IndexOf(slots, "", target);
            if (gap > target)
                for (int i = gap; i > target; i--) slots[i] = slots[i - 1];
            else if (Array.LastIndexOf(slots, "", target) is int left and >= 0)
                for (int i = left; i < target; i++) slots[i] = slots[i + 1];
        }
        slots[target] = id;
    }

    /// <summary>Moves the item in one slot to another (the slot it came from is left empty).</summary>
    public static void Move(string[] slots, int from, int to)
    {
        if (from < 0 || from >= slots.Length || slots[from].Length == 0 || from == to) return;
        string id = slots[from];
        slots[from] = "";
        Place(slots, to, id);
    }
}
