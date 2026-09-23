using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TokenUsage.App.Controls;

// Keep the small dashboard model preview measured while it leaves the viewport.
// Full-report capture also uses this layout temporarily for measured rows.
internal sealed class ReportCaptureStackLayout(Orientation orientation, double spacing) : NonVirtualizingLayout
{
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        bool vertical = orientation == Orientation.Vertical;
        Size constraint = vertical ? new(availableSize.Width, double.PositiveInfinity)
            : new(double.PositiveInfinity, availableSize.Height);
        double length = 0, breadth = 0;
        foreach (var child in context.Children)
        {
            child.Measure(constraint);
            length += vertical ? child.DesiredSize.Height : child.DesiredSize.Width;
            breadth = Math.Max(breadth, vertical ? child.DesiredSize.Width : child.DesiredSize.Height);
        }
        length += Math.Max(0, context.Children.Count - 1) * spacing;
        return vertical ? new(breadth, length) : new(length, breadth);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        double offset = 0;
        foreach (var child in context.Children)
        {
            bool vertical = orientation == Orientation.Vertical;
            double length = vertical ? child.DesiredSize.Height : child.DesiredSize.Width;
            child.Arrange(vertical ? new Rect(0, offset, finalSize.Width, length)
                : new Rect(offset, 0, length, finalSize.Height));
            offset += length + spacing;
        }
        return finalSize;
    }
}
