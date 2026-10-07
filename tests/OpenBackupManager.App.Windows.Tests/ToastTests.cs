using System.Xml.Linq;

namespace OpenBackupManager.App.Windows.Tests;

public class ToastTests
{
    [Test]
    public void ToString_HasTheTextAndClickArguments()
    {
        var toast = XElement.Parse(new Toast("open").Text("Sync failed").Text("Photos & videos couldn't sync").ToString());

        using (Assert.EnterMultipleScope())
        {
            Assert.That((string?)toast.Attribute("launch"), Is.EqualTo("open"));
            Assert.That(toast.Descendants("text").Select(t => t.Value), Is.EqualTo(["Sync failed", "Photos & videos couldn't sync"]));
            Assert.That(toast.Element("actions"), Is.Null);
        }
    }

    [Test]
    public void ToString_HasEachKindOfButton()
    {
        var toast = new Toast()
            .Text("Update available")
            .Button("Restart & update", "restart")
            .Link("What's new", new Uri("https://example.com/releases/tag/v1.0.0"))
            .DismissButton("Not now");

        var actions = XElement.Parse(toast.ToString()).Element("actions")!.Elements("action")
            .Select(a => ((string?)a.Attribute("content"), (string?)a.Attribute("arguments"), (string?)a.Attribute("activationType")));

        (string?, string?, string?)[] expected =
        [
            ("Restart & update", "restart", "foreground"),
            ("What's new", "https://example.com/releases/tag/v1.0.0", "protocol"),
            ("Not now", "dismiss", "system"),
        ];
        Assert.That(actions, Is.EqualTo(expected));
    }

    [Test]
    public void ToString_LeavesOutTheClickWithoutArguments()
    {
        var toast = XElement.Parse(new Toast().Text("Update installed").ToString());

        Assert.That(toast.Attribute("launch"), Is.Null);
    }
}
