using System.IO;

namespace StarRuptureSync.Services;

/// <summary>Well-known local directories used by the app (never inside the Steam save folder).</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StarRuptureSync");

    /// <summary>Working clone of the shared repository.</summary>
    public static string Repo => Path.Combine(Root, "repo");

    /// <summary>Timestamped backups of local saves taken before a download overwrites them.</summary>
    public static string Backups => Path.Combine(Root, "backups");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static void EnsureRoot() => Directory.CreateDirectory(Root);
}
