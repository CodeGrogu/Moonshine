using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Moonshine.UI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool flag && flag;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility vis)
        {
            return vis == Visibility.Visible;
        }
        return false;
    }
}
