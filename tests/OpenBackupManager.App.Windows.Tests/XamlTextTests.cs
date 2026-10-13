using System.Xml.Linq;

namespace OpenBackupManager.App.Windows.Tests;

public class XamlTextTests
{
    // Attributes whose text people see or hear, which comes from Strings so it can be translated
    private static readonly string[] TextAttributes =
        ["Text", "Content", "Title", "Header", "PlaceholderText", "Description", "Label", "ToolTipService.ToolTip", "AutomationProperties.Name"];

    [Test]
    public void Xaml_HasNoLiteralText()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Xaml"), "*.xaml", SearchOption.AllDirectories);
        var literals = files.SelectMany(file => XDocument.Load(file).Descendants().SelectMany(element =>
            element.Attributes()
                .Where(a => TextAttributes.Contains(a.Name.LocalName) && !a.Value.StartsWith('{'))
                .Select(a => $"{Path.GetFileName(file)}: {element.Name.LocalName} {a.Name.LocalName}=\"{a.Value}\"")
                .Concat(element.Nodes().OfType<XText>()
                    .Where(t => !string.IsNullOrWhiteSpace(t.Value))
                    .Select(t => $"{Path.GetFileName(file)}: {element.Name.LocalName} \"{t.Value.Trim()}\""))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(files, Is.Not.Empty);
            Assert.That(literals, Is.Empty);
        }
    }
}
