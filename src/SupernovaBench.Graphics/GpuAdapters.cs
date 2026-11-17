using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace SupernovaBench.Graphics;

/// <summary>Uma placa de vídeo vista pelo DXGI (VRAM real em 64 bits, sem o limite de 4 GB do WMI).</summary>
public sealed record GpuAdapter(int Index, string Name, uint VendorId, uint DeviceId, uint SubsystemId, uint Revision,
                                ulong DedicatedVideoMemory, ulong DedicatedSystemMemory, ulong SharedSystemMemory,
                                long Luid, bool IsSoftware, string? FeatureLevel)
{
    public string VendorHex => VendorId.ToString("X4");
    public string DeviceHex => DeviceId.ToString("X4");

    public string VendorName => VendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 => "AMD",
        0x8086 => "Intel",
        0x1414 => "Microsoft",
        _ => VendorHex,
    };
}

/// <summary>Enumera as placas de vídeo e escolhe a principal (dedicada com mais VRAM).</summary>
public static class GpuAdapters
{
    public static List<GpuAdapter> Enumerate(bool includeSoftware = false)
    {
        var list = new List<GpuAdapter>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var d = adapter.Description1;
                    bool software = (d.Flags & AdapterFlags.Software) != 0;
                    if (software && !includeSoftware) continue;
                    long luid = ((long)d.Luid.HighPart << 32) | d.Luid.LowPart;
                    list.Add(new GpuAdapter((int)i, d.Description.Trim(), d.VendorId, d.DeviceId, d.SubsystemId, d.Revision,
                                            (ulong)d.DedicatedVideoMemory, (ulong)d.DedicatedSystemMemory, (ulong)d.SharedSystemMemory,
                                            luid, software, software ? null : MaxFeatureLevel(adapter)));
                }
            }
        }
        catch
        {
            // Sem DXGI (ex.: fora do Windows): lista vazia.
        }
        return list;
    }

    /// <summary>Placa dedicada com mais VRAM (ou a que contém <paramref name="nameHint"/> no nome).</summary>
    public static GpuAdapter? PickMain(IEnumerable<GpuAdapter> adapters, string? nameHint = null)
    {
        var hw = adapters.Where(a => !a.IsSoftware).ToList();
        if (!string.IsNullOrWhiteSpace(nameHint))
        {
            var byName = hw.FirstOrDefault(a => a.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return hw.OrderByDescending(a => a.DedicatedVideoMemory).FirstOrDefault();
    }

    private static string? MaxFeatureLevel(IDXGIAdapter1 adapter)
    {
        try
        {
            foreach (var (level, name) in new[]
                     {
                         (FeatureLevel.Level_12_2, "12_2"), (FeatureLevel.Level_12_1, "12_1"), (FeatureLevel.Level_12_0, "12_0"),
                         (FeatureLevel.Level_11_1, "11_1"), (FeatureLevel.Level_11_0, "11_0"),
                     })
            {
                if (D3D12.IsSupported(adapter, level)) return name;
            }
        }
        catch
        {
            // d3d12.dll ausente (Windows muito antigo) ou driver sem D3D12.
        }
        return null;
    }
}
