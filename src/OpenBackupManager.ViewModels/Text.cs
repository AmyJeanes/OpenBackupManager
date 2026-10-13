using System.Globalization;

namespace OpenBackupManager.ViewModels;

public static class Text
{
    // Fills in a string's placeholders in the user's own formats
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
