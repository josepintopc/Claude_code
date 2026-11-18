using System.Globalization;

namespace SupernovaBench.Core.Drivers;

/// <summary>Categoria de exibição de um driver (agrupa as classes de dispositivo do Windows).</summary>
public enum DriverCategory { Display, Network, Audio, Storage, Usb, System, Bluetooth, Input, Printer, Other }

/// <summary>Driver instalado (Win32_PnPSignedDriver).</summary>
public sealed record DriverInfo
{
    public string DeviceName { get; init; } = "";
    public string DeviceClass { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTime? Date { get; init; }
    public string InfName { get; init; } = "";
    public bool IsSigned { get; init; }
    public string HardwareId { get; init; } = "";
    public string DeviceId { get; init; } = "";

    public DriverCategory Category => CategoryOf(DeviceClass);

    public static DriverCategory CategoryOf(string? deviceClass) => (deviceClass ?? "").ToUpperInvariant() switch
    {
        "DISPLAY" => DriverCategory.Display,
        "NET" or "NETCLIENT" or "NETSERVICE" or "NETTRANS" => DriverCategory.Network,
        "MEDIA" or "AUDIOENDPOINT" or "AUDIOPROCESSINGOBJECT" => DriverCategory.Audio,
        "HDC" or "SCSIADAPTER" or "DISKDRIVE" or "SCSIRAIDCONTROLLER" or "VOLUME" or "STORAGE" => DriverCategory.Storage,
        "USB" or "USBDEVICE" => DriverCategory.Usb,
        "SYSTEM" or "PROCESSOR" or "COMPUTER" or "FIRMWARE" or "SOFTWARECOMPONENT" or "SECURITYDEVICES" => DriverCategory.System,
        "BLUETOOTH" => DriverCategory.Bluetooth,
        "HIDCLASS" or "KEYBOARD" or "MOUSE" => DriverCategory.Input,
        "PRINTER" or "PRINTQUEUE" => DriverCategory.Printer,
        _ => DriverCategory.Other,
    };

    public static string CategoryKey(DriverCategory c) => "drivers.cat." + c.ToString().ToLowerInvariant();

    /// <summary>Data do WMI ("20230512000000.******+***" ou "20230512000000.000000-000") → data local.</summary>
    public static DateTime? ParseWmiDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 8) return null;
        return DateTime.TryParseExact(s[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year > 1980 ? d : null;
    }

    /// <summary>Fabricante da GPU pelo HardwareID (PCI\VEN_10DE...).</summary>
    public static string? GpuVendor(string? hardwareId)
    {
        var id = (hardwareId ?? "").ToUpperInvariant();
        if (id.Contains("VEN_10DE")) return "NVIDIA";
        if (id.Contains("VEN_1002") || id.Contains("VEN_1022")) return "AMD";
        if (id.Contains("VEN_8086")) return "Intel";
        return null;
    }
}

/// <summary>Dispositivo com problema no Gerenciador de Dispositivos (ConfigManagerErrorCode ≠ 0).</summary>
public sealed record ProblemDevice(string Name, string DeviceClass, int ErrorCode, string DeviceId)
{
    public string ErrorKey => ErrorKeyFor(ErrorCode);

    public static string ErrorKeyFor(int code) => code switch
    {
        1 => "drivers.problem.1",
        3 => "drivers.problem.3",
        10 => "drivers.problem.10",
        18 or 28 => "drivers.problem.28",
        22 => "drivers.problem.22",
        24 => "drivers.problem.24",
        31 => "drivers.problem.31",
        43 => "drivers.problem.43",
        45 => "drivers.problem.45",
        52 => "drivers.problem.52",
        _ => "drivers.problem.other",
    };

    /// <summary>"Desconectado" (45) não é defeito: o aparelho só não está ligado agora.</summary>
    public bool IsJustDisconnected => ErrorCode == 45;
}

/// <summary>Atualização de driver oferecida pelo Windows Update.</summary>
public sealed record WuDriverUpdate(string Id, string Title, string? DriverClass, string? Manufacturer, string? Model, DateTime? DriverDate, long SizeBytes);

/// <summary>Resultado de instalar uma atualização do Windows Update.</summary>
public sealed record WuInstallResult(string Id, string Title, int ResultCode, int HResult)
{
    public bool Succeeded => ResultCode is 2 or 3;

    /// <summary>OperationResultCode: 0 não iniciado, 1 em andamento, 2 ok, 3 ok com erros, 4 falhou, 5 cancelado.</summary>
    public string ResultKey => ResultCode switch
    {
        2 => "drivers.wu.result.ok",
        3 => "drivers.wu.result.partial",
        5 => "drivers.wu.result.aborted",
        _ => "drivers.wu.result.failed",
    };
}

/// <summary>Resultado de criar um ponto de restauração.</summary>
public enum RestorePointStatus { Created, SkippedFrequency, Disabled, Failed }

public sealed record RestorePoint(int Sequence, string Description, DateTime Created);
