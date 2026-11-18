using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using SupernovaBench.Core.Info;

namespace SupernovaBench.Core.Benchmarks.Memory;

public enum MemLevel { Memory, L1, L2, L3 }

public enum MemOp { Read, Write, Copy, Latency }

/// <summary>Resultado do benchmark de memória: largura de banda (GB/s) e latência (ns) por nível.</summary>
public sealed class MemoryBenchResult
{
    public Dictionary<string, double> Values { get; } = new();
    public double Seconds { get; init; }
    public int Threads { get; init; }
    public bool Quick { get; init; }

    public double? Get(MemLevel level, MemOp op) => Values.TryGetValue(MemoryBench.Key(level, op), out var v) ? v : null;

    public double Score => MemoryBench.ComputeScore(Get(MemLevel.Memory, MemOp.Read) ?? 0, Get(MemLevel.Memory, MemOp.Write) ?? 0,
                                                    Get(MemLevel.Memory, MemOp.Copy) ?? 0, Get(MemLevel.Memory, MemOp.Latency) ?? 0);
}

/// <summary>
/// Benchmark de cache e memória no estilo do AIDA64: leitura, gravação e cópia (todas as threads, GB/s) e latência
/// (perseguição de ponteiros numa única thread, ns) na RAM e nos caches L1, L2 e L3.
/// </summary>
public sealed unsafe class MemoryBench
{
    public const string EngineVersion = "1.0";

    /// <summary>Referência da pontuação 1000: DDR4-3200 em dual channel típico.</summary>
    public const double RefReadGBs = 45, RefWriteGBs = 45, RefCopyGBs = 40, RefLatencyNs = 80;

    public static readonly MemLevel[] Levels = { MemLevel.Memory, MemLevel.L1, MemLevel.L2, MemLevel.L3 };
    public static readonly MemOp[] Ops = { MemOp.Read, MemOp.Write, MemOp.Copy, MemOp.Latency };

    private readonly double _seconds;
    private readonly int _repeats;
    private static long _sink;

    public MemoryBench(bool quick = false, int? threads = null, CacheSizes? caches = null)
    {
        Quick = quick;
        Threads = Math.Max(1, threads ?? Environment.ProcessorCount);
        Caches = caches ?? CacheInfo.Detect();
        _seconds = quick ? 0.05 : 0.4;
        _repeats = quick ? 1 : 3;
    }

    public bool Quick { get; }
    public int Threads { get; }
    public CacheSizes Caches { get; }

    /// <summary>Chamado ao começar cada medida (nível, operação).</summary>
    public Action<MemLevel, MemOp>? Started { get; set; }

    /// <summary>Chamado com o valor de cada medida (GB/s ou ns).</summary>
    public Action<MemLevel, MemOp, double>? Measured { get; set; }

    public static string LevelId(MemLevel level) => level switch
    {
        MemLevel.L1 => "l1", MemLevel.L2 => "l2", MemLevel.L3 => "l3", _ => "mem",
    };

    public static string OpId(MemOp op) => op.ToString().ToLowerInvariant();

    public static string Key(MemLevel level, MemOp op) => LevelId(level) + "." + OpId(op);

    /// <summary>1000 × média geométrica de (leitura, gravação e cópia ÷ referência, referência ÷ latência).</summary>
    public static double ComputeScore(double readGBs, double writeGBs, double copyGBs, double latencyNs)
    {
        if (readGBs <= 0 || writeGBs <= 0 || copyGBs <= 0 || latencyNs <= 0) return 0;
        double g = Math.Pow(readGBs / RefReadGBs * (writeGBs / RefWriteGBs) * (copyGBs / RefCopyGBs) * (RefLatencyNs / latencyNs), 0.25);
        return Math.Round(1000 * g, 1);
    }

    /// <summary>Tamanho do buffer de cada thread (largura de banda) no nível dado.</summary>
    public long ThreadBufferBytes(MemLevel level)
    {
        int tpc = Caches.ThreadsPerCore;
        long size = level switch
        {
            MemLevel.L1 => Caches.L1 / (2 * tpc),
            MemLevel.L2 => Caches.L2 / (2 * tpc),
            MemLevel.L3 => Math.Max(Caches.L3Total, Caches.L3) / (2L * Threads),
            _ => MemoryTotalBytes() / Threads,
        };
        return Math.Max(4096, size / 256 * 256);
    }

    /// <summary>Total usado no nível RAM: bem maior que o L3, para que nada venha do cache.</summary>
    public long MemoryTotalBytes()
    {
        if (Quick) return 64L << 20;
        long want = Math.Max(512L << 20, 8 * Math.Max(Caches.L3Total, Caches.L3));
        var (_, available) = MemoryStatus.Read();
        if (available > 0) want = Math.Min(want, available / 4);
        return Math.Max(64L << 20, Math.Min(want, 2L << 30));
    }

    /// <summary>Região usada na latência: metade do cache no nível, ou 4× o L3 para a RAM.</summary>
    public long LatencyBytes(MemLevel level) => level switch
    {
        MemLevel.L1 => Math.Max(4096, Caches.L1 / 2),
        MemLevel.L2 => Math.Max(Caches.L1 * 2, Caches.L2 / 2),
        MemLevel.L3 => Math.Max(Caches.L2 * 2, Caches.L3 / 2),
        _ => Quick ? 64L << 20 : Math.Clamp(4 * Math.Max(Caches.L3Total, Caches.L3), 256L << 20, 1L << 30),
    };

    /// <summary>Roda tudo (chame fora da thread da interface). Retorna null se cancelado.</summary>
    public MemoryBenchResult? Run(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var values = new Dictionary<string, double>();

        foreach (var level in Levels)
        {
            foreach (var op in Ops)
            {
                if (ct.IsCancellationRequested) return null;
                Started?.Invoke(level, op);
                double v = op == MemOp.Latency ? MeasureLatency(LatencyBytes(level), ct) : MeasureBandwidth(op, level, ct);
                if (ct.IsCancellationRequested) return null;
                v = Math.Round(v, 2);
                values[Key(level, op)] = v;
                Measured?.Invoke(level, op, v);
            }
        }

        return new MemoryBenchResult { Threads = Threads, Quick = Quick, Seconds = sw.Elapsed.TotalSeconds }.With(values);
    }

    public BenchmarkResult ToResult(MemoryBenchResult r, string? device) => new()
    {
        Test = TestIds.MemoryBench,
        Score = r.Score,
        Unit = "pts",
        CpuName = MachineInfo.CpuName(),
        Device = string.IsNullOrWhiteSpace(device) ? null : device,
        Metrics = new Dictionary<string, double>(r.Values),
        Threads = r.Threads,
        Seconds = Math.Round(r.Seconds, 3),
        Preset = r.Quick ? "quick" : "standard",
        EngineVersion = EngineVersion,
        AppVersion = AppInfo.Version,
    };

    // ------------------------------------------------------------------ largura de banda

    private double MeasureBandwidth(MemOp op, MemLevel level, CancellationToken ct)
    {
        long size = ThreadBufferBytes(level);
        // Cópia: metade do buffer é a origem e metade o destino.
        var buffers = new byte*[Threads];
        try
        {
            for (int t = 0; t < Threads; t++)
            {
                buffers[t] = (byte*)NativeMemory.AlignedAlloc((nuint)size, 4096);
                NativeMemory.Fill(buffers[t], (nuint)size, (byte)(t + 1));
            }

            bool nonTemporal = level == MemLevel.Memory && Avx.IsSupported;
            double best = 0;
            for (int rep = 0; rep < _repeats && !ct.IsCancellationRequested; rep++)
                best = Math.Max(best, BandwidthOnce(op, buffers, size, nonTemporal, ct));
            return best;
        }
        finally
        {
            foreach (var b in buffers)
                if (b != null) NativeMemory.AlignedFree(b);
        }
    }

    private double BandwidthOnce(MemOp op, byte*[] buffers, long size, bool nonTemporal, CancellationToken ct)
    {
        int n = buffers.Length;
        var bytes = new long[n];
        int stop = 0;
        using var start = new Barrier(n + 1);
        var threads = new Thread[n];
        for (int t = 0; t < n; t++)
        {
            int ti = t;
            threads[t] = new Thread(() =>
            {
                byte* p = buffers[ti];
                long done = 0, sink = 0;
                start.SignalAndWait();
                while (Volatile.Read(ref stop) == 0)
                {
                    switch (op)
                    {
                        case MemOp.Read:
                            sink ^= ReadPass(p, size);
                            done += size;
                            break;
                        case MemOp.Write:
                            if (nonTemporal) WritePassNonTemporal(p, size);
                            else WritePass(p, size);
                            done += size;
                            break;
                        default:
                            long half = size / 2 / 64 * 64;
                            Buffer.MemoryCopy(p, p + half, half, half);
                            done += half;
                            break;
                    }
                }
                bytes[ti] = done;
                Interlocked.Add(ref _sink, sink);
            })
            { IsBackground = true, Name = $"Supernova Mem {ti}", Priority = ThreadPriority.AboveNormal };
            threads[t].Start();
        }

        start.SignalAndWait();
        var sw = Stopwatch.StartNew();
        ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(_seconds));
        Volatile.Write(ref stop, 1);
        foreach (var th in threads) th.Join();
        sw.Stop();
        if (nonTemporal && Sse.IsSupported) Sse.StoreFence();
        return bytes.Sum() / sw.Elapsed.TotalSeconds / 1e9;
    }

    private static long ReadPass(byte* p, long size)
    {
        int vs = Vector<byte>.Count;
        var a0 = Vector<long>.Zero;
        var a1 = Vector<long>.Zero;
        var a2 = Vector<long>.Zero;
        var a3 = Vector<long>.Zero;
        long step = vs * 4, end = size - step;
        long i = 0;
        for (; i <= end; i += step)
        {
            a0 ^= *(Vector<long>*)(p + i);
            a1 ^= *(Vector<long>*)(p + i + vs);
            a2 ^= *(Vector<long>*)(p + i + 2 * vs);
            a3 ^= *(Vector<long>*)(p + i + 3 * vs);
        }
        var acc = a0 ^ a1 ^ a2 ^ a3;
        return acc[0];
    }

    private static void WritePass(byte* p, long size)
    {
        int vs = Vector<byte>.Count;
        var v = new Vector<long>(0x5A5A5A5A5A5A5A5A);
        long step = vs * 4, end = size - step;
        for (long i = 0; i <= end; i += step)
        {
            *(Vector<long>*)(p + i) = v;
            *(Vector<long>*)(p + i + vs) = v;
            *(Vector<long>*)(p + i + 2 * vs) = v;
            *(Vector<long>*)(p + i + 3 * vs) = v;
        }
    }

    /// <summary>Gravação com instruções "non-temporal" (não poluem o cache), como o AIDA64 faz na RAM.</summary>
    private static void WritePassNonTemporal(byte* p, long size)
    {
        var v = Vector256.Create(0x5A5A5A5A5A5A5A5AL);
        long end = size - 128;
        for (long i = 0; i <= end; i += 128)
        {
            Avx.StoreAlignedNonTemporal((long*)(p + i), v);
            Avx.StoreAlignedNonTemporal((long*)(p + i + 32), v);
            Avx.StoreAlignedNonTemporal((long*)(p + i + 64), v);
            Avx.StoreAlignedNonTemporal((long*)(p + i + 96), v);
        }
    }

    // ------------------------------------------------------------------ latência

    private double MeasureLatency(long bytes, CancellationToken ct)
    {
        const int Line = 64;
        long lines = Math.Max(16, bytes / Line);
        byte* buf = (byte*)NativeMemory.AlignedAlloc((nuint)(lines * Line), 4096);
        try
        {
            // Ciclo aleatório único sobre as linhas de cache: o prefetcher não consegue adivinhar o próximo endereço.
            // A ordem é aleatória dentro de blocos de 2 MB (e os blocos também vêm em ordem aleatória), para medir a
            // latência da memória e não a das traduções de endereço (TLB) — mesmo critério de ferramentas como o AIDA64.
            var order = BuildChaseOrder(lines, LatencySegmentLines);
            for (long i = 0; i < lines; i++)
            {
                long from = order[i], to = order[(i + 1) % lines];
                *(byte**)(buf + from * Line) = buf + to * Line;
            }
            order = null!;

            // Aquece (traz a região para o cache/TLB) e mede.
            byte* ptr = buf;
            ptr = Chase(ptr, lines);
            double best = double.MaxValue;
            for (int rep = 0; rep < _repeats && !ct.IsCancellationRequested; rep++)
            {
                long hops = 0;
                var sw = Stopwatch.StartNew();
                long chunk = 1 << 18;
                while (sw.Elapsed.TotalSeconds < _seconds && !ct.IsCancellationRequested)
                {
                    ptr = Chase(ptr, chunk);
                    hops += chunk;
                }
                sw.Stop();
                if (hops > 0) best = Math.Min(best, sw.Elapsed.TotalNanoseconds / hops);
            }
            Interlocked.Add(ref _sink, (long)ptr);
            return best == double.MaxValue ? 0 : best;
        }
        finally
        {
            NativeMemory.AlignedFree(buf);
        }
    }

    /// <summary>Linhas de 64 B num bloco de 2 MB.</summary>
    internal const int LatencySegmentLines = (2 << 20) / 64;

    /// <summary>
    /// Ordem de visita das linhas: blocos embaralhados e, dentro de cada bloco, linhas embaralhadas.
    /// Percorrer a ordem em sequência visita cada linha exatamente uma vez (ciclo único).
    /// </summary>
    internal static int[] BuildChaseOrder(long lines, int segmentLines)
    {
        var rng = new Random(12345);
        int segments = (int)((lines + segmentLines - 1) / segmentLines);
        var segOrder = Enumerable.Range(0, segments).ToArray();
        rng.Shuffle(segOrder);
        var order = new int[lines];
        long k = 0;
        foreach (int seg in segOrder)
        {
            int start = seg * segmentLines;
            int count = (int)Math.Min(segmentLines, lines - start);
            var local = new int[count];
            for (int i = 0; i < count; i++) local[i] = start + i;
            rng.Shuffle(local);
            local.CopyTo(order, k);
            k += count;
        }
        return order;
    }

    private static byte* Chase(byte* p, long hops)
    {
        long n = hops / 8;
        for (long i = 0; i < n; i++)
        {
            p = *(byte**)p; p = *(byte**)p; p = *(byte**)p; p = *(byte**)p;
            p = *(byte**)p; p = *(byte**)p; p = *(byte**)p; p = *(byte**)p;
        }
        return p;
    }
}

internal static class MemoryBenchResultExtensions
{
    public static MemoryBenchResult With(this MemoryBenchResult r, Dictionary<string, double> values)
    {
        foreach (var (k, v) in values) r.Values[k] = v;
        return r;
    }
}
