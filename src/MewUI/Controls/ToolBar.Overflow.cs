namespace Aprillz.MewUI.Controls;

public sealed partial class ToolBar
{
    /// <summary>
    /// The popup an overflow button opens. It holds the entry controls the band could not fit, borrowed from the
    /// toolbar for as long as it is open rather than rebuilt as menu rows, so an entry keeps its toggle
    /// state, its bindings and the handlers the application attached. Anything the band can show can be
    /// shown here, including entries with no command behind them.
    /// </summary>
    internal sealed class OverflowPopup
    {
        private readonly Popup _popup = new();
        private readonly Border _frame = new();
        private readonly WrapPanel _panel = new();
        private readonly List<Element> _borrowed = new();
        private readonly List<Separator> _separators = new();
        private int _separatorsUsed;
        private ToolBar? _owner;
        private ToolBarOverflowButton? _anchor;

        internal OverflowPopup()
        {
            _panel.Orientation = Orientation.Horizontal;
            _panel.Spacing = ENTRY_SPACING;
            _frame.Child = _panel;
            _frame.Padding = new Thickness(GROUP_PADDING);
            _popup.Content = _frame;
            _popup.Closed += (_, _) => ReturnBorrowed();
        }

        internal bool IsOpen => _popup.IsOpen;

        /// <summary>Raised once the borrowed controls belong to the toolbar again.</summary>
        internal event Action? Returned;

        /// <summary>Starts a new set of items. Anything the previous one borrowed goes back first.</summary>
        internal void Begin(ToolBar owner)
        {
            ReturnBorrowed();
            _owner = owner;

            var theme = owner.Theme;
            _frame.Background = theme.Palette.ContainerBackground;
            _frame.BorderBrush = theme.Palette.ControlBorder;
            _frame.BorderThickness = theme.Metrics.ControlBorderThickness;
            _frame.CornerRadius = theme.Metrics.ControlCornerRadius;

            // The band is one row, so the popup wraps rather than running off the window. Six entries is
            // what a group holds before it reads as a row of its own.
            _frame.MaxWidth = (theme.Metrics.BaseControlHeight * 6) + (GROUP_PADDING * 2);
        }

        /// <summary>Takes an entry control from the toolbar for as long as the popup is open.</summary>
        internal void Add(Element control)
        {
            _borrowed.Add(control);
            _panel.Add(control);
        }

        internal void AddSeparator()
        {
            if (_separatorsUsed == _separators.Count)
            {
                _separators.Add(new Separator());
            }

            _panel.Add(_separators[_separatorsUsed++]);
        }

        /// <summary>Whether anything was added since <see cref="Begin"/>.</summary>
        internal bool HasItems => _borrowed.Count > 0;

        /// <summary>What the popup is showing, entries and rules in the order they were added.</summary>
        internal IReadOnlyList<Element> Items => _panel.Children;

        internal void Show(ToolBarOverflowButton anchor)
        {
            _anchor = anchor;
            anchor.IsOverflowOpen = true;
            _popup.ShowAt(anchor, anchor.Bounds);
        }

        internal void Close() => _popup.Close();

        private void ReturnBorrowed()
        {
            bool hadAny = _borrowed.Count > 0;

            if (_anchor != null)
            {
                _anchor.IsOverflowOpen = false;
                _anchor = null;
            }

            foreach (var control in _borrowed)
            {
                _panel.Remove(control);
                control.Parent = _owner;
            }

            _borrowed.Clear();

            for (int i = 0; i < _separatorsUsed; i++)
            {
                _panel.Remove(_separators[i]);
            }

            _separatorsUsed = 0;

            if (hadAny)
            {
                Returned?.Invoke();
            }
        }
    }

    /// <summary>
    /// The button a group or a band shows for what it could not fit. It reports <see cref="
    /// VisualStateFlags.Active"/> while its popup is up, the same state a dropdown reports, so the face
    /// stays lit for as long as what it opened is on screen.
    /// </summary>
    internal sealed class ToolBarOverflowButton : Button
    {
        static ToolBarOverflowButton() { }

        private static readonly bool _defaultStyleRegistered =
            DefaultStyles.Register<ToolBarOverflowButton>(DefaultStyles.CreateToolBarOverflowButtonStyle);

        private static readonly MewPropertyKey<bool> IsOverflowOpenPropertyKey =
            MewProperty<bool>.RegisterReadOnly<ToolBarOverflowButton>(nameof(IsOverflowOpen), false,
                MewPropertyOptions.None,
                static (self, _, _) => self.InvalidateVisualState());

        /// <summary>Whether the popup this button opened is on screen.</summary>
        internal static readonly MewProperty<bool> IsOverflowOpenProperty = IsOverflowOpenPropertyKey.Property;

        /// <inheritdoc cref="IsOverflowOpenProperty"/>
        internal bool IsOverflowOpen
        {
            get => GetValue(IsOverflowOpenProperty);
            set => SetValue(IsOverflowOpenPropertyKey, value);
        }

        protected override VisualState ComputeVisualState()
        {
            var state = base.ComputeVisualState();
            if (IsOverflowOpen)
            {
                return state with { Flags = state.Flags | VisualStateFlags.Active };
            }

            return state;
        }
    }

    private static ToolBarOverflowButton CreateOverflowButton(ToolBar owner, Action opening)
    {
        var overflowButton = new ToolBarOverflowButton
        {
            Padding = new Thickness(0),
            MinWidth = OVERFLOW_BUTTON_WIDTH,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = false,
            IsTabStop = false,
            Content = new GlyphElement { Kind = GlyphKind.ChevronDown, GlyphSize = 3 },
        };
        overflowButton.Click += opening;
        overflowButton.Parent = owner;
        return overflowButton;
    }
}
