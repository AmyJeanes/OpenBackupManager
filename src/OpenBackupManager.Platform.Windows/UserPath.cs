using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace OpenBackupManager.Platform.Windows;

// Adds a folder to the user's PATH, which needs no admin rights. Everything else in it is kept exactly as it was
// written, such as %USERPROFILE%\bin, and the value keeps its type, so those still expand
public static class UserPath
{
    private const string UserEnvironmentKey = "Environment";
    private const string Name = "Path";

    public static void Add(string folder)
    {
        if (Add(folder, UserEnvironmentKey))
        {
            Announce();
        }
    }

    public static void Remove(string folder)
    {
        if (Remove(folder, UserEnvironmentKey))
        {
            Announce();
        }
    }

    // True if the value changed
    internal static bool Add(string folder, string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        if (Read(key) is not { } read)
        {
            return false;
        }

        var (value, kind) = read;
        if (value.Split(';').Any(entry => Same(entry, folder)))
        {
            return false;
        }

        key.SetValue(Name, value.Length == 0 ? folder : value.EndsWith(';') ? value + folder : value + ";" + folder, kind);
        return true;
    }

    internal static bool Remove(string folder, string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        if (key is null || Read(key) is not { } read)
        {
            return false;
        }

        var (value, kind) = read;
        var entries = value.Split(';');
        var kept = entries.Where(entry => !Same(entry, folder)).ToArray();
        if (kept.Length == entries.Length)
        {
            return false;
        }

        if (kept.All(entry => entry.Length == 0))
        {
            key.DeleteValue(Name);
        }
        else
        {
            key.SetValue(Name, string.Join(';', kept), kind);
        }

        return true;
    }

    // Unexpanded, as writing back an expanded value would fix every %VARIABLE% in it. Null for a value that isn't a
    // string, which writing a string over would destroy
    private static (string Value, RegistryValueKind Kind)? Read(RegistryKey key)
    {
        if (key.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value)
        {
            return ("", RegistryValueKind.ExpandString);
        }

        var kind = key.GetValueKind(Name);
        return value is string text && kind is RegistryValueKind.String or RegistryValueKind.ExpandString ? (text, kind) : null;
    }

    // Whole entries only, so a folder whose name starts with ours is never taken for it
    private static bool Same(string entry, string folder) =>
        string.Equals(entry.Trim().Trim('"').TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // Explorer reads the environment again, so what it starts from then on, such as a new terminal, has the change
    private static unsafe void Announce()
    {
        fixed (char* environment = "Environment")
        {
            PInvoke.SendMessageTimeout(
                HWND.HWND_BROADCAST, PInvoke.WM_SETTINGCHANGE, default, (nint)environment, SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 5000, null);
        }
    }
}
