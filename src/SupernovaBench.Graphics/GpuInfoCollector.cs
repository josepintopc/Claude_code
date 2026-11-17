using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SupernovaBench.Core.Info;
using SupernovaBench.Graphics.Native;
using static SupernovaBench.Core.Localization.Loc;

namespace SupernovaBench.Graphics;

/// <summary>Uma placa de vídeo com os dados no estilo da aba "Graphics Card" do GPU-Z.</summary>
public sealed class GpuInfo
{
    public string Name { get; init; } = "";
    public string Subtitle { get; set; } = "";
    public string Vendor { get; init; } = "";
    public ulong VramBytes { get; set; }
    public InfoNode Node { get; } = new();
    internal IntPtr NvmlHandle { get; set; }

    /// <summary>Leituras dinâmicas que o LibreHardwareMonitor não traz (P-state, PerfCap, NVENC...). Só NVIDIA.</summary>
    public List<KeyValuePair<string, string>> LiveExtras()
    {
        var list = new List<KeyValuePair<string, string>>();
        var h = NvmlHandle;
        if (h == IntPtr.Zero) return list;
        var c = Culture;
        if (Nvml.PState(h) is int p) list.Add(new(T("gpuz.live.pstate"), $"P{p}"));
        if (Nvml.ThrottleReasons(h) is ulong r) list.Add(new(T("gpuz.live.perfcap"), PerfCapText(r)));
        if (Nvml.PowerUsageMw(h) is uint pw && Nvml.PowerLimitMw(h) is uint lim && lim > 0)
            list.Add(new(T("gpuz.live.power"), string.Format(c, "{0:0.0} W · {1:0} %", pw / 1000.0, 100.0 * pw / lim)));
        if (Nvml.Util(h) is Nvml.Utilization u) list.Add(new(T("gpuz.live.memController"), $"{u.Memory} %"));
        if (Nvml.EncoderUtil(h) is uint enc) list.Add(new("NVENC", $"{enc} %"));
        if (Nvml.DecoderUtil(h) is uint dec) list.Add(new("NVDEC", $"{dec} %"));
        if (Nvml.CurPcieGen(h) is uint g && Nvml.CurPcieWidth(h) is uint w) list.Add(new(T("gpuz.pcie.current"), $"{PcieName(g)} x{w}"));
        return list;
    }

    /// <summary>Motivo de limitação do clock (bits da NVML) traduzido.</summary>
    public static string PerfCapText(ulong reasons)
        => string.Join(" + ", GpuTelemetry.ThrottleFlags(reasons).Select(f => T("gpuz.perfcap." + f)));

    internal static string PcieName(uint gen) => gen switch
    {
        1 => "PCIe 1.1", 2 => "PCIe 2.0", 3 => "PCIe 3.0", 4 => "PCIe 4.0", 5 => "PCIe 5.0", 6 => "PCIe 6.0",
        _ => $"PCIe gen {gen}",
    };
}

/// <summary>
/// Coleta os dados das placas de vídeo (estilo GPU-Z): WMI + DXGI + NVML + registro do driver + banco de especificações.
/// <para>Baseado no GpuInfoCollector do ResetExplode (MIT, © 2026 Gustavo / GustavoReset).</para>
/// </summary>
public static class GpuInfoCollector
{
    private static readonly Regex PnpIds = new(
        @"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(?:&SUBSYS_([0-9A-F]{4})([0-9A-F]{4}))?(?:&REV_([0-9A-F]{2}))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record PciIds(string Vendor, string Device, string SubDevice, string SubVendor, string Revision);

    /// <summary>Lê VEN/DEV/SUBSYS/REV do PNPDeviceID (ex.: PCI\VEN_10DE&amp;DEV_2684&amp;SUBSYS_88E21043&amp;REV_A1\...).</summary>
    public static PciIds ParsePnpId(string pnp)
    {
        var m = PnpIds.Match(pnp ?? "");
        string G(int i) => m.Groups[i].Value.ToUpperInvariant();
        return new PciIds(G(1), G(2), G(3), G(4), G(5));
    }

    /// <summary>Placas de vídeo, a dedicada (mais VRAM) primeiro.</summary>
    public static List<GpuInfo> Collect()
    {
        var result = new List<GpuInfo>();
        var adapters = GpuAdapters.Enumerate();
        var nvDevices = Nvml.Devices();

        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
        foreach (ManagementObject vc in searcher.Get())
        {
            try { result.Add(Build(vc, adapters, nvDevices)); }
            catch (Exception ex)
            {
                var g = new GpuInfo { Name = vc["Name"]?.ToString() ?? "GPU" };
                g.Node.Label = g.Name;
                var err = g.Node.Add(T("hw.error"));
                err.Add(ex.GetType().Name, ex.Message);
                result.Add(g);
            }
        }
        return result.OrderByDescending(g => g.VramBytes).ToList();
    }

    /// <summary>Seção "Placa de vídeo" da página Hardware.</summary>
    public static InfoSection CollectSection()
    {
        var section = new InfoSection { Id = "gpu", Title = T("hw.tab.gpu") };
        foreach (var g in Collect()) section.Nodes.Add(g.Node);
        if (section.Nodes.Count == 0) section.Nodes.Add(new InfoNode { Label = "GPU", Value = T("hw.common.unknown") });
        return section;
    }

    private static GpuInfo Build(ManagementObject vc, List<GpuAdapter> adapters, List<Nvml.Device> nvDevices)
    {
        var c = Culture;
        string name = vc["Name"]?.ToString()?.Trim() ?? "GPU";
        string pnp = vc["PNPDeviceID"]?.ToString() ?? "";
        var ids = ParsePnpId(pnp);
        string ven = ids.Vendor, dev = ids.Device;

        string vendor = ven switch { "10DE" => "NVIDIA", "1002" => "AMD", "8086" => "Intel", _ => ven };
        var spec = GpuSpecDb.Find(ven, dev, name);
        var dx = adapters.FirstOrDefault(a => a.VendorHex == ven && a.DeviceHex == dev);

        var pnpProps = PnpProperties(pnp);
        var reg = DriverRegistry(pnpProps.GetValueOrDefault("DEVPKEY_Device_Driver") as string);

        // NVML: casa pelo barramento PCI (duas placas iguais não se confundem); senão pelo device ID.
        IntPtr h = IntPtr.Zero;
        if (ven == "10DE")
        {
            uint? bus = BusNumber(pnpProps.GetValueOrDefault("DEVPKEY_Device_LocationInfo") as string);
            uint pciId = (Convert.ToUInt32(dev, 16) << 16) | 0x10DE;
            var nv = nvDevices.FirstOrDefault(d => d.Pci.PciDeviceId == pciId && bus != null && d.Bus == bus)
                     ?? nvDevices.FirstOrDefault(d => d.Pci.PciDeviceId == pciId);
            h = nv?.Handle ?? IntPtr.Zero;
        }

        var gpu = new GpuInfo { Name = name, Vendor = vendor, NvmlHandle = h };
        gpu.Node.Label = name;
        InfoNode Section(string key) => gpu.Node.Add(T(key));

        // ---------- Identificação ----------
        var id = Section("gpuz.identification");
        id.Add(T("gpuz.chip"), spec?.Chip ?? "—");
        id.Add(T("gpuz.arch"), spec?.Arch ?? "—");
        id.Add(T("gpuz.revision"), ids.Revision.Length > 0 ? ids.Revision : "—");
        id.Add(T("gpuz.boardMaker"), ids.SubVendor.Length > 0 ? GpuSpecDb.SubvendorName(ids.SubVendor) ?? $"{T("hw.common.unknown")} ({ids.SubVendor})" : "—");
        id.Add("Device ID", $"{ven} {dev} · {ids.SubVendor} {ids.SubDevice}".Trim(' ', '·'));
        id.Add(T("gpuz.process"), spec?.Process ?? "—");
        id.Add(T("gpuz.dieSize"), spec != null ? $"{spec.DieMm2} mm²" : "—");
        id.Add(T("gpuz.transistors"), spec != null ? T("gpuz.transistors.value", spec.TransistorsBi.ToString("0.#", c)) : "—");
        id.Add(T("gpuz.release"), spec?.Release.ToString("d", c) ?? "—");

        // ---------- Unidades e desempenho teórico ----------
        uint? cores = h != IntPtr.Zero ? Nvml.Cores(h) : null;
        int shaders = (int)(cores ?? (uint)(spec?.Shaders ?? 0));
        var units = Section("gpuz.units");
        if (shaders > 0)
        {
            bool nv = ven == "10DE";
            int perUnit = nv ? 128 : 64;
            units.Add(nv ? "CUDA cores" : "Stream processors", shaders.ToString("N0", c));
            units.Add(nv ? "SMs" : "Compute units", (shaders / perUnit).ToString(c));
            if (nv && spec != null && spec.Arch is "Blackwell" or "Ada Lovelace" or "Ampere")
            {
                units.Add("Tensor cores", (shaders / perUnit * 4).ToString(c));
                units.Add("RT cores", (shaders / perUnit).ToString(c));
            }
            else if (!nv && spec != null && spec.Arch.StartsWith("RDNA"))
            {
                units.Add("Ray accelerators", (shaders / perUnit).ToString(c));
            }
        }
        if (spec != null)
        {
            units.Add("ROPs / TMUs", $"{spec.Rops} / {spec.Tmus}");
            units.Add("Pixel fillrate", string.Format(c, "{0:0.0} GPixel/s", spec.Rops * spec.BoostMHz / 1000.0));
            units.Add("Texture fillrate", string.Format(c, "{0:0.0} GTexel/s", spec.Tmus * spec.BoostMHz / 1000.0));
            units.Add(T("gpuz.fp32"), string.Format(c, "{0:0.00} TFLOPS", (shaders > 0 ? shaders : spec.Shaders) * 2.0 * spec.BoostMHz / 1e6));
        }
        if (units.Children.Count == 0) units.Add(T("gpuz.noData"), T("gpuz.notInDb"));

        // ---------- Memória ----------
        var mem = Section("gpuz.memory");
        ulong vram = 0;
        if (h != IntPtr.Zero && Nvml.MemoryInfo(h) is Nvml.Memory nm) vram = nm.Total;
        if (vram == 0 && dx != null) vram = dx.DedicatedVideoMemory;
        if (vram == 0 && reg.TryGetValue("HardwareInformation.qwMemorySize", out var q)) vram = RegUInt64(q);
        gpu.VramBytes = vram;
        bool igpu = spec?.IsIntegrated == true;
        if (igpu || vram < 1UL << 30)
        {
            mem.Add(T(igpu ? "gpuz.mem.reserved" : "gpuz.mem.size"), Bytes(vram));
        }
        else
        {
            // O driver reserva parte da VRAM: 12227 MiB reportados = placa de 12 GB (como mostra o GPU-Z).
            double gib = vram / (double)(1UL << 30);
            mem.Add(T("gpuz.mem.size"), T("gpuz.mem.size.value", Math.Ceiling(gib - 0.15).ToString("0", c), (vram >> 20).ToString("N0", c)));
        }
        if (dx != null && dx.SharedSystemMemory > 0) mem.Add(T("gpuz.mem.shared"), Bytes(dx.SharedSystemMemory));
        mem.Add(T("hw.common.type"), spec?.MemType ?? "—");

        uint? busWidth = h != IntPtr.Zero ? Nvml.BusWidth(h) : null;
        uint? memClock = h != IntPtr.Zero ? Nvml.MaxClock(h, Nvml.ClockMem) : null;
        if (busWidth is uint b) mem.Add(T("gpuz.mem.bus"), T("gpuz.mem.bus.value", b));
        if (memClock is uint mc)
        {
            // A NVML reporta metade da taxa efetiva (GDDR6/6X/7): 14001 MHz → 28 Gbps.
            double gbps = mc * 2 / 1000.0;
            mem.Add(T("gpuz.mem.clock"), T("gpuz.mem.clock.value", mc, gbps.ToString("0.0", c)));
            if (busWidth is uint bw) mem.Add(T("gpuz.mem.bandwidth"), string.Format(c, "{0:0.0} GB/s", gbps * bw / 8));
        }
        if (h != IntPtr.Zero && Nvml.Bar1(h) is Nvml.Memory bar)
            mem.Add("Resizable BAR", bar.Total > 256UL * 1024 * 1024 ? T("gpuz.rebar.on", Bytes(bar.Total)) : T("hw.common.disabled"));

        // ---------- Clocks ----------
        var clk = Section("gpuz.clocks");
        if (spec != null)
        {
            clk.Add(T("gpuz.clock.base"), $"{spec.BaseMHz} MHz");
            clk.Add(T("gpuz.clock.boost"), $"{spec.BoostMHz} MHz");
        }
        if (h != IntPtr.Zero)
        {
            if (Nvml.MaxClock(h, Nvml.ClockGraphics) is uint g) clk.Add(T("gpuz.clock.max"), $"{g} MHz");
            if (Nvml.MaxClock(h, Nvml.ClockVideo) is uint v) clk.Add(T("gpuz.clock.video"), $"{v} MHz");
        }
        if (clk.Children.Count == 0) clk.Add(T("gpuz.noData"), "—");

        // ---------- Barramento ----------
        var pcie = Section("gpuz.pcie");
        uint? maxGen = h != IntPtr.Zero ? Nvml.MaxPcieGen(h) : null;
        uint? maxW = h != IntPtr.Zero ? Nvml.MaxPcieWidth(h) : null;
        maxGen ??= AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkSpeed"));
        maxW ??= AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_MaxLinkWidth"));
        uint? curGen = h != IntPtr.Zero ? Nvml.CurPcieGen(h) : AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkSpeed"));
        uint? curW = h != IntPtr.Zero ? Nvml.CurPcieWidth(h) : AsUInt(pnpProps.GetValueOrDefault("DEVPKEY_PciDevice_CurrentLinkWidth"));
        if (maxGen is uint mg && maxW is uint mw) pcie.Add(T("gpuz.pcie.interface"), $"{GpuInfo.PcieName(mg)} x{mw}");
        if (curGen is uint cg && curW is uint cw)
            pcie.Add(T("gpuz.pcie.current"), $"{GpuInfo.PcieName(cg)} x{cw}" + (maxGen > curGen ? " " + T("gpuz.pcie.powerSaving") : ""));
        if (pnpProps.GetValueOrDefault("DEVPKEY_Device_LocationInfo") is string loc) pcie.Add(T("gpuz.pcie.location"), loc);
        if (pcie.Children.Count == 0) pcie.Add(T("gpuz.noData"), "—");

        // ---------- Energia ----------
        if (h != IntPtr.Zero || spec?.TdpW > 0)
        {
            var pwr = Section("gpuz.power");
            if (spec?.TdpW > 0) pwr.Add(T("gpuz.power.tdp"), $"{spec.TdpW} W");
            if (h != IntPtr.Zero)
            {
                if (Nvml.PowerLimitMw(h) is uint lim) pwr.Add(T("gpuz.power.limit"), string.Format(c, "{0:0} W", lim / 1000.0));
                if (Nvml.PowerDefaultMw(h) is uint def) pwr.Add(T("gpuz.power.default"), string.Format(c, "{0:0} W", def / 1000.0));
                if (Nvml.PowerConstraintsMw(h) is { } pc) pwr.Add(T("gpuz.power.range"), string.Format(c, "{0:0} – {1:0} W", pc.Min / 1000.0, pc.Max / 1000.0));
            }
        }

        // ---------- Driver e BIOS ----------
        var drv = Section("gpuz.driver");
        string? vbios = h != IntPtr.Zero ? Nvml.Vbios(h) : null;
        vbios ??= RegString(reg, "HardwareInformation.BiosString");
        drv.Add("VBIOS", vbios ?? "—");
        string wmiVer = vc["DriverVersion"]?.ToString() ?? "—";
        string? nvVer = h != IntPtr.Zero ? Nvml.DriverVersion() : null;
        drv.Add(T("hw.gpu.driver"), nvVer != null ? $"{nvVer} ({wmiVer})" : wmiVer);
        if (vc["DriverDate"] is string dd && dd.Length >= 8)
        {
            try { drv.Add(T("hw.gpu.driverDate"), ManagementDateTimeConverter.ToDateTime(dd).ToString("d", c)); }
            catch (ArgumentOutOfRangeException) { }
        }
        drv.Add(T("gpuz.signature"), DriverSigner(pnp));
        if (h != IntPtr.Zero)
        {
            if (Nvml.CudaVersion() is string cuda) drv.Add("CUDA", cuda);
            if (Nvml.ComputeCapability(h) is string cc) drv.Add("Compute capability", cc);
        }

        // ---------- Tecnologias ----------
        var tech = Section("gpuz.tech");
        string? fl = dx?.FeatureLevel;
        tech.Add("DirectX", fl switch
        {
            "12_2" => "DirectX 12 Ultimate (12_2)",
            null => "—",
            _ => $"DirectX 12 ({fl.Replace('_', '.')})",
        });
        var apis = new List<string>();
        string sys = Environment.SystemDirectory;
        if (ven == "10DE" && File.Exists(Path.Combine(sys, "nvcuda.dll"))) apis.Add("CUDA");
        if (File.Exists(Path.Combine(sys, "OpenCL.dll"))) apis.Add("OpenCL");
        if (File.Exists(Path.Combine(sys, "vulkan-1.dll"))) apis.Add("Vulkan");
        if (fl != null) { apis.Add("DirectCompute"); apis.Add("DirectML"); }
        if (ven == "10DE") apis.Add("PhysX");
        tech.Add("APIs", apis.Count > 0 ? string.Join(" · ", apis) : "—");
        if (spec != null && GpuSpecDb.ArchFeatures.TryGetValue(spec.Arch, out var feats)) tech.Add(T("gpuz.archFeatures"), feats);

        string memLabel = spec?.MemType.StartsWith("GDDR") == true ? spec.MemType : "";
        gpu.Subtitle = string.Join("  ·  ", new[]
        {
            spec?.Chip, spec?.Arch,
            vram >= 1UL << 30 && !igpu ? $"{Math.Ceiling(vram / (double)(1UL << 30) - 0.15):0} GB {memLabel}".Trim() : null,
            ids.SubVendor.Length > 0 ? GpuSpecDb.SubvendorName(ids.SubVendor) : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
        gpu.Node.Value = gpu.Subtitle;
        return gpu;
    }

    /// <summary>"PCI bus 1, device 0, function 0" → 1.</summary>
    internal static uint? BusNumber(string? location)
    {
        var m = Regex.Match(location ?? "", @"bus\s+(\d+)", RegexOptions.IgnoreCase);
        return m.Success && uint.TryParse(m.Groups[1].Value, out var b) ? b : null;
    }

    /// <summary>Propriedades de dispositivo (link PCIe, chave do driver...) via Win32_PnPEntity.GetDeviceProperties.</summary>
    private static Dictionary<string, object?> PnpProperties(string pnpId)
    {
        var dict = new Dictionary<string, object?>();
        if (pnpId.Length == 0) return dict;
        try
        {
            // Instanciar pelo caminho do objeto falha ("Não encontrado"); via consulta funciona.
            using var s = new ManagementObjectSearcher($"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{pnpId.Replace("\\", "\\\\").Replace("'", "\\'")}'");
            var mo = s.Get().Cast<ManagementObject>().FirstOrDefault();
            if (mo == null) return dict;
            var input = mo.GetMethodParameters("GetDeviceProperties");
            input["devicePropertyKeys"] = new[]
            {
                "DEVPKEY_PciDevice_CurrentLinkSpeed", "DEVPKEY_PciDevice_CurrentLinkWidth",
                "DEVPKEY_PciDevice_MaxLinkSpeed", "DEVPKEY_PciDevice_MaxLinkWidth",
                "DEVPKEY_Device_Driver", "DEVPKEY_Device_LocationInfo",
            };
            var output = mo.InvokeMethod("GetDeviceProperties", input, null);
            if (output?["deviceProperties"] is ManagementBaseObject[] props)
                foreach (var p in props)
                    if (p["KeyName"] is string k) dict[k] = p["Data"];
        }
        catch { }
        return dict;
    }

    /// <summary>Valores "HardwareInformation.*" da chave de classe do driver de vídeo.</summary>
    private static Dictionary<string, object> DriverRegistry(string? driverKey)
    {
        var dict = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(driverKey)) return dict;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{driverKey}");
            if (key == null) return dict;
            foreach (var n in key.GetValueNames())
                if (n.StartsWith("HardwareInformation.") && key.GetValue(n) is object v) dict[n] = v;
        }
        catch { }
        return dict;
    }

    private static ulong RegUInt64(object v) => v switch
    {
        long l => (ulong)l,
        int i => (uint)i,
        byte[] b when b.Length >= 8 => BitConverter.ToUInt64(b, 0),
        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
        _ => 0,
    };

    /// <summary>Alguns drivers gravam strings como REG_BINARY UTF-16.</summary>
    private static string? RegString(Dictionary<string, object> reg, string name)
    {
        if (!reg.TryGetValue(name, out var v)) return null;
        string s = v is byte[] bytes ? System.Text.Encoding.Unicode.GetString(bytes) : v.ToString() ?? "";
        s = s.TrimEnd('\0').Trim();
        return s.Length > 0 ? s : null;
    }

    private static string DriverSigner(string pnpId)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT IsSigned, Signer FROM Win32_PnPSignedDriver WHERE DeviceID='{pnpId.Replace("\\", "\\\\").Replace("'", "\\'")}'");
            foreach (ManagementObject d in s.Get())
            {
                bool signed = d["IsSigned"] is bool b && b;
                string signer = d["Signer"]?.ToString() ?? "";
                if (!signed) return T("gpuz.signature.none");
                return signer.Contains("Windows Hardware Compatibility", StringComparison.OrdinalIgnoreCase)
                    ? "WHQL (Microsoft)" : T("gpuz.signature.by", signer);
            }
        }
        catch { }
        return "—";
    }

    private static uint? AsUInt(object? v)
    {
        try { return v == null ? null : Convert.ToUInt32(v); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    private static string Bytes(ulong b)
    {
        if (b == 0) return "—";
        double v = b;
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return string.Format(Culture, "{0:0.#} {1}", v, u[i]);
    }
}
