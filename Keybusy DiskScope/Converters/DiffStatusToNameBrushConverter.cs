using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Keybusy_DiskScope.Converters;

public sealed class DiffStatusToNameBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value is DiffStatus diffStatus ? diffStatus : DiffStatus.Unchanged;

        if (status == DiffStatus.Removed)
        {
            return GetBrush(
                "SystemFillColorCriticalBrush",
                new SolidColorBrush(Colors.IndianRed));
        }

        return GetBrush(
            "TextFillColorPrimaryBrush",
            new SolidColorBrush(Colors.Black));
    }

    private static Brush GetBrush(string key, Brush fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var resource)
            && resource is Brush brush)
        {
            return brush;
        }

        return fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
