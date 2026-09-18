using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using StarRuptureSync.Models;
using StarRuptureSync.Mvvm;
using StarRuptureSync.Services;

namespace StarRuptureSync.ViewModels;

public class MainViewModel : ObservableObject
{
    private static readonly TimeSpan GameCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(60);

    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly SyncEngine _engine;
    private readonly DispatcherTimer _gameCheckTimer;
    private readonly DispatcherTimer _autoSyncTimer;

    private SessionRowViewModel? _selectedSession;
    private bool _isBusy;
    private string _busyText = "";
    private string _log = "";
    private bool _gameRunning;
    private string _gameProcessName = "";
    private bool? _wasGameRunning;

    public MainViewModel(AppSettings settings, SettingsService settingsService, SyncEngine engine)
    {
        _settings = settings;
        _settingsService = settingsService;
        _engine = engine;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, CanDownload);
        UploadCommand = new AsyncRelayCommand(UploadAsync, CanUpload);
        CheckGameCommand = new RelayCommand(CheckGameRunning);
        ShowDetailsCommand = new RelayCommand(ShowDetails, CanShowDetails);
        ShowHistoryCommand = new AsyncRelayCommand(ShowHistoryAsync, () => !IsBusy);

        _gameCheckTimer = new DispatcherTimer { Interval = GameCheckInterval };
        _gameCheckTimer.Tick += (_, _) => CheckGameRunning();
        _gameCheckTimer.Start();

        _autoSyncTimer = new DispatcherTimer { Interval = AutoSyncInterval };
        _autoSyncTimer.Tick += (_, _) => TriggerAutoSyncPass();
        if (_settings.AutoSyncEnabled)
            _autoSyncTimer.Start();

        CheckGameRunning();
    }

    public string Username => _settings.Username;
    public string RepoUrl => _settings.RepoUrl;
    public string Branch => "main";
    public string SaveGamesPath => _settings.SaveGamesPath;

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public AsyncRelayCommand UploadCommand { get; }
    public RelayCommand CheckGameCommand { get; }
    public RelayCommand ShowDetailsCommand { get; }
    public AsyncRelayCommand ShowHistoryCommand { get; }

    /// <summary>Raised when the user asks for the per-file details window.</summary>
    public event Action<SessionComparison>? DetailsRequested;

    /// <summary>Raised with a ready history view model when the user asks to view history.</summary>
    public event Action<HistoryViewModel>? HistoryRequested;

    public bool GameRunning
    {
        get => _gameRunning;
        private set
        {
            if (SetProperty(ref _gameRunning, value))
            {
                OnPropertyChanged(nameof(GameStatusText));
                OnPropertyChanged(nameof(GameStatusBrush));
                DownloadCommand.RaiseCanExecuteChanged();
                UploadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string GameStatusText => _gameRunning
        ? $"StarRupture is running ({_gameProcessName}) — upload and download are disabled"
        : "StarRupture is not running";

    public Brush GameStatusBrush => _gameRunning
        ? new SolidColorBrush(Color.FromRgb(0xE5, 0x54, 0x4B))
        : new SolidColorBrush(Color.FromRgb(0x3F, 0xB6, 0x5E));

    /// <summary>
    /// When on, the app periodically syncs unattended: downloads sessions cleanly ahead
    /// on the remote and uploads sessions changed locally, without prompting. It never
    /// overwrites a save that looks newer and never force-pushes.
    /// </summary>
    public bool AutoSyncEnabled
    {
        get => _settings.AutoSyncEnabled;
        set
        {
            if (_settings.AutoSyncEnabled == value)
                return;

            _settings.AutoSyncEnabled = value;
            _settingsService.Save(_settings);
            OnPropertyChanged();

            if (value)
            {
                AppendLog("Auto-sync enabled.");
                _autoSyncTimer.Start();
                TriggerAutoSyncPass();
            }
            else
            {
                AppendLog("Auto-sync disabled.");
                _autoSyncTimer.Stop();
            }
        }
    }

    public SessionRowViewModel? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                OnPropertyChanged(nameof(DetailHeadline));
                OnPropertyChanged(nameof(DetailSubtext));
                OnPropertyChanged(nameof(SelectedFileSummary));
                OnPropertyChanged(nameof(InstructionsVisible));
                OnPropertyChanged(nameof(InstructionsText));
                DownloadCommand.RaiseCanExecuteChanged();
                UploadCommand.RaiseCanExecuteChanged();
                ShowDetailsCommand.RaiseCanExecuteChanged();
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
                ShowHistoryCommand.RaiseCanExecuteChanged();
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

    public string DetailSubtext => SelectedSession?.Comparison.RemoteChangeSummary ?? "";

    /// <summary>One-line roll-up of the per-file comparison for the selected session.</summary>
    public string SelectedFileSummary => SelectedSession?.Comparison.FileSummary ?? "";

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
        CheckGameRunning();
    }

    private async Task DownloadAsync()
    {
        var cmp = SelectedSession?.Comparison;
        if (cmp == null)
            return;
        var session = cmp.SessionName;

        if ((cmp.State is SyncState.LocalAhead or SyncState.Conflict)
            && !ConfirmOverwriteNewerLocal(cmp))
        {
            AppendLog($"Download of '{session}' cancelled – local save kept.");
            return;
        }

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

    /// <summary>Warn before a download replaces a local save that looks newer than the remote.</summary>
    private bool ConfirmOverwriteNewerLocal(SessionComparison cmp)
    {
        var lead = cmp.State == SyncState.Conflict
            ? $"\"{cmp.SessionName}\" has changed both on your PC and on the remote."
            : $"Your local copy of \"{cmp.SessionName}\" looks newer than the shared version.";

        var result = MessageBox.Show(
            lead + "\n\n" +
            "Downloading will overwrite your local save with the remote version. " +
            "Your current local save is backed up first (to " +
            $"%LOCALAPPDATA%\\StarRuptureSync\\backups\\{cmp.SessionName}), but the Steam " +
            "folder will then hold the remote version, not your newer one.\n\n" +
            "Overwrite your local save with the remote version?",
            "Overwrite newer local save?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    // ---- helpers --------------------------------------------------------

    /// <param name="silent">
    /// When true, a failure is only written to the Activity log, not popped up in a
    /// MessageBox – used for unattended auto-sync passes, which shouldn't block on
    /// input the user may not be there to give.
    /// </param>
    /// <param name="logStart">
    /// When false, skip logging <paramref name="busyText"/> itself – used for the
    /// periodic auto-sync pass so a no-op tick doesn't spam the Activity log.
    /// </param>
    private async Task RunAsync(string busyText, Action work, bool silent = false, bool logStart = true)
    {
        IsBusy = true;
        BusyText = busyText;
        if (logStart)
            AppendLog(busyText);
        try
        {
            await Task.Run(work);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            if (!silent)
            {
                App.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    ex.Message, "Operation failed", MessageBoxButton.OK, MessageBoxImage.Error));
            }
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

        OnPropertyChanged(nameof(DetailHeadline));
        OnPropertyChanged(nameof(DetailSubtext));
        OnPropertyChanged(nameof(SelectedFileSummary));
        OnPropertyChanged(nameof(InstructionsVisible));
        OnPropertyChanged(nameof(InstructionsText));
        DownloadCommand.RaiseCanExecuteChanged();
        UploadCommand.RaiseCanExecuteChanged();
        ShowDetailsCommand.RaiseCanExecuteChanged();
    }

    private void CheckGameRunning()
    {
        try
        {
            var running = _engine.IsGameRunning(out var name);
            _gameProcessName = name ?? "";
            GameRunning = running;
            OnPropertyChanged(nameof(GameStatusText));

            // The game just closed – a good moment to auto-upload whatever changed,
            // rather than waiting for the next timer tick.
            if (_wasGameRunning == true && !running)
                TriggerAutoSyncPass();
            _wasGameRunning = running;
        }
        catch
        {
            // Enumerating processes can fail transiently – leave the last known state.
        }
    }

    /// <summary>
    /// Pause the periodic auto-sync timer while something else that touches the repo is
    /// open (e.g. the modal history/restore window), and resume it afterwards.
    /// </summary>
    public void SuspendAutoSync() => _autoSyncTimer.Stop();

    public void ResumeAutoSync()
    {
        if (AutoSyncEnabled)
            _autoSyncTimer.Start();
    }

    /// <summary>Fire-and-forget an auto-sync pass if enabled, idle, and the game isn't running.</summary>
    private void TriggerAutoSyncPass()
    {
        if (!AutoSyncEnabled || IsBusy || GameRunning)
            return;
        _ = RunAutoSyncPassAsync();
    }

    private async Task RunAutoSyncPassAsync()
    {
        await RunAsync("Auto-sync…", () =>
        {
            var result = _engine.AutoSyncPass();
            foreach (var line in result.ActionLog)
                AppendLog(line);
            App.Current.Dispatcher.Invoke(() => MergeSessions(result.Comparisons));
        }, silent: true, logStart: false);
    }

    private bool CanShowDetails() => SelectedSession?.Comparison.Files.Count > 0;

    private void ShowDetails()
    {
        if (SelectedSession != null)
            DetailsRequested?.Invoke(SelectedSession.Comparison);
    }

    private async Task ShowHistoryAsync()
    {
        IReadOnlyList<CommitInfo> history = Array.Empty<CommitInfo>();
        await RunAsync("Loading history…", () => history = _engine.History());

        var historyVm = new HistoryViewModel(_engine, history);
        historyVm.RestoreCompleted += () =>
        {
            if (RefreshCommand.CanExecute(null))
                RefreshCommand.Execute(null);
        };
        HistoryRequested?.Invoke(historyVm);
    }

    private bool CanDownload()
    {
        if (IsBusy || GameRunning)
            return false;
        // Any state where both sides hold the session can be downloaded. When the
        // local copy looks newer (LocalAhead / Conflict) DownloadAsync warns first.
        var c = SelectedSession?.Comparison;
        return c is { HasLocal: true, HasRepo: true }
            && c.State is SyncState.RemoteAhead or SyncState.LocalAhead
                       or SyncState.Conflict or SyncState.InSync;
    }

    private bool CanUpload()
    {
        if (IsBusy || GameRunning)
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
