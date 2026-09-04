using System.Windows;
using StarRuptureSync.Models;
using StarRuptureSync.ViewModels;

namespace StarRuptureSync.Views;

public partial class MainWindow : Window
{
    private bool _initialRefreshStarted;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.DetailsRequested -= ShowDetails;
            oldVm.HistoryRequested -= ShowHistory;
        }
        if (e.NewValue is MainViewModel vm)
        {
            vm.DetailsRequested += ShowDetails;
            vm.HistoryRequested += ShowHistory;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Kick off the first fetch + reset --hard as soon as the window is shown.
        if (_initialRefreshStarted)
            return;
        _initialRefreshStarted = true;

        if (DataContext is MainViewModel vm && vm.RefreshCommand.CanExecute(null))
            vm.RefreshCommand.Execute(null);
    }

    private void ShowDetails(SessionComparison comparison)
    {
        new DetailsWindow(comparison) { Owner = this }.ShowDialog();
    }

    private void ShowHistory(HistoryViewModel historyViewModel)
    {
        new HistoryWindow(historyViewModel) { Owner = this }.ShowDialog();
    }
}
