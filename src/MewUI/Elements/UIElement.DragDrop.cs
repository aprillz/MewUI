using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Controls;

public abstract partial class UIElement
{
    /// <summary>
    /// Backing MewProperty for <see cref="AllowDrop"/>.
    /// See the property remarks for platform-specific behavior.
    /// </summary>
    public static readonly MewProperty<bool> AllowDropProperty =
        MewProperty<bool>.Register<UIElement>(nameof(AllowDrop), false, MewPropertyOptions.None);

    /// <summary>
    /// When <see langword="true"/>, mouse-press + threshold movement on this element triggers
    /// <see cref="DragStarting"/> to begin a drag source session.
    /// </summary>
    public static readonly MewProperty<bool> CanDragProperty =
        MewProperty<bool>.Register<UIElement>(nameof(CanDrag), false, MewPropertyOptions.None);

    /// <summary>
    /// Gets or sets whether the element accepts drops.
    /// When <see langword="true"/>, the element participates in drop-target hit testing
    /// and can receive <see cref="DragEnter"/>/<see cref="DragOver"/>/<see cref="DragLeave"/>/<see cref="Drop"/>.
    /// </summary>
    /// <remarks>
    /// Setting <c>AllowDrop</c> on a <see cref="Window"/> additionally triggers platform drop-target
    /// registration (WinForms/WPF style). The behavior per backend:
    /// <list type="bullet">
    /// <item><description><b>Win32</b>: On an STA UI thread the framework calls <c>OleInitialize</c> + <c>RegisterDragDrop</c>
    /// for full <c>IDropTarget</c> support (DragEnter/Over/Leave/Drop, effect negotiation, native preview via
    /// <c>IDropTargetHelper</c>). .NET 6+ entry points default to MTA - apply <c>[STAThread]</c> on
    /// <c>Main</c> to opt in. On MTA threads the framework falls back to the legacy <c>WM_DROPFILES</c>
    /// path (file drop only, no enter/over/leave, no preview).</description></item>
    /// <item><description><b>macOS</b>: Calls <c>[NSView registerForDraggedTypes:]</c> with the file URL type.</description></item>
    /// <item><description><b>X11</b>: Sets the <c>XdndAware</c> property on the window.</description></item>
    /// </list>
    /// On non-<see cref="Window"/> elements, <c>AllowDrop</c> only affects element-chain participation -
    /// element-level <c>DragEnter</c>/<c>Over</c>/<c>Leave</c>/<c>Drop</c> are bubbled by the framework
    /// drag router. Platform registration always happens at the window level.
    /// </remarks>
    public bool AllowDrop
    {
        get => GetValue(AllowDropProperty);
        set => SetValue(AllowDropProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the element initiates drag sessions via a mouse gesture.
    /// </summary>
    public bool CanDrag
    {
        get => GetValue(CanDragProperty);
        set => SetValue(CanDragProperty, value);
    }

    /// <summary>Occurs when a drag enters the element's hit-test bounds.</summary>
    public event Action<DragEventArgs>? DragEnter;

    /// <summary>Occurs while a drag is over the element.</summary>
    public event Action<DragEventArgs>? DragOver;

    /// <summary>Occurs when a drag leaves the element's hit-test bounds.</summary>
    public event Action<DragEventArgs>? DragLeave;

    /// <summary>Occurs when data is dropped onto the element.</summary>
    public event Action<DragEventArgs>? Drop;

    /// <summary>
    /// Occurs after a drag gesture is detected on this element.
    /// Handlers populate <see cref="DragStartingEventArgs.Data"/>/<see cref="DragStartingEventArgs.AllowedEffects"/>
    /// to start the session, or set <see cref="DragStartingEventArgs.Cancel"/>.
    /// </summary>
    public event Action<DragStartingEventArgs>? DragStarting;

    /// <summary>
    /// Occurs once a drag session originating from this element has ended (drop, reject, or cancel).
    /// </summary>
    public event Action<DragCompletedEventArgs>? DragCompleted;

    internal void RaiseDragEnter(DragEventArgs e) => OnDragEnter(e);

    internal void RaiseDragOver(DragEventArgs e) => OnDragOver(e);

    internal void RaiseDragLeave(DragEventArgs e) => OnDragLeave(e);

    internal void RaiseDrop(DragEventArgs e) => OnDrop(e);

    internal void RaiseDragStarting(DragStartingEventArgs e) => OnDragStarting(e);

    internal void RaiseDragCompleted(DragCompletedEventArgs e) => OnDragCompleted(e);

    /// <summary>Override to receive drag-enter events before the public <see cref="DragEnter"/> handler.</summary>
    protected virtual void OnDragEnter(DragEventArgs e) => DragEnter?.Invoke(e);

    /// <summary>Override to receive drag-over events before the public <see cref="DragOver"/> handler.</summary>
    protected virtual void OnDragOver(DragEventArgs e) => DragOver?.Invoke(e);

    /// <summary>Override to receive drag-leave events before the public <see cref="DragLeave"/> handler.</summary>
    protected virtual void OnDragLeave(DragEventArgs e) => DragLeave?.Invoke(e);

    /// <summary>Override to receive drop events before the public <see cref="Drop"/> handler.</summary>
    protected virtual void OnDrop(DragEventArgs e) => Drop?.Invoke(e);

    /// <summary>Override to receive drag-starting before the public <see cref="DragStarting"/> handler.</summary>
    protected virtual void OnDragStarting(DragStartingEventArgs e) => DragStarting?.Invoke(e);

    /// <summary>Override to receive drag-completed before the public <see cref="DragCompleted"/> handler.</summary>
    protected virtual void OnDragCompleted(DragCompletedEventArgs e) => DragCompleted?.Invoke(e);

    /// <summary>
    /// Explicitly starts a drag session originating from this element, bypassing gesture detection.
    /// Fire-and-forget; the result is reported via <see cref="DragCompleted"/>.
    /// </summary>
    public void BeginDrag(IDataObject data, DragDropEffects allowedEffects, DragPreviewContent? preview = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (FindVisualRoot() is not Window window)
        {
            return;
        }

        Input.WindowDragDropRouter.BeginExplicitDrag(window, this, data, allowedEffects, preview);
    }
}
