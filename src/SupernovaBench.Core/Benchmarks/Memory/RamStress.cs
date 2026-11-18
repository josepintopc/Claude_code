using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SupernovaBench.Core.Benchmarks.Memory;

/// <summary>Erro encontrado: posição (bytes desde o início da área testada), valor esperado, lido e o padrão.</summary>
public sealed record RamError(long Offset, ulong Expected, ulong Actual, string Pattern)
{
    public int BitsFlipped => BitOperations.PopCount(Expected ^ Actual);
}

/// <summary>
/// Teste de estabilidade da RAM dentro do Windows (estilo TestMem5/HCI MemTest): reserva uma parte da memória livre,
/// divide entre as threads e repete padrões de escrita e conferência. Não alcança a memória já usada pelo Windows.
/// </summary>
public sealed unsafe class RamStress
{
    /// <summary>Padrões, na ordem em que rodam. Uma "passagem" = todos os padrões em toda a área.</summary>
    public static readonly string[] Patterns = { "address", "inversions", "walking", "random" };

    private const long BlockBytes = 256L << 20;
    private const int ChunkWords = 1 << 17; // 1 MB por vez entre verificações de parada
    public const int MaxErrorsKept = 50;

    private readonly Stopwatch _clock = new();
    private readonly List<RamError> _errors = new();
    private CancellationTokenSource? _cts;
    private Thread[] _threads = Array.Empty<Thread>();
    private long[] _progressWords = Array.Empty<long>();
    private long _wordsPerThread;
    private long _bytesProcessed;
    private long _errorCount;
    private int _running;
    private int _patternIndex;
    private double _maxPasses;

    public long Bytes { get; private set; }
    public int Threads { get; private set; }
    public bool Running => Volatile.Read(ref _running) > 0;
    public TimeSpan Elapsed => _clock.Elapsed;
    public long ErrorCount => Interlocked.Read(ref _errorCount);
    public string? Error { get; private set; }

    /// <summary>Padrão em andamento na thread 0 (os outros seguem o mesmo ritmo).</summary>
    public string CurrentPattern => Patterns[Volatile.Read(ref _patternIndex) % Patterns.Length];

    /// <summary>Velocidade média (MB/s) de leitura + escrita.</summary>
    public double MBps => Elapsed.TotalSeconds > 0 ? Interlocked.Read(ref _bytesProcessed) / 1e6 / Elapsed.TotalSeconds : 0;

    /// <summary>Cobertura em % (100% = uma passagem completa de todos os padrões; passa de 100% com o tempo).</summary>
    public double Coverage
    {
        get
        {
            if (_progressWords.Length == 0 || _wordsPerThread == 0) return 0;
            double perPass = (double)_wordsPerThread * Patterns.Length;
            double sum = 0;
            for (int i = 0; i < _progressWords.Length; i++) sum += Volatile.Read(ref _progressWords[i]) / perPass;
            return 100 * sum / _progressWords.Length;
        }
    }

    public double Passes => Math.Floor(Coverage / 100);

    public IReadOnlyList<RamError> Errors
    {
        get { lock (_errors) return _errors.ToList(); }
    }

    /// <summary>Teste: inverte um bit nessa posição logo depois do padrão "address" ser escrito (uma vez).</summary>
    internal long InjectFaultAt { get; set; } = -1;

    /// <summary>
    /// Começa o teste. <paramref name="maxPasses"/> encerra ao atingir essa cobertura (1 = 100%); null = até parar ou acabar o tempo.
    /// </summary>
    public void Start(long bytes, int threads, TimeSpan? duration = null, double? maxPasses = null)
    {
        if (Running) throw new InvalidOperationException("already running");
        Threads = Math.Clamp(threads, 1, Environment.ProcessorCount);
        long perThread = Math.Max(1L << 20, bytes / Threads / 8 * 8);
        Bytes = perThread * Threads;
        _wordsPerThread = perThread / 8;
        _progressWords = new long[Threads];
        _bytesProcessed = 0;
        _errorCount = 0;
        _patternIndex = 0;
        _maxPasses = maxPasses ?? double.PositiveInfinity;
        Error = null;
        lock (_errors) _errors.Clear();

        _cts = new CancellationTokenSource();
        if (duration is { } d) _cts.CancelAfter(d);
        var ct = _cts.Token;
        _clock.Restart();
        Volatile.Write(ref _running, Threads);
        _threads = new Thread[Threads];
        for (int t = 0; t < Threads; t++)
        {
            int ti = t;
            _threads[t] = new Thread(() => Worker(ti, perThread, ct))
            {
                IsBackground = true,
                Name = $"Supernova RAM {ti}",
                Priority = ThreadPriority.BelowNormal,
            };
            _threads[t].Start();
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        Join();
    }

    public void Join()
    {
        foreach (var t in _threads) t.Join();
        _clock.Stop();
    }

    // ------------------------------------------------------------------ trabalho de cada thread

    private void Worker(int index, long bytes, CancellationToken ct)
    {
        var blocks = new List<(IntPtr Ptr, long Words)>();
        try
        {
            for (long done = 0; done < bytes;)
            {
                long size = Math.Min(BlockBytes, bytes - done);
                var p = (IntPtr)NativeMemory.Alloc((nuint)size);
                blocks.Add((p, size / 8));
                done += size;
            }

            long baseOffset = index * bytes;
            ulong seed = 0x2545F4914F6CDD1DUL * (ulong)(index + 1);
            for (long pass = 0; !ct.IsCancellationRequested; pass++)
            {
                for (int pi = 0; pi < Patterns.Length && !ct.IsCancellationRequested; pi++)
                {
                    if (index == 0) Volatile.Write(ref _patternIndex, pi);
                    RunPattern(pi, pass, blocks, baseOffset, seed + (ulong)pass, ct, index);
                    if (ct.IsCancellationRequested) break;
                    if (Volatile.Read(ref _progressWords[index]) / ((double)_wordsPerThread * Patterns.Length) >= _maxPasses - 1e-9) return;
                }
            }
        }
        catch (OutOfMemoryException)
        {
            Error ??= "out-of-memory";
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            Error ??= ex.Message;
            _cts?.Cancel();
        }
        finally
        {
            foreach (var (p, _) in blocks) NativeMemory.Free((void*)p);
            if (Interlocked.Decrement(ref _running) == 0) _clock.Stop();
        }
    }

    private void RunPattern(int pattern, long pass, List<(IntPtr Ptr, long Words)> blocks, long baseOffset, ulong seed,
                            CancellationToken ct, int index)
    {
        string name = Patterns[pattern];
        long blockStart = 0;
        // Cada padrão conta como 1 unidade de cobertura por palavra (escrita + conferência).
        foreach (var (ptr, words) in blocks)
        {
            ulong* p = (ulong*)ptr;
            long off = baseOffset + blockStart * 8;
            switch (pattern)
            {
                case 0: // endereço-no-endereço: cada palavra guarda a própria posição
                    FillAddress(p, words, off, seed, ct);
                    if (InjectFaultAt >= off && InjectFaultAt < off + words * 8)
                    {
                        p[(InjectFaultAt - off) / 8] ^= 1UL << 7;
                        InjectFaultAt = -1;
                    }
                    VerifyAddress(p, words, off, seed, name, ct, index);
                    break;
                case 1: // inversões móveis: 0x55.. sobe, confere e troca por 0xAA..; desce conferindo e volta
                    const ulong A = 0x5555555555555555UL, B = 0xAAAAAAAAAAAAAAAAUL;
                    FillConstant(p, words, A, ct);
                    Invert(p, words, off, name, A, B, ascending: true, ct, index);
                    Invert(p, words, off, name, B, A, ascending: false, ct, index);
                    break;
                case 2: // "walking ones": um único bit 1 que anda pela palavra
                    FillWalking(p, words, pass, ct);
                    VerifyWalking(p, words, off, pass, name, ct, index);
                    break;
                default: // aleatório com semente: gera, depois regenera para conferir
                    FillRandom(p, words, seed ^ (ulong)off, ct);
                    VerifyRandom(p, words, seed ^ (ulong)off, off, name, ct, index);
                    break;
            }
            blockStart += words;
            if (ct.IsCancellationRequested) return;
        }
    }

    // Laços especializados por padrão (sem delegates), para chegar perto da largura de banda da memória.

    private void FillAddress(ulong* p, long words, long off, ulong seed, CancellationToken ct)
    {
        for (long start = 0; start < words && !ct.IsCancellationRequested; start += ChunkWords)
        {
            long end = Math.Min(words, start + ChunkWords);
            for (long i = start; i < end; i++) p[i] = (ulong)(off + i * 8) ^ seed;
            Interlocked.Add(ref _bytesProcessed, (end - start) * 8);
        }
    }

    private void VerifyAddress(ulong* p, long words, long off, ulong seed, string name, CancellationToken ct, int index)
    {
        for (long start = 0; start < words && !ct.IsCancellationRequested; start += ChunkWords)
        {
            long end = Math.Min(words, start + ChunkWords);
            for (long i = start; i < end; i++)
            {
                ulong expected = (ulong)(off + i * 8) ^ seed, actual = p[i];
                if (actual != expected) Report(off + i * 8, expected, actual, name);
            }
            Interlocked.Add(ref _bytesProcessed, (end - start) * 8);
            Interlocked.Add(ref _progressWords[index], end - start);
        }
    }

    private void FillConstant(ulong* p, long words, ulong value, CancellationToken ct)
    {
        for (long start = 0; start < words && !ct.IsCancellationRequested; start += ChunkWords)
        {
            long end = Math.Min(words, start + ChunkWords);
            new Span<ulong>(p + start, (int)(end - start)).Fill(value);
            Interlocked.Add(ref _bytesProcessed, (end - start) * 8);
        }
    }

    private void FillWalking(ulong* p, long words, long pass, CancellationToken ct)
    {
        for (long start = 0; start < words && !ct.IsCancellationRequested; start += ChunkWords)
        {
            long end = Math.Min(words, start + ChunkWords);
            for (long i = start; i < end; i++) p[i] = 1UL << (int)((i + pass) & 63);
            Interlocked.Add(ref _bytesProcessed, (end - start) * 8);
        }
    }

    private void VerifyWalking(ulong* p, long words, long off, long pass, string name, CancellationToken ct, int index)
    {
        for (long start = 0; start < words && !ct.IsCancellationRequested; start += ChunkWords)
        {
            long end = Math.Min(words, start + ChunkWords);
            for (long i = start; i < end; i++)
            {
                ulong expected = 1UL << (int)((i + pass) & 63), actual = p[i];
                if (actual != expected) Report(off + i * 8, expected, actual, name);
            }
            Interlocked.Add(ref _bytesProcessed, (end - start) * 8);
            Interlocked.Add(ref _progressWords[index], end - start);
        }
    }

    private void Invert(ulong* p, long words, long off, string name, ulong expect, ulong write, bool ascending, CancellationToken ct, int index)
    {
        for (long n = 0; n < words && !ct.IsCancellationRequested; n += ChunkWords)
        {
            long count = Math.Min(ChunkWords, words - n);
            for (long k = 0; k < count; k++)
            {
                long i = ascending ? n + k : words - 1 - (n + k);
                ulong actual = p[i];
                if (actual != expect) Report(off + i * 8, expect, actual, name);
                p[i] = write;
            }
            Interlocked.Add(ref _bytesProcessed, count * 16);
            // As duas varreduras juntas valem uma unidade de cobertura.
            Interlocked.Add(ref _progressWords[index], ascending ? count / 2 : count - count / 2);
        }
    }

    private void FillRandom(ulong* p, long words, ulong seed, CancellationToken ct)
    {
        ulong x = seed | 1;
        for (long start = 0; start < words && !ct.IsCancellationRequested; start += ChunkWords)
        {
            long end = Math.Min(words, start + ChunkWords);
            for (long i = start; i < end; i++)
            {
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                p[i] = x;
            }
            Interlocked.Add(ref _bytesProcessed, (end - start) * 8);
        }
    }

    private void VerifyRandom(ulong* p, long words, ulong seed, long off, string name, CancellationToken ct, int index)
    {
        ulong x = seed | 1;
        for (long start = 0; start < words && !ct.IsCancellationRequested; start += ChunkWords)
        {
            long end = Math.Min(words, start + ChunkWords);
            for (long i = start; i < end; i++)
            {
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                ulong actual = p[i];
                if (actual != x) Report(off + i * 8, x, actual, name);
            }
            Interlocked.Add(ref _bytesProcessed, (end - start) * 8);
            Interlocked.Add(ref _progressWords[index], end - start);
        }
    }

    private void Report(long offset, ulong expected, ulong actual, string pattern)
    {
        Interlocked.Increment(ref _errorCount);
        lock (_errors)
            if (_errors.Count < MaxErrorsKept) _errors.Add(new RamError(offset, expected, actual, pattern));
    }
}
