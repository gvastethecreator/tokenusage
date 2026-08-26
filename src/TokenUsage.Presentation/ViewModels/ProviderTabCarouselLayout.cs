namespace TokenUsage.App.ViewModels;

/// <summary>
/// Sizes provider carousels. The compact flyout is 480 DIPs, so a page of four
/// equal tabs plus the two nav buttons is the layout that fits. Reports use
/// the same four-tab page so the fifth provider always exposes navigation.
/// </summary>
public static class ProviderTabCarouselLayout
{
    public const int MaximumPageSize = 4;
    public const int ReportMaximumPageSize = MaximumPageSize;
    public const double MinimumItemWidth = 64;
    public const double Spacing = 2;
    public const double NavigationWidth = 64;
    public const double ViewportInset = 2;

    public static int PageSize(
        int providerCount,
        double carouselWidth,
        int maximumPageSize = MaximumPageSize)
    {
        if (providerCount <= 0)
        {
            return 1;
        }

        if (maximumPageSize < 1)
        {
            maximumPageSize = 1;
        }

        if (carouselWidth <= 0)
        {
            return Math.Min(maximumPageSize, providerCount);
        }

        int nextPageSize = Math.Min(maximumPageSize, providerCount);
        for (int candidate = nextPageSize; candidate >= 1; candidate--)
        {
            bool needsNavigation = providerCount > candidate;
            double availableWidth = carouselWidth
                - (needsNavigation ? NavigationWidth : 0);
            double itemWidth = (
                availableWidth - (candidate - 1) * Spacing)
                / candidate;
            if (itemWidth >= MinimumItemWidth || candidate == 1)
            {
                return candidate;
            }
        }

        return 1;
    }

    public static double ItemWidth(double viewportWidth, int count)
    {
        if (count <= 0)
        {
            return MinimumItemWidth;
        }

        double availableWidth = Math.Max(0, viewportWidth - ViewportInset * 2);
        double usableWidth = Math.Max(0, availableWidth - (count - 1) * Spacing);
        return Math.Max(MinimumItemWidth, usableWidth / count);
    }
}
