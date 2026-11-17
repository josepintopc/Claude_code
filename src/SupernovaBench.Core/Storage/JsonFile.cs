using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupernovaBench.Core.Storage;

/// <summary>Leitura/escrita de JSON com gravação atômica (arquivo temporário + troca), para não corromper dados se o PC desligar.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(fs, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Arquivo corrompido ou ilegível: guarda uma cópia para diagnóstico e recomeça do zero.
            TryBackupCorrupt(path);
            return null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
            JsonSerializer.Serialize(fs, value, Options);
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryBackupCorrupt(string path)
    {
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch
        {
            // Melhor esforço.
        }
    }
}
