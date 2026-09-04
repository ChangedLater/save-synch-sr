using System.Collections.ObjectModel;
using System.Windows;
using StarRuptureSync.Models;
using StarRuptureSync.Mvvm;
using StarRuptureSync.Services;

namespace StarRuptureSync.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SyncEngine _engine;

    private SessionRowViewModel? _selectedSession;
    private bool _isBusy;
    private string _busyText = "";
    private string _log = "";

    public MainViewModel(AppSettings settings, SyncEngine engine)
    {
        _settings = settings;
        _engine = engine;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, CanDownload);
        UploadCommand = new AsyncRelayCommand(UploadAsync, CanUpload);
    }

    public string Username => _settings.Username;
    public string RepoUrl => _settings.RepoUrl;
    public string Branch => "main";
    public string SaveGamesPath => _settings.SaveGamesPath;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();

    public ObservableCollection<FileComparison> SelectedFiles { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public AsyncRelayCommand UploadCommand { get; }

    public SessionRowViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                RebuildSelectedFiles();
                OnPropertyChanged(nameof(DetailHeadline));
                OnPropertyChanged(nameof(DetailSubtext));
                OnPropertyChanged(nameof(InstructionsVisible));
                OnPropertyChanged(nameof(InstructionsText));
                DownloadCommand.RaiseCanExecuteChanged();
                UploadCommand.RaiseCanExecuteChanged();
            }
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
                RefreshCommand.RaiseCanExecuteChanged();
                DownloadCommand.RaiseCanExecuteChanged();
                UploadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !_isBusy;

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public string Log
    {
        get => _log;
        private set => SetProperty(ref _log, value);
    }

    public string DetailHeadline => SelectedSession?.Comparison.Headline ?? "Select a session";

    public string DetailSubtext
    {
        get
        {
            var c = SelectedSession?.Comparison;
            if (c?.RepoLastAuthor == null)
                return "";
            var when = c.RepoLastChangedUtc?.ToLocalTime().ToString("g") ?? "";
            return $"Remote last changed by {c.RepoLastAuthor} at {when} — \"{c.RepoLastMessage}\"";
        }
    }

    public bool InstructionsVisible => SelectedSession?.Comparison.State == SyncState.NoLocalCopy;

    public string InstructionsText =>
        $"You have no local copy of \"{SelectedSession?.Comparison.SessionName}\".\n\n" +
        "1. Launch StarRupture.\n" +
        $"2. Start a new game and name the session exactly \"{SelectedSession?.Comparison.SessionName}\".\n" +
        "3. Save at least once, then quit the game.\n" +
        "4. Come back here, press Refresh, and use \"Download\" to overwrite it with the shared version.";

    // ---- operations -------------------------------------------------------

    public async Task RefreshAsync()
    {
        await RunAsync("Fetching and comparing…", () =>
        {
            var comparisons = _engine.Refresh();
            App.Current.Dispatcher.Invoke(() => MergeSessions(comparisons));
        });
    }

    private async Task DownloadAsync()
    {
        var session = SelectedSession?.Comparison.SessionName;
        if (session == null)
            return;

        await RunAsync($"Downloading '{session}'…", () =>
        {
            var result = _engine.Download(session);
            AppendLog(result.Message);
            var comparisons = _engine.BuildComparisons();
            App.Current.Dispatcher.Invoke(() => MergeSessions(comparisons));
        });
    }

    private async Task UploadAsync()
    {
        var session = SelectedSession?.Comparison.SessionName;
        if (session == null)
            return;

        await RunAsync($"Uploading '{session}'…", () =>
        {
            var result = _engine.Upload(session, ResolveConflictOnUiThread);
            AppendLog(result.Message);
            var comparisons = _engine.BuildComparisons();
            App.Current.Dispatcher.Invoke(() => MergeSessions(comparisons));
        });
    }

    private ConflictChoice ResolveConflictOnUiThread(RemoteAdvanceInfo? info)
    {
        return App.Current.Dispatcher.Invoke(() =>
        {
            var who = info == null
                ? "Someone else"
                : $"{info.Author} ({info.WhenUtc.ToLocalTime():g})";
            var msg = info == null
                ? "origin/" + Branch + " moved ahead of your upload."
                : $"origin/{Branch} moved ahead of your upload.\n\n" +
                  $"Pushed by: {who}\nMessage: \"{info.Message}\"";

            var box = MessageBox.Show(
                msg +
                "\n\n[Yes]  Discard my upload and re-pull their version" +
                "\n[No]   Overwrite their version with mine (force push)" +
                "\n[Cancel]  Do nothing",
                "origin/" + Branch + " advanced",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            return box switch
            {
                MessageBoxResult.Yes => ConflictChoice.DiscardMine,
                MessageBoxResult.No => ConflictChoice.OverwriteTheirs,
                _ => ConflictChoice.Cancel
            };
        });
    }

    // ---- helpers --------------------------------------------------------

    private async Task RunAsync(string busyText, Action work)
    {
        IsBusy = true;
        BusyText = busyText;
        AppendLog(busyText);
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            App.Current.Dispatcher.Invoke(() => MessageBox.Show(
                ex.Message, "Operation failed", MessageBoxButton.OK, MessageBoxImage.Error));
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    private void MergeSessions(IReadOnlyList<SessionComparison> comparisons)
    {
        var selectedName = SelectedSession?.Comparison.SessionName;

        Sessions.Clear();
        foreach (var c in comparisons)
            Sessions.Add(new SessionRowViewModel(c));

        SelectedSession = Sessions.FirstOrDefault(s => s.SessionName == selectedName)
                          ?? Sessions.FirstOrDefault();

        RebuildSelectedFiles();
        OnPropertyChanged(nameof(DetailHeadline));
        OnPropertyChanged(nameof(DetailSubtext));
        OnPropertyChanged(nameof(InstructionsVisible));
        OnPropertyChanged(nameof(InstructionsText));
        DownloadCommand.RaiseCanExecuteChanged();
        UploadCommand.RaiseCanExecuteChanged();
    }

    private void RebuildSelectedFiles()
    {
        SelectedFiles.Clear();
        if (SelectedSession == null)
            return;
        foreach (var f in SelectedSession.Comparison.Files)
            SelectedFiles.Add(f);
    }

    private bool CanDownload()
    {
        if (IsBusy)
            return false;
        var s = SelectedSession?.Comparison.State;
        return s is SyncState.RemoteAhead or SyncState.Conflict or SyncState.InSync
            && SelectedSession!.Comparison.HasLocal
            && SelectedSession.Comparison.HasRepo;
    }

    private bool CanUpload()
    {
        if (IsBusy)
            return false;
        var c = SelectedSession?.Comparison;
        return c is { HasLocal: true }
            && c.State is SyncState.LocalAhead or SyncState.LocalOnly or SyncState.Conflict or SyncState.InSync;
    }

    private void AppendLog(string line)
    {
        void Add()
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            Log = string.IsNullOrEmpty(Log) ? $"[{stamp}] {line}" : $"{Log}\n[{stamp}] {line}";
        }

        if (App.Current.Dispatcher.CheckAccess())
            Add();
        else
            App.Current.Dispatcher.Invoke(Add);
    }
}
