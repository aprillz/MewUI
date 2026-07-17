using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Coordinates framework-internal drag-and-drop routing.
/// Routes element-level <see cref="UIElement.DragEnter"/>/<c>Over</c>/<c>Leave</c>/<c>Drop</c>
/// using the same popup-aware bubble parent rules as mouse input.
/// </summary>
/// <remarks>
/// Phase 1 scope: framework-only drag-and-drop. Mouse events flow through this router
/// when a drag session is active; the OS DnD APIs (Win32 <c>IDropTarget</c>,
/// macOS <c>NSDraggingSource</c>, X11 Xdnd send) are intentionally not used here.
/// Cross-window routing (Phase 4) reuses the same hit-test/event-dispatch code by
/// resolving the target window from a global cursor probe.
/// </remarks>
internal static class WindowDragDropRouter
{
    // 4 DIPs matches typical platform defaults (Win32 SM_CXDRAG, GtkSettings).
    private const double DragGestureThresholdDip = 4.0;

    private static DragCandidate? _candidate;
    private static DragSession? _activeSession;
    private static ExternalDragSession? _externalSession;
    private static int _routingDepth;

    internal static bool IsActive => _activeSession != null;

    internal static DragSession? ActiveSession => _activeSession;

    internal static bool HasPendingState => _candidate != null || _activeSession != null || _externalSession != null;

    /// <summary>Records a drag candidate when a mouse-down lands on a <see cref="UIElement.CanDrag"/> element (or its ancestor).</summary>
    public static void OnMouseDown(Window window, Point positionInWindow, Point screenPosition, UIElement? leaf)
    {
        if (_routingDepth != 0) return;

        // An active drag in progress consumes new presses (shouldn't normally happen - capture diverts them).
        if (_activeSession != null) return;

        _candidate = null;
        if (leaf == null) return;

        // Walk the bubble chain for the nearest CanDrag element.
        for (var current = leaf; current != null; current = WindowInputRouter.GetInputBubbleParent(window, current))
        {
            if (current.CanDrag)
            {
                _candidate = new DragCandidate(window, current, positionInWindow, screenPosition);
                return;
            }
        }
    }

    /// <summary>
    /// Routes a mouse-move during a potential or active drag session.
    /// Returns <see langword="true"/> if the move was consumed (drag promoted to active, or active session updated).
    /// </summary>
    public static bool OnMouseMove(Window window, Point positionInWindow, Point screenPosition)
    {
        if (!TryEnterRouting()) return _activeSession != null;
        try
        {
            if (_activeSession != null)
            {
                UpdateActiveDrag(window, positionInWindow, screenPosition);
                return true;
            }

            if (_candidate == null) return false;

            var dx = positionInWindow.X - _candidate.StartPositionInWindow.X;
            var dy = positionInWindow.Y - _candidate.StartPositionInWindow.Y;
            if (dx * dx + dy * dy < DragGestureThresholdDip * DragGestureThresholdDip)
            {
                return false;
            }

            // Threshold exceeded - try to promote to an active session.
            return TryPromoteCandidate();
        }
        finally
        {
            ExitRouting();
        }
    }

    /// <summary>
    /// Routes a mouse-up during a potential or active drag session.
    /// Returns <see langword="true"/> if the up was consumed (drop performed or candidate cleared).
    /// </summary>
    public static bool OnMouseUp(Window window, Point positionInWindow, Point screenPosition)
    {
        if (!TryEnterRouting()) return _activeSession != null;
        try
        {
            if (_activeSession != null)
            {
                PerformDropAndEnd(window, positionInWindow, screenPosition);
                return true;
            }

            if (_candidate != null)
            {
                _candidate = null;
                return false; // candidate was a click, not a drag - let normal MouseUp routing run
            }

            return false;
        }
        finally
        {
            ExitRouting();
        }
    }

    /// <summary>Cancels any active drag session (Esc, source window closing, etc.).</summary>
    public static void CancelActive()
    {
        if (!TryEnterRouting()) return;
        try
        {
            if (_activeSession == null) return;
            EndSession(canceled: true);
        }
        finally
        {
            ExitRouting();
        }
    }

    /// <summary>Starts a drag session by explicit API call (bypasses gesture detection).</summary>
    public static void BeginExplicitDrag(Window window, UIElement source, IDataObject data, DragDropEffects effects, DragPreviewContent? preview)
    {
        if (!TryEnterRouting()) return;
        try
        {
            if (_activeSession != null) return;

            var startInWindow = source.TranslatePoint(default, window);
            var startInElement = default(Point);
            var screenPosition = window.LastMouseScreenPositionPx;

            var args = new DragStartingEventArgs(startInElement, startInWindow)
            {
                Data = data,
                AllowedEffects = effects,
                Preview = preview,
            };
            StartSession(window, source, args);
        }
        finally
        {
            ExitRouting();
        }
    }

    private static bool TryPromoteCandidate()
    {
        var candidate = _candidate!;
        _candidate = null;

        var sourceWindow = candidate.SourceWindow;
        var source = candidate.Source;

        var startInElement = sourceWindow.TranslatePoint(candidate.StartPositionInWindow, source);
        var args = new DragStartingEventArgs(startInElement, candidate.StartPositionInWindow);

        // Bubble DragStarting up the chain until handled or canceled.
        for (var current = (UIElement?)source; current != null; current = WindowInputRouter.GetInputBubbleParent(sourceWindow, current))
        {
            current.RaiseDragStarting(args);
            if (args.Cancel || args.Data != null) break;
        }

        if (args.Cancel || args.Data == null) return false;

        return StartSession(sourceWindow, source, args);
    }

    private static bool StartSession(Window sourceWindow, UIElement source, DragStartingEventArgs args)
    {
        if (args.Data == null) return false;

        // When the caller did not specify a hotspot, anchor the preview where the user grabbed the source -
        // this keeps the preview visually aligned with the original at drag-start.
        var hotspot = args.Preview?.Hotspot ?? args.StartPositionInElement;

        var session = new DragSession(
            sourceWindow,
            source,
            args.Data,
            args.AllowedEffects,
            args.Preview,
            hotspot);

        // Any element-level capture (e.g. a Button's OnMouseDown) is replaced by the drag session.
        // Without this the normal hit-test would keep short-circuiting to the captured element after the drop.
        sourceWindow.ClearMouseCaptureState();

        // Backend-level capture so mouse-up arrives at the source window even when the cursor leaves.
        sourceWindow.CaptureMouseForDrag();
        _activeSession = session;

        AttachPreview(sourceWindow, session);
        UpdateActiveDrag(sourceWindow, sourceWindow.LastMousePositionDip, sourceWindow.LastMouseScreenPositionPx);
        return true;
    }

    private static void UpdateActiveDrag(Window eventWindow, Point positionInWindow, Point screenPosition)
    {
        var session = _activeSession!;

        var targetWindow = ResolveTargetWindow(eventWindow, ref positionInWindow, ref screenPosition);
        var leaf = targetWindow?.HitTest(positionInWindow);

        BuildChain(targetWindow, leaf, _scratchNewChain);

        var args = new DragEventArgs(session.Data, positionInWindow, screenPosition, session.AllowedEffects)
        {
            Effect = DragDropEffects.None,
        };

        // Diff old chain vs new chain.
        var oldChain = session.CurrentChain;
        var oldWindow = session.CurrentTargetWindow;

        // DragLeave on entries removed (those in oldChain not in newChain, or when window changed).
        if (!ReferenceEquals(oldWindow, targetWindow))
        {
            // Different window - leave everything in old chain.
            for (int i = 0; i < oldChain.Count; i++)
            {
                oldChain[i].RaiseDragLeave(args);
            }
            oldChain.Clear();
        }
        else
        {
            // Same window - leave entries no longer present.
            for (int i = 0; i < oldChain.Count; i++)
            {
                if (!_scratchNewChain.Contains(oldChain[i]))
                {
                    oldChain[i].RaiseDragLeave(args);
                }
            }
        }

        // DragEnter on entries newly added.
        for (int i = _scratchNewChain.Count - 1; i >= 0; i--)
        {
            if (!oldChain.Contains(_scratchNewChain[i]))
            {
                _scratchNewChain[i].RaiseDragEnter(args);
            }
        }

        // Save new chain.
        oldChain.Clear();
        oldChain.AddRange(_scratchNewChain);
        session.CurrentTargetWindow = targetWindow;

        // DragOver on the current chain (leaf to root).
        var overArgs = new DragEventArgs(session.Data, positionInWindow, screenPosition, session.AllowedEffects)
        {
            Effect = args.Effect,
            Accepted = args.Accepted,
        };
        for (int i = 0; i < oldChain.Count; i++)
        {
            oldChain[i].RaiseDragOver(overArgs);
            if (overArgs.Accepted) overArgs.Handled = true;
            if (overArgs.Handled) break;
        }

        // Normalize the effect: must be within AllowedEffects, and accepted-implies-non-None.
        var effect = overArgs.Effect & session.AllowedEffects;
        if (!overArgs.Accepted) effect = DragDropEffects.None;
        if (overArgs.Accepted && effect == DragDropEffects.None)
        {
            // Default fallback when target says "accepted" but did not pick a specific effect.
            effect = (session.AllowedEffects & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy
                : session.AllowedEffects;
        }
        session.LastEffect = effect;

        UpdatePreviewPosition(targetWindow, session, positionInWindow, screenPosition);
    }

    private static void PerformDropAndEnd(Window eventWindow, Point positionInWindow, Point screenPosition)
    {
        var session = _activeSession!;
        var targetWindow = ResolveTargetWindow(eventWindow, ref positionInWindow, ref screenPosition);
        var leaf = targetWindow?.HitTest(positionInWindow);

        BuildChain(targetWindow, leaf, _scratchNewChain);

        var args = new DragEventArgs(session.Data, positionInWindow, screenPosition, session.AllowedEffects)
        {
            Effect = session.LastEffect,
            Accepted = session.LastEffect != DragDropEffects.None,
        };

        if (targetWindow != null && _scratchNewChain.Count > 0 && args.Accepted)
        {
            for (int i = 0; i < _scratchNewChain.Count; i++)
            {
                _scratchNewChain[i].RaiseDrop(args);
                if (args.Accepted) args.Handled = true;
                if (args.Handled) break;
            }
        }

        var finalEffect = args.Accepted ? (args.Effect & session.AllowedEffects) : DragDropEffects.None;
        if (args.Accepted && finalEffect == DragDropEffects.None)
        {
            finalEffect = (session.AllowedEffects & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy
                : session.AllowedEffects;
        }
        session.LastEffect = finalEffect;

        // A release is NOT a cancel even when no target accepted it: report WasCanceled=false with
        // FinalEffect=None so a source can tell "released over empty space" (e.g. spawn a window there) apart
        // from an Esc cancel. The release screen position travels on the completed args.
        EndSession(canceled: false, screenPosition);
    }

    private static void EndSession(bool canceled, Point screenPosition = default)
    {
        var session = _activeSession;
        if (session == null) return;
        _activeSession = null;

        // Leave any current target chain.
        var leaveArgs = new DragEventArgs(session.Data, default, default, session.AllowedEffects);
        for (int i = 0; i < session.CurrentChain.Count; i++)
        {
            session.CurrentChain[i].RaiseDragLeave(leaveArgs);
        }
        session.CurrentChain.Clear();

        DetachPreview(session);
        session.SourceWindow.ReleaseMouseAfterDrag();

        var completed = new DragCompletedEventArgs(
            canceled ? DragDropEffects.None : session.LastEffect, canceled, screenPosition);
        session.Source.RaiseDragCompleted(completed);
    }

    private static Window? ResolveTargetWindow(Window eventWindow, ref Point positionInWindow, ref Point screenPosition)
    {
        // A within-window drag never targets another window, so skip the cross-window cursor probe entirely.
        if (_activeSession?.Preview is { Scope: DragPreviewScope.WithinWindow })
        {
            return eventWindow;
        }

        // Probe the global cursor and try to find a same-app MewUI window under it.
        // Falls back to the event window (single-window behavior) when the probe is unavailable or finds no match.
        if (!Application.IsRunning)
        {
            return eventWindow;
        }

        var app = Application.Current;
        var cursorScreen = app.PlatformHost.GetCursorScreenPosition();
        if (cursorScreen == default)
        {
            return eventWindow;
        }

        screenPosition = cursorScreen;

        var windows = app.AllWindows;
        // Iterate in reverse - later-registered windows are heuristically more likely to be on top.
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            var candidate = windows[i];
            if (candidate.Handle == 0)
            {
                continue;
            }

            // Skip the preview overlay: it follows the cursor, so drop resolution must see through it.
            if (candidate.Kind == Controls.WindowKind.Overlay)
            {
                continue;
            }

            Point clientDip;
            try
            {
                clientDip = candidate.ScreenToClient(cursorScreen);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var size = candidate.ClientSize;
            if (clientDip.X < 0 || clientDip.Y < 0 ||
                clientDip.X > size.Width || clientDip.Y > size.Height)
            {
                continue;
            }

            positionInWindow = clientDip;
            return candidate;
        }

        // Cursor not over any MewUI window - return null so the chain leaves and the preview shows a rejected state.
        return null;
    }

    private static readonly List<UIElement> _scratchNewChain = new();

    private static void BuildChain(Window? window, UIElement? leaf, List<UIElement> chain)
    {
        chain.Clear();
        if (window == null || leaf == null) return;
        for (var current = leaf; current != null; current = WindowInputRouter.GetInputBubbleParent(window, current))
        {
            if (current.AllowDrop)
            {
                chain.Add(current);
            }
        }
    }

    private static void AttachPreview(Window window, DragSession session)
    {
        if (session.Preview == null) return;

        if (session.Preview.Scope == DragPreviewScope.CrossWindow)
        {
            // A single top-level overlay window that follows the cursor in screen coordinates, so the preview
            // stays visible across windows and the desktop gap. Transparent when the platform composites it,
            // otherwise opaque so a cross-window preview stays continuous instead of falling back to per-window.
            bool transparent = Application.IsRunning && Application.Current.PlatformHost.SupportsTransparentOverlay;
            var previewWindow = new OverlayWindow(transparent);

            if (session.Preview.Element is { Parent: null } ownedElement)
            {
                // Detached element (e.g. a labelled chip): host it as real content, fit to it, cap width.
                double maxWidth = session.Preview.MaxWidth is { } configured and > 0 ? configured : 256;
                previewWindow.WindowSize = WindowSize.FitContentSize(maxWidth, 256);
                previewWindow.Content = ownedElement;
            }
            else
            {
                // Live element/image: snapshot via DragPreviewOverlay so the source is not re-parented.
                var surface = new DragPreviewOverlay(session.Preview, session.PreviewHotspot);
                surface.UpdateCursorPosition(session.PreviewHotspot);
                var size = surface.PreviewSize;
                previewWindow.WindowSize = WindowSize.Fixed(Math.Max(1, size.Width), Math.Max(1, size.Height));
                previewWindow.Content = surface;
            }

            session.PreviewWindow = previewWindow;

            // Owner = source window; Topmost keeps it above targets; no-activate show keeps source capture/focus.
            previewWindow.Show(session.SourceWindow);
            MovePreviewWindow(previewWindow, session, Application.Current.PlatformHost.GetCursorScreenPosition());
        }
        else
        {
            // WithinWindow: a per-window overlay blends into the window surface (real transparency, no compositor
            // needed) but is confined to the window it is over.
            var overlay = new DragPreviewOverlay(session.Preview, session.PreviewHotspot);
            session.PreviewOverlay = overlay;
            window.OverlayLayer.Add(overlay);
        }
    }

    private static void DetachPreview(DragSession session)
    {
        if (session.PreviewWindow is { } previewWindow)
        {
            session.PreviewWindow = null;
            previewWindow.Close();
            return;
        }

        var overlay = session.PreviewOverlay;
        if (overlay == null) return;
        if (overlay.Parent is Window owner)
        {
            owner.OverlayLayer.Remove(overlay);
        }
        session.PreviewOverlay = null;
    }

    private static void MovePreviewWindow(Window previewWindow, DragSession session, Point screenPositionPx)
    {
        // Top-left = cursor - hotspot. screenPositionPx is top-left Y-down px (matches MoveTo).
        double scale = previewWindow.DpiScale > 0 ? previewWindow.DpiScale : 1.0;
        previewWindow.MoveTo(
            screenPositionPx.X / scale - session.PreviewHotspot.X,
            screenPositionPx.Y / scale - session.PreviewHotspot.Y);
    }

    private static void UpdatePreviewPosition(Window? targetWindow, DragSession session, Point cursorInWindow, Point screenPosition)
    {
        // Continuous preview: move the top-level overlay window to track the cursor in screen space.
        if (session.PreviewWindow is { } previewWindow)
        {
            MovePreviewWindow(previewWindow, session, screenPosition);
            return;
        }

        var overlay = session.PreviewOverlay;
        if (overlay == null) return;

        // Cursor is over no MewUI window (e.g. the desktop): detach the preview so it does not linger in the
        // last window. It re-attaches when the cursor re-enters a window. (Per-window overlay fallback.)
        if (targetWindow == null)
        {
            if (overlay.Parent is Window owner)
            {
                owner.OverlayLayer.Remove(overlay);
            }
            return;
        }

        // Move overlay to the target window when the cursor crosses windows.
        if (!ReferenceEquals(overlay.Parent, targetWindow))
        {
            if (overlay.Parent is Window oldOwner)
            {
                oldOwner.OverlayLayer.Remove(overlay);
            }
            targetWindow.OverlayLayer.Add(overlay);
        }
        overlay.UpdateCursorPosition(cursorInWindow);
    }

    // ============================================================================================
    // External drag-in (OS DnD → MewUI elements). Driven by per-platform backend adapters:
    //   Win32  : IDropTarget COM impl
    //   macOS  : NSDraggingDestination protocol callbacks
    //   X11    : XdndEnter / XdndPosition / XdndLeave / XdndDrop client messages
    // The router is responsible only for element-chain routing + window-level fallback.
    // ============================================================================================

    /// <summary>External drag entered the window. Computes the target chain and raises element-level DragEnter.</summary>
    public static void OnExternalDragEnter(Window window, DragEventArgs args)
    {
        if (!TryEnterRouting()) return;
        try
        {
            if (_activeSession != null) return; // ignore while an internal session owns the cursor

            // Clear any stale chain from a prior session that didn't get a clean Leave.
            if (_externalSession != null)
            {
                LeaveExternalChain(args);
            }

            _externalSession = new ExternalDragSession(window);
            DispatchExternalEnterOver(window, args, raiseEnter: true);
        }
        finally
        {
            ExitRouting();
        }
    }

    /// <summary>External drag moved over the window. Diffs the chain and raises Leave/Enter/Over.</summary>
    public static void OnExternalDragOver(Window window, DragEventArgs args)
    {
        if (!TryEnterRouting()) return;
        try
        {
            if (_activeSession != null) return;

            if (_externalSession == null || !ReferenceEquals(_externalSession.Window, window))
            {
                // Treat a stray Over as Enter without re-entering the public routing guard.
                if (_externalSession != null)
                {
                    LeaveExternalChain(args);
                }
                _externalSession = new ExternalDragSession(window);
                DispatchExternalEnterOver(window, args, raiseEnter: true);
                return;
            }

            DispatchExternalEnterOver(window, args, raiseEnter: false);
        }
        finally
        {
            ExitRouting();
        }
    }

    /// <summary>External drag left the window. Leaves the current chain and clears state.</summary>
    public static void OnExternalDragLeave(Window window, DragEventArgs args)
    {
        if (!TryEnterRouting()) return;
        try
        {
            if (_activeSession != null) return;
            if (_externalSession == null || !ReferenceEquals(_externalSession.Window, window)) return;

            LeaveExternalChain(args);
            window.RaiseDragLeave(args);
        }
        finally
        {
            ExitRouting();
        }
    }

    /// <summary>External drag dropped on the window. Routes the drop through the element chain, then falls back to window-level.</summary>
    /// <returns>The final negotiated effect (None when nothing accepted).</returns>
    public static DragDropEffects OnExternalDrop(Window window, DragEventArgs args)
    {
        if (!TryEnterRouting()) return DragDropEffects.None;
        if (_activeSession != null)
        {
            ExitRouting();
            return DragDropEffects.None;
        }

        try
        {
            var leaf = window.HitTest(args.Position);
            BuildChain(window, leaf, _scratchNewChain);

            for (int i = 0; i < _scratchNewChain.Count; i++)
            {
                _scratchNewChain[i].RaiseDrop(args);
                if (args.Accepted) args.Handled = true;
                if (args.Handled) break;
            }

            if (!args.Handled)
            {
                // Same default-accept policy as DragEnter/Over so an unaware window-level Drop handler
                // still produces a valid effect for the OS to confirm the operation.
                if (HasStandardFormat(args.Data))
                {
                    args.Accepted = true;
                    if (args.Effect == DragDropEffects.None)
                    {
                        args.Effect = (args.AllowedEffects & DragDropEffects.Copy) != 0
                            ? DragDropEffects.Copy
                            : args.AllowedEffects;
                    }
                }
                window.RaiseDrop(args);
            }

            return NormalizeEffect(args);
        }
        finally
        {
            _externalSession = null;
            _scratchNewChain.Clear();
            ExitRouting();
        }
    }

    private static void DispatchExternalEnterOver(Window window, DragEventArgs args, bool raiseEnter)
    {
        var session = _externalSession!;
        var leaf = window.HitTest(args.Position);
        BuildChain(window, leaf, _scratchNewChain);

        var oldChain = session.CurrentChain;

        // DragLeave on entries removed.
        for (int i = 0; i < oldChain.Count; i++)
        {
            if (!_scratchNewChain.Contains(oldChain[i]))
            {
                oldChain[i].RaiseDragLeave(args);
            }
        }

        // DragEnter on entries newly added (leaf to root order to mirror mouse-enter semantics).
        for (int i = _scratchNewChain.Count - 1; i >= 0; i--)
        {
            if (!oldChain.Contains(_scratchNewChain[i]))
            {
                _scratchNewChain[i].RaiseDragEnter(args);
            }
        }

        // DragOver bubbling, leaf to root.
        for (int i = 0; i < _scratchNewChain.Count; i++)
        {
            _scratchNewChain[i].RaiseDragOver(args);
            if (args.Accepted) args.Handled = true;
            if (args.Handled) break;
        }

        // Window-level fallback for unhandled events.
        // The framework default at window scope is "accept if any standard format is present" so the OS
        // shows a "drop allowed" cursor without every consumer wiring up a DragOver handler purely for
        // visual feedback. Handlers can still explicitly opt out (set Accepted=false / Effect=None).
        if (!args.Handled)
        {
            if (HasStandardFormat(args.Data))
            {
                args.Accepted = true;
                if (args.Effect == DragDropEffects.None)
                {
                    args.Effect = (args.AllowedEffects & DragDropEffects.Copy) != 0
                        ? DragDropEffects.Copy
                        : args.AllowedEffects;
                }
            }
            if (raiseEnter) window.RaiseDragEnter(args);
            else window.RaiseDragOver(args);
        }

        oldChain.Clear();
        oldChain.AddRange(_scratchNewChain);
    }

    private static bool HasStandardFormat(IDataObject data)
        => data.Contains(StandardDataFormats.StorageItems) || data.Contains(StandardDataFormats.Text);

    private static void LeaveExternalChain(DragEventArgs args)
    {
        var session = _externalSession;
        _externalSession = null;
        if (session == null) return;

        for (int i = 0; i < session.CurrentChain.Count; i++)
        {
            session.CurrentChain[i].RaiseDragLeave(args);
        }
        session.CurrentChain.Clear();
    }

    private static bool TryEnterRouting()
    {
        if (_routingDepth != 0)
        {
            return false;
        }

        _routingDepth = 1;
        return true;
    }

    private static void ExitRouting() => _routingDepth = 0;

    internal static void ResetForRuntimeEnd()
    {
        // Drop process-wide references first. Cleanup below may invoke user/platform code and must
        // not leave a half-ended session reachable if that code fails or re-enters the router.
        _candidate = null;
        var activeSession = _activeSession;
        _activeSession = null;
        var externalSession = _externalSession;
        _externalSession = null;
        _scratchNewChain.Clear();

        if (externalSession != null)
        {
            externalSession.CurrentChain.Clear();
        }

        if (activeSession == null)
        {
            return;
        }

        activeSession.CurrentChain.Clear();
        try
        {
            DetachPreview(activeSession);
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }

        try
        {
            activeSession.SourceWindow.ReleaseMouseAfterDrag();
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
        }
    }

    private static DragDropEffects NormalizeEffect(DragEventArgs args)
    {
        if (!args.Accepted) return DragDropEffects.None;
        var effect = args.Effect & args.AllowedEffects;
        if (effect != DragDropEffects.None) return effect;
        // Target said accepted without picking a specific effect - pick the first allowed one.
        if ((args.AllowedEffects & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
        if ((args.AllowedEffects & DragDropEffects.Move) != 0) return DragDropEffects.Move;
        if ((args.AllowedEffects & DragDropEffects.Link) != 0) return DragDropEffects.Link;
        return DragDropEffects.None;
    }
}

internal sealed class ExternalDragSession
{
    public Window Window { get; }

    public List<UIElement> CurrentChain { get; } = new();

    public ExternalDragSession(Window window) => Window = window;
}

internal sealed class DragCandidate
{
    public Window SourceWindow { get; }

    public UIElement Source { get; }

    public Point StartPositionInWindow { get; }

    public Point StartScreenPosition { get; }

    public DragCandidate(Window sourceWindow, UIElement source, Point startInWindow, Point startScreen)
    {
        SourceWindow = sourceWindow;
        Source = source;
        StartPositionInWindow = startInWindow;
        StartScreenPosition = startScreen;
    }
}

internal sealed class DragSession
{
    public Window SourceWindow { get; }

    public UIElement Source { get; }

    public IDataObject Data { get; }

    public DragDropEffects AllowedEffects { get; }

    public DragPreviewContent? Preview { get; }

    public Point PreviewHotspot { get; }

    public Window? CurrentTargetWindow { get; set; }

    public List<UIElement> CurrentChain { get; } = new();

    public DragDropEffects LastEffect { get; set; }

    // The per-window overlay, confined to the window it is over. Used for a WithinWindow preview; otherwise
    // PreviewWindow (the cursor-following top-level overlay) is used.
    public DragPreviewOverlay? PreviewOverlay { get; set; }

    // The top-level overlay window that follows the cursor in screen coordinates. Non-null for a CrossWindow
    // preview (transparent or opaque); otherwise PreviewOverlay is used.
    public OverlayWindow? PreviewWindow { get; set; }

    public DragSession(Window sourceWindow, UIElement source, IDataObject data, DragDropEffects allowedEffects, DragPreviewContent? preview, Point previewHotspot)
    {
        SourceWindow = sourceWindow;
        Source = source;
        Data = data;
        AllowedEffects = allowedEffects;
        Preview = preview;
        PreviewHotspot = previewHotspot;
    }
}
