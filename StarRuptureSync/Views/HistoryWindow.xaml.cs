using System.Collections.Generic;
using System.Windows;
using StarRuptureSync.Models;

namespace StarRuptureSync.Views;

public partial class HistoryWindow : Window
{
    public HistoryWindow(IReadOnlyList<CommitInfo> history)
    {
        InitializeComponent();
        DataContext = history;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
