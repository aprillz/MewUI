namespace Aprillz.MewUI.Controls;

/// <summary>
/// A horizontal strip of mutually exclusive segments (iOS UISegmentedControl / WinUI Segmented style).
/// Single selection highlights the chosen segment with the accent color. Use for a small fixed set of
/// options (view-mode switches, filters) where a <see cref="ComboBox"/> is too indirect and a
/// <see cref="RadioButton"/> group too large. For independent segments (toolbar / toggle cluster) use
/// <see cref="ButtonGroup"/>.
/// </summary>
public sealed class SegmentedControl : SegmentedBase, ISelector, IIndexedSelector
{
    private bool _syncingSelection;

    public static readonly MewProperty<int> SelectedIndexProperty =
        MewProperty<int>.Register<SegmentedControl>(nameof(SelectedIndex), -1,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedIndexPropertyChanged(newVal));

    public static readonly MewProperty<object?> SelectedItemProperty =
        MewProperty<object?>.Register<SegmentedControl>(nameof(SelectedItem), null,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedItemPropertyChanged(newVal));

    static SegmentedControl()
    {
        FocusableProperty.OverrideDefaultValue<SegmentedControl>(true);
    }

    // Segments share an equal width; selection is a mutually exclusive choice.
    public SegmentedControl() : base(SegmentSizing.Uniform)
    {
    }

    /// <summary>Gets or sets the selected segment index (-1 means no selection).</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Gets the currently selected item object, or <see langword="null"/> when nothing is selected.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Gets the display text of the selected segment, or <see langword="null"/>.</summary>
    public string? SelectedText =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items.GetText(SelectedIndex) : null;

    /// <summary>Occurs when the selected segment changes.</summary>
    public event Action<object?>? SelectionChanged;

    protected override void OnSegmentClicked(int index)
    {
        SelectedIndex = index;

        if (FindVisualRoot() is Window window)
        {
            window.FocusManager.SetFocus(this, resolveDefault: false);
        }
    }

    protected override void OnItemsViewSelectionChanged(int index)
    {
        SyncSelectionProperties();
        UpdateSelectionVisuals();
        SelectionChanged?.Invoke(Items.SelectedItem);
        InvalidateVisual();
    }

    protected override void OnSegmentsRebuilt()
    {
        SyncSelectionProperties();
        UpdateSelectionVisuals();
    }

    private void OnSelectedIndexPropertyChanged(int newIndex)
    {
        if (_syncingSelection)
        {
            return;
        }

        // Reject selecting a disabled segment: revert to the view's current index.
        if (newIndex >= 0 && newIndex < Items.Count && !IsSegmentEnabled(newIndex))
        {
            SyncSelectionProperties();
            return;
        }

        _syncingSelection = true;
        try { Items.SelectedIndex = newIndex; }
        finally { _syncingSelection = false; }
        SyncSelectionProperties();
    }

    private void OnSelectedItemPropertyChanged(object? item)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try { Items.SelectedItem = item; }
        finally { _syncingSelection = false; }
        SyncSelectionProperties();
    }

    private void SyncSelectionProperties()
    {
        bool wasSyncing = _syncingSelection;
        _syncingSelection = true;
        try
        {
            SetValue(SelectedIndexProperty, Items.SelectedIndex);
            SetValue(SelectedItemProperty, Items.SelectedItem);
        }
        finally { _syncingSelection = wasSyncing; }
    }

    private void UpdateSelectionVisuals()
    {
        int selected = SelectedIndex;
        for (int i = 0; i < SegmentCount; i++)
        {
            if (SegmentAt(i) is SegmentButton btn)
            {
                btn.IsChecked = btn.Index == selected;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (Items.Count == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                SelectAdjacent(-1);
                e.Handled = true;
                break;
            case Key.Right:
                SelectAdjacent(+1);
                e.Handled = true;
                break;
            case Key.Home:
                SelectEdge(forward: true);
                e.Handled = true;
                break;
            case Key.End:
                SelectEdge(forward: false);
                e.Handled = true;
                break;
        }
    }

    private void SelectAdjacent(int direction)
    {
        int count = Items.Count;
        int i = SelectedIndex;
        while (true)
        {
            i += direction;
            if (i < 0 || i >= count)
            {
                return;
            }

            if (IsSegmentEnabled(i))
            {
                SelectedIndex = i;
                return;
            }
        }
    }

    private void SelectEdge(bool forward)
    {
        int count = Items.Count;
        if (forward)
        {
            for (int i = 0; i < count; i++)
            {
                if (IsSegmentEnabled(i))
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }
        else
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (IsSegmentEnabled(i))
                {
                    SelectedIndex = i;
                    return;
                }
            }
        }
    }

    protected override void OnVisualStateChanged(VisualState oldState, VisualState newState)
    {
        base.OnVisualStateChanged(oldState, newState);

        if (oldState.IsFocused == newState.IsFocused)
        {
            return;
        }

        for (int i = 0; i < SegmentCount; i++)
        {
            SegmentAt(i)?.RefreshOwnerState();
        }
    }
}
