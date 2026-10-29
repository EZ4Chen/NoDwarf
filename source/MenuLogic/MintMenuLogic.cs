// Adapted from the user's Dubs Mint Menus 1.7 assembly (Dubwise).
// Original methods: DubUtils.SearchMatch, MainTabWindow_MintArchitect.parc/Ordered/CacheDesPanels,
// Patch_BillStack_DoListing.parc. See ThirdParty/DubsMintMenus/NOTICE.txt.
namespace NoDwarf.MenuLogic;

public sealed record MintEntry(string Id,string Label,string Category,string Source,
    IReadOnlyList<string> Materials,IReadOnlyList<string> Tags,bool Visible=true);

public static class MintMenuLogic
{
    public static bool SearchMatch(string search,string? text) =>
        !string.IsNullOrEmpty(text)&&text.Contains(search,StringComparison.OrdinalIgnoreCase);

    // Each field is matched separately, as in the mod's parc methods; no accidental matches
    // across concatenated field boundaries. The adapter supplies game-specific definitions.
    public static bool Matches(MintEntry entry,string search)
    {
        if(!entry.Visible)return false;
        if(string.IsNullOrEmpty(search))return true;
        return SearchMatch(search,entry.Label)||SearchMatch(search,entry.Category)||SearchMatch(search,entry.Source)
            ||entry.Materials.Any(m=>SearchMatch(search,m))||entry.Tags.Any(t=>SearchMatch(search,t));
    }

    // Port of Ordered: stable numeric ordering, followed by entries without the requested stat.
    public static IEnumerable<T> Ordered<T>(IEnumerable<T> entries,Func<T,double?> value,bool ascending)
    {
        var rows=entries.Select(entry=>(Entry:entry,Value:value(entry))).ToArray();
        var measured=rows.Where(row=>row.Value.HasValue);
        var ordered=ascending?measured.OrderBy(row=>row.Value!.Value):measured.OrderByDescending(row=>row.Value!.Value);
        return ordered.Concat(rows.Where(row=>!row.Value.HasValue)).Select(row=>row.Entry);
    }

    public static IEnumerable<T> Categories<T>(IEnumerable<T> categories,Func<T,bool> pinned)=>categories.OrderBy(c=>!pinned(c));
}
