using Keybusy_DiskScope.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Keybusy_DiskScope.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }
}
