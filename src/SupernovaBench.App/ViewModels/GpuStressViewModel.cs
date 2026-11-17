using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Localization;
using SupernovaBench.Graphics;

namespace SupernovaBench.App.ViewModels;

/// <summary>Stress de GPU no estilo FurMark, com limite de temperatura e parada automática.</summary>
public sealed partial class GpuStressViewModel : LocalizedViewModel
{
    private enum StopReason { None, User, Limit }

    private readonly FurStress _stress = new();
    private readonly DispatcherTimer _timer;
    private StopReason _reason;
    private double? _maxTemp;
    private int _ticks;
    private GpuAdapter? _adapter;

    private string _resolution = "1080p";
    private int _msaa = 4;
    private string _intensity = "high";
    private int _limit = 85;
    private int _minutes;

    public GpuStressViewModel()
    {
        Resolutions = new[]
        {
            new Option("720p", () => "720p", false, o => _resolution = o.Value),
            new Option("1080p", () => "1080p", true, o => _resolution = o.Value),
            new Option("1440p", () => "1440p", false, o => _resolution = o.Value),
            new Option("full", () => Loc.T("stress.fullscreen"), false, o => _resolution = o.Value),
        };
        MsaaOptions = new[]
        {
            new Option("1", () => Loc.T("stress.msaa.off"), false, o => _msaa = int.Parse(o.Value)),
            new Option("4", () => "4x", true, o => _msaa = int.Parse(o.Value)),
            new Option("8", () => "8x", false, o => _msaa = int.Parse(o.Value)),
        };
        Intensities = new[]
        {
            new Option("normal", () => Loc.T("stress.intensity.normal"), false, o => _intensity = o.Value),
            new Option("high", () => Loc.T("stress.intensity.high"), true, o => _intensity = o.Value),
            new Option("extreme", () => Loc.T("stress.intensity.extreme"), false, o => _intensity = o.Value),
        };
        Limits = new[] { 75, 80, 85, 90 }
            .Select(t => new Option(t.ToString(), () => $"{t} °C", t == _limit, o => _limit = int.Parse(o.Value))).ToList();
        Durations = new[] { 0, 5, 15, 30 }
            .Select(m => new Option(m.ToString(), () => m == 0 ? Loc.T("stress.duration.unlimited") : Loc.T("stress.duration.minutes", m),
                                    m == _minutes, o => _minutes = int.Parse(o.Value))).ToList();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
        StatusText = Loc.T("cpu.ready");
        _ = LoadAsync();
    }

    public IReadOnlyList<Option> Resolutions { get; }
    public IReadOnlyList<Option> MsaaOptions { get; }
    public IReadOnlyList<Option> Intensities { get; }
    public IReadOnlyList<Option> Limits { get; }
    public IReadOnlyList<Option> Durations { get; }
    public GpuLive Live { get; } = new();

    [ObservableProperty] private string _adapterName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _fpsText = "—";
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _maxTempText = "—";

    private async Task LoadAsync()
    {
        _adapter = await AppServices.Gpu.GetMainAdapterAsync();
        await AppServices.Gpu.GetTelemetryAsync();
        AdapterName = _adapter?.Name ?? Loc.T("gpu.adapter.none");
        Live.Update(AppServices.Gpu.ReadSample());
    }

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (AppServices.Gpu.ReadTemperature() == null &&
            MessageBox.Show(Loc.T("stress.noSensor"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var (shells, iterations) = FurStressSettings.Intensity(_intensity);
        var (w, h) = _resolution switch { "720p" => (1280, 720), "1440p" => (2560, 1440), _ => (1920, 1080) };
        _stress.Start(new FurStressSettings
        {
            Width = w,
            Height = h,
            Fullscreen = _resolution == "full",
            Msaa = _msaa,
            Shells = shells,
            Iterations = iterations,
            AdapterName = _adapter?.Name ?? "",
            Duration = _minutes > 0 ? TimeSpan.FromMinutes(_minutes) : null,
        });

        _reason = StopReason.None;
        _maxTemp = null;
        _ticks = 0;
        SummaryText = "";
        MaxTempText = "—";
        FpsText = "—";
        ElapsedText = "0:00";
        StatusText = Loc.T("stress.status.running");
        IsRunning = true;
        PowerRequest.KeepAwake();
        _timer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        _reason = StopReason.User;
        await Task.Run(_stress.Stop);
        Finish();
    }

    /// <summary>Para sem perguntar (ao fechar o app).</summary>
    public void StopNow()
    {
        if (!IsRunning) return;
        _reason = StopReason.User;
        _stress.Stop();
        Finish();
    }

    private async void Tick()
    {
        if (!IsRunning) return;
        if (!_stress.Running)
        {
            Finish();
            return;
        }

        FpsText = _stress.Fps > 0 ? _stress.Fps.ToString("N0", Loc.Culture) : "—";
        ElapsedText = Formats.Time(_stress.Elapsed);

        var sample = AppServices.Gpu.ReadSample();
        double? temp = sample?.TemperatureC ?? AppServices.Sensors.Monitor.GpuTemperature;
        if (temp is { } t)
        {
            _maxTemp = Math.Max(_maxTemp ?? t, t);
            MaxTempText = string.Format(Loc.Culture, "{0:0} °C", _maxTemp);
            if (t >= _limit && _reason == StopReason.None)
            {
                _reason = StopReason.Limit;
                StatusText = Loc.T("stress.status.limit", t.ToString("0", Loc.Culture), _limit);
                await Task.Run(_stress.Stop);
                Finish();
                return;
            }
        }
        if (++_ticks % 2 == 0) Live.Update(sample);
    }

    private void Finish()
    {
        if (!IsRunning) return;
        _timer.Stop();
        PowerRequest.Release();
        IsRunning = false;

        if (_stress.Error != null) StatusText = Loc.T("cpu.failed", _stress.Error);
        else if (_reason == StopReason.Limit) { /* mensagem já definida */ }
        else if (_stress.ClosedByUser || _reason == StopReason.User) StatusText = Loc.T("stress.status.closed");
        else StatusText = Loc.T("stress.status.duration");

        if (_stress.Frames > 0)
        {
            SummaryText = Loc.T("stress.summary", _stress.Frames.ToString("N0", Loc.Culture), _stress.AverageFps.ToString("N0", Loc.Culture),
                                _maxTemp is { } m ? string.Format(Loc.Culture, "{0:0} °C", m) : "—", Formats.Time(_stress.Elapsed));
        }
        Live.Update(AppServices.Gpu.ReadSample());
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        if (!IsRunning && string.IsNullOrEmpty(SummaryText)) StatusText = Loc.T("cpu.ready");
        if (_adapter == null) AdapterName = Loc.T("gpu.adapter.none");
    }
}
