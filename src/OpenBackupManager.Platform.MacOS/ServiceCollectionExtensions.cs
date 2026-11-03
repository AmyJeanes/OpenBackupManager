using Microsoft.Extensions.DependencyInjection;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Platform.MacOS;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMacOSPlatform(this IServiceCollection services, AppPaths paths)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS' platform services only work on macOS");
        }

        return services.AddKeyedSingleton<ISecretStore>(SecretStoreKind.Keychain, new Keychain(AppHome.InstanceKey(paths.State), Keychain.Login));
    }
}
