using Microsoft.Extensions.DependencyInjection;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Platform.Windows;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services, AppPaths paths)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows' platform services only work on Windows");
        }

        return services.AddKeyedSingleton<ISecretStore>(SecretStoreKind.CredentialManager, new CredentialManager(AppHome.InstanceKey(paths.State)));
    }
}
