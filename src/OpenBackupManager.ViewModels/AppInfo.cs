using System.Reflection;

namespace OpenBackupManager.ViewModels;

// The app's name, version and copyright as the user sees them, and where it's developed
public sealed record AppInfo(string Name, string Version, string? Copyright, Uri Repository)
{
    // Leaves out build details, such as the commit hash, from the version. The repository is RepositoryUrl in
    // Directory.Build.props, which the SDK adds to every assembly
    public static AppInfo FromAssembly(string name, Assembly assembly) => new(
        name,
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "",
        assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright,
        new Uri(assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryUrl").Value!));
}
