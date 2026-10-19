using System.Text.Json;
using OpenBackupManager.Core.Configuration;

namespace OpenBackupManager.Core.Tests;

public class SettingsTests
{
    // Paths rather than settings, so each test is named after its setting
    private static IEnumerable<string> Documented => Settings.Documented.Select(s => s.Path);

    private static IEnumerable<string> DocumentedValues => Settings.Documented.Where(s => !s.IsSection).Select(s => s.Path);

    // So a setting added to AppConfig without being listed, or listed but never read, fails here
    [Test]
    public void MatchAppConfig() =>
        Assert.That(Settings.All.Select(s => (s.Path, s.Kind)), Is.EquivalentTo(Read(typeof(AppConfig), section: null)));

    [TestCaseSource(nameof(Documented))]
    public void Documented_HaveASummary(string path) => Assert.That(Settings.Find(path)!.Summary, Is.Not.Empty);

    [TestCaseSource(nameof(DocumentedValues))]
    public void DocumentedValues_HaveTheirFacts(string path)
    {
        var setting = Settings.Find(path)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(setting.Values, Is.Not.Empty);
            Assert.That(setting.Default, Is.Not.Empty);
            Assert.That(setting.DefaultInFull, Is.Not.Empty);
            Assert.That(setting.Example, Is.Not.Empty);
        }
    }

    [TestCaseSource(nameof(DocumentedValues))]
    public void DocumentedValues_ExampleIsRead(string path)
    {
        var result = ConfigFile.Parse(Settings.Find(path)!.ExampleText);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Problems, Is.Empty);
            Assert.That(result.Config, Is.Not.EqualTo(new AppConfig()));
        }
    }

    // Each property as the file names it, with what kind of setting it is
    private static IEnumerable<(string, SettingKind)> Read(Type type, string? section)
    {
        foreach (var property in type.GetProperties())
        {
            // Not a setting: which layout the file is in
            if (type == typeof(AppConfig) && property.Name == nameof(AppConfig.Version))
            {
                continue;
            }

            var path = (section is null ? "" : section + ".") + JsonNamingPolicy.KebabCaseLower.ConvertName(property.Name);
            var propertyType = property.PropertyType;
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                yield return (path, SettingKind.SectionList);
                foreach (var inner in Read(propertyType.GetGenericArguments()[0], path))
                {
                    yield return inner;
                }
            }
            else if (propertyType.Namespace == typeof(AppConfig).Namespace)
            {
                yield return (path, SettingKind.Section);
                foreach (var inner in Read(propertyType, path))
                {
                    yield return inner;
                }
            }
            else if (propertyType == typeof(bool) || propertyType == typeof(bool?))
            {
                yield return (path, SettingKind.Boolean);
            }
            else if (propertyType == typeof(string))
            {
                yield return (path, SettingKind.Text);
            }
            else
            {
                throw new NotSupportedException($"{path} is a {propertyType.Name}, which needs its own SettingKind");
            }
        }
    }
}
