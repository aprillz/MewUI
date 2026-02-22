using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Manages deferred keyboard-Tab focus movement inside virtualized item hosts.
/// When an item is scrolled into view it may not yet be realized; this helper
/// posts repeated render-priority callbacks until the target container appears,
/// then calls <see cref="FocusManager.FindFirstFocusable"/> and sets focus.
/// </summary>
internal sealed class PendingTabFocusHelper
{
    private const int MaxAttempts = 4;

    private readonly Func<Window?> _getWindow;
    private readonly Func<int, FrameworkElement?> _getContainer;

    private int _index = -1;
    private int _attempts;

    /// <param name="getWindow">Returns the host's current visual-root window, or null if not in a window.</param>
    /// <param name="getContainer">Returns the realized container for the given item index, or null if not yet realized.</param>
    public PendingTabFocusHelper(Func<Window?> getWindow, Func<int, FrameworkElement?> getContainer)
    {
        _getWindow = getWindow ?? throw new ArgumentNullException(nameof(getWindow));
        _getContainer = getContainer ?? throw new ArgumentNullException(nameof(getContainer));
    }

    /// <summary>
    /// Schedules focus to be moved to the container at <paramref name="index"/> once it is realized.
    /// </summary>
    public void Schedule(int index)
    {
        _index = index;
        _attempts = 0;
        PostApply();
    }

    private void PostApply()
    {
        if (_index < 0)
        {
            return;
        }

        var window = _getWindow();
        if (window == null)
        {
            return;
        }

        window.ApplicationDispatcher?.Post(Apply, UiDispatcherPriority.Render);
    }

    private void Apply()
    {
        if (_index < 0)
        {
            return;
        }

        var window = _getWindow();
        if (window == null)
        {
            _index = -1;
            return;
        }

        var container = _getContainer(_index);
        if (container == null)
        {
            if (_attempts++ < MaxAttempts)
            {
                window.ApplicationDispatcher?.Post(Apply, UiDispatcherPriority.Render);
            }
            else
            {
                _index = -1;
            }
            return;
        }

        var target = FocusManager.FindFirstFocusable(container);
        if (target != null)
        {
            window.FocusManager.SetFocus(target);
        }

        _index = -1;
    }
}
