namespace SupernovaBench.Core.Drivers;

/// <summary>Versões de drivers: conversão do formato do Windows para o da NVIDIA e comparação.</summary>
public static class DriverVersions
{
    /// <summary>"32.0.15.6636" (Windows) → "566.36" (NVIDIA): os 5 últimos dígitos das duas últimas partes.</summary>
    public static string? NvidiaFromWindows(string? windowsVersion)
    {
        if (string.IsNullOrWhiteSpace(windowsVersion)) return null;
        var parts = windowsVersion.Split('.');
        if (parts.Length < 4) return null;
        string digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5 || !digits.All(char.IsDigit)) return null;
        digits = digits[^5..];
        return $"{int.Parse(digits[..3])}.{digits[3..]}";
    }

    /// <summary>Compara versões parte a parte (números); &lt;0 se a &lt; b.</summary>
    public static int Compare(string? a, string? b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        for (int i = 0; i < Math.Max(pa.Count, pb.Count); i++)
        {
            long x = i < pa.Count ? pa[i] : 0, y = i < pb.Count ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static List<long> Parts(string? v)
        => (v ?? "").Split('.', '-', ' ').Select(p => long.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out long n) ? n : 0).ToList();
}
