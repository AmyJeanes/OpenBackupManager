using Microsoft.Win32;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Platform.Windows;

// Starts the app at login from the user's Run key, which needs no admin rights. Turning the app off in Windows' Startup
// apps doesn't remove the entry but marks it off under the same name elsewhere, so writing it again keeps that choice
public sealed class RunKey : IStartAtLogin
{
    private const string UserRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _name;
    private readonly string _command;
    private readonly string _path;

    public RunKey(string name, string command)
        : this(name, command, UserRunKey)
    {
    }

    internal RunKey(string name, string command, string path)
    {
        _name = name;
        _command = command;
        _path = path;
    }

    // Only with this command, so an entry left by a copy that has since moved is written again
    public bool IsOn
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(_path);
            return key?.GetValue(_name) as string == _command;
        }
    }

    public void TurnOn()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_path);
        key.SetValue(_name, _command);
    }

    public void TurnOff()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_path, writable: true);
        key?.DeleteValue(_name, throwOnMissingValue: false);
    }

    // When uninstalling, which doesn't know the homes the app was used with, so each one's entry is found by the exe it starts
    public static void RemoveFor(string exe) => RemoveFor(exe, UserRunKey);

    internal static void RemoveFor(string exe, string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        if (key is null)
        {
            return;
        }

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string command && command.StartsWith($"\"{exe}\"", StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(name);
            }
        }
    }
}
