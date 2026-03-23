using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed partial class SettingsService : ObservableObject, ISettingsService
{
    private const string UseColoredFolderIconsKey = "UseColoredFolderIcons";
    private const string AppThemePreferenceKey = "AppThemePreference";

    public SettingsService()
    {
        _useColoredFolderIcons = ReadBool(UseColoredFolderIconsKey, defaultValue: false);
        _appThemePreference = ReadEnum(AppThemePreferenceKey, Models.AppThemePreference.System);
        UpdateFolderIconBrush(_useColoredFolderIcons);
    }

    [ObservableProperty]
    private bool _useColoredFolderIcons;

    [ObservableProperty]
    private Models.AppThemePreference _appThemePreference;

    partial void OnUseColoredFolderIconsChanged(bool value)
    {
        WriteBool(UseColoredFolderIconsKey, value);
        UpdateFolderIconBrush(value);
    }

    partial void OnAppThemePreferenceChanged(Models.AppThemePreference value)
    {
        WriteEnum(AppThemePreferenceKey, value);
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

    private static TEnum ReadEnum<TEnum>(string key, TEnum defaultValue) where TEnum : struct
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.TryGetValue(key, out var value))
        {
            if (value is string text && Enum.TryParse<TEnum>(text, out var parsed))
            {
                return parsed;
            }

            if (value is int number && Enum.IsDefined(typeof(TEnum), number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }
        }

        return defaultValue;
    }

    private static void WriteBool(string key, bool value)
    {
        var settings = ApplicationData.Current.LocalSettings;
        settings.Values[key] = value;
    }

    private static void WriteEnum<TEnum>(string key, TEnum value) where TEnum : struct
    {
        var settings = ApplicationData.Current.LocalSettings;
        settings.Values[key] = value.ToString();
    }
}
