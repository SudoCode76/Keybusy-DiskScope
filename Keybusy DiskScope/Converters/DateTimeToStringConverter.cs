using System.Globalization;

using Microsoft.UI.Xaml.Data;

namespace Keybusy_DiskScope.Converters;

public sealed class DateTimeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string format = parameter as string ?? "g";

        return value switch
        {
            DateTime dt => dt.ToString(format, CultureInfo.CurrentCulture),
            DateTimeOffset dto => dto.ToString(format, CultureInfo.CurrentCulture),
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
