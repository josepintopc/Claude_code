using SupernovaBench.Core.Benchmarks;

namespace SupernovaBench.Core.Storage;

/// <summary>Histórico de resultados (próprios e importados), salvo em history.json. Thread-safe.</summary>
public sealed class ResultHistory
{
    public const int MaxEntries = 500;

    private readonly string _path;
    private readonly object _lock = new();
    private List<BenchmarkResult> _items;

    public event EventHandler? Changed;

    public ResultHistory(string? path = null)
    {
        _path = path ?? AppPaths.HistoryFile;
        _items = JsonFile.Load<HistoryFile>(_path)?.Results ?? new List<BenchmarkResult>();
    }

    public IReadOnlyList<BenchmarkResult> All
    {
        get { lock (_lock) return _items.OrderByDescending(r => r.Timestamp).ToList(); }
    }

    public void Add(BenchmarkResult result) => AddRange(new[] { result });

    /// <summary>Adiciona vários resultados de uma vez (ignora IDs repetidos, como ao importar o mesmo relatório duas vezes).</summary>
    public int AddRange(IEnumerable<BenchmarkResult> results)
    {
        int added = 0;
        lock (_lock)
        {
            var ids = _items.Select(r => r.Id).ToHashSet();
            foreach (var r in results)
            {
                if (!ids.Add(r.Id)) continue;
                _items.Add(r);
                added++;
            }
            if (_items.Count > MaxEntries)
                _items = _items.OrderByDescending(r => r.Timestamp).Take(MaxEntries).ToList();
            if (added > 0) Persist();
        }
        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(r => r.Id == id) > 0;
            if (removed) Persist();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resultados do mesmo teste/preset/motor, do melhor para o pior (ranking).</summary>
    public IReadOnlyList<BenchmarkResult> Ranking(string test, string preset, string engineVersion)
    {
        lock (_lock)
            return _items.Where(r => r.Test == test && r.Preset == preset && r.EngineVersion == engineVersion)
                         .OrderByDescending(r => r.Score).ToList();
    }

    public BenchmarkResult? Latest(string test, bool includeImported = false)
    {
        lock (_lock)
            return _items.Where(r => r.Test == test && (includeImported || !r.Imported))
                         .OrderByDescending(r => r.Timestamp).FirstOrDefault();
    }

    private void Persist() => JsonFile.Save(_path, new HistoryFile { Results = _items });

    private sealed class HistoryFile
    {
        public int Version { get; set; } = 1;
        public List<BenchmarkResult> Results { get; set; } = new();
    }
}
