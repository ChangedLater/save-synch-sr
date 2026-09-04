using System.IO;
using Microsoft.Win32;

namespace StarRuptureSync.Services;

public record SaveLocationCandidate(string Path, string Source)
{
    public override string ToString() => $"{Path}  ({Source})";
}

/// <summary>
/// Auto-discovers the StarRupture SaveGames folder. Resolution order:
/// 1. Steam path from registry HKCU\Software\Valve\Steam\SteamPath
/// 2. [SteamPath]\userdata\[SteamID]\1631270\remote\Saved\SaveGames\  (prompt if several)
/// 3. %LOCALAPPDATA%\StarRupture\Saved\SaveGames\
/// </summary>
public class SaveLocationResolver
{
    private const string StarRuptureAppId = "1631270";

    private static readonly string[] SaveGamesTail =
        { StarRuptureAppId, "remote", "Saved", "SaveGames" };

    public IReadOnlyList<SaveLocationCandidate> Resolve()
    {
        var results = new List<SaveLocationCandidate>();

        var steamPath = GetSteamPath();
        if (steamPath != null)
        {
            var userdata = Path.Combine(steamPath, "userdata");
            if (Directory.Exists(userdata))
            {
                foreach (var userDir in Directory.EnumerateDirectories(userdata))
                {
                    var candidate = Path.Combine(new[] { userDir }.Concat(SaveGamesTail).ToArray());
                    if (Directory.Exists(candidate))
                    {
                        results.Add(new SaveLocationCandidate(
                            candidate,
                            $"Steam userdata / SteamID {Path.GetFileName(userDir)}"));
                    }
                }
            }
        }

        var localApp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StarRupture", "Saved", "SaveGames");
        if (Directory.Exists(localApp))
            results.Add(new SaveLocationCandidate(localApp, "LOCALAPPDATA\\StarRupture"));

        return results;
    }

    /// <summary>True when several distinct Steam SaveGames folders were found and the user must pick.</summary>
    public static bool NeedsUserChoice(IReadOnlyList<SaveLocationCandidate> candidates) =>
        candidates.Select(c => c.Path)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .Count() > 1;

    private static string? GetSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p && !string.IsNullOrWhiteSpace(p))
                return p.Replace('/', '\\');
        }
        catch
        {
            // Registry unreadable – fall through to well-known locations.
        }

        foreach (var guess in new[]
                 {
                     @"C:\Program Files (x86)\Steam",
                     @"C:\Program Files\Steam"
                 })
        {
            if (Directory.Exists(guess))
                return guess;
        }

        return null;
    }
}
