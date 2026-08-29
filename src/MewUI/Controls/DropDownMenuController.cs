namespace Aprillz.MewUI.Controls;

/// <summary>
/// Owns the drop-down menu popup for a single control: the cached <see cref="ContextMenu"/>, the
/// open/close transitions, and the state synchronization with the owner's IsDropDownOpen property.
/// Shared by the drop-down button family, which differ in activation policy rather than in menu
/// lifecycle.
/// </summary>
internal sealed class DropDownMenuController
{
    private enum State
    {
        Closed,
        Opening,
        Open,
        Closing,
        Disposed,
    }

    private readonly Control _owner;
    private readonly Func<Menu?> _getMenu;
    private readonly Func<double> _getMaxHeight;
    private readonly Action _raiseOpening;
    private readonly Action _raiseClosed;
    private readonly Action<bool> _setIsOpen;

    private ContextMenu? _menuPopup;
    private Menu? _menuModel;
    private ContextMenu? _openMenu;
    private State _state = State.Closed;

    public DropDownMenuController(
        Control owner,
        Func<Menu?> getMenu,
        Func<double> getMaxHeight,
        Action raiseOpening,
        Action raiseClosed,
        Action<bool> setIsOpen)
    {
        _owner = owner;
        _getMenu = getMenu;
        _getMaxHeight = getMaxHeight;
        _raiseOpening = raiseOpening;
        _raiseClosed = raiseClosed;
        _setIsOpen = setIsOpen;
    }

    /// <summary>
    /// True while this controller writes the owner's open state, so the owner's property callback
    /// can skip re-entering a transition that is already running.
    /// </summary>
    public bool IsSynchronizing { get; private set; }

    public bool IsOpen => _state == State.Open;

    /// <summary>
    /// Opens the menu. Leaves the owner's state false when there is no window, no menu, or the
    /// opening handler left the menu empty.
    /// </summary>
    public void Open()
    {
        if (_state is State.Disposed or State.Open or State.Opening)
        {
            return;
        }

        _state = State.Opening;

        _raiseOpening();

        // The handler may have swapped the model, so the property is read after the event.
        var model = _getMenu();
        if (model == null || model.Items.Count == 0 || _owner.FindVisualRoot() is not Window window)
        {
            _state = State.Closed;
            SetIsOpen(false);
            return;
        }

        var popup = EnsurePopup(model);
        double maxHeight = _getMaxHeight();
        if (double.IsFinite(maxHeight) && maxHeight > 0)
        {
            popup.MaxMenuHeight = maxHeight;
        }

        // Recorded before showing: the show path can run close callbacks, and those compare against
        // this reference to tell the current menu from a superseded one.
        _openMenu = popup;
        _state = State.Open;
        SetIsOpen(true);

        popup.Placement = MenuPlacement.Below;
        popup.Show(_owner);
    }

    public void Close()
    {
        if (_state is State.Disposed or State.Closed or State.Closing)
        {
            return;
        }

        var popup = _openMenu;
        _state = State.Closing;

        if (popup != null && _owner.FindVisualRoot() is Window window)
        {
            window.ClosePopup(popup);
        }

        // The popup notification finishes the transition; do it here when the popup was never
        // attached or its window is already gone.
        if (_state == State.Closing)
        {
            FinishClose();
        }
    }

    /// <summary>
    /// Handles the owner's popup close notification. Ignores a late callback from a menu that has
    /// already been superseded.
    /// </summary>
    public void OnPopupClosed(UIElement popup)
    {
        if (_state == State.Disposed || !ReferenceEquals(popup, _openMenu))
        {
            return;
        }

        FinishClose();
    }

    /// <summary>Drops the cached popup after the menu model changed, closing it when open.</summary>
    public void OnMenuChanged()
    {
        if (_state == State.Disposed)
        {
            return;
        }

        Close();
        DisposePopup();
    }

    public void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        // A cached popup can exist while closed (Parent == null), so it gets no Window broadcast.
        if (_menuPopup is FrameworkElement popup && popup.Parent == null)
        {
            popup.NotifyThemeChanged(oldTheme, newTheme);
        }
    }

    public void Dispose()
    {
        if (_state == State.Disposed)
        {
            return;
        }

        Close();
        DisposePopup();
        _state = State.Disposed;
    }

    private void FinishClose()
    {
        _openMenu = null;
        _state = State.Closed;
        SetIsOpen(false);
        _raiseClosed();
    }

    private ContextMenu EnsurePopup(Menu model)
    {
        if (_menuPopup != null && ReferenceEquals(_menuModel, model))
        {
            return _menuPopup;
        }

        DisposePopup();
        _menuModel = model;
        _menuPopup = new ContextMenu(model);
        return _menuPopup;
    }

    private void DisposePopup()
    {
        _menuPopup?.Dispose();
        _menuPopup = null;
        _menuModel = null;
    }

    private void SetIsOpen(bool value)
    {
        IsSynchronizing = true;
        try
        {
            _setIsOpen(value);
        }
        finally
        {
            IsSynchronizing = false;
        }
    }
}
