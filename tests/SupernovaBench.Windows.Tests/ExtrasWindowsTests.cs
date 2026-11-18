using SupernovaBench.Games;

namespace SupernovaBench.Windows.Tests;

public class ExtrasWindowsTests
{
    [WindowsFact]
    public void Frame_rate_session_starts_and_stops_cleanly()
    {
        using var m = new FrameRateMonitor();
        bool started = m.Start();
        // O runner do CI roda como administrador; sem admin o motivo fica em Error.
        Assert.True(started || m.Error == "admin", m.Error);
        Thread.Sleep(300);
        m.Stop();
        Assert.False(m.IsRunning);
        Assert.True(m.Start() || m.Error == "admin", m.Error); // de novo, sem "sessão já existe"
        m.Stop();
    }

    [WindowsFact]
    public void Foreground_and_refresh_rate_never_throw()
    {
        _ = ForegroundWindow.Current();
        var hz = ForegroundWindow.RefreshRate();
        Assert.True(hz == null || hz > 1);
        Assert.NotNull(ForegroundWindow.ProcessName(Environment.ProcessId));
    }
}
