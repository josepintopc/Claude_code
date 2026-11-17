using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SupernovaBench.Graphics.Native;

/// <summary>
/// P/Invoke da NVML (nvml.dll, instalada com o driver NVIDIA). Sem NVIDIA, <see cref="Available"/> é false e tudo devolve null.
/// <para>Baseado no ResetExplode (MIT, © 2026 Gustavo / GustavoReset), com temperatura, casamento por BusId e busca no NVSMI.</para>
/// </summary>
public static class Nvml
{
    private const string Dll = "nvml.dll";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct PciInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string BusIdLegacy;
        public uint Domain, Bus, Device;
        public uint PciDeviceId;    // (device << 16) | vendor
        public uint PciSubSystemId; // (subsys << 16) | subvendor
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string BusId;
    }

    public struct Memory { public ulong Total, Free, Used; } // mesmo layout de nvmlBAR1Memory_t
    public struct Utilization { public uint Gpu, Memory; }

    public const int ClockGraphics = 0, ClockSm = 1, ClockMem = 2, ClockVideo = 3;

    static Nvml()
    {
        // Drivers antigos (não-DCH) instalam a nvml.dll só em "Program Files\NVIDIA Corporation\NVSMI".
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(Nvml).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // Já havia um resolvedor registrado para este assembly.
        }
    }

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (!name.Equals(Dll, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        if (NativeLibrary.TryLoad(Dll, asm, path, out var h)) return h;
        var nvsmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", Dll);
        return File.Exists(nvsmi) && NativeLibrary.TryLoad(nvsmi, out h) ? h : IntPtr.Zero;
    }

    [DllImport(Dll, EntryPoint = "nvmlInit_v2")] private static extern int nvmlInit();
    [DllImport(Dll, EntryPoint = "nvmlDeviceGetCount_v2")] private static extern int nvmlDeviceGetCount(out uint count);
    [DllImport(Dll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);
    [DllImport(Dll, EntryPoint = "nvmlDeviceGetPciInfo_v3")] private static extern int nvmlDeviceGetPciInfo(IntPtr device, out PciInfo pci);
    [DllImport(Dll, CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport(Dll, CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetVbiosVersion(IntPtr device, StringBuilder version, uint length);
    [DllImport(Dll, CharSet = CharSet.Ansi)] private static extern int nvmlSystemGetDriverVersion(StringBuilder version, uint length);
    [DllImport(Dll, EntryPoint = "nvmlSystemGetCudaDriverVersion_v2")] private static extern int nvmlSystemGetCudaDriverVersion(out int version);
    [DllImport(Dll)] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory memory);
    [DllImport(Dll)] private static extern int nvmlDeviceGetBAR1MemoryInfo(IntPtr device, out Memory bar1);
    [DllImport(Dll)] private static extern int nvmlDeviceGetMemoryBusWidth(IntPtr device, out uint width);
    [DllImport(Dll)] private static extern int nvmlDeviceGetNumGpuCores(IntPtr device, out uint cores);
    [DllImport(Dll)] private static extern int nvmlDeviceGetCudaComputeCapability(IntPtr device, out int major, out int minor);
    [DllImport(Dll)] private static extern int nvmlDeviceGetMaxPcieLinkGeneration(IntPtr device, out uint gen);
    [DllImport(Dll)] private static extern int nvmlDeviceGetMaxPcieLinkWidth(IntPtr device, out uint width);
    [DllImport(Dll)] private static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr device, out uint gen);
    [DllImport(Dll)] private static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr device, out uint width);
    [DllImport(Dll)] private static extern int nvmlDeviceGetMaxClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] private static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint celsius);
    [DllImport(Dll)] private static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint percent);
    [DllImport(Dll)] private static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint mw);
    [DllImport(Dll)] private static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint mw);
    [DllImport(Dll)] private static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr device, out uint minMw, out uint maxMw);
    [DllImport(Dll)] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint mw);
    [DllImport(Dll)] private static extern int nvmlDeviceGetPerformanceState(IntPtr device, out int pstate);
    [DllImport(Dll)] private static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);
    [DllImport(Dll)] private static extern int nvmlDeviceGetEncoderUtilization(IntPtr device, out uint util, out uint periodUs);
    [DllImport(Dll)] private static extern int nvmlDeviceGetDecoderUtilization(IntPtr device, out uint util, out uint periodUs);
    [DllImport(Dll)] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization util);

    private static bool? _ok;
    private static readonly object Gate = new();

    /// <summary>Inicializa uma única vez; false se não houver driver NVIDIA.</summary>
    public static bool Available
    {
        get
        {
            lock (Gate)
            {
                _ok ??= Call(() => nvmlInit());
                return _ok.Value;
            }
        }
    }

    /// <summary>Executa uma chamada tolerando DLL/função ausentes (drivers antigos ou sem NVIDIA).</summary>
    private static bool Call(Func<int> f)
    {
        try { return f() == 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    }

    public sealed record Device(IntPtr Handle, PciInfo Pci, string Name)
    {
        public uint DeviceId => Pci.PciDeviceId >> 16;

        /// <summary>Barramento PCI no formato do Windows ("PCI bus 1, device 0, function 0").</summary>
        public uint Bus => Pci.Bus;
    }

    public static List<Device> Devices()
    {
        var list = new List<Device>();
        uint n = 0;
        if (!Available || !Call(() => nvmlDeviceGetCount(out n))) return list;
        for (uint i = 0; i < n; i++)
        {
            IntPtr h = IntPtr.Zero;
            PciInfo pci = default;
            if (!Call(() => nvmlDeviceGetHandleByIndex(i, out h)) || !Call(() => nvmlDeviceGetPciInfo(h, out pci))) continue;
            var sb = new StringBuilder(96);
            string name = Call(() => nvmlDeviceGetName(h, sb, (uint)sb.Capacity)) ? sb.ToString() : "NVIDIA";
            list.Add(new Device(h, pci, name));
        }
        return list;
    }

    public static string? DriverVersion()
    {
        var sb = new StringBuilder(96);
        return Call(() => nvmlSystemGetDriverVersion(sb, (uint)sb.Capacity)) ? sb.ToString() : null;
    }

    public static string? CudaVersion()
    {
        int v = 0;
        return Call(() => nvmlSystemGetCudaDriverVersion(out v)) ? $"{v / 1000}.{v % 1000 / 10}" : null;
    }

    public static string? Vbios(IntPtr h)
    {
        var sb = new StringBuilder(64);
        return Call(() => nvmlDeviceGetVbiosVersion(h, sb, (uint)sb.Capacity)) ? sb.ToString() : null;
    }

    public static Memory? MemoryInfo(IntPtr h) { Memory m = default; return Call(() => nvmlDeviceGetMemoryInfo(h, out m)) ? m : null; }
    public static Memory? Bar1(IntPtr h) { Memory m = default; return Call(() => nvmlDeviceGetBAR1MemoryInfo(h, out m)) ? m : null; }
    public static uint? BusWidth(IntPtr h) => U(h, nvmlDeviceGetMemoryBusWidth);
    public static uint? Cores(IntPtr h) => U(h, nvmlDeviceGetNumGpuCores);
    public static uint? MaxPcieGen(IntPtr h) => U(h, nvmlDeviceGetMaxPcieLinkGeneration);
    public static uint? MaxPcieWidth(IntPtr h) => U(h, nvmlDeviceGetMaxPcieLinkWidth);
    public static uint? CurPcieGen(IntPtr h) => U(h, nvmlDeviceGetCurrPcieLinkGeneration);
    public static uint? CurPcieWidth(IntPtr h) => U(h, nvmlDeviceGetCurrPcieLinkWidth);
    public static uint? PowerDefaultMw(IntPtr h) => U(h, nvmlDeviceGetPowerManagementDefaultLimit);
    public static uint? PowerLimitMw(IntPtr h) => U(h, nvmlDeviceGetEnforcedPowerLimit);
    public static uint? PowerUsageMw(IntPtr h) => U(h, nvmlDeviceGetPowerUsage);
    public static uint? FanPercent(IntPtr h) => U(h, nvmlDeviceGetFanSpeed);

    /// <summary>Temperatura do núcleo da GPU em °C (NVML_TEMPERATURE_GPU).</summary>
    public static uint? Temperature(IntPtr h) { uint v = 0; return Call(() => nvmlDeviceGetTemperature(h, 0, out v)) ? v : null; }

    public static uint? MaxClock(IntPtr h, int type) { uint v = 0; return Call(() => nvmlDeviceGetMaxClockInfo(h, type, out v)) ? v : null; }
    public static uint? Clock(IntPtr h, int type) { uint v = 0; return Call(() => nvmlDeviceGetClockInfo(h, type, out v)) ? v : null; }

    public static (uint Min, uint Max)? PowerConstraintsMw(IntPtr h)
    {
        uint a = 0, b = 0;
        return Call(() => nvmlDeviceGetPowerManagementLimitConstraints(h, out a, out b)) ? (a, b) : null;
    }

    public static string? ComputeCapability(IntPtr h)
    {
        int a = 0, b = 0;
        return Call(() => nvmlDeviceGetCudaComputeCapability(h, out a, out b)) ? $"{a}.{b}" : null;
    }

    public static int? PState(IntPtr h) { int v = 0; return Call(() => nvmlDeviceGetPerformanceState(h, out v)) ? v : null; }
    public static ulong? ThrottleReasons(IntPtr h) { ulong v = 0; return Call(() => nvmlDeviceGetCurrentClocksThrottleReasons(h, out v)) ? v : null; }
    public static uint? EncoderUtil(IntPtr h) { uint v = 0; return Call(() => nvmlDeviceGetEncoderUtilization(h, out v, out _)) ? v : null; }
    public static uint? DecoderUtil(IntPtr h) { uint v = 0; return Call(() => nvmlDeviceGetDecoderUtilization(h, out v, out _)) ? v : null; }
    public static Utilization? Util(IntPtr h) { Utilization u = default; return Call(() => nvmlDeviceGetUtilizationRates(h, out u)) ? u : null; }

    private delegate int UFunc(IntPtr h, out uint v);
    private static uint? U(IntPtr h, UFunc f) { uint v = 0; return Call(() => f(h, out v)) ? v : null; }
}
