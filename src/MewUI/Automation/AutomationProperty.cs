namespace Aprillz.MewUI.Automation;

/// <summary>Identifies a piece of peer state whose change is announced to automation clients.</summary>
public sealed class AutomationProperty
{
    private AutomationProperty(string name) => Name = name;

    /// <summary>The property's name, for diagnostics.</summary>
    public string Name { get; }

    public static AutomationProperty NameProperty { get; } = new("Name");

    public static AutomationProperty HelpTextProperty { get; } = new("HelpText");

    public static AutomationProperty IsEnabledProperty { get; } = new("IsEnabled");

    public static AutomationProperty IsOffscreenProperty { get; } = new("IsOffscreen");

    public static AutomationProperty HasKeyboardFocusProperty { get; } = new("HasKeyboardFocus");

    public static AutomationProperty BoundingRectangleProperty { get; } = new("BoundingRectangle");

    public static AutomationProperty ValueProperty { get; } = new("Value");

    public static AutomationProperty IsReadOnlyProperty { get; } = new("IsReadOnly");

    public static AutomationProperty RangeValueProperty { get; } = new("RangeValue");

    public static AutomationProperty RangeMinimumProperty { get; } = new("RangeMinimum");

    public static AutomationProperty RangeMaximumProperty { get; } = new("RangeMaximum");

    public static AutomationProperty ToggleStateProperty { get; } = new("ToggleState");

    public static AutomationProperty ExpandCollapseStateProperty { get; } = new("ExpandCollapseState");

    public static AutomationProperty IsSelectedProperty { get; } = new("IsSelected");

    public static AutomationProperty SelectionProperty { get; } = new("Selection");

    public static AutomationProperty ScrollPercentProperty { get; } = new("ScrollPercent");

    public override string ToString() => Name;
}
