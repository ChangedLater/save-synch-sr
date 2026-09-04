using System.Windows;
using System.Windows.Threading;
using StarRuptureSync.Services;
using StarRuptureSync.ViewModels;
using StarRuptureSync.Views;

namespace StarRuptureSync;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        var login = new LoginWindow
        {
            DataContext = new LoginViewModel(settingsService, settings)
        };
        login.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.ToString(),
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
