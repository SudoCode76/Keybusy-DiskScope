using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace Keybusy_DiskScope.Services.Implementation;

public sealed partial class SettingsService : ObservableObject, ISettingsService
{
    private const string UseColoredFolderIconsKey = "UseColoredFolderIcons";
    private const string AppThemePreferenceKey = "AppThemePreference";
    private const string DefaultSortIndexKey = "DefaultSortIndex";
    private const string DefaultSortDescendingKey = "DefaultSortDescending";
    private const string EnableFastNtfsScanKey = "EnableFastNtfsScan";

    public SettingsService()
    {
        _useColoredFolderIcons = ReadBool(UseColoredFolderIconsKey, defaultValue: false);
        _appThemePreference = ReadEnum(AppThemePreferenceKey, Models.AppThemePreference.System);
        _defaultSortIndex = ReadInt(DefaultSortIndexKey, defaultValue: 1);
        _defaultSortDescending = ReadBool(DefaultSortDescendingKey, defaultValue: true);
        _enableFastNtfsScan = ReadBool(EnableFastNtfsScanKey, defaultValue: true);
        UpdateFolderIconBrush(_useColoredFolderIcons);
    }

    [ObservableProperty]
    private bool _useColoredFolderIcons;

    [ObservableProperty]
    private Models.AppThemePreference _appThemePreference;

    [ObservableProperty]
    private int _defaultSortIndex;

    [ObservableProperty]
    private bool _defaultSortDescending;

    [ObservableProperty]
    private bool _enableFastNtfsScan;

    partial void OnUseColoredFolderIconsChanged(bool value)
    {
        WriteBool(UseColoredFolderIconsKey, value);
        UpdateFolderIconBrush(value);
    }

    partial void OnAppThemePreferenceChanged(Models.AppThemePreference value)
    {
        WriteEnum(AppThemePreferenceKey, value);
    }

    partial void OnDefaultSortIndexChanged(int value)
    {
        WriteInt(DefaultSortIndexKey, value);
    }

    partial void OnDefaultSortDescendingChanged(bool value)
    {
        WriteBool(DefaultSortDescendingKey, value);
    }

    partial void OnEnableFastNtfsScanChanged(bool value)
    {
        WriteBool(EnableFastNtfsScanKey, value);
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

    private static int ReadInt(string key, int defaultValue)
    {
        var settings = ApplicationData.Current.LocalSettings;
        if (settings.Values.TryGetValue(key, out var value) && value is int intValue)
        {
            return intValue;
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

    private static void WriteInt(string key, int value)
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
