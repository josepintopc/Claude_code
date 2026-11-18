namespace SupernovaBench.Core.Extras;

/// <summary>FPS e tempo de quadro de um processo agora.</summary>
public sealed record FrameSnapshot(int ProcessId, double Fps, double FrameTimeMs, int FramesInWindow);

/// <summary>
/// Conta os "presents" (quadros entregues pelo jogo ao Windows) por processo, como o PresentMon.
/// Recebe eventos de qualquer thread; mantém só o último segundo e meio de cada processo.
/// </summary>
public sealed class FrameCounter
{
    private const double KeepMs = 1500;
    private readonly object _lock = new();
    private readonly Dictionary<int, Queue<double>> _frames = new();
    private FrameSession? _session;

    public void AddPresent(int processId, double timestampMs)
    {
        lock (_lock)
        {
            if (!_frames.TryGetValue(processId, out var q)) _frames[processId] = q = new Queue<double>();
            q.Enqueue(timestampMs);
            while (q.Count > 0 && timestampMs - q.Peek() > KeepMs) q.Dequeue();
            if (_session != null && _session.ProcessId == processId) _session.Add(timestampMs);
        }
    }

    /// <summary>FPS do processo no último segundo (null se ele não entregou quadros recentemente).</summary>
    public FrameSnapshot? Snapshot(int processId, double nowMs)
    {
        lock (_lock)
        {
            if (!_frames.TryGetValue(processId, out var q)) return null;
            var recent = q.Where(t => nowMs - t <= 1000).ToList();
            if (recent.Count < 2) return null;
            double span = recent[^1] - recent[0];
            double frameTime = span / (recent.Count - 1);
            return new FrameSnapshot(processId, frameTime > 0 ? 1000 / frameTime : 0, frameTime, recent.Count);
        }
    }

    /// <summary>Processo com mais quadros no último segundo (o jogo, quando a janela em foco não entrega quadros).</summary>
    public int? Busiest(double nowMs)
    {
        lock (_lock)
        {
            return _frames.Select(kv => (kv.Key, Count: kv.Value.Count(t => nowMs - t <= 1000)))
                          .Where(x => x.Count >= 5)
                          .OrderByDescending(x => x.Count)
                          .Select(x => (int?)x.Key)
                          .FirstOrDefault();
        }
    }

    public void StartSession(int processId)
    {
        lock (_lock) _session = new FrameSession(processId);
    }

    public FrameSession? StopSession()
    {
        lock (_lock)
        {
            var s = _session;
            _session = null;
            return s;
        }
    }

    public FrameSession? CurrentSession
    {
        get { lock (_lock) return _session; }
    }

    /// <summary>Descarta processos sem quadros há mais de 1,5 s.</summary>
    public void Prune(double nowMs)
    {
        lock (_lock)
            foreach (var pid in _frames.Where(kv => kv.Value.Count == 0 || nowMs - kv.Value.Last() > KeepMs).Select(kv => kv.Key).ToList())
                _frames.Remove(pid);
    }
}

/// <summary>Gravação de uma sessão de jogo: todos os intervalos entre quadros, para FPS médio e 1% low.</summary>
public sealed class FrameSession
{
    private readonly List<double> _frameTimes = new();
    private double? _last;

    public FrameSession(int processId) => ProcessId = processId;

    public int ProcessId { get; }
    public int Frames => _frameTimes.Count;
    public double DurationMs => _frameTimes.Sum();

    internal void Add(double timestampMs)
    {
        if (_last is { } prev)
        {
            double dt = timestampMs - prev;
            if (dt > 0 && dt < 5000) _frameTimes.Add(dt); // pausas longas (menu, alt+tab) não contam
        }
        _last = timestampMs;
    }

    public double AverageFps => Frames > 0 ? 1000 * Frames / DurationMs : 0;

    /// <summary>1% low: FPS equivalente ao tempo de quadro do percentil 99 (mesma fórmula do benchmark de GPU).</summary>
    public double Low1Fps
    {
        get
        {
            if (Frames < 10) return 0;
            var sorted = _frameTimes.OrderBy(x => x).ToList();
            double p99 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99))];
            return p99 > 0 ? 1000 / p99 : 0;
        }
    }

    public static FrameSession FromFrameTimes(int processId, IEnumerable<double> frameTimesMs)
    {
        var s = new FrameSession(processId);
        double t = 0;
        s.Add(t);
        foreach (var ft in frameTimesMs) s.Add(t += ft);
        return s;
    }
}
