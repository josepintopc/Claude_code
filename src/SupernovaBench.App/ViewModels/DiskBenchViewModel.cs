using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Benchmarks.Disk;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware;

namespace SupernovaBench.App.ViewModels;

/// <summary>Uma linha da grade estilo CrystalDiskMark (um teste, leitura e gravação).</summary>
public sealed partial class DiskRow : ObservableObject
{
    public DiskRow(DiskTestSpec spec) => Spec = spec;

    public DiskTestSpec Spec { get; }
    public string Label => Spec.Label;
    public DiskMeasure? Read { get; set; }
    public DiskMeasure? Write { get; set; }

    [ObservableProperty] private string _readText = "";
    [ObservableProperty] private string _writeText = "";
    [ObservableProperty] private bool _readActive;
    [ObservableProperty] private bool _writeActive;
}

/// <summary>Benchmark de disco no estilo CrystalDiskMark.</summary>
public sealed partial class DiskBenchViewModel : LocalizedViewModel
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private BenchmarkResult? _lastResult;
    private Guid? _highlight;
    private List<DriveTarget> _drives = new();
    private string? _root;
    private long _size = 1L << 30;
    private int _runs = 3;
    private string _unit = "mbps";
    private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(5);

    public DiskBenchViewModel()
    {
        Rows = DiskBench.Tests.Select(t => new DiskRow(t)).ToList();
        Sizes = DiskBench.FileSizes
            .Select(s => new Option(s.ToString(), () => SizeLabel(s), s == _size, o => { _size = long.Parse(o.Value); _highlight = null; RefreshRanking(); }))
            .ToList();
        RunOptions = new[] { 1, 3, 5 }
            .Select(n => new Option(n.ToString(), () => Loc.T("disk.runs.value", n), n == _runs, o => _runs = int.Parse(o.Value)))
            .ToList();
        Units = new[]
        {
            new Option("mbps", () => "MB/s", true, o => SetUnit(o.Value)),
            new Option("iops", () => "IOPS", false, o => SetUnit(o.Value)),
            new Option("us", () => "μs", false, o => SetUnit(o.Value)),
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => ElapsedText = Formats.Time(_clock.Elapsed);
        AppServices.History.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(RefreshRanking);
        StatusText = Loc.T("cpu.ready");
        RefreshRanking();
        _ = LoadDrivesAsync();
    }

    public IReadOnlyList<DiskRow> Rows { get; }
    public ObservableCollection<Option> Drives { get; } = new();
    public IReadOnlyList<Option> Sizes { get; }
    public IReadOnlyList<Option> RunOptions { get; }
    public IReadOnlyList<Option> Units { get; }
    public ObservableCollection<RankingRow> Ranking { get; } = new();

    public string UnitHeader => _unit switch { "iops" => "IOPS", "us" => "μs", _ => "MB/s" };

    [ObservableProperty] private string _driveDetail = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _hasDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isRunning;

    public bool IsIdle => !IsRunning;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _elapsedText = "0:00";
    [ObservableProperty] private string _scoreText = "—";
    [ObservableProperty] private bool _hasScore;
    [ObservableProperty] private bool _isNewBest;

    private DriveTarget? SelectedDrive => _drives.FirstOrDefault(d => d.Root == _root);

    private async Task LoadDrivesAsync()
    {
        try
        {
            _drives = (await Task.Run(DiskInfo.Drives)).ToList();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        Drives.Clear();
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        _root = _drives.FirstOrDefault(d => string.Equals(d.Root, system, StringComparison.OrdinalIgnoreCase))?.Root ?? _drives.FirstOrDefault()?.Root;
        foreach (var d in _drives)
        {
            var drive = d;
            Drives.Add(new Option(d.Root, () => DriveLabel(drive), d.Root == _root, o => SelectDrive(o.Value)));
        }
        HasDrive = _root != null;
        UpdateDriveDetail();
    }

    private void SelectDrive(string root)
    {
        _root = root;
        UpdateDriveDetail();
    }

    private void UpdateDriveDetail()
    {
        var d = SelectedDrive;
        DriveDetail = d == null
            ? Loc.T("disk.noDrives")
            : string.Join(" · ", new[] { d.DeviceName, d.Bus, d.Media, Loc.T("disk.free", Formats.Bytes(d.FreeBytes), Formats.Bytes(d.TotalBytes)) }
                                     .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string DriveLabel(DriveTarget d) => string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Letter : $"{d.Letter} {d.VolumeLabel}";

    private static string SizeLabel(long bytes) => bytes >= 1L << 30 ? $"{bytes >> 30} GB" : $"{bytes >> 20} MB";

    private void SetUnit(string unit)
    {
        _unit = unit;
        OnPropertyChanged(nameof(UnitHeader));
        foreach (var row in Rows) Render(row);
    }

    private void Render(DiskRow row)
    {
        row.ReadText = Format(row.Read);
        row.WriteText = Format(row.Write);
    }

    private string Format(DiskMeasure? m)
    {
        if (m == null) return "";
        var c = Loc.Culture;
        return _unit switch
        {
            "iops" => m.Iops.ToString("N0", c),
            "us" => m.LatencyUs.ToString(m.LatencyUs >= 100 ? "N0" : "N1", c),
            _ => m.MBps.ToString(m.MBps >= 100 ? "N0" : "N2", c),
        };
    }

    private bool CanStart() => !IsRunning && HasDrive;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var drive = SelectedDrive;
        if (drive == null) return;
        foreach (var row in Rows)
        {
            row.Read = row.Write = null;
            row.ReadActive = row.WriteActive = false;
            Render(row);
        }

        var options = new DiskBenchOptions { Directory = drive.Root, FileSize = _size, Runs = _runs, TestDuration = TestDuration };
        var bench = new DiskBench(options);
        var dispatcher = Application.Current.Dispatcher;
        int totalSteps = DiskBench.Tests.Count * 2 * _runs;
        int step = 0;
        const double PrepWeight = 8;

        bench.Preparing = p => dispatcher.InvokeAsync(() =>
        {
            Progress = p * PrepWeight;
            StatusText = Loc.T("disk.status.preparing", (p * 100).ToString("0", Loc.Culture));
        });
        bench.Started = (spec, write, run) => dispatcher.InvokeAsync(() =>
        {
            var row = Rows.First(r => r.Spec.Id == spec.Id);
            if (write) row.WriteActive = true; else row.ReadActive = true;
            StatusText = Loc.T("disk.status.test", spec.Label, Loc.T(write ? "disk.write" : "disk.read"), run, _runs);
        });
        bench.Measured = (spec, write, best) => dispatcher.InvokeAsync(() =>
        {
            var row = Rows.First(r => r.Spec.Id == spec.Id);
            if (write) row.Write = best; else row.Read = best;
            Render(row);
            step++;
            Progress = PrepWeight + (100 - PrepWeight) * step / totalSteps;
            if (step % _runs == 0) row.ReadActive = row.WriteActive = false;
        });

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsRunning = true;
        HasScore = false;
        IsNewBest = false;
        Progress = 0;
        _clock.Restart();
        _timer.Start();
        AppServices.Sensors.Pause();
        PowerRequest.KeepAwake();
        string preset = DiskBench.PresetId(_size, TestDuration);

        try
        {
            double previousBest = AppServices.History.Ranking(TestIds.DiskBench, preset, DiskBench.EngineVersion)
                                                     .Where(r => !r.Imported && r.DeviceName == drive.DeviceName)
                                                     .Select(r => r.Score).DefaultIfEmpty(0).Max();
            var r = await Task.Run(() => bench.RunAsync(ct));
            _clock.Stop();
            if (r == null)
            {
                StatusText = Loc.T("cpu.cancelled");
                return;
            }

            var result = bench.ToResult(r, drive.DeviceName);
            AppServices.History.Add(result);
            _lastResult = result;
            _highlight = result.Id;
            ScoreText = result.Score.ToString("N0", Loc.Culture);
            HasScore = true;
            IsNewBest = previousBest > 0 && result.Score > previousBest;
            Progress = 100;
            StatusText = Loc.T("cpu.done", Formats.Time(TimeSpan.FromSeconds(result.Seconds)));
            RefreshRanking();
        }
        catch (DiskBenchException ex)
        {
            StatusText = ex.Key == "space" ? Loc.T("disk.error.space", SizeLabel(_size)) : Loc.T("disk.error.access", ex.Message);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("cpu.failed", ex.Message);
        }
        finally
        {
            _timer.Stop();
            AppServices.Sensors.Resume();
            PowerRequest.Release();
            foreach (var row in Rows) row.ReadActive = row.WriteActive = false;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    public void StopNow() => _cts?.Cancel();

    private void RefreshRanking()
    {
        var list = AppServices.History.Ranking(TestIds.DiskBench, DiskBench.PresetId(_size, TestDuration), DiskBench.EngineVersion);
        RankingBuilder.Fill(Ranking, list, _highlight, r =>
            string.Format(Loc.Culture, "SEQ {0:N0} / {1:N0} MB/s", r.Metric("seq1m_q8t1.read") ?? 0, r.Metric("seq1m_q8t1.write") ?? 0),
            bestPerDevice: true);
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var row in Rows) Render(row);
        UpdateDriveDetail();
        RefreshRanking();
        if (!IsRunning)
        {
            StatusText = _lastResult != null ? Loc.T("cpu.done", Formats.Time(TimeSpan.FromSeconds(_lastResult.Seconds))) : Loc.T("cpu.ready");
            if (_lastResult != null) ScoreText = _lastResult.Score.ToString("N0", Loc.Culture);
        }
    }
}
