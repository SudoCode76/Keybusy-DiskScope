using Keybusy_DiskScope.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Keybusy_DiskScope.Views;

public sealed partial class ComparePage : Page
{
    public CompareViewModel ViewModel { get; }

    public ComparePage()
    {
        ViewModel = App.Services.GetRequiredService<CompareViewModel>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadSnapshotsCommand.ExecuteAsync(null);
    }
}
