using System;
using Microsoft.UI.Xaml.Data;

namespace Moonshine.UI.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool flag && flag;
        if (targetType == typeof(Microsoft.UI.Xaml.Visibility))
        {
            return b ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        }
        return !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Microsoft.UI.Xaml.Visibility vis)
        {
            return vis != Microsoft.UI.Xaml.Visibility.Visible;
        }
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}
