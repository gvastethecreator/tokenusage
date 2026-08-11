using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TokenUsage.App.Controls;

public sealed partial class ProviderMarkImage : UserControl
{
    private static readonly Dictionary<string, string> FileNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["antigravity"] = "antigravity.svg",
            ["claude"] = "claude.svg",
            ["codex"] = "codex.svg",
            ["cursor"] = "cursor.svg",
            ["grok"] = "grok.svg",
            ["opencode"] = "opencode.svg",
            ["vercel-ai-gateway"] = "vercel-ai-gateway.svg",
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
        if (providerId is null || !FileNames.TryGetValue(providerId, out string? fileName))
        {
            MarkImage.Source = null;
            HighContrastMark.Text = string.Empty;
            return;
        }

        MarkImage.Source = new SvgImageSource(
            new Uri($"ms-appx:///Assets/ProviderMarks/{fileName}"));
        HighContrastMark.Text = providerId[..1].ToUpperInvariant();
    }
}
