using System;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Keybusy_DiskScope.Converters;

public sealed class DepthToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var depth = value is int intValue ? intValue : 0;
        var step = 20d;
        if (parameter is string paramText && double.TryParse(paramText, out var parsed))
        {
            step = parsed;
        }

        var left = depth * step;
        return new Thickness(left, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
