using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MySQLManager.Helpers
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public static readonly NullToVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class InverseBoolConverter : IValueConverter
    {
        public static readonly InverseBoolConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? !b : false;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b ? !b : false;
    }

    public class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public static readonly NullOrEmptyToVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public static readonly InverseBoolToVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Collapsed;
    }

    public class StringToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            try
            {
                if (value is string hex)
                    return new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
            }
            catch { }
            return System.Windows.Media.Brushes.Gray;
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
public class BlobSizeConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is byte[] bytes)
        {
            if (bytes.Length < 1024)          return $"[BLOB {bytes.Length} B]";
            if (bytes.Length < 1024 * 1024)   return $"[BLOB {bytes.Length / 1024.0:F1} KB]";
            return $"[BLOB {bytes.Length / (1024.0 * 1024):F2} MB]";
        }
        return value?.ToString() ?? "";
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

}
