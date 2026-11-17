using System.Reflection;

namespace SupernovaBench.Core;

public static class AppInfo
{
    public const string Name = "Supernova Bench";
    public const string RepositoryUrl = "https://github.com/josepintopc/Claude_code";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
