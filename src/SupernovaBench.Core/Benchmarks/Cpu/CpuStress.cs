using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SupernovaBench.Core.Benchmarks.Cpu;

/// <summary>Carga do stress de CPU.</summary>
public enum CpuStressMode
{
    /// <summary>Cadeias de FMA em vetores de 256 bits: o máximo de calor e consumo (estilo Linpack/Prime95 small FFT).</summary>
    Fma,

    /// <summary>O path tracer do benchmark de CPU em loop: carga parecida com a de um render de verdade.</summary>
    Render,

    /// <summary>Multiplicações e embaralhamento de inteiros de 64 bits.</summary>
    Integer,
}

/// <summary>
/// Stress de CPU com detecção de erros de cálculo: cada bloco de trabalho é determinístico e o resultado é comparado
/// com o calculado no início. Uma diferença indica instabilidade (overclock, undervolt ou CPU com defeito), como no Prime95.
/// </summary>
public sealed class CpuStress
{
    private const int FmaIterations = 20_000, IntIterations = 200_000;

    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _cts;
    private Thread[] _threads = Array.Empty<Thread>();
    private Thread? _renderThread;
    private long _iterations;
    private int _errors;
    private int _running;

    public CpuStressMode Mode { get; private set; }
    public int Threads { get; private set; }
    public bool StopOnError { get; set; } = true;
    public bool Running => Volatile.Read(ref _running) > 0;
    public TimeSpan Elapsed => _clock.Elapsed;
    public long Iterations => Interlocked.Read(ref _iterations);
    public int Errors => Volatile.Read(ref _errors);

    /// <summary>Thread (1..N) onde o primeiro erro apareceu.</summary>
    public int? FirstErrorThread { get; private set; }

    /// <summary>Exceção inesperada (texto), se houver.</summary>
    public string? Error { get; private set; }

    /// <summary>Teste: força um erro na thread 0 depois desse número de blocos.</summary>
    internal long InjectFaultAfter { get; set; } = -1;

    public void Start(CpuStressMode mode, int threads, TimeSpan? duration = null)
    {
        if (Running) throw new InvalidOperationException("already running");
        Mode = mode;
        Threads = Math.Clamp(threads, 1, Environment.ProcessorCount);
        Interlocked.Exchange(ref _iterations, 0);
        Volatile.Write(ref _errors, 0);
        FirstErrorThread = null;
        Error = null;
        _cts = new CancellationTokenSource();
        if (duration is { } d) _cts.CancelAfter(d);
        var ct = _cts.Token;
        _clock.Restart();

        if (mode == CpuStressMode.Render)
        {
            Volatile.Write(ref _running, 1);
            _renderThread = new Thread(() => RunRender(ct)) { IsBackground = true, Name = "Supernova Stress Render" };
            _renderThread.Start();
            return;
        }

        ulong reference = mode == CpuStressMode.Fma ? FmaBlock(0) : IntBlock(0);
        Volatile.Write(ref _running, Threads);
        _threads = new Thread[Threads];
        for (int t = 0; t < Threads; t++)
        {
            int ti = t;
            _threads[t] = new Thread(() => Worker(ti, reference, ct))
            {
                IsBackground = true,
                Name = $"Supernova Stress {ti}",
                Priority = ThreadPriority.BelowNormal, // a interface continua respondendo
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
        _renderThread?.Join();
        _clock.Stop();
    }

    private void Worker(int index, ulong reference, CancellationToken ct)
    {
        try
        {
            long blocks = 0;
            while (!ct.IsCancellationRequested)
            {
                ulong sum = Mode == CpuStressMode.Fma ? FmaBlock(0) : IntBlock(0);
                if (index == 0 && InjectFaultAfter >= 0 && blocks == InjectFaultAfter) sum ^= 1;
                blocks++;
                Interlocked.Increment(ref _iterations);
                if (sum != reference) ReportError(index);
            }
        }
        catch (Exception ex)
        {
            Error ??= ex.Message;
        }
        finally
        {
            Finish();
        }
    }

    private void RunRender(CancellationToken ct)
    {
        try
        {
            var renderer = new CpuRenderBench(RenderPreset.Quick);
            ulong? reference = null;
            renderer.Run(Threads, TimeSpan.FromDays(365), ct, pass =>
            {
                ulong hash = renderer.ImageHash();
                if (InjectFaultAfter >= 0 && pass - 1 == InjectFaultAfter) hash ^= 1;
                Interlocked.Increment(ref _iterations);
                if (reference == null) reference = hash;
                else if (hash != reference) ReportError(0);
            });
        }
        catch (Exception ex)
        {
            Error ??= ex.Message;
        }
        finally
        {
            Finish();
        }
    }

    private void ReportError(int threadIndex)
    {
        if (Interlocked.Increment(ref _errors) == 1) FirstErrorThread = threadIndex + 1;
        if (StopOnError) _cts?.Cancel();
    }

    private void Finish()
    {
        if (Interlocked.Decrement(ref _running) == 0) _clock.Stop();
    }

    /// <summary>8 cadeias independentes de FMA (mantém as unidades de ponto flutuante cheias). Resultado sempre igual.</summary>
    internal static ulong FmaBlock(int seed)
    {
        var m = Vector256.Create(0.999_999_9);
        var c = Vector256.Create(1e-7);
        var a0 = Vector256.Create(0.11 + seed, 0.12, 0.13, 0.14);
        var a1 = Vector256.Create(0.21, 0.22, 0.23, 0.24);
        var a2 = Vector256.Create(0.31, 0.32, 0.33, 0.34);
        var a3 = Vector256.Create(0.41, 0.42, 0.43, 0.44);
        var a4 = Vector256.Create(0.51, 0.52, 0.53, 0.54);
        var a5 = Vector256.Create(0.61, 0.62, 0.63, 0.64);
        var a6 = Vector256.Create(0.71, 0.72, 0.73, 0.74);
        var a7 = Vector256.Create(0.81, 0.82, 0.83, 0.84);
        if (Fma.IsSupported)
        {
            for (int i = 0; i < FmaIterations; i++)
            {
                a0 = Fma.MultiplyAdd(a0, m, c); a1 = Fma.MultiplyAdd(a1, m, c);
                a2 = Fma.MultiplyAdd(a2, m, c); a3 = Fma.MultiplyAdd(a3, m, c);
                a4 = Fma.MultiplyAdd(a4, m, c); a5 = Fma.MultiplyAdd(a5, m, c);
                a6 = Fma.MultiplyAdd(a6, m, c); a7 = Fma.MultiplyAdd(a7, m, c);
            }
        }
        else
        {
            for (int i = 0; i < FmaIterations; i++)
            {
                a0 = a0 * m + c; a1 = a1 * m + c; a2 = a2 * m + c; a3 = a3 * m + c;
                a4 = a4 * m + c; a5 = a5 * m + c; a6 = a6 * m + c; a7 = a7 * m + c;
            }
        }
        var all = ((a0 + a1) + (a2 + a3)) + ((a4 + a5) + (a6 + a7));
        ulong h = 0;
        for (int i = 0; i < 4; i++) h = h * 31 + BitConverter.DoubleToUInt64Bits(all.GetElement(i));
        return h;
    }

    /// <summary>Mistura de multiplicações, rotações e xor de 64 bits (4 cadeias). Resultado sempre igual.</summary>
    internal static ulong IntBlock(int seed)
    {
        ulong a = 0x9E3779B97F4A7C15UL + (ulong)seed, b = 0xBF58476D1CE4E5B9UL, c = 0x94D049BB133111EBUL, d = 0x2545F4914F6CDD1DUL;
        for (int i = 0; i < IntIterations; i++)
        {
            a = BitOperations.RotateLeft(a * 0xD6E8FEB86659FD93UL, 17) ^ b;
            b = BitOperations.RotateLeft(b * 0xA0761D6478BD642FUL, 29) ^ c;
            c = BitOperations.RotateLeft(c * 0xE7037ED1A0B428DBUL, 41) ^ d;
            d = BitOperations.RotateLeft(d * 0x8EBC6AF09C88C6E3UL, 53) ^ a;
        }
        return a ^ b ^ c ^ d;
    }
}
