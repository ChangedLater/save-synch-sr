using System.Windows;
using StarRuptureSync.ViewModels;

namespace StarRuptureSync.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LoginViewModel oldVm)
            oldVm.Completed -= OnCompleted;

        if (e.NewValue is LoginViewModel vm)
        {
            vm.Completed += OnCompleted;
            TokenBox.PasswordChanged += (_, _) => vm.ApiKeyInput = TokenBox.Password;
        }
    }

    private void OnCompleted(MainViewModel mainViewModel)
    {
        var main = new MainWindow { DataContext = mainViewModel };
        Application.Current.MainWindow = main;
        main.Show();
        main.Loaded += (_, _) => mainViewModel.RefreshCommand.Execute(null);
        Close();
    }
}
