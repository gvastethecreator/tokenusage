using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TokenUsage.Providers.Catalog;

namespace TokenUsage.App.Controls;

public sealed partial class ProviderMarkImage : UserControl
{
    private const double RasterSupersampling = 2d;

    public static readonly DependencyProperty ProviderIdProperty =
        DependencyProperty.Register(
            nameof(ProviderId),
            typeof(string),
            typeof(ProviderMarkImage),
            new PropertyMetadata(string.Empty, OnProviderIdChanged));

    public ProviderMarkImage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public string ProviderId
    {
        get => (string)GetValue(ProviderIdProperty);
        set => SetValue(ProviderIdProperty, value);
    }

    private static void OnProviderIdChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var control = (ProviderMarkImage)dependencyObject;
        control.UpdateSource(args.NewValue as string);
    }

    private void UpdateSource(string? providerId)
    {
        string fallback = string.IsNullOrWhiteSpace(providerId)
            ? string.Empty
            : providerId[..1].ToUpperInvariant();
        HighContrastMark.Text = fallback;
        if (ProviderPresentationCatalog.MarkFileName(providerId) is not string fileName)
        {
            MarkImage.Source = null;
            FallbackMark.Text = fallback;
            FallbackMark.Visibility = string.IsNullOrEmpty(fallback)
                ? Visibility.Collapsed
                : Visibility.Visible;
            return;
        }

        FallbackMark.Text = string.Empty;
        FallbackMark.Visibility = Visibility.Collapsed;
        MarkImage.Source = new SvgImageSource(
            new Uri($"ms-appx:///Assets/ProviderMarks/{fileName}"));
        UpdateRasterSize();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateRasterSize();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateRasterSize();

    private void UpdateRasterSize()
    {
        if (MarkImage.Source is not SvgImageSource source)
        {
            return;
        }

        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        if (!double.IsFinite(width)
            || !double.IsFinite(height)
            || width <= 0
            || height <= 0)
        {
            return;
        }

        double rasterizationScale = XamlRoot?.RasterizationScale ?? 1d;
        double pixelWidth = Math.Ceiling(width * rasterizationScale * RasterSupersampling);
        double pixelHeight = Math.Ceiling(height * rasterizationScale * RasterSupersampling);
        if (Math.Abs(source.RasterizePixelWidth - pixelWidth) < 0.5
            && Math.Abs(source.RasterizePixelHeight - pixelHeight) < 0.5)
        {
            return;
        }

        source.RasterizePixelWidth = pixelWidth;
        source.RasterizePixelHeight = pixelHeight;
    }
}
