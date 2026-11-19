using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Controls;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware;

namespace SupernovaBench.App.Services;

/// <summary>
/// Mantém o <see cref="SensorMonitor"/> rodando: lê o hardware numa thread de fundo e aplica na thread da interface.
/// Expõe os valores principais já formatados e o histórico para o gráfico da Visão geral.
/// </summary>
public sealed partial class SensorService : ObservableObject, IDisposable
{
    public const int HistoryLength = 120;

    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public SensorMonitor Monitor { get; } = new();

    public ChartSeries CpuTempSeries { get; } = new("CPU", Color.FromRgb(0xFF, 0x7A, 0x2F), HistoryLength);
    public ChartSeries GpuTempSeries { get; } = new("GPU", Color.FromRgb(0x9B, 0x6B, 0xFF), HistoryLength);
    public IReadOnlyList<ChartSeries> TemperatureSeries { get; }

    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private int _revision;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _cpuTempText = "—";
    [ObservableProperty] private string _cpuLoadText = "—";
    [ObservableProperty] private string _cpuPowerText = "—";
    [ObservableProperty] private string _cpuClockText = "—";
    [ObservableProperty] private string _gpuTempText = "—";
    [ObservableProperty] private string _gpuLoadText = "—";
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private bool _hasCpuProblem;
    [ObservableProperty] private bool _canInstallPawnIO;
    [ObservableProperty] private string _problemText = "";
    [ObservableProperty] private string _installStatus = "";

    public int IntervalMs { get; set; } = 1000;

    /// <summary>Pausa a leitura do LibreHardwareMonitor (ex.: durante o benchmark de GPU, para não perturbar os tempos de frame).</summary>
    public bool IsPaused { get; private set; }

    public void Pause() => IsPaused = true;

    public void Resume() => IsPaused = false;
    public bool IsElevated { get; } = Elevation.IsElevated;

    public event EventHandler? Updated;

    public SensorService()
    {
        TemperatureSeries = new[] { CpuTempSeries, GpuTempSeries };
        StatusText = Loc.T("sensors.starting");
        Loc.LanguageChanged += (_, _) =>
        {
            foreach (var r in Monitor.Rows) r.Relocalize();
            RefreshTexts();
        };
    }

    /// <summary>Inicia a detecção e o laço de leitura (chamar na thread da interface).</summary>
    public async void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            await Monitor.OpenAsync();
            if (Monitor.OpenError != null)
                StatusText = Loc.T("sensors.openFailed", Monitor.OpenError.Message);

            while (!_cts.IsCancellationRequested)
            {
                if (IsPaused)
                {
                    await Task.Delay(250, _cts.Token);
                    continue;
                }
                var poll = await PollOnSensorThreadAsync();
                if (_cts.IsCancellationRequested) break;
                Monitor.Apply(poll);
                if (!IsReady && Monitor.IsOpen)
                {
                    IsReady = true;
                    StatusText = "";
                }
                RefreshTexts();
                CpuTempSeries.Push(Monitor.CpuTemperature);
                GpuTempSeries.Push(Monitor.GpuTemperature);
                Revision++;
                Updated?.Invoke(this, EventArgs.Empty);
                await Task.Delay(IntervalMs, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText = Loc.T("sensors.openFailed", ex.Message);
        }
    }

    // ------------------------------------------------------------------ thread de leitura

    private readonly SemaphoreSlim _pollRequest = new(0);
    private TaskCompletionSource<SensorPoll>? _pendingPoll;
    private Thread? _pollThread;

    /// <summary>
    /// Lê os sensores numa thread fixa com prioridade acima do normal. Para ler cada núcleo, a biblioteca roda
    /// naquele núcleo; com o benchmark de CPU ocupando todos na prioridade normal, a leitura do pool de threads
    /// esperava a vez em cada um e o painel ficava parado no valor de antes do teste (e o limite de temperatura
    /// do stress no modo render não disparava). Não baixamos a prioridade do benchmark: nas CPUs híbridas da
    /// Intel o Windows manda threads abaixo do normal para os E-cores, o que mudaria a nota.
    /// </summary>
    private Task<SensorPoll> PollOnSensorThreadAsync()
    {
        if (_pollThread == null)
        {
            _pollThread = new Thread(PollLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "Supernova Sensors" };
            _pollThread.Start();
        }
        var tcs = new TaskCompletionSource<SensorPoll>(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _pendingPoll, tcs);
        _pollRequest.Release();
        return tcs.Task;
    }

    private void PollLoop()
    {
        while (true)
        {
            try { _pollRequest.Wait(_cts.Token); }
            catch (OperationCanceledException) { return; }
            var tcs = Volatile.Read(ref _pendingPoll);
            if (tcs == null) continue;
            try { tcs.TrySetResult(Monitor.Poll()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }
    }

    private void RefreshTexts()
    {
        var c = Loc.Culture;
        string F(float? v, string fmt) => v is { } x ? string.Format(c, fmt, x) : "—";
        CpuTempText = F(Monitor.CpuTemperature, "{0:0} °C");
        CpuLoadText = F(Monitor.CpuLoad, "{0:0} %");
        CpuPowerText = F(Monitor.CpuPower, "{0:0} W");
        CpuClockText = F(Monitor.CpuClock, "{0:0} MHz");
        GpuTempText = F(Monitor.GpuTemperature, "{0:0} °C");
        GpuLoadText = F(Monitor.GpuLoad, "{0:0} %");
        GpuName = Monitor.GpuName ?? "";

        if (!IsReady)
        {
            HasCpuProblem = false;
            return;
        }
        var problem = Monitor.DiagnoseCpuSensors();
        HasCpuProblem = problem != SensorMonitor.CpuSensorProblem.None;
        CanInstallPawnIO = problem == SensorMonitor.CpuSensorProblem.PawnIOMissing;
        ProblemText = problem switch
        {
            SensorMonitor.CpuSensorProblem.NotElevated => Loc.T("sensors.problem.notElevated"),
            SensorMonitor.CpuSensorProblem.PawnIOMissing => Loc.T("sensors.problem.pawnio"),
            SensorMonitor.CpuSensorProblem.ReadFailed => Loc.T("sensors.problem.readFailed"),
            _ => "",
        };
    }

    [RelayCommand]
    private async Task InstallPawnIOAsync()
    {
        InstallStatus = Loc.T("sensors.installing");
        try
        {
            int code = await PawnIO.InstallWithWingetAsync();
            InstallStatus = code == 0 ? Loc.T("sensors.installDone") : Loc.T("sensors.installFailed", $"winget {code}");
        }
        catch (Exception ex)
        {
            InstallStatus = Loc.T("sensors.installFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenPawnIOSite() => Shell.Open(PawnIO.Website);

    [RelayCommand]
    private void ResetMinMax() => Monitor.RequestResetMinMax();

    public void Dispose()
    {
        _cts.Cancel();
        _pendingPoll?.TrySetCanceled();
        Monitor.Dispose();
    }
}
