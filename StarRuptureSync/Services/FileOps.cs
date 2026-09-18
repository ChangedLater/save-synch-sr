using System.IO;
using System.IO.Enumeration;

namespace StarRuptureSync.Services;

/// <summary>Small directory helpers shared by backup and sync code.</summary>
public static class FileOps
{
    /// <summary>The two file extensions that make up a StarRupture save slot.</summary>
    public static readonly string[] SaveExtensions = { ".sav", ".met" };

    /// <summary>
    /// Save files matching this pattern (the game's auto-saves) are never uploaded to,
    /// deleted for, or compared against the repo. They stay purely local.
    /// </summary>
    private const string SyncExcludedPattern = "AutoSave*.*";

    public static bool IsSaveFile(string path) =>
        SaveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsExcludedFromSync(string fileName) =>
        FileSystemName.MatchesSimpleExpression(SyncExcludedPattern, fileName);

    /// <param name="excludeAutoSaves">Skip files matching <see cref="IsExcludedFromSync"/> (e.g. when writing into the repo).</param>
    public static void CopyDirectory(
        string sourceDir, string destDir, bool saveFilesOnly = false, bool excludeAutoSaves = false)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            if (saveFilesOnly && !IsSaveFile(file))
                continue;
            var name = Path.GetFileName(file);
            if (excludeAutoSaves && IsExcludedFromSync(name))
                continue;
            File.Copy(file, Path.Combine(destDir, name), overwrite: true);
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

    /// <summary>Save-file names eligible for git sync, i.e. excluding auto-saves.</summary>
    public static IEnumerable<string> SyncableSaveFileNames(string sessionDir) =>
        SaveFileNames(sessionDir).Where(n => !IsExcludedFromSync(n));
}
