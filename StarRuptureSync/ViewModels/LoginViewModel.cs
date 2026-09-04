using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using StarRuptureSync.Models;
using StarRuptureSync.Mvvm;
using StarRuptureSync.Services;

namespace StarRuptureSync.ViewModels;

/// <summary>
/// First screen: collects username, repo URL and git token (stored locally,
/// token DPAPI-encrypted) and resolves the Steam SaveGames folder.
/// </summary>
public class LoginViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly SaveLocationResolver _resolver = new();

    private string _username;
    private string _repoUrl;
    private string _saveGamesPath;
    private string _statusText = "";
    private bool _hasStoredKey;

    public LoginViewModel(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;

        _username = settings.Username;
        _repoUrl = settings.RepoUrl;
        _saveGamesPath = settings.SaveGamesPath;
        _hasStoredKey = !string.IsNullOrEmpty(settings.ApiKeyProtected);

        DetectLocationCommand = new RelayCommand(DetectLocation);
        BrowseCommand = new RelayCommand(Browse);
        ContinueCommand = new RelayCommand(Continue, CanContinue);
        UseCandidateCommand = new RelayCommand(o =>
        {
            if (o is SaveLocationCandidate c)
            {
                SaveGamesPath = c.Path;
                LocationCandidates.Clear();
            }
        });

        if (string.IsNullOrWhiteSpace(_saveGamesPath))
            DetectLocation();
    }

    /// <summary>Raised once the user has valid settings; carries the ready-to-use main view model.</summary>
    public event Action<MainViewModel>? Completed;

    public string Username
    {
        get => _username;
        set { if (SetProperty(ref _username, value)) RefreshCanContinue(); }
    }

    public string RepoUrl
    {
        get => _repoUrl;
        set { if (SetProperty(ref _repoUrl, value)) RefreshCanContinue(); }
    }

    public string SaveGamesPath
    {
        get => _saveGamesPath;
        set { if (SetProperty(ref _saveGamesPath, value)) RefreshCanContinue(); }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool HasStoredKey
    {
        get => _hasStoredKey;
        set => SetProperty(ref _hasStoredKey, value);
    }

    /// <summary>Set from the PasswordBox in code-behind. Empty means "keep the stored token".</summary>
    public string ApiKeyInput { get; set; } = "";

    public ObservableCollection<SaveLocationCandidate> LocationCandidates { get; } = new();

    public RelayCommand DetectLocationCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand ContinueCommand { get; }
    public RelayCommand UseCandidateCommand { get; }

    private void DetectLocation()
    {
        LocationCandidates.Clear();
        var candidates = _resolver.Resolve();

        if (candidates.Count == 0)
        {
            StatusText = "Could not auto-detect a SaveGames folder. Use Browse to select it manually.";
            return;
        }

        if (SaveLocationResolver.NeedsUserChoice(candidates))
        {
            foreach (var c in candidates)
                LocationCandidates.Add(c);
            StatusText = "Multiple SaveGames folders found – choose the one you play with.";
            return;
        }

        SaveGamesPath = candidates[0].Path;
        StatusText = $"Detected SaveGames folder via {candidates[0].Source}.";
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the StarRupture SaveGames folder",
            InitialDirectory = Directory.Exists(SaveGamesPath)
                ? SaveGamesPath
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (dialog.ShowDialog() == true)
            SaveGamesPath = dialog.FolderName;
    }

    private bool CanContinue() =>
        !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(RepoUrl)
        && !string.IsNullOrWhiteSpace(SaveGamesPath);

    private void RefreshCanContinue() => ContinueCommand.RaiseCanExecuteChanged();

    private void Continue()
    {
        if (!Directory.Exists(SaveGamesPath))
        {
            StatusText = "That SaveGames folder does not exist.";
            return;
        }

        _settings.Username = Username.Trim();
        _settings.RepoUrl = RepoUrl.Trim();
        _settings.SaveGamesPath = SaveGamesPath.Trim();

        if (!string.IsNullOrEmpty(ApiKeyInput))
            _settingsService.SetApiKey(_settings, ApiKeyInput);

        if (string.IsNullOrEmpty(_settings.ApiKeyProtected))
        {
            StatusText = "Enter your git personal access token (needed to fetch and push).";
            return;
        }

        _settingsService.Save(_settings);

        var token = _settingsService.GetApiKey(_settings);
        var git = new GitSyncService(_settings.RepoUrl, _settings.Username, token);
        var engine = new SyncEngine(_settings, _settingsService, git,
            new BackupService(), new GameProcessService());

        Completed?.Invoke(new MainViewModel(_settings, engine));
    }
}
