using System;
using System.Windows;
using System.Windows.Data;

namespace Vydejna.Gui.Common
{
    public class BooleanVisibilityConverter : IValueConverter
    {
        public bool Inverted { get; set; }
        public bool OnlyHide { get; set; }

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var boolValue = value is bool && (bool)value;
            if (Inverted)
                boolValue = !boolValue;
            return boolValue ? Visibility.Visible : OnlyHide ? Visibility.Hidden : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
