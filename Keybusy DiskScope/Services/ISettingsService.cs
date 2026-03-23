namespace Keybusy_DiskScope.Services;

public interface ISettingsService
{
    bool UseColoredFolderIcons { get; set; }
    Models.AppThemePreference AppThemePreference { get; set; }
}
