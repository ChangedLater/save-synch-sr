using System.IO;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using StarRuptureSync.Models;

namespace StarRuptureSync.Services;

/// <summary>
/// All git access for the app, via LibGit2Sharp only (no git CLI). Maintains the
/// working clone at <see cref="AppPaths.Repo"/>.
/// </summary>
public class GitSyncService
{
    /// <summary>Synchronisation always uses <c>main</c>; there is no branch selection.</summary>
    private const string BranchName = "main";

    private readonly string _repoUrl;
    private readonly string _username;
    private readonly string _token;

    public GitSyncService(string repoUrl, string username, string token)
    {
        _repoUrl = repoUrl.Trim();
        _username = string.IsNullOrWhiteSpace(username) ? "token" : username.Trim();
        _token = token;
    }

    public string Branch => BranchName;

    private CredentialsHandler Credentials => (_, _, _) =>
        new UsernamePasswordCredentials { Username = _username, Password = _token };

    // ---- clone / update -------------------------------------------------------

    /// <summary>
    /// Clone the shared repo on first use; no-op if the working clone already exists.
    /// A brand-new, never-initialised remote (no commits, no <c>main</c>) is seeded
    /// with a first commit and pushed, so the rest of the app can assume origin/main.
    /// </summary>
    public void EnsureCloned()
    {
        AppPaths.EnsureRoot();

        if (!Repository.IsValid(AppPaths.Repo))
        {
            if (Directory.Exists(AppPaths.Repo))
                Directory.Delete(AppPaths.Repo, recursive: true);

            var options = new CloneOptions();
            options.FetchOptions.CredentialsProvider = Credentials;
            Repository.Clone(_repoUrl, AppPaths.Repo, options);
        }

        SeedEmptyRemoteIfNeeded();
    }

    /// <summary>
    /// If the freshly cloned repo has no history and origin has no <c>main</c>
    /// (i.e. the remote was created but never pushed to), create an initial commit
    /// on <c>main</c> and push it.
    /// </summary>
    private void SeedEmptyRemoteIfNeeded()
    {
        using var repo = new Repository(AppPaths.Repo);

        var remoteMainExists = repo.Branches[$"origin/{BranchName}"] != null;
        if (remoteMainExists || repo.Head.Tip != null)
            return;

        // Clone of an empty repo can leave HEAD pointing at another name – force main.
        if (repo.Info.IsHeadUnborn)
            repo.Refs.UpdateTarget("HEAD", $"refs/heads/{BranchName}");

        var readme = Path.Combine(AppPaths.Repo, "README.md");
        if (!File.Exists(readme))
        {
            File.WriteAllText(readme,
                "# StarRupture save sync\n\n" +
                "Managed by the StarRupture Save Sync app. Each top-level folder is a\n" +
                "game session; the files inside are its save slots (`*.sav` / `*.met`).\n");
        }

        Commands.Stage(repo, "*");
        var signature = new Signature(_username, EmailFor(_username), DateTimeOffset.Now);
        repo.Commit("Initialise StarRupture save sync repository", signature, signature,
            new CommitOptions { AllowEmptyCommit = true });

        var remote = repo.Network.Remotes["origin"];
        repo.Network.Push(remote, $"refs/heads/{BranchName}:refs/heads/{BranchName}",
            new PushOptions { CredentialsProvider = Credentials });

        var localMain = repo.Branches[BranchName];
        if (localMain != null)
        {
            try
            {
                repo.Branches.Update(localMain,
                    b => b.Remote = "origin",
                    b => b.UpstreamBranch = $"refs/heads/{BranchName}");
            }
            catch (LibGit2SharpException)
            {
                // Tracking config is a convenience; fetch/reset works without it.
            }
        }
    }

    /// <summary>
    /// fetch, then reset --hard to origin/&lt;branch&gt; and drop untracked files.
    /// Run before every comparison so local repo state exactly mirrors the remote.
    /// </summary>
    public void FetchAndResetHard()
    {
        EnsureCloned();
        using var repo = new Repository(AppPaths.Repo);

        var remote = repo.Network.Remotes["origin"];
        Commands.Fetch(
            repo,
            remote.Name,
            Array.Empty<string>(),
            new FetchOptions { CredentialsProvider = Credentials, Prune = true },
            logMessage: null);

        var remoteBranch = repo.Branches[$"origin/{BranchName}"]
                           ?? throw new InvalidOperationException(
                               $"Remote branch origin/{BranchName} was not found after fetch.");

        var localBranch = repo.Branches[BranchName];
        if (localBranch == null)
        {
            localBranch = repo.CreateBranch(BranchName, remoteBranch.Tip);
            repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
        }

        Commands.Checkout(repo, localBranch,
            new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
        repo.Reset(ResetMode.Hard, remoteBranch.Tip);
        repo.RemoveUntrackedFiles();
    }

    // ---- reads --------------------------------------------------------------

    public IReadOnlyList<string> Sessions()
    {
        if (!Directory.Exists(AppPaths.Repo))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(AppPaths.Repo)
            .Select(Path.GetFileName)
            .Where(n => n != null && !n.Equals(".git", StringComparison.OrdinalIgnoreCase))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string RepoSessionDir(string session) => Path.Combine(AppPaths.Repo, session);

    public string HeadSha()
    {
        using var repo = new Repository(AppPaths.Repo);
        return repo.Head.Tip?.Sha ?? "";
    }

    /// <summary>Author / time / message of the most recent commit that touched a session folder.</summary>
    public (string author, DateTimeOffset whenUtc, string message)? LastChange(string session)
    {
        try
        {
            using var repo = new Repository(AppPaths.Repo);
            var entry = repo.Commits
                .QueryBy(session, new CommitFilter { SortBy = CommitSortStrategies.Time })
                .FirstOrDefault();
            var commit = entry?.Commit ?? repo.Head.Tip;
            if (commit == null)
                return null;
            return (commit.Author.Name, commit.Author.When.ToUniversalTime(), commit.MessageShort);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Recent commits on the current branch, newest first (message, author, time, sha).</summary>
    public IReadOnlyList<CommitInfo> History(int max = 200)
    {
        var list = new List<CommitInfo>();
        try
        {
            using var repo = new Repository(AppPaths.Repo);
            foreach (var c in repo.Commits
                         .QueryBy(new CommitFilter { SortBy = CommitSortStrategies.Time })
                         .Take(max))
            {
                list.Add(new CommitInfo(
                    string.IsNullOrWhiteSpace(c.MessageShort) ? "(no message)" : c.MessageShort,
                    c.Author.Name,
                    c.Author.When.ToUniversalTime(),
                    c.Sha));
            }
        }
        catch
        {
            // No history yet / repo unavailable – return whatever we have.
        }

        return list;
    }

    /// <summary>
    /// For each save file directly under a session folder, the time of the most recent
    /// commit that changed it. One history walk; keyed by file name.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastChangeTimesForSession(string session)
    {
        var times = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var repo = new Repository(AppPaths.Repo);
            var prefix = session.Replace('\\', '/').TrimEnd('/') + "/";

            foreach (var commit in repo.Commits.QueryBy(
                         new CommitFilter { SortBy = CommitSortStrategies.Time }))
            {
                var parentTree = commit.Parents.FirstOrDefault()?.Tree;
                var changes = repo.Diff.Compare<TreeChanges>(parentTree, commit.Tree);
                var when = commit.Author.When.ToUniversalTime();

                foreach (var change in changes)
                {
                    if (!change.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var file = change.Path[prefix.Length..];
                    if (file.Length == 0 || file.Contains('/'))
                        continue;
                    // Commits are newest-first, so the first sighting is the latest change.
                    times.TryAdd(file, when);
                }
            }
        }
        catch
        {
            // History unavailable – callers fall back to no timestamp.
        }

        return times;
    }

    // ---- writes ------------------------------------------------------------

    /// <summary>Stage every change and commit. Returns the new SHA, or null if nothing changed.</summary>
    public string? StageAndCommitAll(string message, string authorName)
    {
        using var repo = new Repository(AppPaths.Repo);
        Commands.Stage(repo, "*");

        if (!repo.RetrieveStatus(new StatusOptions()).IsDirty)
            return null;

        var signature = new Signature(authorName, EmailFor(authorName), DateTimeOffset.Now);
        return repo.Commit(message, signature, signature).Sha;
    }

    /// <summary>Push local &lt;branch&gt; to origin. <paramref name="force"/> overwrites the remote.</summary>
    public PushOutcome Push(bool force)
    {
        using var repo = new Repository(AppPaths.Repo);
        var remote = repo.Network.Remotes["origin"];
        var spec = $"{(force ? "+" : "")}refs/heads/{BranchName}:refs/heads/{BranchName}";

        var rejected = false;
        var options = new PushOptions
        {
            CredentialsProvider = Credentials,
            OnPushStatusError = _ => rejected = true
        };

        try
        {
            repo.Network.Push(remote, spec, options);
        }
        catch (NonFastForwardException)
        {
            return PushOutcome.RejectedRemoteAdvanced;
        }
        catch (LibGit2SharpException ex) when (LooksLikeNonFastForward(ex.Message))
        {
            return PushOutcome.RejectedRemoteAdvanced;
        }
        catch (LibGit2SharpException)
        {
            return PushOutcome.Failed;
        }

        return rejected ? PushOutcome.RejectedRemoteAdvanced : PushOutcome.Pushed;
    }

    /// <summary>After a rejected push: re-fetch and report who advanced origin/&lt;branch&gt;.</summary>
    public RemoteAdvanceInfo? RefetchAndDescribeRemoteTip()
    {
        using var repo = new Repository(AppPaths.Repo);
        var remote = repo.Network.Remotes["origin"];
        Commands.Fetch(repo, remote.Name, Array.Empty<string>(),
            new FetchOptions { CredentialsProvider = Credentials, Prune = true }, null);

        var tip = repo.Branches[$"origin/{BranchName}"]?.Tip;
        if (tip == null)
            return null;

        return new RemoteAdvanceInfo(
            tip.Author.Name,
            tip.Author.When.ToUniversalTime(),
            tip.MessageShort);
    }

    private static bool LooksLikeNonFastForward(string message)
    {
        message = message.ToLowerInvariant();
        return message.Contains("non-fast-forward")
               || message.Contains("fetch first")
               || message.Contains("cannot push")
               || message.Contains("tip of your current branch is behind");
    }

    private static string EmailFor(string username)
    {
        var local = new string(username.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        if (string.IsNullOrEmpty(local))
            local = "user";
        return $"{local}@starrupture.sync";
    }
}
