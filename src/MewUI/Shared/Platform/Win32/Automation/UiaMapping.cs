
using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Platform.Win32.Automation;

/// <summary>Translates the automation tree's roles, states, and values to their UI Automation identifiers.</summary>
internal static class UiaMapping
{
    internal static int ControlTypeId(AutomationControlType type) => type switch
    {
        AutomationControlType.Button => UiaNative.UIA_BUTTON_CONTROL_TYPE_ID,
        AutomationControlType.CheckBox => UiaNative.UIA_CHECK_BOX_CONTROL_TYPE_ID,
        AutomationControlType.RadioButton => UiaNative.UIA_RADIO_BUTTON_CONTROL_TYPE_ID,
        AutomationControlType.ComboBox => UiaNative.UIA_COMBO_BOX_CONTROL_TYPE_ID,
        AutomationControlType.Edit => UiaNative.UIA_EDIT_CONTROL_TYPE_ID,
        AutomationControlType.Text => UiaNative.UIA_TEXT_CONTROL_TYPE_ID,
        AutomationControlType.Hyperlink => UiaNative.UIA_HYPERLINK_CONTROL_TYPE_ID,
        AutomationControlType.Image => UiaNative.UIA_IMAGE_CONTROL_TYPE_ID,
        AutomationControlType.List => UiaNative.UIA_LIST_CONTROL_TYPE_ID,
        AutomationControlType.ListItem => UiaNative.UIA_LIST_ITEM_CONTROL_TYPE_ID,
        AutomationControlType.Tree => UiaNative.UIA_TREE_CONTROL_TYPE_ID,
        AutomationControlType.TreeItem => UiaNative.UIA_TREE_ITEM_CONTROL_TYPE_ID,
        AutomationControlType.Tab => UiaNative.UIA_TAB_CONTROL_TYPE_ID,
        AutomationControlType.TabItem => UiaNative.UIA_TAB_ITEM_CONTROL_TYPE_ID,
        AutomationControlType.Menu => UiaNative.UIA_MENU_CONTROL_TYPE_ID,
        AutomationControlType.MenuBar => UiaNative.UIA_MENU_BAR_CONTROL_TYPE_ID,
        AutomationControlType.MenuItem => UiaNative.UIA_MENU_ITEM_CONTROL_TYPE_ID,
        AutomationControlType.Slider => UiaNative.UIA_SLIDER_CONTROL_TYPE_ID,
        AutomationControlType.Spinner => UiaNative.UIA_SPINNER_CONTROL_TYPE_ID,
        AutomationControlType.ProgressBar => UiaNative.UIA_PROGRESS_BAR_CONTROL_TYPE_ID,
        AutomationControlType.ScrollBar => UiaNative.UIA_SCROLL_BAR_CONTROL_TYPE_ID,
        AutomationControlType.Group => UiaNative.UIA_GROUP_CONTROL_TYPE_ID,
        AutomationControlType.Pane => UiaNative.UIA_PANE_CONTROL_TYPE_ID,
        AutomationControlType.Window => UiaNative.UIA_WINDOW_CONTROL_TYPE_ID,
        AutomationControlType.ToolBar => UiaNative.UIA_TOOL_BAR_CONTROL_TYPE_ID,
        AutomationControlType.ToolTip => UiaNative.UIA_TOOL_TIP_CONTROL_TYPE_ID,
        AutomationControlType.Separator => UiaNative.UIA_SEPARATOR_CONTROL_TYPE_ID,
        AutomationControlType.DataGrid => UiaNative.UIA_DATA_GRID_CONTROL_TYPE_ID,
        AutomationControlType.DataItem => UiaNative.UIA_DATA_ITEM_CONTROL_TYPE_ID,
        AutomationControlType.Header => UiaNative.UIA_HEADER_CONTROL_TYPE_ID,
        AutomationControlType.HeaderItem => UiaNative.UIA_HEADER_ITEM_CONTROL_TYPE_ID,
        AutomationControlType.Calendar => UiaNative.UIA_CALENDAR_CONTROL_TYPE_ID,
        _ => UiaNative.UIA_CUSTOM_CONTROL_TYPE_ID,
    };

    /// <summary>The value of the UI Automation ToggleState enumeration.</summary>
    internal static int ToggleStateValue(ToggleState state) => state switch
    {
        ToggleState.On => 1,
        ToggleState.Indeterminate => 2,
        _ => 0,
    };

    /// <summary>The value of the UI Automation ExpandCollapseState enumeration.</summary>
    internal static int ExpandCollapseStateValue(ExpandCollapseState state) => state switch
    {
        ExpandCollapseState.Expanded => 1,
        ExpandCollapseState.PartiallyExpanded => 2,
        ExpandCollapseState.LeafNode => 3,
        _ => 0,
    };

    /// <summary>The amount a value of the UI Automation ScrollAmount enumeration names.</summary>
    internal static ScrollAmount ScrollAmountOf(int value) => value switch
    {
        0 => ScrollAmount.LargeDecrement,
        1 => ScrollAmount.SmallDecrement,
        3 => ScrollAmount.LargeIncrement,
        4 => ScrollAmount.SmallIncrement,
        _ => ScrollAmount.NoAmount,
    };

    internal static int EventId(AutomationEventKind kind) => kind switch
    {
        AutomationEventKind.MenuOpened => UiaNative.UIA_MENU_OPENED_EVENT_ID,
        AutomationEventKind.MenuClosed => UiaNative.UIA_MENU_CLOSED_EVENT_ID,
        AutomationEventKind.ToolTipOpened => UiaNative.UIA_TOOL_TIP_OPENED_EVENT_ID,
        AutomationEventKind.ToolTipClosed => UiaNative.UIA_TOOL_TIP_CLOSED_EVENT_ID,
        AutomationEventKind.TextChanged => UiaNative.UIA_TEXT_CHANGED_EVENT_ID,
        _ => UiaNative.UIA_TEXT_SELECTION_CHANGED_EVENT_ID,
    };

    /// <summary>The UI Automation property a change of <paramref name="property"/> on <paramref name="peer"/> announces, or 0.</summary>
    internal static int PropertyId(AutomationPeer peer, AutomationProperty property)
    {
        if (property == AutomationProperty.NameProperty)
        {
            return UiaNative.UIA_NAME_PROPERTY_ID;
        }
        else if (property == AutomationProperty.HelpTextProperty)
        {
            return UiaNative.UIA_HELP_TEXT_PROPERTY_ID;
        }
        else if (property == AutomationProperty.IsEnabledProperty)
        {
            return UiaNative.UIA_IS_ENABLED_PROPERTY_ID;
        }
        else if (property == AutomationProperty.IsOffscreenProperty)
        {
            return UiaNative.UIA_IS_OFFSCREEN_PROPERTY_ID;
        }
        else if (property == AutomationProperty.HasKeyboardFocusProperty)
        {
            return UiaNative.UIA_HAS_KEYBOARD_FOCUS_PROPERTY_ID;
        }
        else if (property == AutomationProperty.ValueProperty)
        {
            return UiaNative.UIA_VALUE_VALUE_PROPERTY_ID;
        }
        else if (property == AutomationProperty.IsReadOnlyProperty)
        {
            return peer is IRangeValueProvider
                ? UiaNative.UIA_RANGE_VALUE_IS_READ_ONLY_PROPERTY_ID
                : UiaNative.UIA_VALUE_IS_READ_ONLY_PROPERTY_ID;
        }
        else if (property == AutomationProperty.RangeValueProperty)
        {
            return UiaNative.UIA_RANGE_VALUE_VALUE_PROPERTY_ID;
        }
        else if (property == AutomationProperty.RangeMinimumProperty)
        {
            return UiaNative.UIA_RANGE_VALUE_MINIMUM_PROPERTY_ID;
        }
        else if (property == AutomationProperty.RangeMaximumProperty)
        {
            return UiaNative.UIA_RANGE_VALUE_MAXIMUM_PROPERTY_ID;
        }
        else if (property == AutomationProperty.ToggleStateProperty)
        {
            return UiaNative.UIA_TOGGLE_STATE_PROPERTY_ID;
        }
        else if (property == AutomationProperty.ExpandCollapseStateProperty)
        {
            return UiaNative.UIA_EXPAND_COLLAPSE_STATE_PROPERTY_ID;
        }
        else if (property == AutomationProperty.IsSelectedProperty)
        {
            return UiaNative.UIA_SELECTION_ITEM_IS_SELECTED_PROPERTY_ID;
        }
        else
        {
            return 0;
        }
    }

    /// <summary>A VARIANT holding <paramref name="value"/>, which the caller disposes; empty for a value it cannot hold.</summary>
    internal static UiaVariant VariantOf(object? value) => value switch
    {
        string text => UiaVariant.Create(text),
        bool flag => UiaVariant.Create(flag),
        double number => UiaVariant.Create(number),
        int integer => UiaVariant.Create(integer),
        ToggleState state => UiaVariant.Create(ToggleStateValue(state)),
        ExpandCollapseState state => UiaVariant.Create(ExpandCollapseStateValue(state)),
        _ => default,
    };
}
