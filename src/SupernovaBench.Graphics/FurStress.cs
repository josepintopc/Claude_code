using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using SupernovaBench.Graphics.Native;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace SupernovaBench.Graphics;

public sealed class FurStressSettings
{
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public bool Fullscreen { get; init; }

    /// <summary>1 = sem MSAA. Reduzido automaticamente se a placa não suportar.</summary>
    public int Msaa { get; init; } = 4;

    /// <summary>Camadas de pelo (overdraw).</summary>
    public int Shells { get; init; } = 128;

    /// <summary>Carga extra de ALU no pixel shader.</summary>
    public int Iterations { get; init; } = 48;

    public string AdapterName { get; init; } = "";

    /// <summary>Usa o WARP (software) em vez da GPU — só para testes automáticos.</summary>
    public bool UseWarp { get; init; }

    /// <summary>Sem janela: renderiza só fora da tela (testes automáticos).</summary>
    public bool Headless { get; init; }

    /// <summary>Para sozinho depois deste tempo (null = até mandar parar).</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Presets de intensidade (camadas, iterações), como no ResetExplode.</summary>
    public static (int Shells, int Iterations) Intensity(string level) => level switch
    {
        "normal" => (96, 32),
        "extreme" => (160, 64),
        _ => (128, 48),
    };
}

/// <summary>
/// Stress de GPU no estilo FurMark: um toro "peludo" renderizado por camadas (shells) instanciadas, com descarte de
/// pixels para formar os fios, blending e uma cadeia de FMAs no pixel shader. Sem limite de FPS: renderiza fora da tela
/// mantendo até 6 frames na fila e só apresenta a ~60 Hz, para o V-Sync nunca deixar a GPU ociosa.
/// <para>Baseado no GpuBurn do ResetExplode (MIT, © 2026 Gustavo / GustavoReset), agora com as cores do Supernova.</para>
/// </summary>
public sealed class FurStress
{
    private Thread? _thread;
    private volatile bool _stop;
    private long _frames, _presents;
    private double _seconds;
    private readonly Stopwatch _clock = new();

    public bool Running => _thread?.IsAlive == true;
    public double Fps { get; private set; }
    public long Frames => Interlocked.Read(ref _frames);
    public long Presents => Interlocked.Read(ref _presents);
    public double AverageFps => _seconds > 0 ? Frames / _seconds : 0;
    public TimeSpan Elapsed => _clock.Elapsed;
    public string? Error { get; private set; }
    public string AdapterUsed { get; private set; } = "";
    public bool ClosedByUser { get; private set; }
    public int MsaaUsed { get; private set; }

    public void Start(FurStressSettings settings)
    {
        if (Running) return;
        _stop = false;
        Interlocked.Exchange(ref _frames, 0);
        Interlocked.Exchange(ref _presents, 0);
        _seconds = 0;
        Fps = 0;
        Error = null;
        ClosedByUser = false;
        AdapterUsed = "";
        _clock.Reset();
        _thread = new Thread(() => Run(settings)) { IsBackground = true, Name = "Supernova GPU stress" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(5000);
    }

    /// <summary>Espera o término (para testes).</summary>
    public bool Join(TimeSpan timeout) => _thread?.Join(timeout) ?? true;

    // ------------------------------------------------------------------ renderização

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public Vector3 Pos; public Vector3 Normal; public Vector2 Uv; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix4x4 World, ViewProj;
        public float Time, ShellCount, FurLength, Iterations;
    }

    internal const string Hlsl = @"
cbuffer CB : register(b0)
{
    row_major float4x4 World;
    row_major float4x4 ViewProj;
    float Time; float ShellCount; float FurLength; float Iterations;
};

struct VSIn  { float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; uint inst : SV_InstanceID; };
struct PSIn  { float4 pos : SV_POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; float shell : TEXCOORD1; };

PSIn VS(VSIn i)
{
    PSIn o;
    float s = i.inst / ShellCount;                       // 0 = raiz, 1 = ponta
    float3 p = i.pos + i.nrm * (FurLength * s);
    float3 w = mul(float4(p, 1), World).xyz;
    w.y -= 0.10 * s * s;                                  // gravidade
    w   += 0.025 * s * sin(Time * 2.7 + i.pos.yzx * 7.0); // vento
    o.pos = mul(float4(w, 1), ViewProj);
    o.nrm = normalize(mul(float4(i.nrm, 0), World).xyz);
    o.uv = i.uv;
    o.shell = s;
    return o;
}

float hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// Paleta Supernova: raiz laranja, meio magenta, ponta roxa.
float3 Palette(float s)
{
    float3 o = float3(1.00, 0.48, 0.18), m = float3(0.88, 0.22, 0.61), v = float3(0.48, 0.25, 0.95);
    return s < 0.5 ? lerp(o, m, s * 2) : lerp(m, v, s * 2 - 1);
}

float4 PS(PSIn i) : SV_TARGET
{
    float h = hash(floor(i.uv * float2(900, 380)));

    // Carga de ALU: cadeia de FMAs vetoriais (o que mais consome energia na GPU), antes do descarte
    // e ligada a ele, para o compilador não poder pular o cálculo.
    float4 a = float4(h, i.uv, i.shell), b = a.wzyx + 0.5;
    [loop] for (int k = 0; k < (int)Iterations; k++)
    {
        a = mad(a, 1.0001, b * 0.0003);
        b = mad(b, 0.9997, a * 0.0002);
    }
    float acc = frac(dot(a, b));

    if (i.shell > 0 && h + acc * 1e-6 < i.shell) discard; // fios afinam até a ponta

    float3 L = normalize(float3(-0.4, 0.8, -0.5));
    float diff = saturate(dot(i.nrm, L)) * 0.75 + 0.25;
    float ao = lerp(0.25, 1.0, i.shell);                  // raiz escura, ponta clara
    float3 c = Palette(i.shell) * diff * ao * (0.9 + 0.1 * acc);
    return float4(c, lerp(1.0, 0.55, i.shell));           // pontas translúcidas
}";

    private void Run(FurStressSettings s)
    {
        RenderWindow? window = null;
        D3DContext? dx = null;
        var keep = new List<IDisposable>();
        T K<T>(T d) where T : IDisposable { keep.Add(d); return d; }

        IDXGISwapChain1? swap = null;
        ID3D11Texture2D? backBuffer = null, colorMs = null, depthTex = null;
        ID3D11RenderTargetView? rtv = null;
        ID3D11DepthStencilView? dsv = null;
        void ReleaseTargets()
        {
            rtv?.Dispose(); dsv?.Dispose(); depthTex?.Dispose(); colorMs?.Dispose(); backBuffer?.Dispose();
            rtv = null; dsv = null; depthTex = null; colorMs = null; backBuffer = null;
        }

        try
        {
            dx = D3DContext.Create(s.AdapterName, s.UseWarp);
            var device = dx.Device;
            var ctx = dx.Context;
            AdapterUsed = dx.AdapterName;

            int msaa = Math.Max(1, s.Msaa);
            while (msaa > 1 && device.CheckMultisampleQualityLevels(Format.B8G8R8A8_UNorm, (uint)msaa) == 0) msaa /= 2;
            MsaaUsed = msaa;

            int cw = s.Width, ch = s.Height;
            bool tearing = dx.SupportsTearing;
            var scFlags = tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None;
            if (!s.Headless)
            {
                window = RenderWindow.Create("Supernova Bench · GPU stress", s.Width, s.Height, s.Fullscreen);
                (cw, ch) = window.ClientSize;
                cw = Math.Max(1, cw);
                ch = Math.Max(1, ch);
                swap = dx.Factory.CreateSwapChainForHwnd(device, window.Handle,
                    new SwapChainDescription1((uint)cw, (uint)ch, Format.B8G8R8A8_UNorm, false, Usage.RenderTargetOutput, 2,
                                              Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, scFlags));
                dx.Factory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAltEnter);
            }

            // Renderiza sempre numa textura própria; o back buffer só recebe uma cópia na hora de apresentar.
            void CreateTargets(int tw, int th)
            {
                if (swap != null) backBuffer = swap.GetBuffer<ID3D11Texture2D>(0);
                var cd = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)tw, (uint)th, 1, 1, BindFlags.RenderTarget,
                                                  ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa, 0, ResourceOptionFlags.None);
                colorMs = device.CreateTexture2D(in cd);
                rtv = device.CreateRenderTargetView(colorMs);
                var dd = new Texture2DDescription(Format.D32_Float, (uint)tw, (uint)th, 1, 1, BindFlags.DepthStencil,
                                                  ResourceUsage.Default, CpuAccessFlags.None, (uint)msaa, 0, ResourceOptionFlags.None);
                depthTex = device.CreateTexture2D(in dd);
                dsv = device.CreateDepthStencilView(depthTex);
            }
            CreateTargets(cw, ch);

            // ---- Shaders, malha, estados ----
            var vsCode = Compiler.Compile(Hlsl, "VS", "fur.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3);
            var psCode = Compiler.Compile(Hlsl, "PS", "fur.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3);
            var vs = K(device.CreateVertexShader(vsCode.Span));
            var ps = K(device.CreatePixelShader(psCode.Span));
            var layout = K(device.CreateInputLayout(new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
            }, vsCode.Span));

            BuildTorus(out var vertices, out var indices);
            var vb = K(device.CreateBuffer(vertices, BindFlags.VertexBuffer));
            var ib = K(device.CreateBuffer(indices, BindFlags.IndexBuffer));
            var cb = K(device.CreateBuffer((uint)Marshal.SizeOf<Constants>(), BindFlags.ConstantBuffer,
                                           ResourceUsage.Dynamic, CpuAccessFlags.Write));
            var raster = K(device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { MultisampleEnable = msaa > 1 }));
            // Blending nas camadas: soma carga de ROP e banda de memória (como a pelagem translúcida do FurMark).
            var blend = K(device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha)));

            const int inFlight = 6;
            var fences = new ID3D11Query[inFlight];
            for (int i = 0; i < inFlight; i++) fences[i] = K(device.CreateQuery(new QueryDescription(QueryType.Event)));
            long frame = 0;
            var background = new Color4(0.03f, 0.025f, 0.06f, 1);

            _clock.Restart();
            double lastT = 0, lastPresent = 0;
            long lastFrames = 0;
            while (!_stop)
            {
                if (window != null)
                {
                    window.Pump();
                    if (window.CloseRequested) { ClosedByUser = true; break; }
                    var (nw, nh) = window.ClientSize;
                    if (nw <= 0 || nh <= 0) { Thread.Sleep(50); continue; } // minimizada
                    if (nw != cw || nh != ch)
                    {
                        (cw, ch) = (nw, nh);
                        ctx.OMSetRenderTargets((ID3D11RenderTargetView)null!, null);
                        ReleaseTargets();
                        swap!.ResizeBuffers(2, (uint)cw, (uint)ch, Format.B8G8R8A8_UNorm, scFlags).CheckError();
                        CreateTargets(cw, ch);
                    }
                }
                if (s.Duration is { } max && _clock.Elapsed >= max) break;

                var fence = fences[frame % inFlight];
                if (frame >= inFlight)
                    while (!ctx.IsDataAvailable(fence) && !_stop) Thread.Yield();

                float t = (float)_clock.Elapsed.TotalSeconds;
                var world = Matrix4x4.CreateRotationY(t * 0.6f) * Matrix4x4.CreateRotationX(0.55f + 0.25f * MathF.Sin(t * 0.35f));
                var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, -3.5f), Vector3.Zero, Vector3.UnitY);
                var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, cw / (float)ch, 0.1f, 50f);
                var c = new Constants
                {
                    World = world, ViewProj = view * proj, Time = t,
                    ShellCount = s.Shells, FurLength = 0.2f, Iterations = s.Iterations,
                };
                var map = ctx.Map(cb, MapMode.WriteDiscard);
                Marshal.StructureToPtr(c, map.DataPointer, false);
                ctx.Unmap(cb);

                ctx.OMSetRenderTargets(rtv!, dsv);
                ctx.ClearRenderTargetView(rtv!, background);
                ctx.ClearDepthStencilView(dsv!, DepthStencilClearFlags.Depth, 1f, 0);
                ctx.RSSetViewport(0, 0, cw, ch);
                ctx.RSSetState(raster);
                ctx.OMSetBlendState(blend);
                ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                ctx.IASetInputLayout(layout);
                ctx.IASetVertexBuffer(0, vb, (uint)Marshal.SizeOf<Vertex>());
                ctx.IASetIndexBuffer(ib, Format.R32_UInt, 0);
                ctx.VSSetShader(vs);
                ctx.PSSetShader(ps);
                ctx.VSSetConstantBuffer(0, cb);
                ctx.PSSetConstantBuffer(0, cb);
                ctx.DrawIndexedInstanced((uint)indices.Length, (uint)s.Shells, 0, 0, 0);
                ctx.End(fence);
                frame++;
                Interlocked.Increment(ref _frames);

                double now = _clock.Elapsed.TotalSeconds;
                if (swap != null && now - lastPresent >= 1 / 60.0)
                {
                    if (msaa > 1) ctx.ResolveSubresource(backBuffer!, 0, colorMs!, 0, Format.B8G8R8A8_UNorm);
                    else ctx.CopyResource(backBuffer!, colorMs!);
                    swap.Present(0, tearing ? PresentFlags.AllowTearing : PresentFlags.None);
                    Interlocked.Increment(ref _presents);
                    lastPresent = now;
                }
                else if (swap == null)
                {
                    ctx.Flush();
                }
                _seconds = now;
                if (now - lastT >= 0.5)
                {
                    long f = Frames;
                    Fps = (f - lastFrames) / (now - lastT);
                    lastT = now;
                    lastFrames = f;
                    window?.SetTitle($"Supernova Bench · GPU stress · {Fps:0} FPS · {cw}x{ch}" +
                                     (msaa > 1 ? $" · MSAA {msaa}x" : "") + $" · {AdapterUsed}  (Esc)");
                }
            }
            // Espera os frames que ainda estão na GPU, para AverageFps contar só trabalho concluído.
            // (só as consultas já enviadas; uma consulta nunca usada não fica "pronta").
            var drain = Stopwatch.StartNew();
            for (int i = 0; i < Math.Min(frame, inFlight); i++)
                while (!ctx.IsDataAvailable(fences[i]) && drain.Elapsed < TimeSpan.FromSeconds(5)) Thread.Yield();
            _seconds = _clock.Elapsed.TotalSeconds;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            _clock.Stop();
            ReleaseTargets();
            for (int i = keep.Count - 1; i >= 0; i--)
                try { keep[i].Dispose(); } catch { }
            try { swap?.Dispose(); } catch { }
            try { dx?.Dispose(); } catch { }
            window?.Dispose();
            Fps = 0;
        }
    }

    private static void BuildTorus(out Vertex[] vertices, out uint[] indices)
    {
        const int seg = 160, sides = 64;
        const float R = 1f, r = 0.42f;
        vertices = new Vertex[(seg + 1) * (sides + 1)];
        for (int i = 0, k = 0; i <= seg; i++)
        {
            float u = i / (float)seg * MathF.Tau;
            for (int j = 0; j <= sides; j++, k++)
            {
                float v = j / (float)sides * MathF.Tau;
                var n = new Vector3(MathF.Cos(u) * MathF.Cos(v), MathF.Sin(v), MathF.Sin(u) * MathF.Cos(v));
                vertices[k] = new Vertex
                {
                    Pos = new Vector3(MathF.Cos(u) * R, 0, MathF.Sin(u) * R) + n * r,
                    Normal = n,
                    Uv = new Vector2(i / (float)seg, j / (float)sides),
                };
            }
        }
        indices = new uint[seg * sides * 6];
        for (int i = 0, k = 0; i < seg; i++)
            for (int j = 0; j < sides; j++)
            {
                uint a = (uint)(i * (sides + 1) + j), b = (uint)((i + 1) * (sides + 1) + j);
                indices[k++] = a; indices[k++] = b; indices[k++] = a + 1;
                indices[k++] = a + 1; indices[k++] = b; indices[k++] = b + 1;
            }
    }
}
