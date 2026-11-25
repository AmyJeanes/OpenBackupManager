using System.Globalization;
using System.Runtime.InteropServices;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// Before something that saves a secret, as Linux has no store until one is chosen. It asks only in a terminal, so a
// script stops with what to set rather than waiting for an answer
internal static class StoreChoice
{
    // Input is null with no terminal. Returns false when none was chosen, having said why
    public static async Task<bool> EnsureAsync(
        SecretsStatus secrets,
        OSPlatform os,
        Func<string, CancellationToken, Task<MigrateSecretsResult>> choose,
        TextReader? input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (secrets.Store is not null)
        {
            return true;
        }

        List<string> names = [.. SecretStoreKinds.NamesOn(os)];
        if (input is null)
        {
            await NoneChosenAsync(names, error, cancellationToken);
            return false;
        }

        output.WriteLine("This device has nowhere to keep secrets yet, such as the password that protects your remotes' connection details.");
        output.WriteLine();
        for (var i = 0; i < names.Count; i++)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {i + 1}. {names[i]}: {SecretStoreKinds.Parse(names[i])!.Value.Facts(CultureInfo.CurrentCulture)}"));
        }

        output.WriteLine();
        while (true)
        {
            output.Write($"Which? ({string.Join(" or ", Enumerable.Range(1, names.Count))}): ");
            if (await input.ReadLineAsync(cancellationToken) is not { } answer)
            {
                output.WriteLine();
                await NoneChosenAsync(names, error, cancellationToken);
                return false;
            }

            answer = answer.Trim();
            var name = int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= names.Count
                ? names[number - 1]
                : names.Find(n => n == answer);
            if (name is null)
            {
                continue;
            }

            try
            {
                output.WriteLine(Migrate.Moved(await choose(name, cancellationToken), name));
                return true;
            }
            // Such as Secret Service with no default keyring, so another can be chosen
            catch (EngineException e)
            {
                await error.WriteLineAsync("obm: " + e.Message, cancellationToken);
            }
        }
    }

    private static Task NoneChosenAsync(List<string> names, TextWriter error, CancellationToken cancellationToken) =>
        error.WriteLineAsync(
            $"obm: this device has nowhere to keep secrets yet. Choose where with {string.Join(" or ", names.Select(name => "obm secrets migrate " + name))}, "
            + "or set secret-store in config.toml, then run this again",
            cancellationToken);
}
