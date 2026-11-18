using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace SupernovaBench.Core.Drivers;

public sealed record NvidiaProduct(string Name, int SeriesId, int ProductId);

public sealed record NvidiaDriver(string Version, DateTime? Date, string DownloadUrl, string? Size, string? DetailsUrl, string Name);

/// <summary>
/// Consulta pública de drivers da NVIDIA (a mesma do site nvidia.com/Download): lista de produtos (série e família)
/// e o driver mais recente para o produto. Aqui só monta endereços e interpreta as respostas.
/// </summary>
public static class NvidiaDriverApi
{
    public const string ProductsUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";
    public const string DriversPage = "https://www.nvidia.com/Download/index.aspx";

    /// <summary>osID 57 = Windows 10/11 64 bits; studio = driver Studio (upCRD=1) em vez de Game Ready.</summary>
    public static string LatestUrl(NvidiaProduct p, bool studio)
        => "https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php" +
           $"?func=DriverManualLookup&psid={p.SeriesId}&pfid={p.ProductId}&osID=57&languageCode=1033&isWHQL=1&dch=1" +
           $"&upCRD={(studio ? 1 : 0)}&sort1=0&numberOfResults=1";

    public static List<NvidiaProduct> ParseProducts(string xml)
    {
        var doc = XDocument.Parse(xml);
        var list = new List<NvidiaProduct>();
        foreach (var lv in doc.Descendants().Where(e => e.Name.LocalName == "LookupValue"))
        {
            string? name = lv.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value;
            string? value = lv.Elements().FirstOrDefault(e => e.Name.LocalName == "Value")?.Value;
            if (string.IsNullOrWhiteSpace(name) || !int.TryParse(value, out int pfid)) continue;
            if (!int.TryParse(lv.Attribute("ParentID")?.Value, out int psid)) continue;
            list.Add(new NvidiaProduct(name.Trim(), psid, pfid));
        }
        return list;
    }

    private static string Key(string name)
    {
        string n = name.Trim();
        if (n.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase)) n = n[7..];
        return string.Join(' ', n.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    /// <summary>Produto pelo nome da placa ("NVIDIA GeForce RTX 4070", "... Laptop GPU"). Null se não houver igual.</summary>
    public static NvidiaProduct? FindProduct(IReadOnlyList<NvidiaProduct> products, string gpuName)
    {
        string target = Key(gpuName);
        var exact = products.FirstOrDefault(p => Key(p.Name) == target);
        if (exact != null) return exact;
        // Notebooks às vezes aparecem só como "Laptop" ou sem o sufixo "GPU".
        if (target.EndsWith(" laptop", StringComparison.Ordinal))
            return products.FirstOrDefault(p => Key(p.Name) == target + " gpu");
        return null;
    }

    public static NvidiaDriver? ParseLatest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!r.TryGetProperty("Success", out var ok) || ok.GetString() != "1") return null;
        if (!r.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0) return null;
        if (!ids[0].TryGetProperty("downloadInfo", out var d)) return null;
        string? S(string p) => d.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? Uri.UnescapeDataString(v.GetString()!) : null;

        string? version = S("Version"), url = S("DownloadURL");
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url)) return null;
        DateTime? date = DateTime.TryParseExact(S("ReleaseDateTime"), "ddd MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt : null;
        return new NvidiaDriver(version, date, url, S("DownloadURLFileSize"), S("DetailsURL"), S("Name") ?? "NVIDIA Driver");
    }
}
