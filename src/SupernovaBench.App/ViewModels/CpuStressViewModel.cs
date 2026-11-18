using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Benchmarks.Cpu;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Stress de CPU com limite de temperatura e detecção de erros de cálculo.</summary>
public sealed partial class CpuStressViewModel : LocalizedViewModel
{
    private enum StopReason { None, User, Limit }

    private readonly CpuStress _stress = new();
    private readonly DispatcherTimer _timer;
    private StopReason _reason;
    private double? _maxTemp;
    private double _clockSum;
    private int _clockSamples;

    private CpuStressMode _mode = CpuStressMode.Fma;
    private string _threads = "all";
    private int _limit = 90;
    private int _minutes;

    public CpuStressViewModel()
    {
        Modes = new[]
        {
            new Option("fma", () => Loc.T("cpustress.mode.fma"), true, o => SetMode(CpuStressMode.Fma)),
            new Option("render", () => Loc.T("cpustress.mode.render"), false, o => SetMode(CpuStressMode.Render)),
            new Option("int", () => Loc.T("cpustress.mode.int"), false, o => SetMode(CpuStressMode.Integer)),
        };
        ThreadOptions = new[]
        {
            new Option("all", () => Loc.T("cpustress.threads.all", Environment.ProcessorCount), true, o => _threads = o.Value),
            new Option("half", () => Loc.T("cpustress.threads.half", Math.Max(1, Environment.ProcessorCount / 2)), false, o => _threads = o.Value),
            new Option("one", () => Loc.T("cpustress.threads.one"), false, o => _threads = o.Value),
        };
        Limits = new[] { 80, 85, 90, 95 }
            .Select(t => new Option(t.ToString(), () => $"{t} °C", t == _limit, o => _limit = int.Parse(o.Value))).ToList();
        Durations = new[] { 0, 5, 15, 30, 60 }
            .Select(m => new Option(m.ToString(), () => m == 0 ? Loc.T("stress.duration.unlimited") : Loc.T("stress.duration.minutes", m),
                                    m == _minutes, o => _minutes = int.Parse(o.Value))).ToList();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
        StatusText = Loc.T("cpu.ready");
    }

    public IReadOnlyList<Option> Modes { get; }
    public IReadOnlyList<Option> ThreadOptions { get; }
    public IReadOnlyList<Option> Limits { get; }
    public IReadOnlyList<Option> Durations { get; }
    public SensorService Sensors => AppServices.Sensors;
    public string CpuName => MachineInfo.CpuName();

    public string ModeDescription => _mode switch
    {
        CpuStressMode.Render => Loc.T("cpustress.mode.render.desc"),
        CpuStressMode.Integer => Loc.T("cpustress.mode.int.desc"),
        _ => Loc.T("cpustress.mode.fma.desc"),
    };

    [ObservableProperty] private bool _stopOnError = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _maxTempText = "—";
    [ObservableProperty] private string _errorsText = "0";
    [ObservableProperty] private bool _hasErrors;
    [ObservableProperty] private string _iterationsText = "—";

    private void SetMode(CpuStressMode mode)
    {
        _mode = mode;
        OnPropertyChanged(nameof(ModeDescription));
    }

    private int ThreadCount => _threads switch
    {
        "half" => Math.Max(1, Environment.ProcessorCount / 2),
        "one" => 1,
        _ => Environment.ProcessorCount,
    };

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        if (AppServices.Sensors.Monitor.CpuTemperature == null &&
            MessageBox.Show(Loc.T("cpustress.noSensor"), AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _stress.StopOnError = StopOnError;
        _stress.Start(_mode, ThreadCount, _minutes > 0 ? TimeSpan.FromMinutes(_minutes) : null);
        _reason = StopReason.None;
        _maxTemp = null;
        _clockSum = 0;
        _clockSamples = 0;
        SummaryText = "";
        MaxTempText = "—";
        ErrorsText = "0";
        HasErrors = false;
        IterationsText = "—";
        ElapsedText = "0:00";
        StatusText = Loc.T("cpustress.status.running", ThreadCount);
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
        Update();
        if (!_stress.Running)
        {
            Finish();
            return;
        }

        var m = AppServices.Sensors.Monitor;
        if (m.CpuClock is { } clock)
        {
            _clockSum += clock;
            _clockSamples++;
        }
        if (m.CpuTemperature is { } t)
        {
            _maxTemp = Math.Max(_maxTemp ?? t, t);
            MaxTempText = string.Format(Loc.Culture, "{0:0} °C", _maxTemp);
            if (t >= _limit && _reason == StopReason.None)
            {
                _reason = StopReason.Limit;
                StatusText = Loc.T("stress.status.limit", t.ToString("0", Loc.Culture), _limit);
                await Task.Run(_stress.Stop);
                Finish();
            }
        }
    }

    private void Update()
    {
        ElapsedText = Formats.Time(_stress.Elapsed);
        ErrorsText = _stress.Errors.ToString("N0", Loc.Culture);
        HasErrors = _stress.Errors > 0;
        IterationsText = _stress.Iterations.ToString("N0", Loc.Culture);
    }

    private void Finish()
    {
        if (!IsRunning) return;
        _timer.Stop();
        PowerRequest.Release();
        IsRunning = false;
        Update();

        if (_stress.Error != null) StatusText = Loc.T("cpu.failed", _stress.Error);
        else if (_stress.Errors > 0) StatusText = Loc.T("cpustress.status.errors", _stress.Errors, _stress.FirstErrorThread ?? 1);
        else if (_reason == StopReason.Limit) { /* mensagem já definida */ }
        else if (_reason == StopReason.User) StatusText = Loc.T("cpustress.status.stopped");
        else StatusText = Loc.T("cpustress.status.passed");

        string clock = _clockSamples > 0 ? string.Format(Loc.Culture, "{0:N0} MHz", _clockSum / _clockSamples) : "—";
        SummaryText = Loc.T("cpustress.summary", Formats.Time(_stress.Elapsed),
                            _maxTemp is { } mt ? string.Format(Loc.Culture, "{0:0} °C", mt) : "—", clock,
                            _stress.Errors.ToString("N0", Loc.Culture));
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        if (!IsRunning && string.IsNullOrEmpty(SummaryText)) StatusText = Loc.T("cpu.ready");
    }
}
