using System.Runtime.InteropServices;
using SupernovaBench.Core.Drivers;

namespace SupernovaBench.Hardware.Drivers;

/// <summary>
/// Drivers oferecidos pelo Windows Update (API COM "Microsoft.Update.Session", a mesma do Configurações do Windows).
/// A busca leva de 10 a 60 s; tudo roda numa thread de fundo.
/// </summary>
public sealed class WindowsUpdateDrivers
{
    private readonly Dictionary<string, object> _updates = new();

    /// <summary>Cria a sessão (confirma que o serviço do Windows Update responde).</summary>
    public static bool IsAvailable()
    {
        try { return CreateSession() != null; }
        catch (Exception) { return false; }
    }

    private static dynamic? CreateSession()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session");
        return type == null ? null : Activator.CreateInstance(type);
    }

    public Task<List<WuDriverUpdate>> SearchAsync(CancellationToken ct) => Task.Run(() =>
    {
        dynamic session = CreateSession() ?? throw new InvalidOperationException("Windows Update indisponível");
        session.ClientApplicationID = "Supernova Bench";
        dynamic searcher = session.CreateUpdateSearcher();
        ct.ThrowIfCancellationRequested();
        dynamic result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
        var list = new List<WuDriverUpdate>();
        lock (_updates)
        {
            _updates.Clear();
            int count = result.Updates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic u = result.Updates.Item(i);
                string id = u.Identity.UpdateID;
                _updates[id] = u;
                list.Add(new WuDriverUpdate(id, (string)u.Title, Try(() => (string?)u.DriverClass), Try(() => (string?)u.DriverManufacturer),
                                            Try(() => (string?)u.DriverModel), Try(() => (DateTime?)u.DriverVerDate),
                                            Try(() => Convert.ToInt64(u.MaxDownloadSize)) ?? 0));
            }
        }
        return list;
    }, ct);

    /// <summary>Baixa e instala as atualizações escolhidas. progress: (etapa "download"/"install", índice, total).</summary>
    public Task<(List<WuInstallResult> Results, bool RebootRequired)> InstallAsync(IReadOnlyCollection<string> ids, Action<string, int, int>? progress,
                                                                                  CancellationToken ct) => Task.Run(() =>
    {
        dynamic session = CreateSession() ?? throw new InvalidOperationException("Windows Update indisponível");
        session.ClientApplicationID = "Supernova Bench";
        var chosen = new List<(string Id, dynamic Update)>();
        lock (_updates)
            foreach (var id in ids)
                if (_updates.TryGetValue(id, out var u)) chosen.Add((id, u));

        var results = new List<WuInstallResult>();
        bool reboot = false;
        for (int i = 0; i < chosen.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (id, update) = chosen[i];
            string title = update.Title;
            try
            {
                if (!(bool)update.EulaAccepted) update.AcceptEula();
                dynamic coll = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
                coll.Add(update);

                if (!(bool)update.IsDownloaded)
                {
                    progress?.Invoke("download", i, chosen.Count);
                    dynamic downloader = session.CreateUpdateDownloader();
                    downloader.Updates = coll;
                    downloader.Download();
                }
                ct.ThrowIfCancellationRequested();
                progress?.Invoke("install", i, chosen.Count);
                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = coll;
                dynamic r = installer.Install();
                int code = (int)r.ResultCode;
                int hr = (int)r.HResult;
                if ((bool)r.RebootRequired) reboot = true;
                results.Add(new WuInstallResult(id, title, code, hr));
            }
            catch (COMException ex)
            {
                results.Add(new WuInstallResult(id, title, 4, ex.HResult));
            }
        }
        return (results, reboot);
    }, ct);

    private static T? Try<T>(Func<T?> f)
    {
        try { return f(); }
        catch (Exception) { return default; }
    }
}
