using CommunityToolkit.Mvvm.ComponentModel;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Item do menu lateral.</summary>
public sealed partial class NavItem : LocalizedViewModel
{
    private readonly Action<NavItem> _onSelected;

    public NavItem(string key, string titleKey, string glyph, int phase, Action<NavItem> onSelected)
    {
        Key = key;
        TitleKey = titleKey;
        Glyph = glyph;
        Phase = phase;
        _onSelected = onSelected;
    }

    public string Key { get; }
    public string TitleKey { get; }
    public string Glyph { get; }

    /// <summary>Fase em que o módulo é entregue (1 = já disponível).</summary>
    public int Phase { get; }
    public bool IsSoon => Phase > 1;
    public string Title => Loc.T(TitleKey);

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(this);
    }
}

/// <summary>Grupo do menu lateral (SISTEMA, BENCHMARK...).</summary>
public sealed class NavGroup : LocalizedViewModel
{
    public NavGroup(string titleKey, IReadOnlyList<NavItem> items)
    {
        TitleKey = titleKey;
        Items = items;
    }

    public string TitleKey { get; }
    public string Title => Loc.T(TitleKey);
    public IReadOnlyList<NavItem> Items { get; }
}
