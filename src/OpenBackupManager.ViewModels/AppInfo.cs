using System.Reflection;

namespace OpenBackupManager.ViewModels;

// The app's name, version and copyright as the user sees them
public sealed record AppInfo(string Name, string Version, string? Copyright)
{
    // Leaves out build details, such as the commit hash, from the version
    public static AppInfo FromAssembly(string name, Assembly assembly) => new(
        name,
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "",
        assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
}
