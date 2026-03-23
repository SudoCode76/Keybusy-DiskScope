using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Keybusy_DiskScope.Converters;

public sealed class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is bool b && b;
        if (!visible)
        {
            return new GridLength(0);
        }

        if (parameter is string raw && double.TryParse(raw, out var width))
        {
            return new GridLength(width, GridUnitType.Pixel);
        }

        return GridLength.Auto;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is GridLength length && length.Value > 0;
}
