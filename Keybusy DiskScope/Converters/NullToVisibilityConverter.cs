using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Keybusy_DiskScope.Converters;

/// <summary>
/// Returns Visible when the value is non-null, Collapsed when null.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
