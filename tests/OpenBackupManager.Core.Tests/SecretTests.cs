using System.Text.Json;

namespace OpenBackupManager.Core.Tests;

public class SecretTests
{
    private const string Value = "token-value";

    [Test]
    public void Reveal_ReturnsTheValue() => Assert.That(new Secret(Value).Reveal(), Is.EqualTo(Value));

    [Test]
    public void ToString_HidesTheValue() => Assert.That($"{new Secret(Value)}", Is.EqualTo("***"));

    [Test]
    public void Serialising_HidesTheValue() => Assert.That(JsonSerializer.Serialize(new Secret(Value)), Does.Not.Contain(Value));
}
