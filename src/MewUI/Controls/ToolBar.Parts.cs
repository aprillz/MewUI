using System.Collections;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed partial class ToolBar
{
    /// <summary>
    /// The plate behind one group. Toolbar chrome rather than a part an application supplies: its
    /// rectangle is the group's own, which is what a reorder drop lands against. It is an element with its
    /// own default style rather than something the toolbar paints, so a theme reaches its background and
    /// corners.
    /// </summary>
    internal sealed class ToolBarGroupPlate : Control
    {
        static ToolBarGroupPlate() { }

        private static readonly bool _defaultStyleRegistered =
            DefaultStyles.Register<ToolBarGroupPlate>(DefaultStyles.CreateToolBarGroupPlateStyle);

        // A press that misses an entry belongs to the group under it: that is what starts a reorder drag.
        internal ToolBarGroupPlate() => IsHitTestVisible = false;

        protected override void OnRender(IGraphicsContext context)
            => DrawBackgroundAndBorder(context, GetSnappedBorderBounds(Bounds), Background, BorderBrush,
                BorderThickness, CornerRadius);
    }

    /// <summary>The handle at the left of a group, and the only thing a drag starts from.</summary>
    internal sealed class ToolBarGripElement : FrameworkElement
    {
        internal const double GRIP_WIDTH = 8;

        private const double DOT_SPACING = 3;

        internal ToolBarGripElement()
        {
            // The four-directional cursor, because a group moves along both axes: to another place on its
            // band and to another band. Hand would say the grip is something to click.
            Cursor = CursorType.SizeAll;
        }

        protected override Size MeasureContent(Size availableSize) => new(GRIP_WIDTH, 0);

        protected override void OnRender(IGraphicsContext context)
        {
            double dpiScale = GetDpi() / 96.0;
            double dot = LayoutRounding.SnapThicknessToPixels(2, dpiScale, 2);
            double x = LayoutRounding.RoundToPixel(Bounds.X + ((Bounds.Width - dot) / 2), dpiScale);
            var color = Theme.Palette.ControlBorder;

            for (double y = Bounds.Y + 4; y + dot <= Bounds.Bottom - 4; y += DOT_SPACING + dot)
            {
                context.FillRectangle(new Rect(x, LayoutRounding.RoundToPixel(y, dpiScale), dot, dot), color);
            }
        }
    }

    /// <summary>
    /// One group's visuals: the plate it is drawn on, the grip that drags it, a control per entry, and the
    /// overflow button that offers the entries the band could not fit. The button is the group's own, so a group
    /// that has given up every entry is still a plate with a grip on the band: it can be dragged to
    /// another band, and what it holds is still reachable.
    /// </summary>
    internal sealed class GroupVisual
    {
        private readonly List<Element> _entries = new();
        private readonly List<double> _entryWidths = new();
        private readonly OverflowPopup _overflow = new();
        private ToolBarOverflowButton? _overflowButton;

        internal GroupVisual(ToolBarGroup group) => Group = group;

        internal ToolBarGroup Group { get; }

        internal ToolBarGroupPlate Plate { get; } = new();

        internal ToolBarGripElement Grip { get; } = new();

        /// <summary>The button offering the entries the band could not fit.</summary>
        internal ToolBarOverflowButton OverflowButton => _overflowButton!;

        /// <summary>The popup that button opens.</summary>
        internal OverflowPopup OverflowContent => _overflow;

        /// <summary>
        /// Whether a popup currently holds this group's entries. Neither the band's measure nor its arrange
        /// touches them while it does: they answer to whatever popup they sit in.
        /// </summary>
        internal bool EntriesOnLoan => _overflow.IsOpen || _onLoanToBand;

        /// <summary>Records that the band's own popup took this group's entries.</summary>
        internal void SetOnLoanToBand(bool onLoan) => _onLoanToBand = onLoan;

        private bool _onLoanToBand;

        internal IReadOnlyList<Element> Entries => _entries;

        /// <summary>The plate's rectangle, which is the whole of the group on screen.</summary>
        internal Rect Bounds => Plate.Bounds;

        internal bool IsHidden { get; private set; }

        /// <summary>How many of the group's entries the last arrange put on the band.</summary>
        internal int VisibleEntryCount { get; private set; }

        /// <summary>Whether the group is on the band but not all of its entries are.</summary>
        internal bool IsTruncated => !IsHidden && VisibleEntryCount < _entries.Count;

        internal void Build(ToolBar owner)
        {
            Plate.Parent = owner;
            Grip.Parent = owner;
            Grip.MouseDown += args => owner.OnGripPressed(this, args);
            _overflowButton = CreateOverflowButton(owner, () => OpenOverflow(owner));

            foreach (var entry in Group.ItemsInternal)
            {
                var control = owner.CreateEntryControl(entry);
                control.Parent = owner;
                _entries.Add(control);
            }
        }

        internal void Detach(ToolBar owner)
        {
            // Returns whatever the popup borrowed, so the entries are the toolbar's again to release.
            _overflow.Close();

            Release(Plate, owner);
            Release(Grip, owner);
            if (_overflowButton != null)
            {
                Release(_overflowButton, owner);
            }

            foreach (var entry in _entries)
            {
                Release(entry, owner);
            }

            _entries.Clear();
        }

        private static void Release(Element element, ToolBar owner)
        {
            if (ReferenceEquals(element.Parent, owner))
            {
                element.Parent = null;
            }
        }

        /// <summary>
        /// Hands the entries past the band's last one to the popup. They are the controls the group
        /// already built, so what the popup shows is what the band would have shown.
        /// </summary>
        internal void OpenOverflow(ToolBar owner)
        {
            if (_overflow.IsOpen)
            {
                _overflow.Close();
                return;
            }

            _overflow.Begin(owner);
            for (int i = VisibleEntryCount; i < _entries.Count; i++)
            {
                _overflow.Add(_entries[i]);
            }

            if (_overflow.HasItems)
            {
                _overflow.Show(_overflowButton!);
            }
        }

        /// <summary>Measures the parts and returns the plate width the whole group needs.</summary>
        internal double Measure(double entryHeight, double padding, double spacing, bool showGrip)
        {
            if (showGrip)
            {
                Grip.Measure(new Size(ToolBarGripElement.GRIP_WIDTH, entryHeight));
            }

            _overflowButton!.Measure(new Size(double.PositiveInfinity, entryHeight));

            _entryWidths.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                // An entry a popup is holding keeps the width it was last measured at. Measuring it against
                // the band would overwrite the size the popup arranged it from.
                bool held = _onLoanToBand || (_overflow.IsOpen && i >= VisibleEntryCount);
                if (!held)
                {
                    _entries[i].Measure(new Size(double.PositiveInfinity, entryHeight));
                }

                _entryWidths.Add(_entries[i].DesiredSize.Width);
            }

            return WidthFor(_entries.Count, padding, spacing, showGrip, withOverflowButton: false);
        }

        /// <summary>
        /// The plate width of a group showing its first <paramref name="count"/> entries, with room for its
        /// an overflow button when <paramref name="withOverflowButton"/> says the rest of them need one.
        /// </summary>
        internal double WidthFor(int count, double padding, double spacing, bool showGrip, bool withOverflowButton)
        {
            double width = showGrip ? ToolBarGripElement.GRIP_WIDTH + spacing : 0;
            for (int i = 0; i < count; i++)
            {
                width += _entryWidths[i] + (i > 0 ? spacing : 0);
            }

            if (withOverflowButton)
            {
                width += _overflowButton!.DesiredSize.Width + (count > 0 || showGrip ? spacing : 0);
            }

            return width + (padding * 2);
        }

        internal void Arrange(Rect plate, double padding, double spacing, bool showGrip, int visibleEntries)
        {
            IsHidden = false;
            int previousVisible = VisibleEntryCount;
            VisibleEntryCount = Math.Clamp(visibleEntries, 0, _entries.Count);

            // The open popup holds exactly the entries past the old count. A new count means it is holding
            // the wrong ones, and re-filling it under the pointer would move what is being clicked.
            if (_overflow.IsOpen && previousVisible != VisibleEntryCount)
            {
                _overflow.Close();
            }

            Plate.Measure(new Size(plate.Width, plate.Height));
            Plate.Arrange(plate);

            double x = plate.X + padding;
            double y = plate.Y + padding;
            double height = Math.Max(0, plate.Height - (padding * 2));

            if (showGrip)
            {
                Grip.Arrange(new Rect(x, y, ToolBarGripElement.GRIP_WIDTH, height));
                x += ToolBarGripElement.GRIP_WIDTH + spacing;
            }
            else
            {
                Grip.Arrange(Rect.Empty);
            }

            // Left to right until the plate runs out: what is cut is always the tail, so a group that
            // straddles the band's edge still reads as itself.
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i >= VisibleEntryCount)
                {
                    // While the popup holds it, the entry is laid out by the popup and not by the band.
                    if (!_overflow.IsOpen)
                    {
                        _entries[i].Arrange(Rect.Empty);
                    }

                    continue;
                }

                double width = _entries[i].DesiredSize.Width;
                _entries[i].Arrange(new Rect(x, y, width, height));
                x += width + spacing;
            }

            if (IsTruncated)
            {
                double width = _overflowButton!.DesiredSize.Width;
                _overflowButton.Arrange(new Rect(Math.Max(x, plate.Right - padding - width), y, width, height));
            }
            else
            {
                _overflowButton!.Arrange(Rect.Empty);
            }
        }

        internal void Hide()
        {
            _overflow.Close();
            IsHidden = true;
            VisibleEntryCount = 0;
            Plate.Arrange(Rect.Empty);
            Grip.Arrange(Rect.Empty);
            _overflowButton!.Arrange(Rect.Empty);

            // The band's popup may be showing this hidden group's entries; they are laid out there.
            if (_onLoanToBand)
            {
                return;
            }

            foreach (var entry in _entries)
            {
                entry.Arrange(Rect.Empty);
            }
        }

        internal void Render(IGraphicsContext context)
        {
            if (IsHidden)
            {
                return;
            }

            Plate.Render(context);
            if (Grip.Bounds.Width > 0)
            {
                Grip.Render(context);
            }

            for (int i = 0; i < VisibleEntryCount; i++)
            {
                _entries[i].Render(context);
            }

            if (IsTruncated)
            {
                _overflowButton!.Render(context);
            }
        }

        internal UIElement? HitTest(Point point)
        {
            if (IsHidden)
            {
                return null;
            }

            if (IsTruncated && _overflowButton!.HitTest(point) is UIElement overflowHit)
            {
                return overflowHit;
            }

            if (Grip.Bounds.Width > 0 && Grip.HitTest(point) is UIElement gripHit)
            {
                return gripHit;
            }

            for (int i = VisibleEntryCount - 1; i >= 0; i--)
            {
                if (_entries[i] is UIElement element && element.HitTest(point) is UIElement hit)
                {
                    return hit;
                }
            }

            return null;
        }

        internal bool VisitChildren(Func<Element, bool> visitor)
        {
            if (!visitor(Plate) || !visitor(Grip) || !visitor(_overflowButton!))
            {
                return false;
            }

            foreach (var entry in _entries)
            {
                if (!visitor(entry))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// One band's visuals: a group visual per group, and the overflow button that offers the groups this band had
    /// no room for at all. The button belongs to the band, so a band that overflows never pushes anything
    /// onto another band.
    /// </summary>
    internal sealed class BandVisual
    {
        private readonly List<GroupVisual> _groups = new();
        private readonly OverflowPopup _overflowPopup = new();
        private ToolBarOverflowButton? _overflow;
        private ToolBar? _owner;

        internal BandVisual(ToolBarBand band) => Band = band;

        internal ToolBarBand Band { get; }

        internal IReadOnlyList<GroupVisual> Groups => _groups;

        internal Rect Bounds { get; private set; }

        /// <summary>Whether the band had to drop a group whole.</summary>
        internal bool IsOverflowing { get; private set; }

        internal ToolBarOverflowButton OverflowButton => _overflow!;

        /// <summary>The popup the band's overflow button opens.</summary>
        internal OverflowPopup OverflowContent => _overflowPopup;

        internal void Build(ToolBar owner)
        {
            _owner = owner;
            _overflow = CreateOverflowButton(owner, () => OpenOverflow(owner));
            _overflowPopup.Returned += () =>
            {
                foreach (var group in _groups)
                {
                    group.SetOnLoanToBand(false);
                }
            };

            foreach (var group in Band.GroupsInternal)
            {
                var visual = new GroupVisual(group);
                visual.Build(owner);
                _groups.Add(visual);
            }
        }

        internal void Detach(ToolBar owner)
        {
            // Returns whatever the popup borrowed before the groups release their entries.
            _overflowPopup.Close();

            foreach (var group in _groups)
            {
                group.Detach(owner);
            }

            _groups.Clear();

            if (_overflow != null && ReferenceEquals(_overflow.Parent, owner))
            {
                _overflow.Parent = null;
            }
        }

        /// <summary>
        /// Measures every part against the entry height and returns the width the whole band wants. The
        /// arrange measures against the same height: a different constraint there would re-measure the
        /// parts on every pass, which shows up as the band twitching while a window is resized.
        /// </summary>
        internal double Measure(double entryHeight)
        {
            bool showGrip = _owner!.CanReorderGroups;
            double width = 0;
            for (int i = 0; i < _groups.Count; i++)
            {
                width += _groups[i].Measure(entryHeight, GROUP_PADDING, ENTRY_SPACING, showGrip)
                    + (GROUP_MARGIN * 2);
            }

            _overflow!.Measure(new Size(double.PositiveInfinity, entryHeight));
            return width;
        }

        internal void Arrange(Rect band, double margin, double padding, double spacing)
        {
            Bounds = band;
            bool showGrip = _owner!.CanReorderGroups;
            double plateHeight = Math.Max(0, band.Height - (margin * 2));
            double plateY = band.Y + margin;
            double entryHeight = Math.Max(0, plateHeight - (padding * 2));

            // Measured here as well as in Measure: an arrange that follows a rebuild would otherwise plan
            // against parts of no width. The constraint is the one Measure used, so this costs nothing when
            // the measure pass has already run.
            Measure(entryHeight);
            var overflow = _overflow!;

            // Planned twice at most: the band's own overflow button takes room, but only if the plan turns out to
            // drop a group whole. Deciding it up front would leave a gap whenever nothing was dropped.
            var plan = Plan(band.Width, margin, padding, spacing, showGrip);
            if (plan.AnyHidden)
            {
                plan = Plan(band.Width - overflow.DesiredSize.Width - spacing, margin, padding, spacing, showGrip);
            }

            IsOverflowing = plan.AnyHidden;

            double x = band.X + margin;
            for (int i = 0; i < _groups.Count; i++)
            {
                int count = plan.Counts[i];
                if (count < 0)
                {
                    _groups[i].Hide();
                    continue;
                }

                double width = _groups[i].WidthFor(count, padding, spacing, showGrip,
                    count < _groups[i].Entries.Count);
                _groups[i].Arrange(new Rect(x, plateY, width, plateHeight), padding, spacing, showGrip, count);
                x += width + (margin * 2);
            }

            if (IsOverflowing)
            {
                double width = Math.Min(overflow.DesiredSize.Width, band.Width);
                overflow.Arrange(new Rect(
                    Math.Max(band.X, band.Right - margin - width), plateY + padding, width, entryHeight));
            }
            else
            {
                overflow.Arrange(Rect.Empty);
            }

            // The open popup holds the groups that were hidden when it opened. A different set means it is
            // showing groups the band has taken back, or missing ones it has just dropped.
            if (_overflowPopup.IsOpen && HiddenGroupMask() != _openedHiddenMask)
            {
                _overflowPopup.Close();
            }
        }

        private ulong HiddenGroupMask()
        {
            ulong mask = 0;
            for (int i = 0; i < _groups.Count && i < 64; i++)
            {
                if (_groups[i].IsHidden)
                {
                    mask |= 1UL << i;
                }
            }

            return mask;
        }

        /// <summary>
        /// How many entries each group shows in the given width, or -1 for a group the band has no room for
        /// at all. Every group is first given the width it needs to stand collapsed, a grip beside a
        /// an overflow button, and only what is left over is handed out as entries from the left. Narrowing therefore
        /// empties the groups on the right one entry at a time instead of dropping them, and a group only
        /// goes away when the band cannot hold even that minimum.
        /// </summary>
        private (int[] Counts, bool AnyHidden) Plan(
            double available, double margin, double padding, double spacing, bool showGrip)
        {
            int count = _groups.Count;
            var counts = new int[count];
            var minimums = new double[count];
            double used = 0;

            for (int i = 0; i < count; i++)
            {
                minimums[i] = _groups[i].WidthFor(0, padding, spacing, showGrip, withOverflowButton: true);
                used += minimums[i] + (margin * 2);
            }

            // The groups the band cannot hold even collapsed go, from the right: the leftmost groups are
            // the ones an application puts first.
            int last = count - 1;
            bool anyHidden = false;
            while (last >= 0 && used > available)
            {
                counts[last] = -1;
                used -= minimums[last] + (margin * 2);
                anyHidden = true;
                last--;
            }

            // What is left over buys entries, left to right, and stops at the first group it cannot fill.
            // Going on would put entries to the right of a group that is still collapsed, which reads as
            // the band having skipped that group rather than run out of room.
            for (int i = 0; i <= last; i++)
            {
                var group = _groups[i];
                double current = minimums[i];
                for (int entries = 1; entries <= group.Entries.Count; entries++)
                {
                    double candidate = group.WidthFor(
                        entries, padding, spacing, showGrip, withOverflowButton: entries < group.Entries.Count);
                    if (used - current + candidate > available)
                    {
                        break;
                    }

                    counts[i] = entries;
                    used += candidate - current;
                    current = candidate;
                }

                if (counts[i] < group.Entries.Count)
                {
                    break;
                }
            }

            return (counts, anyHidden);
        }

        /// <summary>
        /// Hands the entries of every group the band dropped whole to the popup, one group after another
        /// with a rule between them, so the grouping the application declared survives the collapse.
        /// </summary>
        internal void OpenOverflow(ToolBar owner)
        {
            if (_overflowPopup.IsOpen)
            {
                _overflowPopup.Close();
                return;
            }

            _overflowPopup.Begin(owner);
            bool anyBefore = false;
            foreach (var group in _groups)
            {
                if (!group.IsHidden)
                {
                    continue;
                }

                if (anyBefore)
                {
                    _overflowPopup.AddSeparator();
                }

                group.SetOnLoanToBand(true);
                foreach (var entry in group.Entries)
                {
                    _overflowPopup.Add(entry);
                    anyBefore = true;
                }
            }

            if (_overflowPopup.HasItems)
            {
                _openedHiddenMask = HiddenGroupMask();
                _overflowPopup.Show(_overflow!);
            }
        }

        private ulong _openedHiddenMask;

        internal void Render(IGraphicsContext context)
        {
            foreach (var group in _groups)
            {
                group.Render(context);
            }

            if (IsOverflowing)
            {
                _overflow!.Render(context);
            }
        }

        internal UIElement? HitTest(Point point)
        {
            if (IsOverflowing && _overflow!.HitTest(point) is UIElement overflowHit)
            {
                return overflowHit;
            }

            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                if (_groups[i].HitTest(point) is UIElement hit)
                {
                    return hit;
                }
            }

            return null;
        }

        internal bool VisitChildren(Func<Element, bool> visitor)
        {
            foreach (var group in _groups)
            {
                if (!group.VisitChildren(visitor))
                {
                    return false;
                }
            }

            return visitor(_overflow!);
        }
    }

    /// <summary>The band list. A mutation rebuilds the visuals the previous layout materialized.</summary>
    private sealed class BandCollection(ToolBar owner) : IList<ToolBarBand>
    {
        private List<ToolBarBand> Bands => owner._bands;

        public ToolBarBand this[int index]
        {
            get => Bands[index];
            set
            {
                Attach(value);
                Bands[index] = value;
                owner.OnBandsChanged();
            }
        }

        public int Count => Bands.Count;

        public bool IsReadOnly => false;

        private void Attach(ToolBarBand band)
        {
            ArgumentNullException.ThrowIfNull(band);
            band.Owner = owner;
        }

        public void Add(ToolBarBand item)
        {
            Attach(item);
            Bands.Add(item);
            owner.OnBandsChanged();
        }

        public void Clear()
        {
            Bands.Clear();
            owner.OnBandsChanged();
        }

        public bool Contains(ToolBarBand item) => Bands.Contains(item);

        public void CopyTo(ToolBarBand[] array, int arrayIndex) => Bands.CopyTo(array, arrayIndex);

        public IEnumerator<ToolBarBand> GetEnumerator() => Bands.GetEnumerator();

        public int IndexOf(ToolBarBand item) => Bands.IndexOf(item);

        public void Insert(int index, ToolBarBand item)
        {
            Attach(item);
            Bands.Insert(index, item);
            owner.OnBandsChanged();
        }

        public bool Remove(ToolBarBand item)
        {
            bool removed = Bands.Remove(item);
            if (removed)
            {
                owner.OnBandsChanged();
            }

            return removed;
        }

        public void RemoveAt(int index)
        {
            Bands.RemoveAt(index);
            owner.OnBandsChanged();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
