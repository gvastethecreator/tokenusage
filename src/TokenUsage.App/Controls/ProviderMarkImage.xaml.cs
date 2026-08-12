using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TokenUsage.App.Controls;

public sealed partial class ProviderMarkImage : UserControl
{
    private const double RasterSupersampling = 2d;

    private static readonly Dictionary<string, string> FileNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alibaba-cloud"] = "alibaba-cloud.svg",
            ["amp"] = "amp.svg",
            ["antigravity"] = "antigravity.svg",
            ["anthropic"] = "anthropic.svg",
            ["azure-openai"] = "openai.svg",
            ["claude"] = "claude.svg",
            ["codex"] = "codex.svg",
            ["copilot"] = "copilot.svg",
            ["cursor"] = "cursor.svg",
            ["cursor-agent"] = "cursor.svg",
            ["deepseek"] = "deepseek.svg",
            ["devin"] = "devin.svg",
            ["droid"] = "droid.svg",
            ["gemini-api"] = "gemini-api.svg",
            ["gemini-cli"] = "gemini-cli.svg",
            ["goose"] = "goose.svg",
            ["grok"] = "grok.svg",
            ["groq"] = "groq.svg",
            ["hermes"] = "hermes.svg",
            ["kilo-code"] = "kilo-code.svg",
            ["kimi-cli"] = "kimi.svg",
            ["kimi-code"] = "kimi.svg",
            ["kiro"] = "kiro.svg",
            ["mistral"] = "mistral.svg",
            ["mistral-vibe"] = "mistral.svg",
            ["moonshot"] = "moonshot.svg",
            ["mux"] = "mux.svg",
            ["ollama"] = "ollama.svg",
            ["openai"] = "openai.svg",
            ["openclaude"] = "claude.svg",
            ["openclaw"] = "openclaw.svg",
            ["opencode"] = "opencode.svg",
            ["openrouter"] = "openrouter.svg",
            ["perplexity"] = "perplexity.svg",
            ["pi"] = "pi.svg",
            ["qwen-cli"] = "qwen-cli.svg",
            ["roo-code"] = "roo-code.svg",
            ["vercel-ai-gateway"] = "vercel-ai-gateway.svg",
            ["xai"] = "xai.svg",
            ["zai"] = "zai.svg",
            ["zed"] = "zed.svg",
        };

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
        if (providerId is null || !FileNames.TryGetValue(providerId, out string? fileName))
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
