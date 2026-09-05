using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace JavMetaLite.App;

public enum PreciseScrollAxis
{
    None,
    Vertical,
    Horizontal
}

public static class PreciseScrollBehavior
{
    public const double VerticalPixelsPerNotch = 24.0;
    public const double HorizontalPixelsPerNotch = 60.0;

    public static readonly DependencyProperty AxisProperty = DependencyProperty.RegisterAttached(
        "Axis",
        typeof(PreciseScrollAxis),
        typeof(PreciseScrollBehavior),
        new PropertyMetadata(PreciseScrollAxis.None, AxisChanged));

    public static PreciseScrollAxis GetAxis(DependencyObject element) =>
        (PreciseScrollAxis)element.GetValue(AxisProperty);

    public static void SetAxis(DependencyObject element, PreciseScrollAxis value) =>
        element.SetValue(AxisProperty, value);

    private static void AxisChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not UIElement element)
        {
            return;
        }

        if ((PreciseScrollAxis)e.OldValue != PreciseScrollAxis.None)
        {
            element.PreviewMouseWheel -= Element_PreviewMouseWheel;
        }

        if ((PreciseScrollAxis)e.NewValue != PreciseScrollAxis.None)
        {
            element.PreviewMouseWheel += Element_PreviewMouseWheel;
        }
    }

    private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject root || e.Delta == 0)
        {
            return;
        }

        var axis = GetAxis(root);
        if (axis == PreciseScrollAxis.None)
        {
            return;
        }

        var nestedRoot = FindNestedScrollRoot(e.OriginalSource as DependencyObject, root, axis);
        if (nestedRoot is not null)
        {
            var nestedViewer = ResolveScrollViewer(nestedRoot);
            if (nestedViewer is not null && CanMove(nestedViewer, axis, e.Delta))
            {
                return;
            }
        }

        var scrollViewer = ResolveScrollViewer(root);
        if (scrollViewer is null || !CanMove(scrollViewer, axis, e.Delta))
        {
            return;
        }

        var pixelsPerNotch = axis == PreciseScrollAxis.Vertical
            ? VerticalPixelsPerNotch
            : HorizontalPixelsPerNotch;
        var pixelDelta = -(e.Delta / 120.0) * pixelsPerNotch;
        var currentOffset = axis == PreciseScrollAxis.Vertical
            ? scrollViewer.VerticalOffset
            : scrollViewer.HorizontalOffset;
        var scrollableLength = axis == PreciseScrollAxis.Vertical
            ? scrollViewer.ScrollableHeight
            : scrollViewer.ScrollableWidth;
        var targetOffset = Math.Clamp(currentOffset + pixelDelta, 0, scrollableLength);
        if (Math.Abs(targetOffset - currentOffset) < 0.01)
        {
            return;
        }

        if (axis == PreciseScrollAxis.Vertical)
        {
            scrollViewer.ScrollToVerticalOffset(targetOffset);
        }
        else
        {
            scrollViewer.ScrollToHorizontalOffset(targetOffset);
        }

        e.Handled = true;
    }

    private static bool CanMove(ScrollViewer scrollViewer, PreciseScrollAxis axis, int wheelDelta)
    {
        var currentOffset = axis == PreciseScrollAxis.Vertical
            ? scrollViewer.VerticalOffset
            : scrollViewer.HorizontalOffset;
        var scrollableLength = axis == PreciseScrollAxis.Vertical
            ? scrollViewer.ScrollableHeight
            : scrollViewer.ScrollableWidth;
        return wheelDelta < 0
            ? currentOffset < scrollableLength - 0.01
            : currentOffset > 0.01;
    }

    private static DependencyObject? FindNestedScrollRoot(
        DependencyObject? source,
        DependencyObject root,
        PreciseScrollAxis axis)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, root); current = GetParent(current))
        {
            if (GetAxis(current) == axis)
            {
                return current;
            }
        }

        return null;
    }

    private static ScrollViewer? ResolveScrollViewer(DependencyObject root) =>
        root as ScrollViewer ?? FindVisualChild<ScrollViewer>(root);

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement);
        }

        return child is Visual or Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
