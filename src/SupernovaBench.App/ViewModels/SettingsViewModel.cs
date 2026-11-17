using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.App.ViewModels;

/// <summary>Opção de escolha única (idioma, tema, intervalo) ligada a um RadioButton.</summary>
public sealed partial class Option : LocalizedViewModel
{
    private readonly Action<Option> _onSelected;
    private readonly Func<string> _label;

    public Option(string value, Func<string> label, bool selected, Action<Option> onSelected)
    {
        Value = value;
        _label = label;
        _isSelected = selected;
        _onSelected = onSelected;
    }

    public string Value { get; }
    public string Label => _label();

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(this);
    }
}

public sealed partial class SettingsViewModel : LocalizedViewModel
{
    private readonly MainViewModel _main;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        var s = AppServices.Settings;
        Languages = Loc.Languages.Select(l => new Option(l.Code, () => l.NativeName, l.Code == Loc.Current, OnLanguage)).ToList();
        Themes = new[]
        {
            new Option(ThemeManager.Cosmic, () => Loc.T("set.theme.cosmic"), ThemeManager.Current == ThemeManager.Cosmic, OnTheme),
            new Option(ThemeManager.Light, () => Loc.T("set.theme.light"), ThemeManager.Current == ThemeManager.Light, OnTheme),
        };
        Intervals = new[] { 500, 1000, 2000 }
            .Select(ms => new Option(ms.ToString(), () => $"{ms / 1000.0:0.#} s", s.SensorIntervalMs == ms, OnInterval)).ToList();
        ThemeManager.ThemeChanged += (_, _) =>
        {
            foreach (var t in Themes) t.IsSelected = t.Value == ThemeManager.Current;
        };
    }

    public IReadOnlyList<Option> Languages { get; }
    public IReadOnlyList<Option> Themes { get; }
    public IReadOnlyList<Option> Intervals { get; }

    public string VersionText => Loc.T("set.version", AppInfo.Version);
    public string DataFolder => AppPaths.Root;
    public string RepositoryUrl => AppInfo.RepositoryUrl;

    private void OnLanguage(Option o)
    {
        if (Loc.Current == o.Value) return;
        AppServices.Settings.Language = o.Value;
        AppServices.TrySaveSettings();
        Loc.SetLanguage(o.Value);
    }

    private void OnTheme(Option o)
    {
        if (ThemeManager.Current == o.Value) return;
        ThemeManager.Apply(o.Value);
        AppServices.Settings.Theme = o.Value;
        AppServices.TrySaveSettings();
        _main.NotifyThemeChanged();
    }

    private void OnInterval(Option o)
    {
        int ms = int.Parse(o.Value);
        AppServices.Settings.SensorIntervalMs = ms;
        AppServices.Sensors.IntervalMs = ms;
        AppServices.TrySaveSettings();
    }

    [RelayCommand]
    private void OpenDataFolder() => Shell.Open(AppPaths.Root);

    [RelayCommand]
    private void OpenRepository() => Shell.Open(AppInfo.RepositoryUrl);
}
