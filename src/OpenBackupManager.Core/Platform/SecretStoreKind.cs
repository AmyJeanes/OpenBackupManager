using System.Runtime.InteropServices;
using OpenBackupManager.Core.Configuration;

namespace OpenBackupManager.Core.Platform;

public enum SecretStoreKind
{
    CredentialManager,
    Keychain,
    SecretService,
    File,
}

public static class SecretStoreKinds
{
    private static readonly (SecretStoreKind Kind, string Name, string Title, Os WorksOn)[] All =
    [
        (SecretStoreKind.CredentialManager, "credential-manager", "Credential Manager", Os.Windows),
        (SecretStoreKind.Keychain, "keychain", "the login Keychain", Os.MacOS),
        (SecretStoreKind.SecretService, "secret-service", "Secret Service", Os.Linux),
        (SecretStoreKind.File, "file", "a file only you can read", Os.All),
    ];

    // As written in config.toml
    public static string Name(this SecretStoreKind kind) => All.Single(k => k.Kind == kind).Name;

    // For a sentence, such as "Secrets are kept in Credential Manager"
    public static string Title(this SecretStoreKind kind) => All.Single(k => k.Kind == kind).Title;

    // Linux has none, as Secret Service can't be read while you're not signed in with your password
    public static SecretStoreKind? DefaultOn(OSPlatform os) =>
        os == OSPlatform.Windows ? SecretStoreKind.CredentialManager : os == OSPlatform.OSX ? SecretStoreKind.Keychain : null;

    public static SecretStoreKind? Parse(string name) => All.FirstOrDefault(k => k.Name == name) is { Name: not null } match ? match.Kind : null;

    public static IEnumerable<string> NamesOn(OSPlatform os) => All.Where(k => k.WorksOn.HasFlag(OsOf(os))).Select(k => k.Name);

    internal static Os OsOf(OSPlatform os) => os == OSPlatform.Windows ? Os.Windows : os == OSPlatform.OSX ? Os.MacOS : Os.Linux;
}
