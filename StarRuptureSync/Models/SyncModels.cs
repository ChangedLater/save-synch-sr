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
    string? RepoHash,
    DateTimeOffset? LocalEditedUtc = null,
    DateTimeOffset? RepoEditedUtc = null)
{
    public bool InLocal => LocalHash != null;
    public bool InRepo => RepoHash != null;
    public bool Matches => LocalHash != null && LocalHash == RepoHash;

    public string Status =>
        Matches ? "identical"
        : !InLocal ? "only in repo"
        : !InRepo ? "only in local"
        : "differs";

    /// <summary>Local file's last-write time, local zone (— when the file is not present locally).</summary>
    public string LocalEditedText => LocalEditedUtc?.ToLocalTime().ToString("g") ?? "—";

    /// <summary>Time of the last commit that changed this file (— when not in the repo).</summary>
    public string RepoEditedText => RepoEditedUtc?.ToLocalTime().ToString("g") ?? "—";
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

    // ---- per-file roll-up (shown as a one-line summary; full table is in the details window) ----

    public int IdenticalCount => Files.Count(f => f.Matches);
    public int DifferingCount => Files.Count(f => f.InLocal && f.InRepo && !f.Matches);
    public int RepoOnlyCount => Files.Count(f => f is { InRepo: true, InLocal: false });
    public int LocalOnlyCount => Files.Count(f => f is { InLocal: true, InRepo: false });

    /// <summary>Plain-language one-liner such as "All 4 files identical" or "remote has 2 newer files".</summary>
    public string FileSummary
    {
        get
        {
            var n = Files.Count;
            if (n == 0)
                return "No save files in this session.";
            if (IdenticalCount == n)
                return $"All {n} save file{Plural(n)} identical.";

            var parts = new List<string>();
            switch (State)
            {
                case SyncState.RemoteAhead:
                {
                    var newer = DifferingCount + RepoOnlyCount;
                    if (newer > 0) parts.Add($"remote has {newer} newer file{Plural(newer)}");
                    if (LocalOnlyCount > 0) parts.Add($"{LocalOnlyCount} only on your PC");
                    break;
                }
                case SyncState.LocalAhead:
                {
                    var newer = DifferingCount + LocalOnlyCount;
                    if (newer > 0) parts.Add($"your PC has {newer} newer file{Plural(newer)}");
                    if (RepoOnlyCount > 0) parts.Add($"{RepoOnlyCount} only on remote");
                    break;
                }
                default:
                {
                    if (DifferingCount > 0) parts.Add($"{DifferingCount} file{Plural(DifferingCount)} differ");
                    if (RepoOnlyCount > 0) parts.Add($"{RepoOnlyCount} only on remote");
                    if (LocalOnlyCount > 0) parts.Add($"{LocalOnlyCount} only on your PC");
                    break;
                }
            }

            if (IdenticalCount > 0)
                parts.Add($"{IdenticalCount} identical");
            return string.Join("  •  ", parts);
        }
    }

    /// <summary>Who last changed this session on the remote, or "" when unknown.</summary>
    public string RemoteChangeSummary =>
        RepoLastAuthor == null
            ? ""
            : $"Remote last changed by {RepoLastAuthor} at " +
              $"{RepoLastChangedUtc?.ToLocalTime():g} — \"{RepoLastMessage}\"";

    private static string Plural(int count) => count == 1 ? "" : "s";
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

/// <summary>One entry in the repo history view.</summary>
public record CommitInfo(string Message, string Author, DateTimeOffset WhenUtc, string Sha)
{
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;
    public string WhenText => WhenUtc.ToLocalTime().ToString("g");
    public string Subtitle => $"{Author}  ·  {WhenText}  ·  {ShortSha}";
}
