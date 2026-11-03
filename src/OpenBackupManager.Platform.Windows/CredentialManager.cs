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

    // Windows has no async versions of these
    public Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default) => Run(() => Read(name), cancellationToken);

    public Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default) => Run(() =>
    {
        Save(name, secret);
        return true;
    }, cancellationToken);

    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default) => Run(() => Remove(name), cancellationToken);

    private Secret? Read(string name)
    {
        SecretName.Check(name);
        if (!PInvoke.CredRead(Target(name), CRED_TYPE.CRED_TYPE_GENERIC, out var credential))
        {
            return NotFound() ? null : throw Failed("read", name);
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

    private void Save(string name, Secret secret)
    {
        SecretName.Check(name);
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
                throw Failed("save", name);
            }
        }
    }

    private bool Remove(string name)
    {
        SecretName.Check(name);
        return PInvoke.CredDelete(Target(name), CRED_TYPE.CRED_TYPE_GENERIC) || (NotFound() ? false : throw Failed("remove", name));
    }

    // So what goes wrong comes back in the task, as from any other store
    private static Task<T> Run<T>(Func<T> call, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        try
        {
            return Task.FromResult(call());
        }
        catch (Exception e)
        {
            return Task.FromException<T>(e);
        }
    }

    // A network sign-in, such as over SSH, has no credentials of its own for Credential Manager to keep them with
    private static InvalidOperationException Failed(string action, string name)
    {
        var error = Marshal.GetLastPInvokeError();
        var reason = error == (int)WIN32_ERROR.ERROR_NO_SUCH_LOGON_SESSION
            ? "Windows doesn't let this session use it, as with a sign-in over SSH. Set secret-store to file to keep secrets in a file instead"
            : new Win32Exception(error).Message;
        return new InvalidOperationException($"Credential Manager couldn't {action} {name}: {reason}");
    }

    private static bool NotFound() => Marshal.GetLastPInvokeError() == (int)WIN32_ERROR.ERROR_NOT_FOUND;

    private string Target(string name) => $"{prefix}/{name}";
}
