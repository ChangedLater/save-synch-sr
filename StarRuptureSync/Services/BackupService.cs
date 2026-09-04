using System.IO;

namespace StarRuptureSync.Services;

/// <summary>
/// Copies a session's local save files to a timestamped folder under
/// %LOCALAPPDATA%\StarRuptureSync\backups before a download overwrites them.
/// Backups are deliberately kept out of git.
/// </summary>
public class BackupService
{
    /// <summary>
    /// Back up <paramref name="localSessionDir"/> and return the backup folder path,
    /// or null when there was nothing local to back up.
    /// </summary>
    public string? BackupSession(string sessionName, string localSessionDir)
    {
        if (!Directory.Exists(localSessionDir) ||
            !FileOps.SaveFileNames(localSessionDir).Any())
        {
            return null;
        }

        var safeName = MakeSafe(sessionName);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dest = Path.Combine(AppPaths.Backups, safeName, stamp);

        FileOps.CopyDirectory(localSessionDir, dest, saveFilesOnly: true);
        return dest;
    }

    private static string MakeSafe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
