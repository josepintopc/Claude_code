using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SupernovaBench.Core.Benchmarks.Disk;

/// <summary>Um teste do perfil padrão do CrystalDiskMark.</summary>
public sealed record DiskTestSpec(string Id, string Label, int BlockSize, int Queue, bool Random);

/// <summary>Medida de um teste: MB/s (1 MB = 1.000.000 bytes, como no CrystalDiskMark), IOPS e latência média.</summary>
public sealed record DiskMeasure(double MBps, double Iops, double LatencyUs);

public sealed class DiskBenchOptions
{
    /// <summary>Pasta onde o arquivo de teste é criado (normalmente a raiz da unidade).</summary>
    public string Directory { get; init; } = Path.GetTempPath();
    public long FileSize { get; init; } = 1L << 30;
    public int Runs { get; init; } = 3;
    public TimeSpan TestDuration { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Lê e grava direto no disco, sem o cache do Windows (FILE_FLAG_NO_BUFFERING). Só tem efeito no Windows.</summary>
    public bool Unbuffered { get; init; } = true;
}

public sealed class DiskBenchException : Exception
{
    public DiskBenchException(string key, string message) : base(message) => Key = key;

    /// <summary>"space" (sem espaço livre) ou "access" (sem permissão para gravar).</summary>
    public string Key { get; }
}

/// <summary>Resultado completo: 4 testes × leitura/gravação.</summary>
public sealed class DiskBenchResult
{
    public Dictionary<string, DiskMeasure> Read { get; } = new();
    public Dictionary<string, DiskMeasure> Write { get; } = new();
    public long FileSize { get; init; }
    public double TestSeconds { get; init; }
    public double Seconds { get; set; }

    public double Score => DiskBench.ComputeScore(this);
}

/// <summary>
/// Benchmark de disco no estilo CrystalDiskMark: SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1 e RND4K Q1T1, em leitura e gravação.
/// A fila (Q) é feita com Q operações assíncronas simultâneas sobre um único arquivo aberto sem cache.
/// </summary>
public sealed class DiskBench
{
    public const string EngineVersion = "1.0";
    public const string FileName = "SupernovaBench.test.tmp";
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private const int Alignment = 4096;

    public static readonly IReadOnlyList<DiskTestSpec> Tests = new[]
    {
        new DiskTestSpec("seq1m_q8t1", "SEQ1M Q8T1", 1 << 20, 8, false),
        new DiskTestSpec("seq1m_q1t1", "SEQ1M Q1T1", 1 << 20, 1, false),
        new DiskTestSpec("rnd4k_q32t1", "RND4K Q32T1", 4096, 32, true),
        new DiskTestSpec("rnd4k_q1t1", "RND4K Q1T1", 4096, 1, true),
    };

    /// <summary>Referência da pontuação 1000: SSD SATA típico (MB/s de leitura e gravação por teste).</summary>
    public static readonly IReadOnlyDictionary<string, (double Read, double Write)> Reference = new Dictionary<string, (double, double)>
    {
        ["seq1m_q8t1"] = (560, 530),
        ["seq1m_q1t1"] = (520, 490),
        ["rnd4k_q32t1"] = (380, 330),
        ["rnd4k_q1t1"] = (45, 120),
    };

    public static readonly long[] FileSizes = { 256L << 20, 1L << 30, 4L << 30 };

    private readonly DiskBenchOptions _options;

    public DiskBench(DiskBenchOptions options) => _options = options;

    public DiskBenchOptions Options => _options;

    /// <summary>Preparando o arquivo (0..1).</summary>
    public Action<double>? Preparing { get; set; }

    /// <summary>Começou um teste (spec, gravação?, rodada 1..N).</summary>
    public Action<DiskTestSpec, bool, int>? Started { get; set; }

    /// <summary>Melhor resultado até agora do teste (spec, gravação?, medida).</summary>
    public Action<DiskTestSpec, bool, DiskMeasure>? Measured { get; set; }

    public static string SizeId(long bytes) => bytes >= 1L << 30 ? $"{bytes >> 30}g" : $"{bytes >> 20}m";

    public static string PresetId(long fileSize, TimeSpan duration) => $"{SizeId(fileSize)}-{(int)Math.Round(duration.TotalSeconds)}s";

    public static string MetricKey(string testId, bool write) => testId + (write ? ".write" : ".read");

    /// <summary>1000 × média geométrica das 8 medidas divididas pelas de um SSD SATA típico.</summary>
    public static double ComputeScore(DiskBenchResult r)
    {
        double logSum = 0;
        int n = 0;
        foreach (var t in Tests)
        {
            var (refRead, refWrite) = Reference[t.Id];
            if (!r.Read.TryGetValue(t.Id, out var rd) || !r.Write.TryGetValue(t.Id, out var wr)) return 0;
            if (rd.MBps <= 0 || wr.MBps <= 0) return 0;
            logSum += Math.Log(rd.MBps / refRead) + Math.Log(wr.MBps / refWrite);
            n += 2;
        }
        return n == 0 ? 0 : Math.Round(1000 * Math.Exp(logSum / n), 1);
    }

    public BenchmarkResult ToResult(DiskBenchResult r, string? device)
    {
        var metrics = new Dictionary<string, double>();
        foreach (var (dict, write) in new[] { (r.Read, false), (r.Write, true) })
            foreach (var (id, m) in dict)
            {
                string k = MetricKey(id, write);
                metrics[k] = Math.Round(m.MBps, 2);
                metrics[k + ".iops"] = Math.Round(m.Iops, 1);
                metrics[k + ".lat"] = Math.Round(m.LatencyUs, 2);
            }
        return new BenchmarkResult
        {
            Test = TestIds.DiskBench,
            Score = r.Score,
            Unit = "pts",
            CpuName = MachineInfo.CpuName(),
            Device = string.IsNullOrWhiteSpace(device) ? null : device,
            Metrics = metrics,
            Threads = 1,
            Seconds = Math.Round(r.Seconds, 3),
            Preset = PresetId(r.FileSize, TimeSpan.FromSeconds(r.TestSeconds)),
            EngineVersion = EngineVersion,
            AppVersion = AppInfo.Version,
        };
    }

    /// <summary>Roda os 8 testes (leituras primeiro, como o CrystalDiskMark). Retorna null se cancelado.</summary>
    public async Task<DiskBenchResult?> RunAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string dir = Path.GetFullPath(_options.Directory);
        long fileSize = Math.Max(16L << 20, _options.FileSize / (1 << 20) * (1 << 20));
        CheckFreeSpace(dir, fileSize);
        string path = Path.Combine(dir, FileName);

        var options = FileOptions.Asynchronous | FileOptions.WriteThrough | FileOptions.DeleteOnClose;
        if (_options.Unbuffered && OperatingSystem.IsWindows()) options |= NoBuffering;

        SafeFileHandle handle;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, options, fileSize);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new DiskBenchException("access", ex.Message);
        }

        try
        {
            await PrepareAsync(handle, fileSize, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) return null;

            var result = new DiskBenchResult { FileSize = fileSize, TestSeconds = _options.TestDuration.TotalSeconds };
            foreach (bool write in new[] { false, true })
            {
                foreach (var spec in Tests)
                {
                    DiskMeasure? best = null;
                    for (int run = 1; run <= Math.Max(1, _options.Runs); run++)
                    {
                        if (ct.IsCancellationRequested) return null;
                        Started?.Invoke(spec, write, run);
                        var m = await MeasureAsync(handle, fileSize, spec, write, _options.TestDuration, ct).ConfigureAwait(false);
                        if (ct.IsCancellationRequested) return null;
                        if (best == null || m.MBps > best.MBps) best = m;
                        Measured?.Invoke(spec, write, best);
                        if (_options.Interval > TimeSpan.Zero)
                            await Task.Delay(_options.Interval, ct).ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
                    }
                    (write ? result.Write : result.Read)[spec.Id] = best!;
                }
            }
            result.Seconds = sw.Elapsed.TotalSeconds;
            return result;
        }
        finally
        {
            handle.Dispose();
            try { if (File.Exists(path)) File.Delete(path); } catch { /* DeleteOnClose já remove no Windows */ }
        }
    }

    private static void CheckFreeSpace(string dir, long fileSize)
    {
        if (!System.IO.Directory.Exists(dir)) throw new DiskBenchException("access", dir);
        try
        {
            var root = Path.GetPathRoot(dir);
            if (string.IsNullOrEmpty(root)) return;
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < fileSize + fileSize / 10)
                throw new DiskBenchException("space", $"{drive.AvailableFreeSpace} < {fileSize}");
        }
        catch (ArgumentException) { }
        catch (IOException) { }
    }

    /// <summary>Preenche o arquivo inteiro com dados aleatórios (incompressíveis), para as leituras virem do disco de verdade.</summary>
    private async Task PrepareAsync(SafeFileHandle handle, long fileSize, CancellationToken ct)
    {
        const int Block = 1 << 20;
        using var mem = new AlignedBuffer(Block);
        FillRandom(mem.Span, 7);
        long written = 0;
        int lastPct = -1;
        while (written < fileSize && !ct.IsCancellationRequested)
        {
            int len = (int)Math.Min(Block, fileSize - written);
            await RandomAccess.WriteAsync(handle, mem.Memory[..len], written, ct).ConfigureAwait(false);
            written += len;
            int pct = (int)(100 * written / fileSize);
            if (pct != lastPct)
            {
                lastPct = pct;
                Preparing?.Invoke(written / (double)fileSize);
            }
        }
    }

    internal static async Task<DiskMeasure> MeasureAsync(SafeFileHandle handle, long fileSize, DiskTestSpec spec, bool write,
                                                        TimeSpan duration, CancellationToken ct)
    {
        long blocks = fileSize / spec.BlockSize;
        long nextSeq = -1;
        long ops = 0, latencyTicks = 0;
        int stop = 0;
        var buffers = new AlignedBuffer[spec.Queue];
        for (int q = 0; q < spec.Queue; q++)
        {
            buffers[q] = new AlignedBuffer(spec.BlockSize);
            FillRandom(buffers[q].Span, 1000 + q);
        }

        async Task Worker(int q)
        {
            var mem = buffers[q].Memory;
            ulong rng = (ulong)(q + 1) * 0x9E3779B97F4A7C15UL;
            long localOps = 0, localTicks = 0;
            while (Volatile.Read(ref stop) == 0 && !ct.IsCancellationRequested)
            {
                long block;
                if (spec.Random)
                {
                    rng ^= rng << 13; rng ^= rng >> 7; rng ^= rng << 17;
                    block = (long)(rng % (ulong)blocks);
                }
                else
                {
                    block = Interlocked.Increment(ref nextSeq) % blocks;
                }
                long offset = block * spec.BlockSize;
                long t0 = Stopwatch.GetTimestamp();
                if (write) await RandomAccess.WriteAsync(handle, mem, offset).ConfigureAwait(false);
                else await RandomAccess.ReadAsync(handle, mem, offset).ConfigureAwait(false);
                localTicks += Stopwatch.GetTimestamp() - t0;
                localOps++;
            }
            Interlocked.Add(ref ops, localOps);
            Interlocked.Add(ref latencyTicks, localTicks);
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(0, spec.Queue).Select(q => Task.Run(() => Worker(q))).ToArray();
            try { await Task.Delay(duration, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            Volatile.Write(ref stop, 1);
            await Task.WhenAll(tasks).ConfigureAwait(false);
            sw.Stop();

            double secs = sw.Elapsed.TotalSeconds;
            double mbps = ops * (double)spec.BlockSize / 1e6 / secs;
            double iops = ops / secs;
            double latUs = ops > 0 ? latencyTicks * 1e6 / Stopwatch.Frequency / ops : 0;
            return new DiskMeasure(mbps, iops, latUs);
        }
        finally
        {
            foreach (var b in buffers) ((IDisposable)b).Dispose();
        }
    }

    private static void FillRandom(Span<byte> span, int seed)
    {
        ulong x = (ulong)seed * 0x9E3779B97F4A7C15UL + 1;
        var words = MemoryMarshal.Cast<byte, ulong>(span);
        for (int i = 0; i < words.Length; i++)
        {
            x ^= x << 13; x ^= x >> 7; x ^= x << 17;
            words[i] = x;
        }
    }

    /// <summary>Buffer de memória nativa alinhado a 4 KB (exigência da E/S sem cache), exposto como <see cref="Memory{T}"/>.</summary>
    private sealed unsafe class AlignedBuffer : MemoryManager<byte>
    {
        private byte* _ptr;
        private readonly int _length;

        public AlignedBuffer(int length)
        {
            _length = length;
            _ptr = (byte*)NativeMemory.AlignedAlloc((nuint)length, Alignment);
        }

        public Span<byte> Span => new(_ptr, _length);

        public override Span<byte> GetSpan() => new(_ptr, _length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(_ptr + elementIndex);

        public override void Unpin() { }

        protected override void Dispose(bool disposing)
        {
            if (_ptr != null)
            {
                NativeMemory.AlignedFree(_ptr);
                _ptr = null;
            }
        }
    }
}
