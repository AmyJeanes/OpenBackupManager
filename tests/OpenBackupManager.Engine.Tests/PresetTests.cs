namespace OpenBackupManager.Engine.Tests;

public class PresetTests
{
    // Such as someone's own app registration
    [Test]
    public void AClientsOwnAnswers_Win()
    {
        var answers = Preset.Answer(Preset.For("onedrive"), new Dictionary<string, string> { ["client_id"] = "mine", ["client_secret"] = "theirs" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(answers["client_id"], Is.EqualTo("mine"));
            Assert.That(answers["client_secret"], Is.EqualTo("theirs"));
            Assert.That(answers["region"], Is.EqualTo("global"));
        }
    }

    [Test]
    public void ABackendWithoutOne_HasOnlyTheClientsAnswers()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Preset.For("alias"), Is.Null);
            Assert.That(Preset.Answer(null, new Dictionary<string, string> { ["remote"] = "/x" }), Is.EqualTo(new Dictionary<string, string> { ["remote"] = "/x" }));
        }
    }
}
