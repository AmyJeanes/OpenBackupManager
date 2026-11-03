using System.Text.RegularExpressions;

namespace OpenBackupManager.Core.Platform;

// Every store takes only these names: a newline would split the Keychain tool's command, and Credential Manager ignores
// case, so two names differing only in case would be one secret there
public static partial class SecretName
{
    public static bool IsValid(string name) => Valid().IsMatch(name);

    public static void Check(string name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException($"Secret names are up to 64 lowercase letters, digits and dashes, starting with a letter or digit, not \"{name}\"", nameof(name));
        }
    }

    // \z rather than $, which also matches before a newline at the end
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{0,63}\z")]
    private static partial Regex Valid();
}
