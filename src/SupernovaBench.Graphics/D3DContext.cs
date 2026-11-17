using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SupernovaBench.Graphics;

/// <summary>Fábrica DXGI + dispositivo D3D11 na GPU escolhida (ou no WARP, o renderizador por software do Windows).</summary>
internal sealed class D3DContext : IDisposable
{
    public IDXGIFactory2 Factory { get; }
    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public string AdapterName { get; }

    /// <summary>A tela suporta apresentação sem V-Sync (tearing) — necessário para não limitar o FPS do stress.</summary>
    public bool SupportsTearing { get; }

    private readonly IDXGIAdapter1 _adapter;

    private D3DContext(IDXGIFactory2 factory, IDXGIAdapter1 adapter, ID3D11Device device, ID3D11DeviceContext context, bool tearing)
    {
        Factory = factory;
        _adapter = adapter;
        Device = device;
        Context = context;
        AdapterName = adapter.Description1.Description.Trim();
        SupportsTearing = tearing;
    }

    public static D3DContext Create(string? adapterHint, bool useWarp)
    {
        var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        IDXGIAdapter1? chosen = null;
        try
        {
            if (useWarp)
            {
                using var f4 = factory.QueryInterface<IDXGIFactory4>();
                chosen = f4.EnumWarpAdapter<IDXGIAdapter1>();
            }
            else
            {
                var candidates = new List<IDXGIAdapter1>();
                for (uint i = 0; factory.EnumAdapters1(i, out var a).Success; i++)
                {
                    if ((a.Description1.Flags & AdapterFlags.Software) == 0) candidates.Add(a);
                    else a.Dispose();
                }
                chosen = candidates.FirstOrDefault(a => !string.IsNullOrWhiteSpace(adapterHint) &&
                                                        a.Description1.Description.Contains(adapterHint, StringComparison.OrdinalIgnoreCase))
                         ?? candidates.OrderByDescending(a => (ulong)a.Description1.DedicatedVideoMemory).FirstOrDefault();
                foreach (var c in candidates) if (!ReferenceEquals(c, chosen)) c.Dispose();
                if (chosen == null) throw new InvalidOperationException("No Direct3D 11 GPU found.");
            }

            D3D11.D3D11CreateDevice(chosen, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                                    out ID3D11Device device, out ID3D11DeviceContext context).CheckError();

            bool tearing = false;
            using (var f5 = factory.QueryInterfaceOrNull<IDXGIFactory5>()) tearing = f5?.PresentAllowTearing ?? false;
            return new D3DContext(factory, chosen, device, context, tearing);
        }
        catch
        {
            chosen?.Dispose();
            factory.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Context.ClearState();
        Context.Flush();
        Context.Dispose();
        Device.Dispose();
        _adapter.Dispose();
        Factory.Dispose();
    }
}
