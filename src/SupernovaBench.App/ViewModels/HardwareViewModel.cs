using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Info;

namespace SupernovaBench.App.ViewModels;

/// <summary>Página Hardware (estilo CPU-Z / AIDA64): seções em chips e cartões de rótulo/valor.</summary>
public sealed partial class HardwareViewModel : LocalizedViewModel
{
    public HardwareViewModel() => _ = LoadAsync(false);

    public ObservableCollection<InfoSection> Sections { get; } = new();

    [ObservableProperty] private InfoSection? _selectedSection;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showCopied;

    private async Task LoadAsync(bool refresh)
    {
        IsLoading = true;
        string? selectedId = SelectedSection?.Id;
        try
        {
            var sections = await AppServices.SystemInfo.GetSectionsAsync(refresh);
            Sections.Clear();
            foreach (var s in sections) Sections.Add(s);
            SelectedSection = Sections.FirstOrDefault(s => s.Id == selectedId) ?? Sections.FirstOrDefault();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            var err = new InfoSection { Id = "error", Title = SupernovaBench.Core.Localization.Loc.T("common.error") };
            err.Nodes.Add(new InfoNode { Label = ex.GetType().Name, Value = ex.Message });
            Sections.Clear();
            Sections.Add(err);
            SelectedSection = err;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync(true);

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (SelectedSection == null) return;
        try
        {
            Clipboard.SetText(SelectedSection.ToText(includeSensitive: false));
            ShowCopied = true;
            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            App.Log(ex); // a área de transferência pode estar ocupada por outro programa
        }
        finally
        {
            ShowCopied = false;
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        _ = LoadAsync(false); // os rótulos vêm traduzidos do coletor
    }
}
