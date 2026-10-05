using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace OpenBackupManager.Core;

/// <summary>
/// A synced folder's permanent ID: 8 random characters of lowercase Crockford base32.
/// It names the folder's bookkeeping files on the remote, so it never changes
/// </summary>
public readonly record struct FolderId
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
    private const int Length = 8;

    public string Value { get; }

    private FolderId(string value) => Value = value;

    public static FolderId New() => new(RandomNumberGenerator.GetString(Alphabet, Length));

    public static bool TryParse([NotNullWhen(true)] string? value, out FolderId id)
    {
        if (value is { Length: Length } && value.All(Alphabet.Contains))
        {
            id = new FolderId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value;
}
