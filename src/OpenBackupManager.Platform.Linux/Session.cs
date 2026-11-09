using System.Numerics;
using System.Security.Cryptography;

namespace OpenBackupManager.Platform.Linux;

// Encrypts secrets on their way over D-Bus, as libsecret does, in the steps section 7.3 of the Secret Service spec sets
internal sealed class Session(string path, byte[] key)
{
    private const string Algorithm = "dh-ietf1024-sha256-aes128-cbc-pkcs7";
    private const int PrimeLength = 128;
    private const int Generator = 2;
    private const int BlockLength = 16;

    // RFC 2409's "Second Oakley Group", which the spec names: 2^1024 - 2^960 - 1 + 2^64 * (floor(2^894 * pi) + 129093)
    private static readonly BigInteger Prime = new(Convert.FromHexString(
        "FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DD"
        + "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7ED"
        + "EE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381FFFFFFFFFFFFFFFF"), isUnsigned: true, isBigEndian: true);

    public string Path => path;

    public static async Task<Session> OpenAsync(Func<string, byte[], Task<(byte[] Output, string Path)>> openSession)
    {
        // Diffie-Hellman: each side sends 2 to the power of its own secret number, and raises the other's to it, so
        // both get the same value without it crossing the bus
        var privateKey = new BigInteger(RandomNumberGenerator.GetBytes(PrimeLength), isUnsigned: true, isBigEndian: true);
        var ourPublicKey = BigInteger.ModPow(Generator, privateKey, Prime);
        var (output, path) = await openSession(Algorithm, ourPublicKey.ToByteArray(isUnsigned: true, isBigEndian: true));
        var theirPublicKey = new BigInteger(output, isUnsigned: true, isBigEndian: true);

        // These would make the shared value one anyone could work out. Its length isn't checked, as KDE Wallet puts a zero
        // byte in front when the top bit is set, which libsecret accepts. Newer KDE no longer does, but older KDE still
        // will (https://bugs.kde.org/show_bug.cgi?id=514194)
        if (theirPublicKey < 2 || theirPublicKey > Prime - 2)
        {
            throw new CryptographicException("its public key isn't valid");
        }

        var shared = BigInteger.ModPow(theirPublicKey, privateKey, Prime).ToByteArray(isUnsigned: true, isBigEndian: true);

        // Padded to the prime's length, as libsecret does, or a value with a leading zero byte would give another key
        var padded = new byte[PrimeLength];
        shared.CopyTo(padded, PrimeLength - shared.Length);

        return new Session(path, HKDF.DeriveKey(HashAlgorithmName.SHA256, padded, 16));
    }

    public static bool IsEncrypted(byte[] parameters, byte[] value) =>
        parameters.Length == BlockLength && value.Length > 0 && value.Length % BlockLength == 0;

    public (byte[] Parameters, byte[] Value) Encrypt(byte[] secret)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var iv = RandomNumberGenerator.GetBytes(BlockLength);
        return (iv, aes.EncryptCbc(secret, iv));
    }

    public byte[] Decrypt(byte[] parameters, byte[] value)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(value, parameters);
    }
}
