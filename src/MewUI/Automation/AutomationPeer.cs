using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// Describes one element to assistive technology and automation clients: its role, name, state, position, its
/// place in the automation tree, and the patterns it supports. A peer supports a pattern by implementing its
/// interface, such as <see cref="IInvokeProvider"/>.
/// </summary>
/// <remarks>Peers are used on the UI thread only.</remarks>
public abstract class AutomationPeer
{
    public AutomationControlType GetControlType() => GetControlTypeCore();

    /// <summary>The name a reader announces; empty when the element has none.</summary>
    public string GetName() => GetNameCore();

    public string GetAutomationId() => GetAutomationIdCore();

    public string GetHelpText() => GetHelpTextCore();

    public bool IsEnabled() => IsEnabledCore();

    /// <summary>True when the element cannot be seen: it or an ancestor is hidden, or it has no area.</summary>
    public bool IsOffscreen() => IsOffscreenCore();

    public bool IsKeyboardFocusable() => IsKeyboardFocusableCore();

    public bool HasKeyboardFocus() => HasKeyboardFocusCore();

    /// <summary>The element's bounds in its window's client area, in DIPs.</summary>
    public Rect GetBoundingRectangle() => GetBoundingRectangleCore();

    /// <summary>The window whose client area <see cref="GetBoundingRectangle"/> is measured in.</summary>
    public Window? GetWindow() => GetWindowCore();

    public AutomationPeer? GetParent() => GetParentCore();

    /// <summary>The peers directly below this one, in reading order.</summary>
    public IReadOnlyList<AutomationPeer> GetChildren() => GetChildrenCore();

    public void SetFocus() => SetFocusCore();

    /// <summary>The pattern <typeparamref name="T"/> this peer supports, or null.</summary>
    public T? GetProvider<T>() where T : class => this as T;

    protected abstract AutomationControlType GetControlTypeCore();

    protected abstract string GetNameCore();

    protected abstract string GetAutomationIdCore();

    protected abstract string GetHelpTextCore();

    protected abstract bool IsEnabledCore();

    protected abstract bool IsOffscreenCore();

    protected abstract bool IsKeyboardFocusableCore();

    protected abstract bool HasKeyboardFocusCore();

    protected abstract Rect GetBoundingRectangleCore();

    protected abstract Window? GetWindowCore();

    protected abstract AutomationPeer? GetParentCore();

    protected abstract IReadOnlyList<AutomationPeer> GetChildrenCore();

    protected abstract void SetFocusCore();

    /// <summary>Announces a change of <paramref name="property"/> to automation clients.</summary>
    protected void RaisePropertyChangedEvent(AutomationProperty property, object? oldValue, object? newValue)
    {
        if (AutomationEvents.HasSinks)
        {
            AutomationEvents.RaisePropertyChanged(this, property, oldValue, newValue);
        }
    }

    /// <summary>Announces that this peer's children changed.</summary>
    protected void RaiseStructureChangedEvent()
    {
        if (AutomationEvents.HasSinks)
        {
            AutomationEvents.RaiseStructureChanged(this);
        }
    }

    /// <summary>Announces that this peer's element received keyboard focus.</summary>
    protected void RaiseFocusChangedEvent()
    {
        if (AutomationEvents.HasSinks)
        {
            AutomationEvents.RaiseFocusChanged(this);
        }
    }

    internal virtual void NotifyOwnerPropertyChanged(MewProperty property)
    {
    }

    internal void RaiseStructureChangedFromTree() => RaiseStructureChangedEvent();
}
