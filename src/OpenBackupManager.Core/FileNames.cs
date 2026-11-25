using System.Runtime.InteropServices;
using System.Text;

namespace OpenBackupManager.Core;

// What a file or folder can be called on each OS, such as for one named on Linux that Windows can't hold
public static class FileNames
{
    // Over the most each OS's usual filesystems take: NTFS counts UTF-16 units, and APFS and ext4 count UTF-8 bytes
    private const int Longest = 255;

    private static readonly OSPlatform[] AllPlatforms = [OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux];

    // Null when it's fine on this platform, or on all of them, or the first problem
    public static FileNameProblem? Check(string name, bool allPlatforms = false) =>
        (allPlatforms ? AllPlatforms : [CurrentOS.Platform]).Select(os => Check(name, os)).FirstOrDefault(problem => problem is not null);

    internal static FileNameProblem? Check(string name, OSPlatform os)
    {
        var windows = os == OSPlatform.Windows;
        if (name.Length == 0)
        {
            return new FileNameProblem(FileNameProblemKind.Empty);
        }

        if (name is "." or "..")
        {
            return new FileNameProblem(FileNameProblemKind.Dots);
        }

        foreach (var c in name)
        {
            if (c is '/' or '\0' || (windows && (c < ' ' || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')))
            {
                return new FileNameProblem(FileNameProblemKind.Character, c);
            }
        }

        if (windows && IsDeviceName(name))
        {
            return new FileNameProblem(FileNameProblemKind.DeviceName);
        }

        // Windows drops them, so the file would have another name
        if (windows && name[^1] is ' ' or '.')
        {
            return new FileNameProblem(FileNameProblemKind.EndsWithSpaceOrDot);
        }

        return (windows ? name.Length : Encoding.UTF8.GetByteCount(name)) > Longest ? new FileNameProblem(FileNameProblemKind.TooLong) : null;
    }

    // Windows can open the device instead, in any case and with anything after a dot, such as NUL.txt, or spaces before
    // it. COM and LPT take a superscript digit too
    private static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd(' ');
        return stem.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && (char.IsAsciiDigit(stem[3]) || stem[3] is '¹' or '²' or '³'));
    }
}

// Character is the one it can't have
public sealed record FileNameProblem(FileNameProblemKind Kind, char? Character = null);

public enum FileNameProblemKind
{
    Empty,

    // . or .., which name the folder itself and the one above
    Dots,
    Character,
    DeviceName,
    EndsWithSpaceOrDot,
    TooLong,
}
