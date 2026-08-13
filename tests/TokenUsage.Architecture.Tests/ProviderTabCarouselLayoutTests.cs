using TokenUsage.App.ViewModels;

namespace TokenUsage.Architecture.Tests;

public sealed class ProviderTabCarouselLayoutTests
{
    [Fact]
    public void CompactFlyoutKeepsFourTabsWhenFiveProvidersNeedNavigation()
    {
        Assert.Equal(4, ProviderTabCarouselLayout.MaximumPageSize);
        Assert.Equal(4, ProviderTabCarouselLayout.PageSize(5, 448));
        Assert.Equal(4, ProviderTabCarouselLayout.PageSize(5, 436));
        Assert.Equal(4, ProviderTabCarouselLayout.PageSize(5, 400));
    }

    [Fact]
    public void FourProvidersFillTheRowWithoutNavigation()
    {
        Assert.Equal(4, ProviderTabCarouselLayout.PageSize(4, 448));
    }

    [Fact]
    public void ThreeProvidersStayOnOnePage()
    {
        Assert.Equal(3, ProviderTabCarouselLayout.PageSize(3, 448));
    }

    [Fact]
    public void FourEqualTabsFillTheViewport()
    {
        double width = ProviderTabCarouselLayout.ItemWidth(384, 4);
        Assert.True(width >= 64);
        Assert.Equal(384 - 4 - 6, width * 4, 3);
    }

    [Fact]
    public void ReportWindowKeepsSixTabsUntilASeventhNeedsNavigation()
    {
        Assert.Equal(6, ProviderTabCarouselLayout.ReportMaximumPageSize);
        Assert.Equal(
            6,
            ProviderTabCarouselLayout.PageSize(
                6,
                1200,
                ProviderTabCarouselLayout.ReportMaximumPageSize));
        Assert.Equal(
            6,
            ProviderTabCarouselLayout.PageSize(
                7,
                1200,
                ProviderTabCarouselLayout.ReportMaximumPageSize));
        Assert.Equal(
            6,
            ProviderTabCarouselLayout.PageSize(
                10,
                800,
                ProviderTabCarouselLayout.ReportMaximumPageSize));
    }
}
