using System.Xml.Linq;

namespace OpenBackupManager.App.Windows.Tests;

public class AppNotificationExtensionsTests
{
    [Test]
    public void WithDismissButton_AddsASystemButtonAfterTheOthers()
    {
        var payload = """<toast><visual /><actions><action content="Restart now" arguments="action=restart" /></actions></toast>""";

        var actions = XDocument.Parse(AppNotificationExtensions.WithDismissButton(payload, "Later & close")).Root!.Element("actions")!.Elements().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actions.Select(a => (string?)a.Attribute("content")), Is.EqualTo(["Restart now", "Later & close"]));
            Assert.That((string?)actions[1].Attribute("activationType"), Is.EqualTo("system"));
            Assert.That((string?)actions[1].Attribute("arguments"), Is.EqualTo("dismiss"));
        }
    }

    [Test]
    public void WithDismissButton_AddsActionsWhenThereAreNone()
    {
        var actions = XDocument.Parse(AppNotificationExtensions.WithDismissButton("<toast><visual /></toast>", "Later")).Root!.Element("actions");

        Assert.That(actions?.Elements().Count(), Is.EqualTo(1));
    }
}
