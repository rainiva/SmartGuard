using System.Globalization;
using System.Windows.Data;

namespace SmartGuard.Settings;

public sealed class LogViewportHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double actualHeight)
        {
            // Reserve space for page title, card padding, header (title, search, filters),
            // status bar and bottom action bar so the log viewer does not overflow the viewport.
            var reserved = 260.0;
            if (parameter is string param && double.TryParse(param, NumberStyles.Float, CultureInfo.InvariantCulture, out var custom))
            {
                reserved = custom;
            }

            return Math.Max(120.0, actualHeight - reserved);
        }

        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
