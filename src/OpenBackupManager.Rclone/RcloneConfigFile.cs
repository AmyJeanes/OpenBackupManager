namespace OpenBackupManager.Rclone;

public static class RcloneConfigFile
{
    // As rclone checks
    public static bool IsEncrypted(string path) =>
        File.ReadLines(path).Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && line[0] is not ('#' or ';')) == "RCLONE_ENCRYPT_V0:";
}
