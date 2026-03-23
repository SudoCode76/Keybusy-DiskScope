using System.Collections.Generic;
using System.ComponentModel;

namespace Keybusy_DiskScope.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private static readonly string[] ThemeOptions = { "Sistema", "Claro", "Oscuro" };

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        if (_settingsService is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged += OnSettingsChanged;
        }
    }

    public bool UseColoredFolderIcons
    {
        get => _settingsService.UseColoredFolderIcons;
        set
        {
            if (_settingsService.UseColoredFolderIcons == value)
            {
                return;
            }

            _settingsService.UseColoredFolderIcons = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> ThemeOptionLabels => ThemeOptions;

    public int SelectedThemeIndex
    {
        get => (int)_settingsService.AppThemePreference;
        set
        {
            if (value == (int)_settingsService.AppThemePreference)
            {
                return;
            }

            if (value < 0 || value >= ThemeOptions.Length)
            {
                return;
            }

            _settingsService.AppThemePreference = (Models.AppThemePreference)value;
            OnPropertyChanged();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ISettingsService.UseColoredFolderIcons), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(UseColoredFolderIcons));
        }

        if (string.Equals(e.PropertyName, nameof(ISettingsService.AppThemePreference), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedThemeIndex));
        }
    }
}
