using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;
using Tmds.DBus.Protocol;

namespace OpenBackupManager.Platform.Linux;

// Over D-Bus itself, as go-keyring does, so no native library has to be installed. Only tests pass a bus address
[SupportedOSPlatform("linux")]
public sealed class SecretService(string prefix, string? bus = null) : ISecretStore
{
    // An unlocked keyring answers in a fraction of a second, so one that doesn't answer in this time isn't going to
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default) => UseAsync("read", name, async keyring =>
        await keyring.SearchAsync(Attributes(name)) is [var item, ..] ? new Secret(Encoding.UTF8.GetString(await keyring.GetSecretAsync(item))) : null,
        cancellationToken);

    public Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default) => UseAsync("save", name, async keyring =>
    {
        var saved = await keyring.CreateItemAsync(Service(name), Attributes(name), Encoding.UTF8.GetBytes(secret.Reveal()));

        // So it's the only copy: saving replaces one with the same attributes only if the keyring does as the spec says,
        // and only in the default keyring
        var (unlocked, _) = await keyring.SearchEveryKeyringAsync(Attributes(name));
        foreach (var item in unlocked)
        {
            if (item != saved)
            {
                await keyring.DeleteAsync(item);
            }
        }

        return true;
    }, cancellationToken);

    // From every keyring, not only the default one, so a copy left in one that used to be the default goes too
    public Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default) => UseAsync("remove", name, async keyring =>
    {
        var (unlocked, locked) = await keyring.SearchEveryKeyringAsync(Attributes(name));
        if (locked.Length > 0)
        {
            throw Failed("remove", name, "a keyring holding it is locked");
        }

        foreach (var item in unlocked)
        {
            await keyring.DeleteAsync(item);
        }

        return unlocked.Length > 0;
    }, cancellationToken);

    private string Service(string name) => $"{prefix}/{name}";

    private Dictionary<string, string> Attributes(string name) => new() { ["service"] = Service(name), ["account"] = name };

    private async Task<T> UseAsync<T>(string action, string name, Func<Keyring, Task<T>> use, CancellationToken cancellationToken)
    {
        var address = bus ?? Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address))
        {
            throw Failed(action, name, "there's no D-Bus session to reach it through, as DBUS_SESSION_BUS_ADDRESS isn't set");
        }

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().AsTask().WaitAsync(Timeout, cancellationToken);
            var keyring = await Keyring.DefaultAsync(connection, reason => Failed(action, name, reason), cancellationToken);

            // secret-tool reports a secret in a locked keyring as missing, so this checks first, and never asks to unlock it
            if (await keyring.LockedAsync())
            {
                throw Failed(action, name, "the keyring is locked");
            }

            return await use(keyring);
        }
        catch (DBusErrorReplyException e) when (e.ErrorName == "org.freedesktop.DBus.Error.ServiceUnknown")
        {
            throw Failed(action, name, "nothing provides it, such as GNOME Keyring or KDE Wallet");
        }
        catch (DBusConnectFailedException e)
        {
            throw Failed(action, name, $"D-Bus at {address} can't be reached. {e.Message}");
        }
        catch (TimeoutException)
        {
            throw Failed(action, name, $"it didn't answer within {Timeout.TotalSeconds} seconds");
        }
        catch (Exception e) when (e is DBusExceptionBase or CryptographicException)
        {
            throw Failed(action, name, e.Message);
        }
    }

    private static InvalidOperationException Failed(string action, string name, string reason) =>
        new($"Secret Service couldn't {action} {name}: {reason}");

    // The default collection, as secret-tool uses, through the calls the Secret Service API spec describes
    private sealed class Keyring(DBusConnection connection, string collection, Func<string, Exception> failed, CancellationToken cancellationToken)
    {
        private const string Destination = "org.freedesktop.secrets";
        private const string ServicePath = "/org/freedesktop/secrets";
        private const string ServiceInterface = "org.freedesktop.Secret.Service";
        private const string CollectionInterface = "org.freedesktop.Secret.Collection";
        private const string ItemInterface = "org.freedesktop.Secret.Item";

        // The spec's path for no collection, or no prompt
        private const string None = "/";

        private Session? _session;

        public static async Task<Keyring> DefaultAsync(DBusConnection connection, Func<string, Exception> failed, CancellationToken cancellationToken)
        {
            var collection = await CallAsync(connection, ServicePath, ServiceInterface, "ReadAlias", "s", (ref writer) => writer.WriteString("default"),
                (message, _) => message.GetBodyReader().ReadObjectPathAsString(), cancellationToken);
            return collection == None ? throw failed("it has no default keyring") : new Keyring(connection, collection, failed, cancellationToken);
        }

        public Task<bool> LockedAsync() => CallAsync(collection, "org.freedesktop.DBus.Properties", "Get", "ss",
            (ref writer) =>
            {
                writer.WriteString(CollectionInterface);
                writer.WriteString("Locked");
            },
            (message, _) =>
            {
                var reader = message.GetBodyReader();
                reader.ReadSignature("b");
                return reader.ReadBool();
            });

        public Task<string[]> SearchAsync(Dictionary<string, string> attributes) =>
            CallAsync(collection, CollectionInterface, "SearchItems", "a{ss}", (ref writer) => WriteAttributes(ref writer, attributes),
                (message, _) =>
                {
                    var reader = message.GetBodyReader();
                    return ReadPaths(ref reader);
                });

        public Task<(string[] Unlocked, string[] Locked)> SearchEveryKeyringAsync(Dictionary<string, string> attributes) =>
            CallAsync(ServicePath, ServiceInterface, "SearchItems", "a{ss}", (ref writer) => WriteAttributes(ref writer, attributes),
                (message, _) =>
                {
                    var reader = message.GetBodyReader();
                    return (ReadPaths(ref reader), ReadPaths(ref reader));
                });

        public async Task<byte[]> GetSecretAsync(string item)
        {
            var session = await GetSessionAsync();
            var (sessionPath, parameters, value) = await CallAsync(item, ItemInterface, "GetSecret", "o", (ref writer) => writer.WriteObjectPath(session.Path),
                (message, _) =>
                {
                    var reader = message.GetBodyReader();
                    reader.AlignStruct();
                    return (reader.ReadObjectPathAsString(), reader.ReadArrayOfByte(), reader.ReadArrayOfByte());
                });

            // Checked before decrypting, which would otherwise fail with a less clear message
            if (sessionPath != session.Path || !Session.IsEncrypted(parameters, value))
            {
                throw failed("it sent back a secret that isn't encrypted for this session");
            }

            return session.Decrypt(parameters, value);
        }

        public async Task<string> CreateItemAsync(string label, Dictionary<string, string> attributes, byte[] secret)
        {
            var session = await GetSessionAsync();
            var (parameters, value) = session.Encrypt(secret);
            var (item, prompt) = await CallAsync(collection, CollectionInterface, "CreateItem", "a{sv}(oayays)b",
                (ref writer) =>
                {
                    var properties = writer.WriteDictionaryStart();
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("org.freedesktop.Secret.Item.Label");
                    writer.WriteVariantString(label);
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString("org.freedesktop.Secret.Item.Attributes");
                    writer.WriteVariant(new Dict<string, string>(attributes));
                    writer.WriteDictionaryEnd(properties);

                    writer.WriteStructureStart();
                    writer.WriteObjectPath(session.Path);
                    writer.WriteArray(parameters);
                    writer.WriteArray(value);
                    writer.WriteString("text/plain");

                    // Replaces an item with the same attributes
                    writer.WriteBool(true);
                },
                (message, _) =>
                {
                    var reader = message.GetBodyReader();
                    return (reader.ReadObjectPathAsString(), reader.ReadObjectPathAsString());
                });
            NoPromptNeeded(prompt);
            return item;
        }

        public async Task DeleteAsync(string item) =>
            NoPromptNeeded(await CallAsync(item, ItemInterface, "Delete", null, null, (message, _) => message.GetBodyReader().ReadObjectPathAsString()));

        // The keyring is unlocked, so it shouldn't ask, and the engine has nowhere to show a prompt
        private void NoPromptNeeded(string prompt)
        {
            if (prompt != None)
            {
                throw failed("it asked to show a prompt");
            }
        }

        private async Task<Session> GetSessionAsync() => _session ??= await Session.OpenAsync(OpenSessionAsync);

        private Task<(byte[] Output, string Path)> OpenSessionAsync(string algorithm, byte[] input) =>
            CallAsync(ServicePath, ServiceInterface, "OpenSession", "sv",
                (ref writer) =>
                {
                    writer.WriteString(algorithm);
                    writer.WriteVariant(VariantValue.Array(input));
                },
                (message, _) =>
                {
                    var reader = message.GetBodyReader();
                    reader.ReadSignature("ay");
                    return (reader.ReadArrayOfByte(), reader.ReadObjectPathAsString());
                });

        private static string[] ReadPaths(ref Reader reader) => [.. reader.ReadArrayOfObjectPath().Select(path => path.ToString())];

        private static void WriteAttributes(ref MessageWriter writer, Dictionary<string, string> attributes)
        {
            var start = writer.WriteDictionaryStart();
            foreach (var (key, value) in attributes)
            {
                writer.WriteDictionaryEntryStart();
                writer.WriteString(key);
                writer.WriteString(value);
            }

            writer.WriteDictionaryEnd(start);
        }

        private delegate void Body(ref MessageWriter writer);

        private Task<T> CallAsync<T>(string path, string @interface, string member, string? signature, Body? body, MessageValueReader<T> reader) =>
            CallAsync(connection, path, @interface, member, signature, body, reader, cancellationToken);

        // The message is written before anything is awaited, as its writer can't be kept across an await
        private static Task<T> CallAsync<T>(DBusConnection connection, string path, string @interface, string member, string? signature, Body? body,
            MessageValueReader<T> reader, CancellationToken cancellationToken)
        {
            var writer = connection.GetMessageWriter();
            MessageBuffer message;
            try
            {
                writer.WriteMethodCallHeader(destination: Destination, path: path, @interface: @interface, member: member, signature: signature);
                body?.Invoke(ref writer);
                message = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }

            return connection.CallMethodAsync(message, reader).WaitAsync(Timeout, cancellationToken);
        }
    }
}
