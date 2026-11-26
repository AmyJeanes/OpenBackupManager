using System.Reflection;

namespace OpenBackupManager.Core;

// From RepositoryUrl and GuideUrl in Directory.Build.props, so each is set in one place
public static class AppLinks
{
    public static string Repository { get; } = Metadata("RepositoryUrl");

    public static string GuideHome { get; } = Metadata("GuideUrl");

    // A guide page, with a heading's anchor after it if wanted, such as docker/#keeping-the-password-out-of-config
    public static string Guide(string page) => GuideHome.TrimEnd('/') + "/" + page;

    private static string Metadata(string key) =>
        typeof(AppLinks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;
}
