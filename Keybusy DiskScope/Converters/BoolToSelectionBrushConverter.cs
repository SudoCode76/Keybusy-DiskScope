using System;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Keybusy_DiskScope.Converters;

public sealed class BoolToSelectionBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isSelected && isSelected)
        {
            var resources = Application.Current.Resources;
            if (resources.TryGetValue("SystemControlHighlightListAccentLowBrush", out var brush))
            {
                return brush;
            }
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
