using System.Collections.ObjectModel;
using System.Windows;
using StarRuptureSync.Models;
using StarRuptureSync.Mvvm;
using StarRuptureSync.Services;

namespace StarRuptureSync.ViewModels;

/// <summary>
/// Backs the history window: lists commits, and lets the user restore one session
/// from a chosen commit into the Steam save folder.
/// </summary>
public class HistoryViewModel : ObservableObject
{
    private readonly SyncEngine _engine;

    private CommitInfo? _selectedCommit;
    private string? _selectedSessionName;
    private bool _isBusy;
    private string _statusText = "";

    public HistoryViewModel(SyncEngine engine, IReadOnlyList<CommitInfo> commits)
    {
        _engine = engine;
        Commits = commits;
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, CanRestore);
    }

    /// <summary>Raised after a successful restore so the main window can refresh.</summary>
    public event Action? RestoreCompleted;

    public IReadOnlyList<CommitInfo> Commits { get; }

    public ObservableCollection<string> SessionsInSelectedCommit { get; } = new();

    public AsyncRelayCommand RestoreCommand { get; }

    public CommitInfo? SelectedCommit
    {
        get => _selectedCommit;
        set
        {
            if (SetProperty(ref _selectedCommit, value))
            {
                LoadSessions();
                RestoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? SelectedSessionName
    {
        get => _selectedSessionName;
        set
        {
            if (SetProperty(ref _selectedSessionName, value))
                RestoreCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RestoreCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !_isBusy;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private void LoadSessions()
    {
        SessionsInSelectedCommit.Clear();
        SelectedSessionName = null;

        if (_selectedCommit == null)
        {
            StatusText = "";
            return;
        }

        foreach (var s in _engine.SessionsInCommit(_selectedCommit.Sha))
            SessionsInSelectedCommit.Add(s);

        StatusText = SessionsInSelectedCommit.Count == 0
            ? "This commit contains no sessions."
            : "Pick a session, then Restore.";
    }

    private bool CanRestore() =>
        !IsBusy && _selectedCommit != null && !string.IsNullOrEmpty(_selectedSessionName);

    private async Task RestoreAsync()
    {
        var commit = _selectedCommit!;
        var session = _selectedSessionName!;

        var confirm = MessageBox.Show(
            $"Restore session \"{session}\" from this commit?\n\n" +
            $"{commit.ShortSha}  ·  {commit.Message}\n{commit.Subtitle}\n\n" +
            $"Your current local save for \"{session}\" is backed up first, then overwritten " +
            "with this older version. The repository is returned to the latest version afterwards.",
            "Restore session from history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK)
            return;

        IsBusy = true;
        StatusText = $"Restoring \"{session}\" from {commit.ShortSha}…";

        var result = new OperationResult(false, "");
        try
        {
            await Task.Run(() => result = _engine.RestoreSessionFromCommit(commit.Sha, session));
        }
        catch (Exception ex)
        {
            result = new OperationResult(false, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        StatusText = result.Message;
        MessageBox.Show(
            result.Message,
            result.Success ? "Restore complete" : "Restore failed",
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);

        if (result.Success)
            RestoreCompleted?.Invoke();
    }
}
