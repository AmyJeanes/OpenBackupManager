using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.Credentials;

namespace OpenBackupManager.Platform.Windows;

// Secrets as generic credentials in Credential Manager, named "<prefix>/<name>". Windows encrypts them for this user
// and keeps them to this computer. Based on Git Credential Manager's store (see THIRD-PARTY-NOTICES.md)
public sealed unsafe class CredentialManager(string prefix) : ISecretStore
{
    // Credential Manager holds up to 2,560 bytes a secret, kept as UTF-16 as Windows' own tools read them
    public const int MaxLength = 2560 / sizeof(char);

    public Secret? Read(string name)
    {
        if (!PInvoke.CredRead(Target(name), CRED_TYPE.CRED_TYPE_GENERIC, out var credential))
        {
            return NotFound() ? null : throw new Win32Exception();
        }

        try
        {
            return new Secret(new string((char*)credential->CredentialBlob, 0, (int)credential->CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            PInvoke.CredFree(credential);
        }
    }

    public void Save(string name, Secret secret)
    {
        var value = secret.Reveal();
        if (value.Length > MaxLength)
        {
            throw new ArgumentException($"Credential Manager holds secrets of up to {MaxLength} characters", nameof(secret));
        }

        fixed (char* target = Target(name), userName = name, blob = value)
        {
            var credential = new CREDENTIALW
            {
                Type = CRED_TYPE.CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = userName,
                CredentialBlob = (byte*)blob,
                CredentialBlobSize = (uint)(value.Length * sizeof(char)),
                Persist = CRED_PERSIST.CRED_PERSIST_LOCAL_MACHINE,
            };
            if (!PInvoke.CredWrite(credential, 0))
            {
                throw new Win32Exception();
            }
        }
    }

    public bool Remove(string name) =>
        PInvoke.CredDelete(Target(name), CRED_TYPE.CRED_TYPE_GENERIC) || (NotFound() ? false : throw new Win32Exception());

    private static bool NotFound() => Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_NOT_FOUND;

    private string Target(string name) => $"{prefix}/{name}";
}
