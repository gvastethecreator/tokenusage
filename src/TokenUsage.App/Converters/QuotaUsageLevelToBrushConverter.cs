using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.Converters;

public sealed class QuotaUsageLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string key = value is QuotaUsageLevel level
            ? level switch
            {
                QuotaUsageLevel.Healthy => "QuotaHealthyBrush",
                QuotaUsageLevel.Caution => "QuotaCautionBrush",
                QuotaUsageLevel.Warning => "QuotaWarningBrush",
                QuotaUsageLevel.Critical => "QuotaCriticalBrush",
                _ => "TextFillColorTertiaryBrush",
            }
            : "TextFillColorTertiaryBrush";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
