using System.IO;

namespace StarRuptureSync.Services;

/// <summary>Small directory helpers shared by backup and sync code.</summary>
public static class FileOps
{
    /// <summary>The two file extensions that make up a StarRupture save slot.</summary>
    public static readonly string[] SaveExtensions = { ".sav", ".met" };

    public static bool IsSaveFile(string path) =>
        SaveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static void CopyDirectory(string sourceDir, string destDir, bool saveFilesOnly = false)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            if (saveFilesOnly && !IsSaveFile(file))
                continue;
            var target = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Enumerate save-file names (e.g. "0.sav", "AutoSave0.met") in a session folder.</summary>
    public static IEnumerable<string> SaveFileNames(string sessionDir)
    {
        if (!Directory.Exists(sessionDir))
            return Enumerable.Empty<string>();

        return Directory.EnumerateFiles(sessionDir)
            .Where(IsSaveFile)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }
}
