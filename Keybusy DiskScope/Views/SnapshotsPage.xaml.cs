using Keybusy_DiskScope.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Keybusy_DiskScope.Views;

public sealed partial class SnapshotsPage : Page
{
    public SnapshotsViewModel ViewModel { get; }

    public SnapshotsPage()
    {
        ViewModel = App.Services.GetRequiredService<SnapshotsViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
