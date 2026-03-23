using System.ComponentModel;

using Keybusy_DiskScope.Models;
using Keybusy_DiskScope.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Keybusy_DiskScope;

public sealed partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService;

    public MainWindow()
    {
        InitializeComponent();

        // Allow ShellPage to designate the title bar drag region.
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();

        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        ApplyTheme(_settingsService.AppThemePreference);
        if (_settingsService is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnSettingsChanged;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ISettingsService.AppThemePreference), StringComparison.Ordinal))
        {
            ApplyTheme(_settingsService.AppThemePreference);
        }
    }

    private void ApplyTheme(AppThemePreference preference)
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = preference switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
