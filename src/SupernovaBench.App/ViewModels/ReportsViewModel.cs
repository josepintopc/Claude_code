using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Reports;

namespace SupernovaBench.App.ViewModels;

public sealed record HistoryRow(Guid Id, string Test, string Score, string Cpu, string Detail, bool IsImported);

/// <summary>Exportar relatório (HTML/JSON), importar resultados de outro PC e gerenciar o histórico.</summary>
public sealed partial class ReportsViewModel : LocalizedViewModel
{
    public ReportsViewModel()
    {
        AppServices.History.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshHistory);
        RefreshHistory();
    }

    public ObservableCollection<HistoryRow> History { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportHtmlCommand), nameof(ExportJsonCommand), nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _lastSavedPath;
    [ObservableProperty] private bool _isEmpty;

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task ExportHtml() => ExportAsync("html");

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task ExportJson() => ExportAsync("json");

    private async Task ExportAsync(string kind)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"supernova-bench-{DateTime.Now:yyyyMMdd-HHmm}.{kind}",
            Filter = kind == "html" ? $"{Loc.T("rep.filter.html")} (*.html)|*.html" : $"{Loc.T("rep.filter.json")} (*.json)|*.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        StatusText = Loc.T("rep.generating");
        try
        {
            var report = await BuildReportAsync();
            string content = kind == "html" ? ReportBuilder.ToHtml(report) : ReportBuilder.ToJson(report);
            await File.WriteAllTextAsync(dialog.FileName, content);
            LastSavedPath = dialog.FileName;
            StatusText = Loc.T("rep.saved", dialog.FileName);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = ex.Message;
            LastSavedPath = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<SystemReport> BuildReportAsync()
    {
        var sections = await AppServices.SystemInfo.GetSectionsAsync();
        var sensors = AppServices.Sensors.Monitor.Rows
            .Where(r => r.RawValue != null)
            .OrderBy(r => r.Hardware).ThenBy(r => r.Order).ThenBy(r => r.Name)
            .Select(r => new SensorSnapshot(r.Hardware, r.Category, r.Name, r.Value, r.Min, r.Max))
            .ToList();
        return new SystemReport
        {
            Language = Loc.Current,
            CpuName = MachineInfo.CpuName(),
            Sections = sections,
            Results = AppServices.History.All.ToList(),
            Sensors = sensors,
        };
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = $"{Loc.T("rep.filter.json")} (*.json)|*.json", Multiselect = true };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        int total = 0;
        try
        {
            foreach (var file in dialog.FileNames)
            {
                var json = await File.ReadAllTextAsync(file);
                var results = ReportBuilder.ParseResultsForImport(json);
                total += AppServices.History.AddRange(results);
            }
            StatusText = Loc.T("rep.imported", total);
        }
        catch (InvalidDataException)
        {
            StatusText = Loc.T("rep.importFailed");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            LastSavedPath = null;
        }
    }

    [RelayCommand]
    private void OpenLast()
    {
        if (LastSavedPath != null) Shell.Open(LastSavedPath);
    }

    [RelayCommand]
    private void Remove(HistoryRow? row)
    {
        if (row != null) AppServices.History.Remove(row.Id);
    }

    [RelayCommand]
    private void Clear()
    {
        if (MessageBox.Show(Loc.T("rep.clearConfirm"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            AppServices.History.Clear();
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var r in AppServices.History.All)
        {
            string detail = $"{ReportBuilder.Details(r, Loc.Culture)} · {r.Seconds.ToString("N1", Loc.Culture)} s · {r.Timestamp.ToString("g", Loc.Culture)}";
            if (r.Imported) detail = $"{Loc.T("cpu.ranking.imported")} · {detail}";
            History.Add(new HistoryRow(r.Id, Loc.T(TestIds.NameKey(r.Test)), $"{r.Score.ToString("N0", Loc.Culture)} {r.Unit}",
                                       r.DeviceName, detail, r.Imported));
        }
        IsEmpty = History.Count == 0;
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        RefreshHistory();
    }
}
