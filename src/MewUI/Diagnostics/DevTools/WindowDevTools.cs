using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI;

/// <summary>
/// A window's development tools: element inspector, visual tree, performance monitor and profiler.
/// Reached through <see cref="Window.DevTools"/>, which is null when the app did not opt in.
/// </summary>
public sealed class WindowDevTools
{
    private readonly Window _window;

    private Adorner? _inspectorAdorner;
    private DebugVisualTreeWindow? _visualTreeWindow;
    private Adorner? _performanceAdorner;
    private DebugPerformanceOverlay? _performanceOverlay;
    private DebugProfilerWindow? _profilerWindow;

    internal WindowDevTools(Window window)
    {
        _window = window;
        window.InputMap.Map(new KeyGesture(Key.I, ModifierKeys.Primary | ModifierKeys.Shift), ToggleInspector);
        window.InputMap.Map(new KeyGesture(Key.T, ModifierKeys.Primary | ModifierKeys.Shift), ToggleVisualTree);
        window.InputMap.Map(new KeyGesture(Key.P, ModifierKeys.Primary | ModifierKeys.Shift), TogglePerformanceMonitor);
        window.InputMap.Map(new KeyGesture(Key.P, ModifierKeys.Primary | ModifierKeys.Shift | ModifierKeys.Alt), ToggleProfiler);
    }

    /// <summary>
    /// Whether this build can host the development tools. False in release, trimmed and NativeAOT
    /// builds, where the trimmer folds this to a constant so guarded app code is removed too.
    /// </summary>
    public static bool IsSupported => DevToolsGate.IsSupported;

    /// <summary>Raised when the inspector overlay appears or disappears.</summary>
    public event Action<bool>? InspectorVisibleChanged;

    /// <summary>Raised when the visual tree window opens or closes.</summary>
    public event Action<bool>? VisualTreeOpenChanged;

    /// <summary>Raised when the performance monitor overlay appears or disappears.</summary>
    public event Action<bool>? PerformanceMonitorVisibleChanged;

    /// <summary>Raised when the profiler window opens or closes.</summary>
    public event Action<bool>? ProfilerOpenChanged;

    /// <summary>Whether the inspector overlay is on this window.</summary>
    public bool InspectorIsVisible => _inspectorAdorner != null;

    /// <summary>Whether the visual tree window is open.</summary>
    public bool VisualTreeIsOpen => _visualTreeWindow != null;

    /// <summary>Whether the performance monitor overlay is on this window.</summary>
    public bool PerformanceMonitorIsVisible => _performanceAdorner != null;

    /// <summary>Whether the profiler window is open.</summary>
    public bool ProfilerIsOpen => _profilerWindow != null;

    /// <summary>The adorner-hosted inspector overlay, or null when the inspector is off.</summary>
    internal DebugInspectorOverlay? InspectorOverlay { get; private set; }

    /// <summary>The performance monitor's adorner, which the render loop draws after everything else.</summary>
    internal Adorner? PerformanceAdorner => _performanceAdorner;

    /// <summary>Shows or hides the element inspector overlay.</summary>
    public void ToggleInspector()
    {
        if (_inspectorAdorner != null)
        {
            _window.AdornerLayer.Remove(_inspectorAdorner);
            _inspectorAdorner = null;
            InspectorOverlay = null;
            _window.RequestUpdatePass();
            _window.RequestRender();
            InspectorVisibleChanged?.Invoke(false);
            return;
        }

        InspectorOverlay = new DebugInspectorOverlay(_window)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        _inspectorAdorner = new Adorner(_window, InspectorOverlay)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        _window.AdornerLayer.Add(_inspectorAdorner);
        InspectorVisibleChanged?.Invoke(true);
    }

    /// <summary>Opens or closes the visual tree window.</summary>
    public void ToggleVisualTree()
    {
        if (_visualTreeWindow != null)
        {
            try
            {
                _visualTreeWindow.Close();
            }
            catch { }
            _visualTreeWindow = null;
            VisualTreeOpenChanged?.Invoke(false);
            return;
        }

        // The tree window is much more useful with the overlay on (selection highlighting),
        // so ensure it's enabled.
        if (InspectorOverlay == null)
        {
            ToggleInspector();
        }

        var treeWindow = new DebugVisualTreeWindow(_window);
        _visualTreeWindow = treeWindow;

        treeWindow.Closed += () =>
        {
            if (ReferenceEquals(_visualTreeWindow, treeWindow))
            {
                _visualTreeWindow = null;
                VisualTreeOpenChanged?.Invoke(false);
            }

            if (InspectorOverlay != null)
            {
                InspectorOverlay.HighlightedElement = null;
                _window.RequestRender();
            }
        };

        _window.Closed += CloseTreeOnOwnerClose;
        void CloseTreeOnOwnerClose()
        {
            _window.Closed -= CloseTreeOnOwnerClose;
            try { _visualTreeWindow?.Close(); } catch { }
            _visualTreeWindow = null;
            VisualTreeOpenChanged?.Invoke(false);
        }

        treeWindow.Show();
        VisualTreeOpenChanged?.Invoke(true);
    }

    /// <summary>Shows or hides the frame statistics overlay.</summary>
    public void TogglePerformanceMonitor()
    {
        if (_performanceAdorner != null)
        {
            _window.AdornerLayer.Remove(_performanceAdorner);
            _performanceAdorner = null;
            _performanceOverlay = null;
            UpdateProfilerEnabled();
            _window.RequestUpdatePass();
            _window.RequestRender();
            PerformanceMonitorVisibleChanged?.Invoke(false);
            return;
        }

        _performanceOverlay = new DebugPerformanceOverlay(_window)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        _performanceAdorner = new Adorner(_window, _performanceOverlay)
        {
            IsHitTestVisible = false,
            IsVisible = true,
        };

        _window.AdornerLayer.Add(_performanceAdorner);
        UpdateProfilerEnabled();
        _window.RequestRender();
        PerformanceMonitorVisibleChanged?.Invoke(true);
    }

    /// <summary>Opens or closes the profiler timeline window.</summary>
    public void ToggleProfiler()
    {
        if (_profilerWindow != null)
        {
            try
            {
                _profilerWindow.Close();
            }
            catch { }
            _profilerWindow = null;
            UpdateProfilerEnabled();
            ProfilerOpenChanged?.Invoke(false);
            return;
        }

        var profilerWindow = new DebugProfilerWindow(_window);
        _profilerWindow = profilerWindow;
        UpdateProfilerEnabled();

        profilerWindow.Closed += () =>
        {
            if (ReferenceEquals(_profilerWindow, profilerWindow))
            {
                _profilerWindow = null;
                UpdateProfilerEnabled();
                ProfilerOpenChanged?.Invoke(false);
            }
        };

        _window.Closed += CloseProfilerOnOwnerClose;
        void CloseProfilerOnOwnerClose()
        {
            _window.Closed -= CloseProfilerOnOwnerClose;
            try { _profilerWindow?.Close(); } catch { }
            _profilerWindow = null;
            UpdateProfilerEnabled();
            ProfilerOpenChanged?.Invoke(false);
        }

        profilerWindow.Show();
        ProfilerOpenChanged?.Invoke(true);
    }

    internal void OnAfterMouseDownHitTest(Point positionInWindow, MouseButton button, UIElement? element)
        => _visualTreeWindow?.OnTargetMouseDown(positionInWindow, button, element);

    private void UpdateProfilerEnabled()
        => PerformanceProfiler.Instance.IsEnabled = _performanceAdorner != null || _profilerWindow != null;
}
