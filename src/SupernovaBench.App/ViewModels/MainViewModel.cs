using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Janela principal: menu lateral, página atual e mini-monitor do rodapé.</summary>
public sealed partial class MainViewModel : LocalizedViewModel
{
    // Ícones: Segoe Fluent Icons / Segoe MDL2 Assets.
    private const string GHome = "", GHardware = "", GSensors = "", GCpu = "", GGpu = "",
                         GMemory = "", GDisk = "", GStress = "", GGames = "", GDrivers = "",
                         GReports = "", GSettings = "";

    private const string GHealth = "\uE95E";

    private readonly Dictionary<string, object> _pages = new();
    private readonly Dictionary<string, NavItem> _items = new();

    public MainViewModel()
    {
        NavItem I(string key, string title, string glyph, int phase = 1)
        {
            var item = new NavItem(key, title, glyph, phase, OnItemSelected);
            _items[key] = item;
            return item;
        }

        Groups = new[]
        {
            new NavGroup("nav.section.system", new[]
            {
                I("overview", "nav.overview", GHome),
                I("hardware", "nav.hardware", GHardware),
                I("sensors", "nav.sensors", GSensors),
                I("health.disk", "nav.health.disk", GHealth),
            }),
            new NavGroup("nav.section.benchmark", new[]
            {
                I("bench.cpu", "nav.bench.cpu", GCpu),
                I("bench.gpu", "nav.bench.gpu", GGpu),
                I("bench.memory", "nav.bench.memory", GMemory),
                I("bench.disk", "nav.bench.disk", GDisk),
            }),
            new NavGroup("nav.section.stress", new[]
            {
                I("stress.cpu", "nav.stress.cpu", GStress),
                I("stress.gpu", "nav.stress.gpu", GStress),
                I("stress.ram", "nav.stress.ram", GMemory),
            }),
            new NavGroup("nav.section.games", new[]
            {
                I("games", "nav.games.library", GGames),
            }),
            new NavGroup("nav.section.tools", new[]
            {
                I("drivers", "nav.tools.drivers", GDrivers),
                I("reports", "nav.tools.reports", GReports),
                I("settings", "nav.tools.settings", GSettings),
            }),
        };

        Navigate("overview");
    }

    public IReadOnlyList<NavGroup> Groups { get; }
    public SensorService Sensors => AppServices.Sensors;

    /// <summary>Atalhos da Visão geral: módulos já disponíveis.</summary>
    public IEnumerable<NavItem> AvailableModules => _items.Values.Where(i => !i.IsSoon && i.Key != "overview");

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private NavItem? _currentItem;

    public string PageTitle => CurrentItem?.Title ?? "";
    public string ThemeGlyph => ThemeManager.Current == ThemeManager.Light ? "" : "";

    partial void OnCurrentItemChanged(NavItem? value) => OnPropertyChanged(nameof(PageTitle));

    [RelayCommand]
    public void Navigate(string key)
    {
        if (!_items.TryGetValue(key, out var item)) return;
        if (!item.IsSelected) item.IsSelected = true; // dispara OnItemSelected
        else OnItemSelected(item);
    }

    private void OnItemSelected(NavItem item)
    {
        foreach (var other in _items.Values)
            if (!ReferenceEquals(other, item) && other.IsSelected) other.IsSelected = false;
        CurrentItem = item;
        CurrentPage = GetPage(item);
    }

    private object GetPage(NavItem item)
    {
        if (_pages.TryGetValue(item.Key, out var page)) return page;
        page = item.Key switch
        {
            "overview" => new OverviewViewModel(this),
            "hardware" => new HardwareViewModel(),
            "sensors" => new SensorsViewModel(),
            "bench.cpu" => CpuBench,
            "bench.gpu" => GpuBench,
            "stress.gpu" => GpuStress,
            "bench.memory" => MemoryBench,
            "bench.disk" => DiskBench,
            "stress.cpu" => CpuStress,
            "stress.ram" => RamStress,
            "health.disk" => new DiskHealthViewModel(),
            "games" => Games,
            "drivers" => Drivers,
            "reports" => new ReportsViewModel(),
            "settings" => new SettingsViewModel(this),
            _ => new ComingSoonViewModel(item, _items.Values.Where(i => i.IsSoon).ToList()),
        };
        _pages[item.Key] = page;
        return page;
    }

    /// <summary>O benchmark de CPU é criado sob demanda e mantido (o teste continua se o usuário trocar de página).</summary>
    public CpuBenchViewModel CpuBench => _cpuBench ??= new CpuBenchViewModel();
    private CpuBenchViewModel? _cpuBench;

    public GpuBenchViewModel GpuBench => _gpuBench ??= new GpuBenchViewModel();
    private GpuBenchViewModel? _gpuBench;

    public GpuStressViewModel GpuStress => _gpuStress ??= new GpuStressViewModel();
    private GpuStressViewModel? _gpuStress;

    public DriversViewModel Drivers => _drivers ??= new DriversViewModel();
    private DriversViewModel? _drivers;

    public GamesViewModel Games => _games ??= new GamesViewModel();
    private GamesViewModel? _games;

    public MemoryBenchViewModel MemoryBench => _memoryBench ??= new MemoryBenchViewModel();
    private MemoryBenchViewModel? _memoryBench;

    public DiskBenchViewModel DiskBench => _diskBench ??= new DiskBenchViewModel();
    private DiskBenchViewModel? _diskBench;

    public CpuStressViewModel CpuStress => _cpuStress ??= new CpuStressViewModel();
    private CpuStressViewModel? _cpuStress;

    public RamStressViewModel RamStress => _ramStress ??= new RamStressViewModel();
    private RamStressViewModel? _ramStress;

    public bool IsBenchmarkRunning => _cpuBench?.IsRunning == true || _gpuBench?.IsRunning == true || _gpuStress?.IsRunning == true
                                      || _memoryBench?.IsRunning == true || _diskBench?.IsRunning == true
                                      || _cpuStress?.IsRunning == true || _ramStress?.IsRunning == true
                                      || _drivers?.IsBusy == true;

    public void StopBenchmarks()
    {
        _cpuBench?.StopCommand.Execute(null);
        _gpuBench?.StopNow();
        _gpuStress?.StopNow();
        _memoryBench?.StopNow();
        _diskBench?.StopNow();
        _cpuStress?.StopNow();
        _ramStress?.StopNow();
        _drivers?.StopNow();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = ThemeManager.Current == ThemeManager.Light ? ThemeManager.Cosmic : ThemeManager.Light;
        ThemeManager.Apply(next);
        AppServices.Settings.Theme = next;
        AppServices.TrySaveSettings();
        OnPropertyChanged(nameof(ThemeGlyph));
    }

    public void NotifyThemeChanged() => OnPropertyChanged(nameof(ThemeGlyph));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(PageTitle));
    }
}
