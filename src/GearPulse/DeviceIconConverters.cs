using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace GearPulse;

public sealed class SilhouetteSourceConverter : IMultiValueConverter
{
    private static readonly Dictionary<string, BitmapImage> Images = new();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] as string != "silhouette" || values[0] is not string kind ||
            kind is not ("mouse" or "headset" or "keyboard" or "gamepad")) return null;
        if (!Images.TryGetValue(kind, out var image))
        {
            image = new BitmapImage(new Uri($"pack://application:,,,/GearPulse;component/Assets/{kind}.png"));
            image.Freeze();
            Images[kind] = image;
        }
        return image;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LineIconVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[1] as string == "silhouette" && values[0] is string kind &&
        kind is "mouse" or "headset" or "keyboard" or "gamepad" ? Visibility.Collapsed : Visibility.Visible;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
