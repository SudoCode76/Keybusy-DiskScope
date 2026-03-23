using System.ComponentModel;

namespace Keybusy_DiskScope.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

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

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(ISettingsService.UseColoredFolderIcons), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(UseColoredFolderIcons));
        }
    }
}
