using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenBackupManager.ViewModels.Tests;

// Only Debug builds have the pseudo-locale
#if DEBUG
public partial class PseudoLocaleTests
{
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    [Test]
    public void EveryString_IsAccentedAndKeepsItsPlaceholders()
    {
        var strings = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

        Assert.That(strings, Is.Not.Empty);
        using (Assert.EnterMultipleScope())
        {
            foreach (var (key, english) in strings)
            {
                var pseudo = Strings.ResourceManager.GetString(key, Pseudo);
                Assert.That(pseudo, Does.StartWith("[").And.EndWith("]").And.Not.Contains(english), key);
                Assert.That(Placeholders(pseudo!), Is.EqualTo(Placeholders(english)), key);
            }
        }
    }

    private static string[] Placeholders(string text) => [.. Placeholder().Matches(text).Select(m => m.Value)];

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex Placeholder();
}
#endif
