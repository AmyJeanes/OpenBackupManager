namespace OpenBackupManager.Testing;

public static class TestFolders
{
    // On Windows, something else, such as an antivirus scan, can hold a file, such as rclone's log, for a moment after
    // the program that wrote it exits
    public static void Delete(string folder)
    {
        for (var tries = 1; ; tries++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (IOException) when (tries < 20)
            {
                Thread.Sleep(100);
            }
        }
    }
}
