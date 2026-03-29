using Keybusy_DiskScope.Models;
using Keybusy_DiskScope.ViewModels;
using Microsoft.UI.Xaml;
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

    private async void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not SnapshotRecord snapshot)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Eliminar snapshot",
            Content = "Esta accion no se puede deshacer. El snapshot se eliminara permanentemente.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.DeleteCommand.Execute(snapshot);
        }
    }
}
