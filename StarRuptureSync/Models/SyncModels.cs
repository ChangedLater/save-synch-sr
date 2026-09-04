namespace StarRuptureSync.Models;

/// <summary>Overall synchronisation state of one session.</summary>
public enum SyncState
{
    /// <summary>Local and repo copies are byte-for-byte identical.</summary>
    InSync,

    /// <summary>Session exists in the repo but the user has no local copy of that session.</summary>
    NoLocalCopy,

    /// <summary>Session exists locally but has never been pushed to the repo.</summary>
    LocalOnly,

    /// <summary>Local files differ and appear to be the newer version (upload recommended).</summary>
    LocalAhead,

    /// <summary>Repo files differ and appear to be the newer version (download recommended).</summary>
    RemoteAhead,

    /// <summary>Both sides changed since the last sync; user must choose a direction.</summary>
    Conflict
}

/// <summary>Per-file comparison inside a session folder.</summary>
public record FileComparison(
    string FileName,
    string? LocalHash,
    string? RepoHash)
{
    public bool InLocal => LocalHash != null;
    public bool InRepo => RepoHash != null;
    public bool Matches => LocalHash != null && LocalHash == RepoHash;

    public string Status =>
        Matches ? "identical"
        : !InLocal ? "only in repo"
        : !InRepo ? "only in local"
        : "differs";
}

/// <summary>Result of comparing one session's local folder against the repo.</summary>
public class SessionComparison
{
    public required string SessionName { get; init; }
    public SyncState State { get; set; }
    public bool HasLocal { get; set; }
    public bool HasRepo { get; set; }
    public List<FileComparison> Files { get; } = new();

    /// <summary>Author of the current repo HEAD for this session's last change.</summary>
    public string? RepoLastAuthor { get; set; }
    public DateTimeOffset? RepoLastChangedUtc { get; set; }
    public string? RepoLastMessage { get; set; }

    public string Headline => State switch
    {
        SyncState.InSync => "Up to date",
        SyncState.NoLocalCopy => "No local copy – create the session in-game first",
        SyncState.LocalOnly => "Local only – not uploaded yet",
        SyncState.LocalAhead => "Your local save is newer – upload to share it",
        SyncState.RemoteAhead => "A newer version is available to download",
        SyncState.Conflict => "Conflict – local and remote both changed",
        _ => ""
    };
}

/// <summary>Outcome of a push attempt.</summary>
public enum PushOutcome
{
    Pushed,
    RejectedRemoteAdvanced,
    Failed
}

/// <summary>Details of who moved origin/main ahead of us, shown on a rejected push.</summary>
public record RemoteAdvanceInfo(string Author, DateTimeOffset WhenUtc, string Message);
