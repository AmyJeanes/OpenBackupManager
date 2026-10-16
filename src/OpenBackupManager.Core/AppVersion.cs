using System.Reflection;

namespace OpenBackupManager.Core;

public static class AppVersion
{
    // With build details such as the commit hash, so two different builds of one version can be told apart
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
}
