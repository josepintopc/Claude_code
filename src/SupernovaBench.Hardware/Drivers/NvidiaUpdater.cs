using System.Diagnostics;
using System.Net.Http;
using SupernovaBench.Core;
using SupernovaBench.Core.Drivers;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.Hardware.Drivers;

/// <summary>Driver mais recente da NVIDIA (consulta pública do site) e download do instalador.</summary>
public static class NvidiaUpdater
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan ProductsCacheAge = TimeSpan.FromDays(7);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"SupernovaBench/{AppInfo.Version}");
        return c;
    }

    public static string ProductsCacheFile => Path.Combine(AppPaths.Root, "nvidia-products.xml");

    private static async Task<List<NvidiaProduct>> ProductsAsync(CancellationToken ct)
    {
        string file = ProductsCacheFile;
        if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < ProductsCacheAge)
        {
            try { return NvidiaDriverApi.ParseProducts(await File.ReadAllTextAsync(file, ct)); }
            catch (Exception) { /* cache corrompido: baixa de novo */ }
        }
        string xml = await Http.GetStringAsync(NvidiaDriverApi.ProductsUrl, ct);
        var products = NvidiaDriverApi.ParseProducts(xml);
        if (products.Count > 0)
        {
            try { await File.WriteAllTextAsync(file, xml, ct); } catch (IOException) { }
        }
        return products;
    }

    /// <summary>Driver mais recente para a placa (null se a placa não estiver na lista da NVIDIA).</summary>
    public static async Task<NvidiaDriver?> LatestAsync(string gpuName, bool studio, CancellationToken ct)
    {
        var products = await ProductsAsync(ct);
        var product = NvidiaDriverApi.FindProduct(products, gpuName);
        if (product == null) return null;
        string json = await Http.GetStringAsync(NvidiaDriverApi.LatestUrl(product, studio), ct);
        return NvidiaDriverApi.ParseLatest(json);
    }

    public static string DownloadFolder
    {
        get
        {
            var dir = Path.Combine(Path.GetTempPath(), "SupernovaBench");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Baixa o instalador (retoma um .part interrompido). progress: (bytes baixados, total ou null).</summary>
    public static async Task<string> DownloadAsync(string url, Action<long, long?>? progress, CancellationToken ct)
    {
        string name = Path.GetFileName(new Uri(url).LocalPath);
        string dest = Path.Combine(DownloadFolder, name);
        if (File.Exists(dest)) return dest;
        string part = dest + ".part";
        long existing = File.Exists(part) ? new FileInfo(part).Length : 0;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SupernovaBench/{AppInfo.Version}");
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        bool resumed = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!resumed) existing = 0;
        long? total = resp.Content.Headers.ContentLength is { } len ? len + existing : null;

        await using (var input = await resp.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
        {
            var buffer = new byte[1 << 16];
            long done = existing;
            var sw = Stopwatch.StartNew();
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (sw.ElapsedMilliseconds >= 200)
                {
                    progress?.Invoke(done, total);
                    sw.Restart();
                }
            }
            progress?.Invoke(done, total);
        }
        File.Move(part, dest, overwrite: true);
        return dest;
    }

    /// <summary>Abre o instalador da NVIDIA (ele conduz a instalação).</summary>
    public static void RunInstaller(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
