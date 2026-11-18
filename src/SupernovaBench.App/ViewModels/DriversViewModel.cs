using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Drivers;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware.Drivers;

namespace SupernovaBench.App.ViewModels;

/// <summary>Uma placa de vídeo: driver instalado e, para NVIDIA, o mais recente.</summary>
public sealed partial class GpuDriverCard : ObservableObject
{
    public GpuDriverCard(DriverInfo driver)
    {
        Driver = driver;
        Vendor = DriverInfo.GpuVendor(driver.HardwareId) ?? "";
        InstalledVersion = GpuVendorTools.VendorVersion(driver) ?? driver.Version;
    }

    public DriverInfo Driver { get; }
    public string Name => Driver.DeviceName;
    public string Vendor { get; }
    public bool IsNvidia => Vendor == "NVIDIA";
    public bool IsOther => !IsNvidia;
    public string InstalledVersion { get; }
    public string InstalledText => Loc.T("drivers.gpu.installed", InstalledVersion, Driver.Date?.ToString("d", Loc.Culture) ?? "—");
    public string VendorButtonText => Vendor switch
    {
        "AMD" => Loc.T("drivers.gpu.amd"),
        "Intel" => Loc.T("drivers.gpu.intel"),
        _ => Loc.T("drivers.gpu.nvidiaSite"),
    };

    [ObservableProperty] private NvidiaDriver? _latest;
    [ObservableProperty] private string _latestText = "";
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private bool _isUpToDate;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadText = "";

    public void SetLatest(NvidiaDriver? latest, string? error)
    {
        Latest = latest;
        if (latest == null)
        {
            HasUpdate = IsUpToDate = false;
            LatestText = error ?? Loc.T("drivers.nvidia.notFound");
            return;
        }
        bool newer = DriverVersions.Compare(latest.Version, InstalledVersion) > 0;
        HasUpdate = newer;
        IsUpToDate = !newer;
        string date = latest.Date?.ToString("d", Loc.Culture) ?? "—";
        LatestText = newer
            ? Loc.T("drivers.nvidia.update", latest.Version, date, latest.Size ?? "—")
            : Loc.T("drivers.nvidia.upToDate", latest.Version, date);
    }

    public void Relocalize()
    {
        OnPropertyChanged(nameof(InstalledText));
        OnPropertyChanged(nameof(VendorButtonText));
        if (Latest != null) SetLatest(Latest, null);
    }
}

/// <summary>Uma atualização de driver do Windows Update, com caixa de seleção.</summary>
public sealed partial class WuItem : ObservableObject
{
    public WuItem(WuDriverUpdate update) => Update = update;

    public WuDriverUpdate Update { get; }
    public string Title => Update.Title;

    public string Detail => string.Join(" · ", new[]
    {
        Update.DriverClass, Update.Manufacturer, Update.DriverDate?.ToString("d", Loc.Culture),
        Update.SizeBytes > 0 ? Formats.Bytes(Update.SizeBytes) : null,
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _failed;
}

public sealed record ProblemItem(string Name, string Detail, bool IsDisconnected);

public sealed record DriverRow(string Name, string Category, string Provider, string Version, string Date, bool IsSigned);

public sealed record RestorePointRow(string Description, string Date);

/// <summary>Drivers: inventário, problemas, NVIDIA, Windows Update, ponto de restauração e backup.</summary>
public sealed partial class DriversViewModel : LocalizedViewModel
{
    private readonly WindowsUpdateDrivers _wu = new();
    private List<DriverInfo> _inventory = new();
    private List<ProblemDevice> _problems = new();
    private List<RestorePoint> _points = new();
    private CancellationTokenSource? _cts;
    private string _category = "all";
    private bool _studio;

    public DriversViewModel()
    {
        Branches = new[]
        {
            new Option("game", () => Loc.T("drivers.nvidia.gameReady"), true, o => OnBranch(false)),
            new Option("studio", () => Loc.T("drivers.nvidia.studio"), false, o => OnBranch(true)),
        };
        _ = LoadAsync();
    }

    public ObservableCollection<GpuDriverCard> Gpus { get; } = new();
    public ObservableCollection<WuItem> WuUpdates { get; } = new();
    public ObservableCollection<ProblemItem> Problems { get; } = new();
    public ObservableCollection<DriverRow> Drivers { get; } = new();
    public ObservableCollection<Option> Categories { get; } = new();
    public ObservableCollection<RestorePointRow> RestorePointRows { get; } = new();
    public IReadOnlyList<Option> Branches { get; }

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private bool _noProblems;
    [ObservableProperty] private bool _noRestorePoints;
    [ObservableProperty] private bool _noWuUpdates;
    [ObservableProperty] private bool _rebootRequired;
    [ObservableProperty] private string _wuStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand), nameof(InstallWindowsUpdatesCommand), nameof(CreateRestorePointCommand),
                                nameof(BackupCommand), nameof(InstallNvidiaCommand), nameof(CancelCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    partial void OnSearchChanged(string value) => RebuildDrivers();

    // ------------------------------------------------------------------ carga inicial

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var (inventory, problems, points) = await Task.Run(() => (DriverScanner.Inventory(), DriverScanner.ProblemDevices(), RestorePoints.List()));
            _inventory = inventory;
            _problems = problems;
            _points = points;

            Gpus.Clear();
            foreach (var d in DriverScanner.DisplayDrivers(_inventory)) Gpus.Add(new GpuDriverCard(d));
            RebuildProblems();
            RebuildCategories();
            RebuildDrivers();
            RebuildRestorePoints();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("cpu.failed", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildProblems()
    {
        Problems.Clear();
        foreach (var p in _problems)
            Problems.Add(new ProblemItem(p.Name, Loc.T(p.ErrorKey, p.ErrorCode), p.IsJustDisconnected));
        NoProblems = Problems.Count == 0;
    }

    private void RebuildCategories()
    {
        Categories.Clear();
        Categories.Add(new Option("all", () => Loc.T("drivers.cat.all", _inventory.Count), _category == "all", o => { _category = o.Value; RebuildDrivers(); }));
        foreach (var g in _inventory.GroupBy(d => d.Category).OrderBy(g => g.Key))
        {
            var cat = g.Key;
            int count = g.Count();
            string key = cat.ToString();
            Categories.Add(new Option(key, () => $"{Loc.T(DriverInfo.CategoryKey(cat))} ({count})", _category == key, o => { _category = o.Value; RebuildDrivers(); }));
        }
    }

    private void RebuildDrivers()
    {
        IEnumerable<DriverInfo> list = _inventory;
        if (_category != "all") list = list.Where(d => d.Category.ToString() == _category);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            string q = Search.Trim();
            list = list.Where(d => d.DeviceName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                   || d.Provider.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                   || d.Version.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        Drivers.Clear();
        foreach (var d in list)
            Drivers.Add(new DriverRow(d.DeviceName, Loc.T(DriverInfo.CategoryKey(d.Category)), d.Provider, d.Version,
                                      d.Date?.ToString("d", Loc.Culture) ?? "—", d.IsSigned));
        CountText = Loc.T("drivers.count", _inventory.Count);
    }

    private void RebuildRestorePoints()
    {
        RestorePointRows.Clear();
        foreach (var p in _points.Take(5))
            RestorePointRows.Add(new RestorePointRow(p.Description, p.Created == DateTime.MinValue ? "—" : p.Created.ToString("g", Loc.Culture)));
        NoRestorePoints = RestorePointRows.Count == 0;
    }

    // ------------------------------------------------------------------ busca de atualizações

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = Loc.T("drivers.status.checking");
        try
        {
            await Task.WhenAll(CheckNvidiaAsync(_cts.Token), SearchWindowsUpdateAsync(_cts.Token));
            HasChecked = true;
            int updates = Gpus.Count(g => g.HasUpdate) + WuUpdates.Count;
            StatusText = updates == 0 ? Loc.T("drivers.status.allUpToDate") : Loc.T("drivers.status.found", updates);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("cpu.cancelled");
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task CheckNvidiaAsync(CancellationToken ct)
    {
        foreach (var gpu in Gpus.Where(g => g.IsNvidia))
        {
            try
            {
                var latest = await NvidiaUpdater.LatestAsync(gpu.Name, _studio, ct);
                gpu.SetLatest(latest, null);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Log(ex);
                gpu.SetLatest(null, Loc.T("drivers.nvidia.error"));
            }
        }
    }

    private async Task SearchWindowsUpdateAsync(CancellationToken ct)
    {
        WuStatus = Loc.T("drivers.wu.searching");
        WuUpdates.Clear();
        RebootRequired = false;
        try
        {
            var updates = await _wu.SearchAsync(ct);
            foreach (var u in updates)
            {
                var item = new WuItem(u);
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(WuItem.IsSelected)) InstallWindowsUpdatesCommand.NotifyCanExecuteChanged();
                };
                WuUpdates.Add(item);
            }
            InstallWindowsUpdatesCommand.NotifyCanExecuteChanged();
            NoWuUpdates = WuUpdates.Count == 0;
            WuStatus = WuUpdates.Count == 0 ? Loc.T("drivers.wu.none") : Loc.T("drivers.wu.found", WuUpdates.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            App.Log(ex);
            NoWuUpdates = false;
            WuStatus = Loc.T("drivers.wu.error", ex.Message);
        }
    }

    private async void OnBranch(bool studio)
    {
        if (_studio == studio) return;
        _studio = studio;
        if (!HasChecked || IsBusy) return;
        IsBusy = true;
        try { await CheckNvidiaAsync(CancellationToken.None); }
        finally { IsBusy = false; }
    }

    // ------------------------------------------------------------------ instalação

    /// <summary>Cria o ponto de restauração; se o Windows recusar, pergunta se continua.</summary>
    private async Task<bool> EnsureRestorePointAsync()
    {
        StatusText = Loc.T("drivers.restore.creating");
        var status = await Task.Run(() => RestorePoints.Create(Loc.T("drivers.restore.description")));
        _points = await Task.Run(RestorePoints.List);
        RebuildRestorePoints();
        switch (status)
        {
            case RestorePointStatus.Created:
                return true;
            case RestorePointStatus.SkippedFrequency:
                string last = _points.FirstOrDefault() is { } p ? p.Created.ToString("g", Loc.Culture) : "—";
                return Ask(Loc.T("drivers.restore.skipped", last));
            case RestorePointStatus.Disabled:
                return Ask(Loc.T("drivers.restore.disabled"));
            default:
                return Ask(Loc.T("drivers.restore.failed"));
        }
    }

    private static bool Ask(string text)
        => MessageBox.Show(text, AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private bool CanInstallNvidia(GpuDriverCard? gpu) => !IsBusy && gpu?.Latest != null;

    [RelayCommand(CanExecute = nameof(CanInstallNvidia))]
    private async Task InstallNvidiaAsync(GpuDriverCard? gpu)
    {
        if (gpu?.Latest is not { } latest) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        PowerRequest.KeepAwake();
        try
        {
            if (!await EnsureRestorePointAsync())
            {
                StatusText = Loc.T("cpu.cancelled");
                return;
            }
            gpu.IsDownloading = true;
            StatusText = Loc.T("drivers.nvidia.downloading", latest.Version);
            var dispatcher = Application.Current.Dispatcher;
            string file = await NvidiaUpdater.DownloadAsync(latest.DownloadUrl, (done, total) => dispatcher.InvokeAsync(() =>
            {
                gpu.DownloadProgress = total is > 0 ? 100.0 * done / total.Value : 0;
                gpu.DownloadText = total is > 0 ? $"{Formats.Bytes(done)} / {Formats.Bytes(total.Value)}" : Formats.Bytes(done);
            }), _cts.Token);
            gpu.DownloadProgress = 100;
            NvidiaUpdater.RunInstaller(file);
            StatusText = Loc.T("drivers.nvidia.installerOpened");
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("cpu.cancelled");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("drivers.nvidia.downloadFailed", ex.Message);
        }
        finally
        {
            gpu.IsDownloading = false;
            PowerRequest.Release();
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanInstallWu() => !IsBusy && WuUpdates.Any(u => u.IsSelected);

    [RelayCommand(CanExecute = nameof(CanInstallWu))]
    private async Task InstallWindowsUpdatesAsync()
    {
        var chosen = WuUpdates.Where(u => u.IsSelected).ToList();
        if (chosen.Count == 0) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        PowerRequest.KeepAwake();
        try
        {
            if (!await EnsureRestorePointAsync())
            {
                StatusText = Loc.T("cpu.cancelled");
                return;
            }
            foreach (var c in chosen) { c.Status = Loc.T("drivers.wu.waiting"); c.Failed = false; }
            var dispatcher = Application.Current.Dispatcher;
            var (results, reboot) = await _wu.InstallAsync(chosen.Select(c => c.Update.Id).ToList(), (stage, i, total) => dispatcher.InvokeAsync(() =>
            {
                chosen[i].Status = Loc.T(stage == "download" ? "drivers.wu.downloading" : "drivers.wu.installing");
                StatusText = Loc.T("drivers.wu.progress", i + 1, total, chosen[i].Title);
            }), _cts.Token);
            foreach (var r in results)
            {
                var item = chosen.First(c => c.Update.Id == r.Id);
                item.Status = Loc.T(r.ResultKey, $"0x{r.HResult:X8}");
                item.Failed = !r.Succeeded;
            }
            RebootRequired = reboot;
            int ok = results.Count(r => r.Succeeded);
            StatusText = Loc.T("drivers.wu.done", ok, results.Count);
            _inventory = await Task.Run(DriverScanner.Inventory);
            RebuildCategories();
            RebuildDrivers();
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("cpu.cancelled");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("drivers.wu.error", ex.Message);
        }
        finally
        {
            PowerRequest.Release();
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ------------------------------------------------------------------ outras ações

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task CreateRestorePointAsync()
    {
        IsBusy = true;
        try
        {
            StatusText = Loc.T("drivers.restore.creating");
            var status = await Task.Run(() => RestorePoints.Create(Loc.T("drivers.restore.manual")));
            _points = await Task.Run(RestorePoints.List);
            RebuildRestorePoints();
            StatusText = status switch
            {
                RestorePointStatus.Created => Loc.T("drivers.restore.created"),
                RestorePointStatus.SkippedFrequency => Loc.T("drivers.restore.skippedInfo"),
                RestorePointStatus.Disabled => Loc.T("drivers.restore.disabledInfo"),
                _ => Loc.T("drivers.restore.failedInfo"),
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task BackupAsync()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("drivers.backup.choose") };
        if (dlg.ShowDialog() != true) return;
        string folder = Path.Combine(dlg.FolderName, $"SupernovaBench-drivers-{DateTime.Now:yyyy-MM-dd-HHmm}");
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            StatusText = Loc.T("drivers.backup.running");
            var dispatcher = Application.Current.Dispatcher;
            int count = await DriverBackup.ExportAsync(folder, line => dispatcher.InvokeAsync(() => StatusText = line), _cts.Token);
            StatusText = Loc.T("drivers.backup.done", count, folder);
            Shell.Open(folder);
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("cpu.cancelled");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("cpu.failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanCancel() => IsBusy && _cts != null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Ao fechar o app no meio de uma instalação/download.</summary>
    public void StopNow() => _cts?.Cancel();

    [RelayCommand]
    private void OpenReleaseNotes(GpuDriverCard? gpu)
    {
        if (gpu?.Latest?.DetailsUrl is { } url) Shell.Open(url);
    }

    [RelayCommand]
    private void OpenVendorPage(GpuDriverCard? gpu)
    {
        if (GpuVendorTools.OfficialPage(gpu?.Vendor) is { } url) Shell.Open(url);
    }

    [RelayCommand]
    private void OpenDeviceManager()
    {
        try { RestorePoints.OpenDeviceManager(); }
        catch (Exception ex) { App.Log(ex); }
    }

    [RelayCommand]
    private void OpenSystemRestore()
    {
        try { RestorePoints.OpenSystemRestore(); }
        catch (Exception ex) { App.Log(ex); }
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var g in Gpus) g.Relocalize();
        RebuildProblems();
        RebuildCategories();
        RebuildDrivers();
        RebuildRestorePoints();
        if (!IsBusy && !HasChecked) StatusText = "";
    }
}
