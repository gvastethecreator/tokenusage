using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace TokenUsage.App.Controls;

/// <summary>Local Tabler outline vectors (v3.46.0, MIT); colors inherit the current theme.</summary>
public sealed class TablerIcon : UserControl
{
    private readonly Path _path = new()
    {
        StrokeThickness = 2,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["stairs"] = "M22 5h-5v5h-5v5h-5v5h-5",
        ["list"] = "M9 6l11 0 M9 12l11 0 M9 18l11 0 M5 6l0 .01 M5 12l0 .01 M5 18l0 .01",
        ["calendar-stats"] = "M11.795 21h-6.795a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v4 M18 14v4h4 M14 18a4 4 0 1 0 8 0a4 4 0 1 0 -8 0 M15 3v4 M7 3v4 M3 11h16",
        ["share"] = "M3 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M15 6a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M15 18a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M8.7 10.7l6.6 -3.4 M8.7 13.3l6.6 3.4",
        ["refresh"] = "M20 11a8.1 8.1 0 0 0 -15.5 -2m-.5 -4v4h4 M4 13a8.1 8.1 0 0 0 15.5 2m.5 4v-4h-4",
        ["settings"] = "M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065 M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0",
        ["chart-donut"] = "M10 3.2a9 9 0 1 0 10.8 10.8a1 1 0 0 0 -1 -1h-3.8a4.1 4.1 0 1 1 -5 -5v-4a.9 .9 0 0 0 -1 -.8 M15 3.5a9 9 0 0 1 5.5 5.5h-4.5a9 9 0 0 0 -1 -1v-4.5",
        ["chart-area-line"] = "M4 19l4 -6l4 2l4 -5l4 4l0 5l-16 0 M4 12l3 -4l4 2l5 -6l4 4",
        ["chart-line"] = "M4 19l16 0 M4 15l4 -6l4 2l4 -5l4 4",
        ["bell"] = "M10 5a2 2 0 1 1 4 0a7 7 0 0 1 4 6v3a4 4 0 0 0 2 3h-16a4 4 0 0 0 2 -3v-3a7 7 0 0 1 4 -6 M9 17v1a3 3 0 0 0 6 0v-1",
        ["file-analytics"] = "M14 3v4a1 1 0 0 0 1 1h4 M17 21h-10a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2h7l5 5v11a2 2 0 0 1 -2 2 M9 17l0 -5 M12 17l0 -1 M15 17l0 -3",
        ["moon"] = "M12 3c.132 0 .263 0 .393 0a7.5 7.5 0 0 0 7.92 12.446a9 9 0 1 1 -8.313 -12.454l0 .008",
        ["chart-bar"] = "M3 13a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v6a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -6 M15 9a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -10 M9 5a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v14a1 1 0 0 1 -1 1h-4a1 1 0 0 1 -1 -1l0 -14 M4 20h14",
    };

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(TablerIcon),
        new PropertyMetadata("share", (sender, _) => ((TablerIcon)sender).UpdateGeometry()));

    public TablerIcon()
    {
        Width = 16;
        Height = 16;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(_path);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        _path.SetBinding(Shape.StrokeProperty, new Binding
        {
            Source = this,
            Path = new PropertyPath(nameof(Foreground)),
        });
        UpdateGeometry();
    }

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void UpdateGeometry()
    {
        string data = Paths[Kind];
        // Convert directly: a Geometry already attached to another Path cannot be reparented.
        _path.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
    }
}
