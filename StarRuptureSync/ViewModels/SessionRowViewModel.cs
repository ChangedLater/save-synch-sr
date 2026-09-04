using System.Windows.Media;
using StarRuptureSync.Models;
using StarRuptureSync.Mvvm;

namespace StarRuptureSync.ViewModels;

/// <summary>One row in the sessions list.</summary>
public class SessionRowViewModel : ObservableObject
{
    public SessionRowViewModel(SessionComparison comparison) => Update(comparison);

    public SessionComparison Comparison { get; private set; } = null!;

    public string SessionName => Comparison.SessionName;

    public bool HasLocalCopy => Comparison.HasLocal;

    public string LocalCopyText => Comparison.HasLocal ? "local copy ✓" : "no local copy";

    public string StateText => Comparison.Headline;

    public int SaveCount => Comparison.Files.Count;

    public Brush StateBrush => Comparison.State switch
    {
        SyncState.InSync => Brushes.MediumSeaGreen,
        SyncState.LocalAhead => new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)),
        SyncState.RemoteAhead => new SolidColorBrush(Color.FromRgb(0xE0, 0xA3, 0x3E)),
        SyncState.Conflict => new SolidColorBrush(Color.FromRgb(0xE5, 0x54, 0x4B)),
        SyncState.NoLocalCopy => new SolidColorBrush(Color.FromRgb(0xD5, 0xD7, 0xDE)),
        SyncState.LocalOnly => new SolidColorBrush(Color.FromRgb(0xD5, 0xD7, 0xDE)),
        _ => new SolidColorBrush(Color.FromRgb(0xD5, 0xD7, 0xDE))
    };

    public void Update(SessionComparison comparison)
    {
        Comparison = comparison;
        OnPropertyChanged(string.Empty); // refresh every bound property
    }
}
