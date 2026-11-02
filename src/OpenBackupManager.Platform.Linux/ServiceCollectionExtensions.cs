using Microsoft.Extensions.DependencyInjection;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Platform.Linux;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLinuxPlatform(this IServiceCollection services, AppPaths paths)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux' platform services only work on Linux");
        }

        return services.AddKeyedSingleton<ISecretStore>(SecretStoreKind.SecretService, new SecretService(AppHome.InstanceKey(paths.State)));
    }
}
