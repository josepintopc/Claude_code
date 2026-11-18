using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Página dos módulos das próximas fases.</summary>
public sealed class ComingSoonViewModel : LocalizedViewModel
{
    private static readonly Dictionary<string, string> TextKey = new()
    {
        ["games"] = "soon.games",
        ["drivers"] = "soon.drivers",
    };

    public ComingSoonViewModel(NavItem item, IReadOnlyList<NavItem> allSoon)
    {
        Item = item;
        Roadmap = allSoon.OrderBy(i => i.Phase).ThenBy(i => i.Key)
                         .Select(i => new RoadmapEntry(i, TextKey.GetValueOrDefault(i.Key, "soon.extras")))
                         .Append(new RoadmapEntry(null, "soon.extras"))
                         .ToList();
    }

    public NavItem Item { get; }
    public string Glyph => Item.Glyph;
    public string Title => Loc.T(TextKey.GetValueOrDefault(Item.Key, "soon.extras") + ".title");
    public string Description => Loc.T(TextKey.GetValueOrDefault(Item.Key, "soon.extras") + ".desc");
    public string PhaseText => Loc.T("soon.phase", Item.Phase);
    public IReadOnlyList<RoadmapEntry> Roadmap { get; }
}

public sealed class RoadmapEntry : LocalizedViewModel
{
    private readonly NavItem? _item;
    private readonly string _key;

    public RoadmapEntry(NavItem? item, string key)
    {
        _item = item;
        _key = key;
    }

    public string Glyph => _item?.Glyph ?? "";
    public string Title => Loc.T(_key + ".title");
    public string PhaseText => Loc.T("soon.phase", _item?.Phase ?? 6);
}
