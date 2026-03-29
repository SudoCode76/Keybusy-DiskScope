using System;

using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Keybusy_DiskScope.Converters;

public sealed class DiffStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DiffStatus status)
        {
            return status switch
            {
                DiffStatus.Added => new SolidColorBrush(Colors.LightGreen) { Opacity = 0.25 },
                DiffStatus.Removed => new SolidColorBrush(Colors.IndianRed) { Opacity = 0.25 },
                DiffStatus.Grown => new SolidColorBrush(Colors.Gold) { Opacity = 0.25 },
                DiffStatus.Shrunk => new SolidColorBrush(Colors.DeepSkyBlue) { Opacity = 0.25 },
                _ => new SolidColorBrush(Colors.Transparent)
            };
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
