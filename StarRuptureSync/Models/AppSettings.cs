using System.Text.Json.Serialization;

namespace StarRuptureSync.Models;

/// <summary>
/// Persisted, per-user configuration. Stored as JSON in
/// %LOCALAPPDATA%\StarRuptureSync\settings.json. The git token is kept
/// DPAPI-encrypted (see <see cref="ApiKeyProtected"/>) and never written in clear.
/// </summary>
public class AppSettings
{
    /// <summary>Friendly name recorded as the git commit author for this user's uploads.</summary>
    public string Username { get; set; } = "";

    /// <summary>HTTPS URL of the shared git repository that backs the save files.</summary>
    public string RepoUrl { get; set; } = "";

    /// <summary>DPAPI-protected (CurrentUser) git personal access token, base64 encoded.</summary>
    public string ApiKeyProtected { get; set; } = "";

    /// <summary>
    /// Resolved StarRupture SaveGames directory (the folder that contains one
    /// sub-folder per session). Chosen by auto-detection or by the user.
    /// </summary>
    public string SaveGamesPath { get; set; } = "";

    /// <summary>
    /// For each session name, the commit SHA that local files matched at the last
    /// successful download/upload. Used to tell "local changed" from "remote advanced".
    /// </summary>
    public Dictionary<string, string> LastSyncedCommitBySession { get; set; } = new();

    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(RepoUrl) &&
        !string.IsNullOrWhiteSpace(SaveGamesPath);
}
