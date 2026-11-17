using System.Diagnostics;
using System.Numerics;

namespace SupernovaBench.Core.Benchmarks.Cpu;

/// <summary>Resolução e amostras por pixel de uma execução. A pontuação só é comparável dentro do mesmo preset.</summary>
public sealed record RenderPreset(string Id, int Width, int Height, int SamplesPerPixel)
{
    /// <summary>Preset oficial do Supernova Bench (o que aparece no histórico e nos relatórios).</summary>
    public static readonly RenderPreset Standard = new("standard", 1280, 720, 512);

    /// <summary>Preset rápido para testes e para a linha de comando.</summary>
    public static readonly RenderPreset Quick = new("quick", 480, 270, 64);

    public static RenderPreset FromId(string? id) => id switch
    {
        "quick" => Quick,
        _ => Standard,
    };

    public long TotalSamples => (long)Width * Height * SamplesPerPixel;
}

/// <summary>Números brutos de uma execução do renderizador.</summary>
public sealed record RenderRunStats(int Threads, double Seconds, long Samples, int Passes, bool Cancelled)
{
    /// <summary>
    /// Pontos Supernova: amostras de path tracing por segundo ÷ 10.000. A cena e a carga são sempre as mesmas,
    /// então a nota é comparável entre execuções e entre PCs (para o mesmo preset e a mesma versão do motor).
    /// </summary>
    public double Score => CpuRenderBench.ComputeScore(Samples, Seconds);
}

/// <summary>
/// Benchmark de CPU no modelo do Cinebench: path tracer em cores, imagem dividida em blocos (buckets) distribuídos
/// entre as threads a partir do centro. Determinístico: cada pixel tem semente fixa, então a imagem e o volume de
/// trabalho são idênticos em toda execução, com qualquer quantidade de threads.
/// <para>
/// Baseado no RESET BENCH do ResetExplode (MIT, © 2026 Gustavo / GustavoReset), adaptado para cor,
/// presets configuráveis e a cena "supernova".
/// </para>
/// </summary>
public sealed class CpuRenderBench
{
    /// <summary>Muda sempre que a cena ou o algoritmo mudarem (resultados de versões diferentes não se comparam).</summary>
    public const string EngineVersion = "1.0";
    public const int BucketSize = 32;
    private const int MaxDepth = 6;
    private const float SampleClamp = 4f; // limita "fireflies"

    public RenderPreset Preset { get; }
    public int Width => Preset.Width;
    public int Height => Preset.Height;

    /// <summary>Pixels BGRA32 (lidos pela interface enquanto o teste roda).</summary>
    public int[] Pixels { get; }

    public IReadOnlyList<RenderBucket> Buckets { get; }

    /// <summary>Bloco em que cada thread está trabalhando (-1 = livre). Recriado a cada execução.</summary>
    public int[] Active { get; private set; } = Array.Empty<int>();

    public long SamplesDone => Interlocked.Read(ref _samples);
    public int BucketsDone => Volatile.Read(ref _bucketsDone);
    public int PassesDone => Volatile.Read(ref _passes);

    private long _samples;
    private int _bucketsDone;
    private int _passes;

    public CpuRenderBench(RenderPreset? preset = null)
    {
        Preset = preset ?? RenderPreset.Standard;
        if (Width <= 0 || Height <= 0 || Preset.SamplesPerPixel <= 0)
            throw new ArgumentException("Preset inválido.", nameof(preset));
        Pixels = new int[Width * Height];
        _camera = new Camera(Width, Height);

        var list = new List<RenderBucket>();
        for (int y = 0; y < Height; y += BucketSize)
            for (int x = 0; x < Width; x += BucketSize)
                list.Add(new RenderBucket(x, y, Math.Min(BucketSize, Width - x), Math.Min(BucketSize, Height - y)));
        // Do centro para fora, como o Cinebench.
        double cx = Width / 2.0, cy = Height / 2.0;
        Buckets = list.OrderBy(b => Math.Pow(b.X + b.W / 2.0 - cx, 2) + Math.Pow((b.Y + b.H / 2.0 - cy) * 1.6, 2)).ToList();
    }

    public static double ComputeScore(long samples, double seconds) => seconds > 0 ? samples / seconds / 10_000 : 0;

    /// <summary>
    /// Renderiza a imagem inteira com N threads (multi-core e single-core fazem o mesmo trabalho).
    /// Com <paramref name="minDuration"/>, repete passadas até completar o tempo (desempenho sustentado).
    /// </summary>
    public RenderRunStats Run(int threads, TimeSpan minDuration, CancellationToken ct, Action<int>? onPass = null)
    {
        if (threads < 1) throw new ArgumentOutOfRangeException(nameof(threads));
        int count = Buckets.Count;
        Interlocked.Exchange(ref _samples, 0);
        Volatile.Write(ref _passes, 0);
        var sw = Stopwatch.StartNew();

        do
        {
            Array.Fill(Pixels, unchecked((int)0xFF000000));
            Volatile.Write(ref _bucketsDone, 0);
            int next = -1;
            var active = Enumerable.Repeat(-1, threads).ToArray();
            Active = active;
            var workers = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                int ti = t;
                workers[t] = new Thread(() =>
                {
                    int b;
                    while (!ct.IsCancellationRequested && (b = Interlocked.Increment(ref next)) < count)
                    {
                        Volatile.Write(ref active[ti], b);
                        RenderBucket(Buckets[b], ct);
                        Interlocked.Increment(ref _bucketsDone);
                    }
                    Volatile.Write(ref active[ti], -1);
                })
                { IsBackground = true, Name = $"Supernova CPU {ti}" };
                workers[t].Start();
            }
            foreach (var w in workers) w.Join();
            if (ct.IsCancellationRequested) break;
            int passes = Interlocked.Increment(ref _passes);
            onPass?.Invoke(passes);
        } while (sw.Elapsed < minDuration);

        sw.Stop();
        return new RenderRunStats(threads, sw.Elapsed.TotalSeconds, SamplesDone, PassesDone, ct.IsCancellationRequested);
    }

    private void RenderBucket(RenderBucket b, CancellationToken ct)
    {
        int spp = Preset.SamplesPerPixel;
        float invSpp = 1f / spp;
        for (int y = b.Y; y < b.Y + b.H; y++)
        {
            if (ct.IsCancellationRequested) return;
            for (int x = b.X; x < b.X + b.W; x++)
            {
                uint seed = (uint)(y * Width + x) * 9781u + 0x9E3779B9u;
                if (seed == 0) seed = 1;
                var sum = Vector3.Zero;
                for (int s = 0; s < spp; s++)
                {
                    float u = (x + Rnd(ref seed)) / Width, v = (y + Rnd(ref seed)) / Height;
                    var c = Trace(_camera.Ray(u, v), ref seed);
                    float m = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
                    if (m > SampleClamp) c *= SampleClamp / m;
                    sum += c;
                }
                Pixels[y * Width + x] = ToPixel(sum * invSpp);
            }
            Interlocked.Add(ref _samples, (long)b.W * spp);
        }
    }

    /// <summary>Hash FNV-1a dos pixels (usado nos testes para provar que a imagem é determinística).</summary>
    public ulong ImageHash()
    {
        ulong h = 14695981039346656037UL;
        foreach (int p in Pixels)
        {
            h ^= (uint)p;
            h *= 1099511628211UL;
        }
        return h;
    }

    // ------------------------------------------------------------------ cena "supernova"

    private enum Mat : byte { Diffuse, Metal, Glass, Emissive }

    private readonly record struct Sphere(Vector3 C, float R, Mat M, Vector3 Albedo, float Rough = 0, Vector3 Emission = default);

    private static readonly Vector3 Orange = new(1.00f, 0.48f, 0.18f);
    private static readonly Vector3 Magenta = new(0.88f, 0.22f, 0.61f);
    private static readonly Vector3 Violet = new(0.48f, 0.25f, 0.95f);
    private static readonly Vector3 Cyan = new(0.25f, 0.75f, 0.95f);

    private static readonly Sphere[] Spheres = BuildScene();
    private static readonly Sphere Lamp = new(new Vector3(-4f, 7f, -3f), 2.2f, Mat.Emissive, Vector3.Zero, 0, new Vector3(1.0f, 0.93f, 0.86f) * 9f);

    private static Sphere[] BuildScene()
    {
        var s = new List<Sphere>
        {
            new(new Vector3(0, 1.1f, 0), 1.1f, Mat.Glass, new Vector3(0.98f, 0.97f, 1.0f)),
            new(new Vector3(-2.5f, 0.9f, 0.9f), 0.9f, Mat.Metal, new Vector3(0.98f, 0.62f, 0.35f), 0.03f),
            new(new Vector3(2.4f, 0.8f, 0.7f), 0.8f, Mat.Diffuse, Magenta),
            new(new Vector3(1.0f, 0.45f, -1.9f), 0.45f, Mat.Metal, new Vector3(0.62f, 0.45f, 1.0f), 0.25f),
            new(new Vector3(-1.1f, 0.35f, -2.0f), 0.35f, Mat.Diffuse, new Vector3(0.9f, 0.9f, 0.92f)),
            // A "supernova" ao fundo: esfera emissiva laranja-rosada, vista entre as esferas.
            new(new Vector3(4.5f, 4.2f, 30f), 2.0f, Mat.Emissive, Vector3.Zero, 0, new Vector3(1.0f, 0.24f, 0.10f) * 1.6f),
        };
        Vector3[] palette = { Orange, Magenta, Violet, Cyan };
        // Anel de esferas pequenas alternando materiais e cores.
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * MathF.Tau;
            var m = (i % 3) switch { 0 => Mat.Metal, 1 => Mat.Diffuse, _ => Mat.Glass };
            var albedo = m == Mat.Glass ? new Vector3(0.95f) : palette[i % palette.Length] * (i % 2 == 0 ? 0.95f : 0.7f);
            s.Add(new(new Vector3(MathF.Cos(a) * 4.2f, 0.28f, MathF.Sin(a) * 4.2f + 1.5f), 0.28f, m, albedo, 0.1f));
        }
        return s.ToArray();
    }

    private readonly Camera _camera;

    private sealed class Camera
    {
        private static readonly Vector3 Origin = new(0, 2.1f, -7.2f);
        private readonly Vector3 _lower, _horizontal, _vertical;

        public Camera(int width, int height)
        {
            var target = new Vector3(0, 0.75f, 0);
            float vfov = 38f * MathF.PI / 180, aspect = (float)width / height;
            float hh = MathF.Tan(vfov / 2), hw = aspect * hh;
            var w = Vector3.Normalize(Origin - target);
            var u = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, w));
            var v = Vector3.Cross(w, u);
            _lower = Origin - hw * u + hh * v - w;   // canto superior esquerdo (y cresce para baixo na imagem)
            _horizontal = 2 * hw * u;
            _vertical = -2 * hh * v;
        }

        public (Vector3 O, Vector3 D) Ray(float s, float t)
            => (Origin, Vector3.Normalize(_lower + s * _horizontal + t * _vertical - Origin));
    }

    // ------------------------------------------------------------------ path tracing

    private static Vector3 Trace((Vector3 O, Vector3 D) ray, ref uint seed)
    {
        var radiance = Vector3.Zero;
        var throughput = Vector3.One;
        bool specularBounce = true; // câmera e reflexos especulares "veem" a lâmpada diretamente
        var (o, d) = ray;

        for (int depth = 0; depth < MaxDepth; depth++)
        {
            if (!Intersect(o, d, out float t, out int idx, out bool lamp))
            {
                radiance += throughput * Sky(d);
                break;
            }
            if (lamp)
            {
                // A lâmpada é amostrada diretamente nas superfícies difusas; aqui só conta em caminhos especulares.
                if (specularBounce) radiance += throughput * Lamp.Emission;
                break;
            }

            var p = o + d * t;
            Vector3 n, albedo;
            Mat mat;
            float rough = 0;
            if (idx < 0)
            {
                // Chão: xadrez em tons de ametista
                n = Vector3.UnitY; mat = Mat.Diffuse;
                bool check = (((int)MathF.Floor(p.X * 0.9f) + (int)MathF.Floor(p.Z * 0.9f)) & 1) == 0;
                albedo = check ? new Vector3(0.58f, 0.55f, 0.64f) : new Vector3(0.24f, 0.22f, 0.30f);
            }
            else
            {
                var sp = Spheres[idx];
                if (sp.M == Mat.Emissive)
                {
                    radiance += throughput * sp.Emission;
                    break;
                }
                n = (p - sp.C) / sp.R; mat = sp.M; albedo = sp.Albedo; rough = sp.Rough;
            }

            switch (mat)
            {
                case Mat.Diffuse:
                {
                    // Luz direta da lâmpada (amostragem por cone) + rebote difuso cosseno-ponderado.
                    radiance += throughput * albedo * DirectLight(p, n, ref seed);
                    throughput *= albedo;
                    d = CosineHemisphere(n, ref seed);
                    o = p + n * 1e-3f;
                    specularBounce = false;
                    break;
                }
                case Mat.Metal:
                {
                    var r = Vector3.Reflect(d, n) + rough * RandomInSphere(ref seed);
                    if (Vector3.Dot(r, n) <= 0) return radiance;
                    d = Vector3.Normalize(r);
                    o = p + n * 1e-3f;
                    throughput *= albedo;
                    specularBounce = true;
                    break;
                }
                case Mat.Glass:
                {
                    const float ior = 1.5f;
                    bool outside = Vector3.Dot(d, n) < 0;
                    var nn = outside ? n : -n;
                    float eta = outside ? 1 / ior : ior;
                    float cos = MathF.Min(-Vector3.Dot(d, nn), 1);
                    float sin2 = eta * eta * (1 - cos * cos);
                    float r0 = (1 - ior) / (1 + ior); r0 *= r0;
                    float fresnel = r0 + (1 - r0) * MathF.Pow(1 - cos, 5);
                    if (sin2 > 1 || Rnd(ref seed) < fresnel)
                    {
                        d = Vector3.Reflect(d, nn);
                        o = p + nn * 1e-3f;
                    }
                    else
                    {
                        d = Vector3.Normalize(eta * d + (eta * cos - MathF.Sqrt(1 - sin2)) * nn);
                        o = p - nn * 1e-3f;
                    }
                    throughput *= albedo;
                    specularBounce = true;
                    break;
                }
            }

            // Roleta russa depois de alguns rebotes.
            if (depth >= 3)
            {
                float q = MathF.Max(0.05f, 1 - MathF.Max(throughput.X, MathF.Max(throughput.Y, throughput.Z)));
                if (Rnd(ref seed) < q) break;
                throughput /= 1 - q;
            }
        }
        return radiance;
    }

    /// <summary>Amostra direção dentro do cone que a lâmpada esférica ocupa e testa sombra.</summary>
    private static Vector3 DirectLight(Vector3 p, Vector3 n, ref uint seed)
    {
        var toC = Lamp.C - p;
        float dist2 = toC.LengthSquared();
        float cosMax = MathF.Sqrt(MathF.Max(0, 1 - Lamp.R * Lamp.R / dist2));
        var w = toC / MathF.Sqrt(dist2);

        float r1 = Rnd(ref seed), r2 = Rnd(ref seed);
        float cosT = 1 - r1 * (1 - cosMax), sinT = MathF.Sqrt(MathF.Max(0, 1 - cosT * cosT)), phi = MathF.Tau * r2;
        Basis(w, out var a, out var b);
        var dir = Vector3.Normalize(a * (MathF.Cos(phi) * sinT) + b * (MathF.Sin(phi) * sinT) + w * cosT);

        float cosN = Vector3.Dot(dir, n);
        if (cosN <= 0) return Vector3.Zero;
        if (!Intersect(p + n * 1e-3f, dir, out _, out _, out bool hitLamp) || !hitLamp) return Vector3.Zero;
        // Lambert (1/π) × radiância × cos × ângulo sólido do cone
        return Lamp.Emission * (cosN * 2 * (1 - cosMax));
    }

    private static bool Intersect(Vector3 o, Vector3 d, out float tMin, out int idx, out bool lamp)
    {
        tMin = float.MaxValue; idx = -2; lamp = false;
        // Chão (plano y = 0)
        if (d.Y < -1e-6f)
        {
            float t = -o.Y / d.Y;
            if (t > 1e-4f && t < 60) { tMin = t; idx = -1; }
        }
        var spheres = Spheres;
        for (int i = 0; i < spheres.Length; i++)
            if (HitSphere(spheres[i], o, d, tMin, out float t)) { tMin = t; idx = i; }
        if (HitSphere(Lamp, o, d, tMin, out float tl)) { tMin = tl; lamp = true; }
        return idx != -2 || lamp;
    }

    private static bool HitSphere(in Sphere s, Vector3 o, Vector3 d, float tMax, out float t)
    {
        var oc = o - s.C;
        float b = Vector3.Dot(oc, d), c = oc.LengthSquared() - s.R * s.R;
        float disc = b * b - c;
        t = 0;
        if (disc < 0) return false;
        float sq = MathF.Sqrt(disc);
        t = -b - sq;
        if (t < 1e-4f) t = -b + sq;
        return t > 1e-4f && t < tMax;
    }

    /// <summary>Céu noturno: violeta no horizonte, azul-escuro no alto.</summary>
    private static Vector3 Sky(Vector3 d)
    {
        float k = Math.Clamp(d.Y, 0, 1);
        return Vector3.Lerp(new Vector3(0.20f, 0.09f, 0.26f), new Vector3(0.03f, 0.04f, 0.13f), MathF.Sqrt(k));
    }

    // ------------------------------------------------------------------ utilidades

    private static float Rnd(ref uint s)
    {
        // xorshift32: rápido e determinístico por pixel
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s >> 8) * (1f / 16777216f);
    }

    private static Vector3 RandomInSphere(ref uint s)
    {
        while (true)
        {
            var v = new Vector3(Rnd(ref s) * 2 - 1, Rnd(ref s) * 2 - 1, Rnd(ref s) * 2 - 1);
            if (v.LengthSquared() < 1) return v;
        }
    }

    private static Vector3 CosineHemisphere(Vector3 n, ref uint s)
    {
        float r1 = Rnd(ref s), r2 = Rnd(ref s);
        float r = MathF.Sqrt(r1), phi = MathF.Tau * r2;
        Basis(n, out var a, out var b);
        return Vector3.Normalize(a * (r * MathF.Cos(phi)) + b * (r * MathF.Sin(phi)) + n * MathF.Sqrt(1 - r1));
    }

    private static void Basis(Vector3 n, out Vector3 a, out Vector3 b)
    {
        a = Vector3.Normalize(Vector3.Cross(MathF.Abs(n.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX, n));
        b = Vector3.Cross(n, a);
    }

    /// <summary>Tone mapping (ACES aproximado) + gama 2.2 → BGRA.</summary>
    private static int ToPixel(Vector3 c)
    {
        static int Channel(float v)
        {
            v *= 1.1f;
            v = v * (2.51f * v + 0.03f) / (v * (2.43f * v + 0.59f) + 0.14f);
            return (int)(MathF.Pow(Math.Clamp(v, 0, 1), 1 / 2.2f) * 255 + 0.5f);
        }
        int r = Channel(c.X), g = Channel(c.Y), b = Channel(c.Z);
        return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
    }
}

public readonly record struct RenderBucket(int X, int Y, int W, int H);
