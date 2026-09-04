using System.Windows;
using StarRuptureSync.ViewModels;

namespace StarRuptureSync.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
