using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Benchmarks.Memory;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Teste de estabilidade da RAM (estilo TestMem5/HCI MemTest) dentro do Windows.</summary>
public sealed partial class RamStressViewModel : LocalizedViewModel
{
    private enum Verdict { None, Passed, Failed, Incomplete, Error }

    private readonly RamStress _stress = new();
    private readonly DispatcherTimer _timer;
    private Verdict _verdict;
    private int _percent = 70;
    private string _duration = "pass";
    private long _available;
    private long _total;
    private bool _userStopped;

    public RamStressViewModel()
    {
        ReadMemory();
        Amounts = new[] { 50, 70, 85 }
            .Select(p => new Option(p.ToString(), () => $"{p}% · {Formats.Bytes(_available * p / 100.0)}", p == _percent,
                                    o => _percent = int.Parse(o.Value)))
            .ToList();
        Durations = new[]
        {
            new Option("pass", () => Loc.T("ramstress.duration.pass"), true, o => _duration = o.Value),
            new Option("30", () => Loc.T("stress.duration.minutes", 30), false, o => _duration = o.Value),
            new Option("60", () => Loc.T("stress.duration.minutes", 60), false, o => _duration = o.Value),
            new Option("0", () => Loc.T("stress.duration.unlimited"), false, o => _duration = o.Value),
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
        StatusText = Loc.T("cpu.ready");
    }

    public IReadOnlyList<Option> Amounts { get; }
    public IReadOnlyList<Option> Durations { get; }
    public ObservableCollection<string> ErrorLines { get; } = new();

    public string MemoryText => Loc.T("ramstress.memory", Formats.Bytes(_total), Formats.Bytes(_available));

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _coverage;
    [ObservableProperty] private string _coverageText = "0%";
    [ObservableProperty] private string _passesText = "0";
    [ObservableProperty] private string _speedText = "—";
    [ObservableProperty] private string _patternText = "—";
    [ObservableProperty] private string _errorsText = "0";
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _testedText = "—";
    [ObservableProperty] private bool _hasErrors;
    [ObservableProperty] private bool _isPassed;
    [ObservableProperty] private bool _isFailed;

    private void ReadMemory()
    {
        (_total, _available) = MemoryStatus.Read();
    }

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        ReadMemory();
        OnPropertyChanged(nameof(MemoryText));
        long bytes = _available * _percent / 100;
        TimeSpan? duration = _duration switch { "30" => TimeSpan.FromMinutes(30), "60" => TimeSpan.FromMinutes(60), _ => null };
        double? maxPasses = _duration == "pass" ? 1 : null;

        ErrorLines.Clear();
        _verdict = Verdict.None;
        _userStopped = false;
        IsPassed = IsFailed = HasErrors = false;
        Coverage = 0;
        CoverageText = "0%";
        PassesText = "0";
        ErrorsText = "0";
        SpeedText = "—";
        ElapsedText = "0:00";

        _stress.Start(bytes, Environment.ProcessorCount, duration, maxPasses);
        TestedText = Formats.Bytes(_stress.Bytes);
        StatusText = Loc.T("ramstress.status.running", Formats.Bytes(_stress.Bytes), _stress.Threads);
        IsRunning = true;
        PowerRequest.KeepAwake();
        _timer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        _userStopped = true;
        await Task.Run(_stress.Stop);
        Finish();
    }

    public void StopNow()
    {
        if (!IsRunning) return;
        _userStopped = true;
        _stress.Stop();
        Finish();
    }

    private void Tick()
    {
        if (!IsRunning) return;
        Update();
        if (!_stress.Running) Finish();
    }

    private void Update()
    {
        var c = Loc.Culture;
        Coverage = Math.Min(100, _stress.Coverage % 100.0001);
        CoverageText = string.Format(c, "{0:0.0}%", _stress.Coverage);
        PassesText = _stress.Passes.ToString("N0", c);
        SpeedText = string.Format(c, "{0:N0} MB/s", _stress.MBps);
        PatternText = Loc.T("ramstress.pattern." + _stress.CurrentPattern);
        ElapsedText = Formats.Time(_stress.Elapsed);
        ErrorsText = _stress.ErrorCount.ToString("N0", c);
        HasErrors = _stress.ErrorCount > 0;

        var errors = _stress.Errors;
        for (int i = ErrorLines.Count; i < errors.Count; i++) ErrorLines.Add(FormatError(errors[i]));
    }

    private static string FormatError(RamError e)
        => Loc.T("ramstress.errorLine", $"0x{e.Offset:X}", $"0x{e.Expected:X16}", $"0x{e.Actual:X16}",
                 Loc.T("ramstress.pattern." + e.Pattern), e.BitsFlipped);

    private void Finish()
    {
        if (!IsRunning) return;
        _timer.Stop();
        _stress.Join();
        PowerRequest.Release();
        Update();
        IsRunning = false;

        if (_stress.Error != null) _verdict = Verdict.Error;
        else if (_stress.ErrorCount > 0) _verdict = Verdict.Failed;
        else if (_stress.Coverage >= 99.99) _verdict = Verdict.Passed;
        else _verdict = Verdict.Incomplete;
        IsPassed = _verdict == Verdict.Passed;
        IsFailed = _verdict is Verdict.Failed or Verdict.Error;
        StatusText = VerdictText();
    }

    private string VerdictText() => _verdict switch
    {
        Verdict.Passed => Loc.T("ramstress.verdict.passed", string.Format(Loc.Culture, "{0:0}%", _stress.Coverage)),
        Verdict.Failed => Loc.T("ramstress.verdict.failed", _stress.ErrorCount.ToString("N0", Loc.Culture)),
        Verdict.Error => _stress.Error == "out-of-memory" ? Loc.T("ramstress.oom") : Loc.T("cpu.failed", _stress.Error ?? "?"),
        Verdict.Incomplete => Loc.T(_userStopped ? "ramstress.verdict.stopped" : "ramstress.verdict.incomplete",
                                    string.Format(Loc.Culture, "{0:0.0}%", _stress.Coverage)),
        _ => Loc.T("cpu.ready"),
    };

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        if (!IsRunning) StatusText = VerdictText();
        if (_stress.Bytes > 0)
        {
            ErrorLines.Clear();
            foreach (var e in _stress.Errors) ErrorLines.Add(FormatError(e));
            PatternText = Loc.T("ramstress.pattern." + _stress.CurrentPattern);
        }
    }
}
