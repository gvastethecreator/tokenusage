using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using TokenUsage.App.ViewModels.Dashboard;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TokenUsage.App.Converters;

public sealed class ProviderStatusKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (new AccessibilitySettings().HighContrast)
        {
            Color foreground = new UISettings().GetColorValue(UIColorType.Foreground);
            return new SolidColorBrush(foreground);
        }

        Color color = value is ProviderStatusKind kind
            ? kind switch
            {
                ProviderStatusKind.Available => Color.FromArgb(255, 23, 137, 79),
                ProviderStatusKind.Partial or ProviderStatusKind.Pending =>
                    Color.FromArgb(255, 168, 111, 0),
                ProviderStatusKind.Missing or ProviderStatusKind.Blocked =>
                    Color.FromArgb(255, 199, 79, 70),
                _ => Color.FromArgb(255, 128, 128, 128),
            }
            : Color.FromArgb(255, 128, 128, 128);
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
