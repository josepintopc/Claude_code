using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SupernovaBench.Core;
using SupernovaBench.Core.Games;
using SupernovaBench.Core.Storage;
using SupernovaBench.Games;

namespace SupernovaBench.App.Services;

/// <summary>Capa encontrada: a imagem e se é só o ícone do executável (mostrado sobre um fundo).</summary>
public sealed record CoverImage(ImageSource Image, bool IsIcon);

/// <summary>
/// Capas dos jogos, na ordem: cache local → cache da própria Steam → CDN da Steam (pelo App ID ou pela busca da
/// loja pelo nome, se o usuário permitir) → ícone do executável. Sem nada disso, a tela mostra as iniciais.
/// </summary>
public sealed class CoverService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly SemaphoreSlim _downloads = new(4);
    private readonly ConcurrentDictionary<string, Task<CoverImage?>> _cache = new();
    private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"SupernovaBench/{AppInfo.Version}");
        return c;
    }

    public Task<CoverImage?> GetAsync(GameEntry game, string? exe) => _cache.GetOrAdd(game.Id, _ => LoadAsync(game, exe));

    /// <summary>Apaga o cache em disco e na memória (Configurações → Limpar capas).</summary>
    public void ClearCache()
    {
        _cache.Clear();
        foreach (var f in Directory.EnumerateFiles(AppPaths.CoversDir))
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }

    private async Task<CoverImage?> LoadAsync(GameEntry game, string? exe)
    {
        try
        {
            string file = Path.Combine(AppPaths.CoversDir, SafeName(game.Id) + ".jpg");
            string missing = Path.ChangeExtension(file, ".none");
            if (File.Exists(file) && await Task.Run(() => Decode(file)) is { } cached) return new CoverImage(cached, false);

            if (game.SteamAppId is int steamId && await Task.Run(() => LocalSteamCover(steamId)) is { } local
                && await Task.Run(() => Decode(local)) is { } localImg)
                return new CoverImage(localImg, false);

            bool recentlyMissing = File.Exists(missing) && DateTime.UtcNow - File.GetLastWriteTimeUtc(missing) < RetryMissingAfter;
            if (AppServices.Settings.DownloadCovers && !recentlyMissing)
            {
                // Só marca "sem capa" quando a resposta foi definitiva (sem internet, tenta de novo na próxima vez).
                var (searched, appId) = game.SteamAppId is int id ? (true, id) : await SearchSteamAsync(game.Name);
                var download = appId != null ? await DownloadAsync(appId.Value, file) : (bool?)false;
                if (download == true && await Task.Run(() => Decode(file)) is { } img)
                    return new CoverImage(img, false);
                if (searched && download == false)
                {
                    try { await File.WriteAllTextAsync(missing, game.Name); } catch (IOException) { }
                }
            }

            if (exe != null && File.Exists(exe) && await Application.Current.Dispatcher.InvokeAsync(() => ExeIcon(exe)) is { } icon)
                return new CoverImage(icon, true);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        return null;
    }

    /// <summary>(a busca respondeu?, App ID encontrado).</summary>
    private async Task<(bool Ok, int? AppId)> SearchSteamAsync(string name)
    {
        await _downloads.WaitAsync();
        try
        {
            string json = await Http.GetStringAsync(CoverSearch.StoreSearchUrl(name));
            return (true, CoverSearch.PickBestMatch(json, name));
        }
        catch (Exception)
        {
            return (false, null); // sem internet ou loja fora do ar
        }
        finally
        {
            _downloads.Release();
        }
    }

    /// <summary>true = salvou; false = o CDN respondeu que não existe; null = falha de rede (tentar depois).</summary>
    private async Task<bool?> DownloadAsync(int appId, string file)
    {
        await _downloads.WaitAsync();
        try
        {
            bool networkError = false;
            foreach (var url in CoverSearch.SteamCoverUrls(appId))
            {
                try
                {
                    using var resp = await Http.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) continue;
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    if (bytes.Length < 1024) continue;
                    await File.WriteAllBytesAsync(file, bytes);
                    return true;
                }
                catch (HttpRequestException) { networkError = true; }
                catch (TaskCanceledException) { networkError = true; }
            }
            return networkError ? null : false;
        }
        finally
        {
            _downloads.Release();
        }
    }

    /// <summary>Capa que a própria Steam já baixou (formato antigo "{id}_library_600x900.jpg" ou pasta por jogo).</summary>
    private static string? LocalSteamCover(int appId)
    {
        if (GameScanner.SteamRoot() is not { } root) return null;
        var cache = Path.Combine(root, "appcache", "librarycache");
        var flat = Path.Combine(cache, $"{appId}_library_600x900.jpg");
        if (File.Exists(flat)) return flat;
        var dir = Path.Combine(cache, appId.ToString());
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "library_600x900*.jpg", SearchOption.AllDirectories).FirstOrDefault()
               ?? Directory.EnumerateFiles(dir, "library_capsule*.jpg", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static ImageSource? Decode(string file)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(File.ReadAllBytes(file));
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 300;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null; // arquivo corrompido: tenta a próxima fonte
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[]? ids, uint count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    private static ImageSource? ExeIcon(string exe)
    {
        var handles = new IntPtr[1];
        if (PrivateExtractIcons(exe, 0, 128, 128, handles, null, 1, 0) == 0 || handles[0] == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            DestroyIcon(handles[0]);
        }
    }

    private static string SafeName(string id) => string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
