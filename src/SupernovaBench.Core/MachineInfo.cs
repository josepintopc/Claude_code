using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace SupernovaBench.Core;

/// <summary>Informações rápidas e multiplataforma da máquina (sem WMI).</summary>
public static class MachineInfo
{
    private static string? _cpuName;

    /// <summary>Nome comercial da CPU (registro no Windows, /proc/cpuinfo no Linux).</summary>
    public static string CpuName()
    {
        if (_cpuName != null) return _cpuName;
        string? name = null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                name = key?.GetValue("ProcessorNameString") as string;
            }
            else if (File.Exists("/proc/cpuinfo"))
            {
                name = File.ReadLines("/proc/cpuinfo")
                           .FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                           ?.Split(':', 2)[1];
            }
        }
        catch
        {
            // Sem permissão ou formato inesperado: cai no nome genérico.
        }
        name = string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return _cpuName = string.IsNullOrWhiteSpace(name) ? RuntimeInformation.ProcessArchitecture + " CPU" : name;
    }

    /// <summary>Conjuntos de instruções que o processador (e o .NET) suportam, no estilo do CPU-Z.</summary>
    public static IReadOnlyList<string> InstructionSets()
    {
        var list = new List<string>();
        void Add(bool ok, string name) { if (ok) list.Add(name); }

        if (RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86)
        {
            Add(Sse.IsSupported, "SSE");
            Add(Sse2.IsSupported, "SSE2");
            Add(Sse3.IsSupported, "SSE3");
            Add(Ssse3.IsSupported, "SSSE3");
            Add(Sse41.IsSupported, "SSE4.1");
            Add(Sse42.IsSupported, "SSE4.2");
            Add(Avx.IsSupported, "AVX");
            Add(Avx2.IsSupported, "AVX2");
            Add(Fma.IsSupported, "FMA3");
            Add(Avx512F.IsSupported, "AVX-512F");
            Add(Avx512BW.IsSupported, "AVX-512BW");
            Add(System.Runtime.Intrinsics.X86.Aes.IsSupported, "AES-NI");
            Add(Pclmulqdq.IsSupported, "PCLMULQDQ");
            Add(Bmi1.IsSupported, "BMI1");
            Add(Bmi2.IsSupported, "BMI2");
            Add(Lzcnt.IsSupported, "LZCNT");
            Add(Popcnt.IsSupported, "POPCNT");
            Add(Environment.Is64BitOperatingSystem, "x86-64");
        }
        else if (RuntimeInformation.ProcessArchitecture is Architecture.Arm64)
        {
            Add(AdvSimd.IsSupported, "NEON");
            Add(System.Runtime.Intrinsics.Arm.Aes.IsSupported, "AES");
            Add(Crc32.IsSupported, "CRC32");
            Add(Dp.IsSupported, "DotProd");
            Add(Rdm.IsSupported, "RDM");
            Add(Sha1.IsSupported, "SHA1");
            Add(Sha256.IsSupported, "SHA256");
        }
        return list;
    }
}
