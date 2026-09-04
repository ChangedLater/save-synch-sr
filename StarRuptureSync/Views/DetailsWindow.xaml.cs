using System.Windows;
using StarRuptureSync.Models;

namespace StarRuptureSync.Views;

public partial class DetailsWindow : Window
{
    public DetailsWindow(SessionComparison comparison)
    {
        InitializeComponent();
        DataContext = comparison;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
