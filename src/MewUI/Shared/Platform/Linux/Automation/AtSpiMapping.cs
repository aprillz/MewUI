using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform.Linux.Automation;

/// <summary>Translates the automation tree's roles and states to AT-SPI2 roles and state bits (atspi-constants.h).</summary>
internal static class AtSpiMapping
{
    internal const uint ROLE_CALENDAR = 5;
    internal const uint ROLE_CHECK_BOX = 7;
    internal const uint ROLE_CHECK_MENU_ITEM = 8;
    internal const uint ROLE_COLUMN_HEADER = 10;
    internal const uint ROLE_COMBO_BOX = 11;
    internal const uint ROLE_FRAME = 23;
    internal const uint ROLE_IMAGE = 27;
    internal const uint ROLE_LABEL = 29;
    internal const uint ROLE_LIST = 31;
    internal const uint ROLE_LIST_ITEM = 32;
    internal const uint ROLE_MENU = 33;
    internal const uint ROLE_MENU_BAR = 34;
    internal const uint ROLE_MENU_ITEM = 35;
    internal const uint ROLE_PAGE_TAB = 37;
    internal const uint ROLE_PAGE_TAB_LIST = 38;
    internal const uint ROLE_PANEL = 39;
    internal const uint ROLE_PASSWORD_TEXT = 40;
    internal const uint ROLE_PROGRESS_BAR = 42;
    internal const uint ROLE_PUSH_BUTTON = 43;
    internal const uint ROLE_RADIO_BUTTON = 44;
    internal const uint ROLE_SCROLL_BAR = 48;
    internal const uint ROLE_SCROLL_PANE = 49;
    internal const uint ROLE_SEPARATOR = 50;
    internal const uint ROLE_SLIDER = 51;
    internal const uint ROLE_SPIN_BUTTON = 52;
    internal const uint ROLE_TABLE = 55;
    internal const uint ROLE_TOGGLE_BUTTON = 62;
    internal const uint ROLE_TOOL_BAR = 63;
    internal const uint ROLE_TOOL_TIP = 64;
    internal const uint ROLE_TREE = 65;
    internal const uint ROLE_UNKNOWN = 67;
    internal const uint ROLE_WINDOW = 69;
    internal const uint ROLE_HEADER = 71;
    internal const uint ROLE_APPLICATION = 75;
    internal const uint ROLE_ENTRY = 79;
    internal const uint ROLE_LINK = 88;
    internal const uint ROLE_TABLE_ROW = 90;
    internal const uint ROLE_TREE_ITEM = 91;

    internal const int STATE_ACTIVE = 1;
    internal const int STATE_CHECKED = 4;
    internal const int STATE_COLLAPSED = 5;
    internal const int STATE_EDITABLE = 7;
    internal const int STATE_ENABLED = 8;
    internal const int STATE_EXPANDABLE = 9;
    internal const int STATE_EXPANDED = 10;
    internal const int STATE_FOCUSABLE = 11;
    internal const int STATE_FOCUSED = 12;
    internal const int STATE_MULTI_LINE = 17;
    internal const int STATE_MULTISELECTABLE = 18;
    internal const int STATE_SELECTABLE = 22;
    internal const int STATE_SELECTED = 23;
    internal const int STATE_SENSITIVE = 24;
    internal const int STATE_SHOWING = 25;
    internal const int STATE_SINGLE_LINE = 26;
    internal const int STATE_VISIBLE = 30;
    internal const int STATE_INDETERMINATE = 32;
    internal const int STATE_CHECKABLE = 41;
    internal const int STATE_HAS_POPUP = 42;
    internal const int STATE_READ_ONLY = 43;

    internal static uint Role(AutomationPeer peer)
    {
        switch (peer.GetControlType())
        {
            case AutomationControlType.Button:
                return peer is IToggleProvider ? ROLE_TOGGLE_BUTTON : ROLE_PUSH_BUTTON;
            case AutomationControlType.CheckBox:
                return ROLE_CHECK_BOX;
            case AutomationControlType.RadioButton:
                return ROLE_RADIO_BUTTON;
            case AutomationControlType.ComboBox:
                return ROLE_COMBO_BOX;
            case AutomationControlType.Edit:
                return OwnerOf(peer) is PasswordBox ? ROLE_PASSWORD_TEXT : ROLE_ENTRY;
            case AutomationControlType.Text:
                return ROLE_LABEL;
            case AutomationControlType.Hyperlink:
                return ROLE_LINK;
            case AutomationControlType.Image:
                return ROLE_IMAGE;
            case AutomationControlType.List:
                return ROLE_LIST;
            case AutomationControlType.ListItem:
                return ROLE_LIST_ITEM;
            case AutomationControlType.Tree:
                return ROLE_TREE;
            case AutomationControlType.TreeItem:
                return ROLE_TREE_ITEM;
            case AutomationControlType.Tab:
                return ROLE_PAGE_TAB_LIST;
            case AutomationControlType.TabItem:
                return ROLE_PAGE_TAB;
            case AutomationControlType.Menu:
                return ROLE_MENU;
            case AutomationControlType.MenuBar:
                return ROLE_MENU_BAR;
            case AutomationControlType.MenuItem:
                return peer is IToggleProvider ? ROLE_CHECK_MENU_ITEM : ROLE_MENU_ITEM;
            case AutomationControlType.Slider:
                return ROLE_SLIDER;
            case AutomationControlType.Spinner:
                return ROLE_SPIN_BUTTON;
            case AutomationControlType.ProgressBar:
                return ROLE_PROGRESS_BAR;
            case AutomationControlType.ScrollBar:
                return ROLE_SCROLL_BAR;
            case AutomationControlType.Pane:
                return OwnerOf(peer) is ScrollViewer ? ROLE_SCROLL_PANE : ROLE_PANEL;
            case AutomationControlType.Window:
                return ROLE_FRAME;
            case AutomationControlType.ToolBar:
                return ROLE_TOOL_BAR;
            case AutomationControlType.ToolTip:
                return ROLE_TOOL_TIP;
            case AutomationControlType.Separator:
                return ROLE_SEPARATOR;
            case AutomationControlType.DataGrid:
                return ROLE_TABLE;
            case AutomationControlType.DataItem:
                return ROLE_TABLE_ROW;
            case AutomationControlType.Header:
                return ROLE_HEADER;
            case AutomationControlType.HeaderItem:
                return ROLE_COLUMN_HEADER;
            case AutomationControlType.Calendar:
                return ROLE_CALENDAR;
            case AutomationControlType.Group:
                return ROLE_PANEL;
            default:
                return ROLE_UNKNOWN;
        }
    }

    /// <summary>The state set as the two 32-bit words AT-SPI2 sends.</summary>
    internal static (uint Low, uint High) States(AutomationPeer peer, Window? surface)
    {
        ulong bits = 0;
        void Set(int state) => bits |= 1UL << state;

        if (peer.IsEnabled())
        {
            Set(STATE_ENABLED);
            Set(STATE_SENSITIVE);
        }

        if (!peer.IsOffscreen())
        {
            Set(STATE_VISIBLE);
            Set(STATE_SHOWING);
        }

        if (peer.IsKeyboardFocusable())
        {
            Set(STATE_FOCUSABLE);
        }

        if (peer.HasKeyboardFocus())
        {
            Set(STATE_FOCUSED);
        }

        if (peer is WindowAutomationPeer && surface != null && surface.IsActive)
        {
            Set(STATE_ACTIVE);
        }

        if (peer is IToggleProvider toggle)
        {
            Set(STATE_CHECKABLE);
            if (toggle.ToggleState == ToggleState.On)
            {
                Set(STATE_CHECKED);
            }
            else if (toggle.ToggleState == ToggleState.Indeterminate)
            {
                Set(STATE_INDETERMINATE);
            }
        }

        if (peer is ISelectionItemProvider item)
        {
            Set(STATE_SELECTABLE);
            if (item.IsSelected)
            {
                Set(STATE_SELECTED);
                if (peer.GetControlType() == AutomationControlType.RadioButton)
                {
                    Set(STATE_CHECKED);
                }
            }
        }

        if (peer is ISelectionProvider selection && selection.CanSelectMultiple)
        {
            Set(STATE_MULTISELECTABLE);
        }

        if (peer is IExpandCollapseProvider expander && expander.ExpandCollapseState != ExpandCollapseState.LeafNode)
        {
            Set(STATE_EXPANDABLE);
            if (peer.GetControlType() == AutomationControlType.ComboBox)
            {
                Set(STATE_HAS_POPUP);
            }

            Set(expander.ExpandCollapseState == ExpandCollapseState.Expanded ? STATE_EXPANDED : STATE_COLLAPSED);
        }

        if (peer is IValueProvider value)
        {
            if (value.IsReadOnly)
            {
                Set(STATE_READ_ONLY);
            }
            else
            {
                Set(STATE_EDITABLE);
            }

            Set(OwnerOf(peer) is MultiLineTextBox ? STATE_MULTI_LINE : STATE_SINGLE_LINE);
        }

        return ((uint)(bits & 0xFFFFFFFF), (uint)(bits >> 32));
    }

    private static UIElement? OwnerOf(AutomationPeer peer) => peer is UIElementAutomationPeer elementPeer ? elementPeer.Owner : null;
}
