using System.IO;
using StarRuptureSync.Models;

namespace StarRuptureSync.Services;

public enum ConflictChoice
{
    /// <summary>Re-pull origin and discard the local upload.</summary>
    DiscardMine,

    /// <summary>Force-push, overwriting the version someone else pushed.</summary>
    OverwriteTheirs,

    /// <summary>Abort the upload, leaving the repo matching origin.</summary>
    Cancel
}

public record OperationResult(bool Success, string Message);

/// <summary>
/// Coordinates git and the local Steam save folder to implement the documented
/// synchronisation pattern. All methods are synchronous; callers marshal to a
/// background thread.
/// </summary>
public class SyncEngine
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly GitSyncService _git;
    private readonly BackupService _backup;
    private readonly GameProcessService _game;

    public SyncEngine(
        AppSettings settings,
        SettingsService settingsService,
        GitSyncService git,
        BackupService backup,
        GameProcessService game)
    {
        _settings = settings;
        _settingsService = settingsService;
        _git = git;
        _backup = backup;
        _game = game;
    }

    private string LocalSessionDir(string session) => Path.Combine(_settings.SaveGamesPath, session);

    /// <summary>True while StarRupture is running; <paramref name="processName"/> is the matched process.</summary>
    public bool IsGameRunning(out string? processName) => _game.IsRunning(out processName);

    /// <summary>Most recent commits on the synced branch (message + author + time), newest first.</summary>
    public IReadOnlyList<CommitInfo> History(int max = 200) => _git.History(max);

    /// <summary>Session folders contained in a specific commit.</summary>
    public IReadOnlyList<string> SessionsInCommit(string commitSha) => _git.SessionsInCommit(commitSha);

    // ---- refresh / compare ----------------------------------------------------

    /// <summary>fetch + reset --hard, then compare every session (repo and local).</summary>
    public IReadOnlyList<SessionComparison> Refresh()
    {
        _git.FetchAndResetHard();
        return BuildComparisons();
    }

    public IReadOnlyList<SessionComparison> BuildComparisons()
    {
        var repoSessions = _git.Sessions();

        var localSessions = Directory.Exists(_settings.SaveGamesPath)
            ? Directory.EnumerateDirectories(_settings.SaveGamesPath)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
            : Enumerable.Empty<string>();

        var all = repoSessions
            .Concat(localSessions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        return all.Select(Compare).ToList();
    }

    public SessionComparison Compare(string session)
    {
        var localDir = LocalSessionDir(session);
        var repoDir = GitSyncService.RepoSessionDir(session);

        var localNames = FileOps.SaveFileNames(localDir).ToList();
        var repoNames = FileOps.SaveFileNames(repoDir).ToList();

        var hasLocal = localNames.Count > 0;
        var hasRepo = Directory.Exists(repoDir) && repoNames.Count > 0;

        var result = new SessionComparison
        {
            SessionName = session,
            HasLocal = hasLocal,
            HasRepo = hasRepo
        };

        var repoEditTimes = hasRepo
            ? _git.LastChangeTimesForSession(session)
            : (IReadOnlyDictionary<string, DateTimeOffset>)new Dictionary<string, DateTimeOffset>();

        foreach (var name in localNames.Concat(repoNames).Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var localPath = Path.Combine(localDir, name);
            var repoPath = Path.Combine(repoDir, name);

            DateTimeOffset? localEdited = File.Exists(localPath)
                ? File.GetLastWriteTimeUtc(localPath)
                : null;
            DateTimeOffset? repoEdited = repoEditTimes.TryGetValue(name, out var rt) ? rt : null;

            result.Files.Add(new FileComparison(
                name,
                HashUtil.HashFile(localPath),
                HashUtil.HashFile(repoPath),
                localEdited,
                repoEdited));
        }

        if (hasRepo)
        {
            var change = _git.LastChange(session);
            if (change is { } c)
            {
                result.RepoLastAuthor = c.author;
                result.RepoLastChangedUtc = c.whenUtc;
                result.RepoLastMessage = c.message;
            }
        }

        result.State = DetermineState(session, result, localDir);
        return result;
    }

    private SyncState DetermineState(string session, SessionComparison cmp, string localDir)
    {
        if (cmp is { HasRepo: false, HasLocal: true })
            return SyncState.LocalOnly;
        if (cmp is { HasRepo: true, HasLocal: false })
            return SyncState.NoLocalCopy;

        var allMatch = cmp.Files.Count > 0 && cmp.Files.All(f => f.Matches);
        if (allMatch)
            return SyncState.InSync;

        var headSha = _git.HeadSha();
        if (_settings.LastSyncedCommitBySession.TryGetValue(session, out var lastSynced)
            && !string.IsNullOrEmpty(lastSynced))
        {
            // We have a baseline: if the repo hasn't moved since we last synced,
            // the difference must be local edits; otherwise the remote advanced.
            return lastSynced == headSha ? SyncState.LocalAhead : SyncState.RemoteAhead;
        }

        // No baseline – fall back to comparing local mtime against the repo commit time.
        var localNewest = SafeNewestWriteUtc(localDir);
        var repoTime = cmp.RepoLastChangedUtc ?? DateTimeOffset.MinValue;
        return localNewest >= repoTime ? SyncState.LocalAhead : SyncState.RemoteAhead;
    }

    private static DateTimeOffset SafeNewestWriteUtc(string dir)
    {
        try
        {
            return FileOps.SaveFileNames(dir)
                .Select(n => (DateTimeOffset)File.GetLastWriteTimeUtc(Path.Combine(dir, n)))
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();
        }
        catch
        {
            return DateTimeOffset.MinValue;
        }
    }

    // ---- download -----------------------------------------------------------

    /// <summary>Copy the repo's version of a session into the Steam save folder.</summary>
    public OperationResult Download(string session)
    {
        var cmp = Compare(session);
        if (!cmp.HasLocal)
            return new OperationResult(false,
                "You need a local copy of this session before it can be synchronised.");
        if (!cmp.HasRepo)
            return new OperationResult(false, "This session is not in the repository.");

        if (_game.IsRunning(out var proc))
            return new OperationResult(false,
                $"StarRupture appears to be running ({proc}). Close the game before downloading a save.");

        var localDir = LocalSessionDir(session);
        var repoDir = GitSyncService.RepoSessionDir(session);

        var backupPath = _backup.BackupSession(session, localDir);

        Directory.CreateDirectory(localDir);
        var repoNames = FileOps.SaveFileNames(repoDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in FileOps.SaveFileNames(localDir).Where(n => !repoNames.Contains(n)))
            File.Delete(Path.Combine(localDir, stale));
        FileOps.CopyDirectory(repoDir, localDir, saveFilesOnly: true);

        _settings.LastSyncedCommitBySession[session] = _git.HeadSha();
        _settingsService.Save(_settings);

        var msg = backupPath == null
            ? $"Downloaded '{session}' to the Steam save folder."
            : $"Downloaded '{session}'. Previous local save backed up to:\n{backupPath}";
        return new OperationResult(true, msg);
    }

    // ---- restore from history -------------------------------------------------

    /// <summary>
    /// Check out <paramref name="commitSha"/>, copy that commit's version of
    /// <paramref name="session"/> into the Steam save folder (backing up the current
    /// local save first), then reset the clone back to origin/main – exactly like Refresh.
    /// </summary>
    public OperationResult RestoreSessionFromCommit(string commitSha, string session)
    {
        if (_game.IsRunning(out var proc))
            return new OperationResult(false,
                $"StarRupture appears to be running ({proc}). Close the game before restoring a save.");

        string message;
        try
        {
            _git.CheckoutCommit(commitSha);

            var repoDir = GitSyncService.RepoSessionDir(session);
            if (!Directory.Exists(repoDir) || !FileOps.SaveFileNames(repoDir).Any())
            {
                TryResetToMain(out _);
                return new OperationResult(false,
                    $"Session '{session}' has no save files in commit {Short(commitSha)}.");
            }

            var localDir = LocalSessionDir(session);
            var backupPath = _backup.BackupSession(session, localDir);

            Directory.CreateDirectory(localDir);
            var keep = FileOps.SaveFileNames(repoDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in FileOps.SaveFileNames(localDir).Where(n => !keep.Contains(n)))
                File.Delete(Path.Combine(localDir, stale));
            FileOps.CopyDirectory(repoDir, localDir, saveFilesOnly: true);

            // Local now reflects the restored commit, which is behind origin/main.
            _settings.LastSyncedCommitBySession[session] = commitSha;
            _settingsService.Save(_settings);

            message = backupPath == null
                ? $"Restored '{session}' from commit {Short(commitSha)} into the Steam save folder."
                : $"Restored '{session}' from commit {Short(commitSha)}. " +
                  $"Previous local save backed up to:\n{backupPath}";
        }
        catch (Exception ex)
        {
            TryResetToMain(out _);
            return new OperationResult(false, $"Restore failed: {ex.Message}");
        }

        if (!TryResetToMain(out var resetError))
        {
            return new OperationResult(true,
                message + $"\n\nNote: the repository could not be reset to the latest version " +
                $"({resetError}). Press Refresh once you are back online.");
        }

        return new OperationResult(true, message);
    }

    private bool TryResetToMain(out string error)
    {
        try
        {
            _git.FetchAndResetHard();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string Short(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    // ---- upload -----------------------------------------------------------

    /// <summary>
    /// Replace the repo's copy of a session with the local Steam files, commit as the
    /// current user, and push. On a rejected push, <paramref name="resolveConflict"/>
    /// is invoked with details of who advanced origin/main.
    /// </summary>
    public OperationResult Upload(string session, Func<RemoteAdvanceInfo?, ConflictChoice> resolveConflict)
    {
        var cmp = Compare(session);
        if (!cmp.HasLocal)
            return new OperationResult(false,
                "You need a local copy of this session before it can be synchronised.");

        var localDir = LocalSessionDir(session);
        var repoDir = GitSyncService.RepoSessionDir(session);
        Directory.CreateDirectory(repoDir);

        var localNames = FileOps.SaveFileNames(localDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in FileOps.SaveFileNames(repoDir).Where(n => !localNames.Contains(n)))
            File.Delete(Path.Combine(repoDir, stale));
        FileOps.CopyDirectory(localDir, repoDir, saveFilesOnly: true);

        var message = $"{_settings.Username}: update session '{session}' ({DateTime.Now:yyyy-MM-dd HH:mm})";
        var sha = _git.StageAndCommitAll(message, _settings.Username);
        if (sha == null)
        {
            _settings.LastSyncedCommitBySession[session] = _git.HeadSha();
            _settingsService.Save(_settings);
            return new OperationResult(true, "Nothing to upload – the repository already matches your local save.");
        }

        var outcome = _git.Push(force: false);
        if (outcome == PushOutcome.Pushed)
        {
            _settings.LastSyncedCommitBySession[session] = sha;
            _settingsService.Save(_settings);
            return new OperationResult(true, $"Uploaded '{session}' and pushed to origin/{_git.Branch}.");
        }

        if (outcome == PushOutcome.Failed)
        {
            _git.FetchAndResetHard();
            return new OperationResult(false,
                "Push failed (network or authentication error). Your local Steam save was not changed.");
        }

        // Rejected: origin/main advanced while we were preparing the upload.
        var info = _git.RefetchAndDescribeRemoteTip();
        var choice = resolveConflict(info);

        switch (choice)
        {
            case ConflictChoice.OverwriteTheirs:
                var forced = _git.Push(force: true);
                if (forced == PushOutcome.Pushed)
                {
                    _settings.LastSyncedCommitBySession[session] = sha;
                    _settingsService.Save(_settings);
                    return new OperationResult(true,
                        $"Force-pushed '{session}', overwriting the version by {info?.Author ?? "someone else"}.");
                }
                _git.FetchAndResetHard();
                return new OperationResult(false, "Force-push failed. The repository was reset to origin.");

            case ConflictChoice.DiscardMine:
                _git.FetchAndResetHard();
                return new OperationResult(true,
                    $"Discarded your upload and re-pulled the version by {info?.Author ?? "someone else"}. " +
                    "Review it, then download if you want it locally.");

            default:
                _git.FetchAndResetHard();
                return new OperationResult(false, "Upload cancelled. The repository was reset to origin.");
        }
    }
}
