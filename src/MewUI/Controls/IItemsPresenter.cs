using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

internal interface IItemsPresenter : IScrollContent, IVisualTreeHost
{
    IItemsView ItemsSource { get; set; }

    IDataTemplate ItemTemplate { get; set; }

    Action<IGraphicsContext, int, Rect>? BeforeItemRender { get; set; }

    Func<int, Rect, Rect>? GetContainerRect { get; set; }

    double ExtentWidth { get; set; }

    double ItemRadius { get; set; }

    bool RebindExisting { get; set; }

    /// <summary>
    /// In fixed mode this is the actual item height; in variable mode this is an estimated height hint.
    /// </summary>
    double ItemHeightHint { get; set; }

    bool TryGetItemIndexAtY(double yContent, out int index);

    /// <summary>
    /// Tries to get the item's vertical range in content coordinates (DIPs).
    /// Used for variable-height virtualization where index-based scrolling cannot assume a fixed item height.
    /// </summary>
    bool TryGetItemYRange(int index, out double top, out double bottom);

    /// <summary>
    /// Requests that the presenter scrolls the specified item into view.
    /// Implementations should use <see cref="OffsetCorrectionRequested"/> to adjust the owner's scroll offsets,
    /// and may perform multi-pass corrections (e.g. estimate first, then re-measure for variable-height items).
    /// </summary>
    void RequestScrollIntoView(int index);

    void RecycleAll();

    void VisitRealized(Action<Element> visitor);

    void VisitRealized(Action<int, FrameworkElement> visitor);

    event Action<Point>? OffsetCorrectionRequested;
}
