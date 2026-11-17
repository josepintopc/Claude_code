using CommunityToolkit.Mvvm.ComponentModel;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Base dos view models: ao trocar de idioma, reavalia todos os textos ligados a este objeto.</summary>
public abstract class LocalizedViewModel : ObservableObject
{
    protected LocalizedViewModel()
    {
        Loc.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    protected virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);
}
