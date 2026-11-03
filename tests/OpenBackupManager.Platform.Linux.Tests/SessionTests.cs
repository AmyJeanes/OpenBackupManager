using System.Numerics;

namespace OpenBackupManager.Platform.Linux.Tests;

public class SessionTests
{
    // KDE Wallet writes its key as a signed number, so one with the top bit set has a zero byte in front
    [Test]
    public async Task APublicKeyWithAZeroByteInFront_IsAccepted()
    {
        var sent = (BigInteger.One << 1023).ToByteArray(isBigEndian: true);

        Assert.That(sent, Has.Length.EqualTo(129));
        await Assert.ThatAsync(() => Session.OpenAsync((_, _) => Task.FromResult((sent, "/org/freedesktop/secrets/session/1"))), Throws.Nothing);
    }
}
