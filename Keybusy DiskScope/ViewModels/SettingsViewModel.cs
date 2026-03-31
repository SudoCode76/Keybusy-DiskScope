using System.Collections.Generic;
using System.ComponentModel;

namespace Keybusy_DiskScope.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private static readonly string[] ThemeOptions = { "Sistema", "Claro", "Oscuro" };
    private static readonly string[] SortOptions = { "Nombre", "Tamano", "Ocupacion", "Archivos", "Ultima modificacion" };
    private static readonly string[] SortDirectionOptions = { "Ascendente", "Descendente" };

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

    public IReadOnlyList<string> DefaultSortOptionLabels => SortOptions;

    public IReadOnlyList<string> SortDirectionLabels => SortDirectionOptions;

    public bool EnableFastNtfsScan
    {
        get => _settingsService.EnableFastNtfsScan;
        set
        {
            if (_settingsService.EnableFastNtfsScan == value)
            {
                return;
            }

            _settingsService.EnableFastNtfsScan = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ForceFastNtfsOnly));
        }
    }

    public bool ForceFastNtfsOnly
    {
        get => _settingsService.ForceFastNtfsOnly;
        set
        {
            if (_settingsService.ForceFastNtfsOnly == value)
            {
                return;
            }

            _settingsService.ForceFastNtfsOnly = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EnableFastNtfsScan));
        }
    }

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

    public int DefaultSortIndex
    {
        get => _settingsService.DefaultSortIndex;
        set
        {
            if (value == _settingsService.DefaultSortIndex)
            {
                return;
            }

            if (value < 0 || value >= SortOptions.Length)
            {
                return;
            }

            _settingsService.DefaultSortIndex = value;
            OnPropertyChanged();
        }
    }

    public int DefaultSortDirectionIndex
    {
        get => _settingsService.DefaultSortDescending ? 1 : 0;
        set
        {
            var descending = value == 1;
            if (descending == _settingsService.DefaultSortDescending)
            {
                return;
            }

            _settingsService.DefaultSortDescending = descending;
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

        if (string.Equals(e.PropertyName, nameof(ISettingsService.DefaultSortIndex), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(DefaultSortIndex));
        }

        if (string.Equals(e.PropertyName, nameof(ISettingsService.DefaultSortDescending), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(DefaultSortDirectionIndex));
        }

        if (string.Equals(e.PropertyName, nameof(ISettingsService.EnableFastNtfsScan), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(EnableFastNtfsScan));
            OnPropertyChanged(nameof(ForceFastNtfsOnly));
        }

        if (string.Equals(e.PropertyName, nameof(ISettingsService.ForceFastNtfsOnly), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ForceFastNtfsOnly));
            OnPropertyChanged(nameof(EnableFastNtfsScan));
        }
    }
}
