using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed partial class SettingsService : ObservableObject, ISettingsService
{
    private const string UseColoredFolderIconsKey = "UseColoredFolderIcons";

    public SettingsService()
    {
        _useColoredFolderIcons = ReadBool(UseColoredFolderIconsKey, defaultValue: false);
        UpdateFolderIconBrush(_useColoredFolderIcons);
    }

    [ObservableProperty]
    private bool _useColoredFolderIcons;

    partial void OnUseColoredFolderIconsChanged(bool value)
    {
        WriteBool(UseColoredFolderIconsKey, value);
        UpdateFolderIconBrush(value);
    }

    private static void UpdateFolderIconBrush(bool useColored)
    {
        var resources = Application.Current.Resources;
        if (resources is null)
        {
            return;
        }

        if (resources.ContainsKey("TextFillColorPrimaryBrush")
            && resources["TextFillColorPrimaryBrush"] is Brush defaultBrush)
        {
            resources["FolderIconBrush"] = defaultBrush;
        }

        if (useColored
            && resources.ContainsKey("SystemFillColorCautionBrush")
            && resources["SystemFillColorCautionBrush"] is Brush cautionBrush)
        {
            resources["FolderIconBrush"] = cautionBrush;
        }
    }

    private static bool ReadBool(string key, bool defaultValue)
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.TryGetValue(key, out var value) && value is bool b)
        {
            return b;
        }

        return defaultValue;
    }

    private static void WriteBool(string key, bool value)
    {
        var settings = ApplicationData.Current.LocalSettings;
        settings.Values[key] = value;
    }
}
