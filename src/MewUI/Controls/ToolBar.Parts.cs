using System.Collections;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed partial class ToolBar
{
    private static DelegateControlTemplate<DropDownButton>? _overflowTemplate;

    // A toolbar wants a bare chevron, so the overflow supplies its own face through the public part
    // contract. The face clears its own minimums because the flat style inherits the base control size,
    // which the owner's minimums cannot undo.
    private static DelegateControlTemplate<DropDownButton> OverflowTemplate
        => _overflowTemplate ??= new DelegateControlTemplate<DropDownButton>(static (owner, ctx) =>
        {
            var face = new ToolBarButton
            {
                Padding = new Thickness(0),
                MinWidth = 0,
                MinHeight = 0,
                Focusable = false,
                IsTabStop = false,
                Content = new GlyphElement { Kind = GlyphKind.ChevronDown, GlyphSize = 3 },
            };
            ctx.Register(DropDownButton.PART_DROP_DOWN_BUTTON, face);
            return face;
        });

    private static DropDownButton CreateChevron(ToolBar owner, Menu menu, Action opening)
    {
        var chevron = new DropDownButton
        {
            Template = OverflowTemplate,
            DropDownMenu = menu,
            MinWidth = CHEVRON_WIDTH,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = false,
            IsTabStop = false,
        };
        chevron.DropDownOpening += opening;
        chevron.Parent = owner;
        return chevron;
    }

    /// <summary>
    /// Fills a menu with the rows the given entries collapse into. A splitter becomes a separator, and one
    /// that would lead or trail is dropped: a menu opens on its first row.
    /// </summary>
    private static void FillMenu(Menu menu, IEnumerable<ToolBarEntry> entries)
    {
        int start = menu.Items.Count;
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case ToolBarSplitter when menu.Items.Count > start && menu.Items[^1] is not MenuSeparator:
                    menu.Items.Add(MenuSeparator.Instance);
                    break;
                case ToolBarSplitItem split when split.Command != null:
                    menu.Items.Add(new MenuItem(split.Command) { SubMenu = split.DropDownMenu });
                    break;
                case ToolBarMenuItem item:
                    menu.Items.Add(new MenuItem(item.Text) { SubMenu = item.DropDownMenu, Icon = item.Icon });
                    break;
                case ToolBarItem item when item.Command != null:
                    menu.Items.Add(new MenuItem(item.Command));
                    break;
            }
        }

        if (menu.Items.Count > start && menu.Items[^1] is MenuSeparator)
        {
            menu.Items.RemoveAt(menu.Items.Count - 1);
        }
    }

    /// <summary>The rule a <see cref="ToolBarSplitter"/> draws inside its group.</summary>
    internal sealed class ToolBarSplitterElement : FrameworkElement
    {
        private const double GAP = 3;

        private const double INSET = 2;

        protected override Size MeasureContent(Size availableSize) => new((GAP * 2) + 1, 0);

        protected override void OnRender(IGraphicsContext context)
        {
            double dpiScale = GetDpi() / 96.0;
            double thickness = LayoutRounding.SnapThicknessToPixels(1, dpiScale, 1);
            double x = LayoutRounding.RoundToPixel(Bounds.X + ((Bounds.Width - thickness) / 2), dpiScale);

            context.FillRectangle(
                new Rect(x, Bounds.Y + INSET, thickness, Math.Max(0, Bounds.Height - (INSET * 2))),
                Theme.Palette.ControlBorder);
        }
    }

    /// <summary>The handle at the left of a group, and the only thing a drag starts from.</summary>
    internal sealed class ToolBarGripElement : FrameworkElement
    {
        internal const double GRIP_WIDTH = 8;

        private const double DOT_SPACING = 3;

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
    /// chevron that offers the entries the band could not fit. The chevron is the group's own, so a group
    /// that has given up every entry is still a plate with a grip on the band: it can be dragged to
    /// another band, and what it holds is still reachable.
    /// </summary>
    internal sealed class GroupVisual
    {
        private readonly List<Element> _entries = new();
        private readonly List<double> _entryWidths = new();
        private readonly Menu _menu = new();
        private DropDownButton? _chevron;

        internal GroupVisual(ToolBarGroup group) => Group = group;

        internal ToolBarGroup Group { get; }

        internal ToolBarGroupPlate Plate { get; } = new();

        internal ToolBarGripElement Grip { get; } = new();

        /// <summary>The chevron holding the entries the band could not fit.</summary>
        internal DropDownButton Chevron => _chevron!;

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
            _chevron = CreateChevron(owner, _menu, RebuildMenu);

            foreach (var entry in Group.ItemsInternal)
            {
                var control = owner.CreateEntryControl(entry);
                control.Parent = owner;
                _entries.Add(control);
            }
        }

        internal void Detach(ToolBar owner)
        {
            Release(Plate, owner);
            Release(Grip, owner);
            if (_chevron != null)
            {
                Release(_chevron, owner);
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

        private void RebuildMenu()
        {
            _menu.Items.Clear();
            FillMenu(_menu, Group.ItemsInternal.Skip(VisibleEntryCount));
        }

        /// <summary>Measures the parts and returns the plate width the whole group needs.</summary>
        internal double Measure(double entryHeight, double padding, double spacing, bool showGrip)
        {
            if (showGrip)
            {
                Grip.Measure(new Size(ToolBarGripElement.GRIP_WIDTH, entryHeight));
            }

            _chevron!.Measure(new Size(double.PositiveInfinity, entryHeight));

            _entryWidths.Clear();
            foreach (var entry in _entries)
            {
                entry.Measure(new Size(double.PositiveInfinity, entryHeight));
                _entryWidths.Add(entry.DesiredSize.Width);
            }

            return WidthFor(_entries.Count, padding, spacing, showGrip, withChevron: false);
        }

        /// <summary>
        /// The plate width of a group showing its first <paramref name="count"/> entries, with room for its
        /// chevron when <paramref name="withChevron"/> says the rest of them need one.
        /// </summary>
        internal double WidthFor(int count, double padding, double spacing, bool showGrip, bool withChevron)
        {
            double width = showGrip ? ToolBarGripElement.GRIP_WIDTH + spacing : 0;
            for (int i = 0; i < count; i++)
            {
                width += _entryWidths[i] + (i > 0 ? spacing : 0);
            }

            if (withChevron)
            {
                width += _chevron!.DesiredSize.Width + (count > 0 || showGrip ? spacing : 0);
            }

            return width + (padding * 2);
        }

        internal void Arrange(Rect plate, double padding, double spacing, bool showGrip, int visibleEntries)
        {
            IsHidden = false;
            VisibleEntryCount = Math.Clamp(visibleEntries, 0, _entries.Count);
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
                    _entries[i].Arrange(Rect.Empty);
                    continue;
                }

                double width = _entries[i].DesiredSize.Width;
                _entries[i].Arrange(new Rect(x, y, width, height));
                x += width + spacing;
            }

            if (IsTruncated)
            {
                double width = _chevron!.DesiredSize.Width;
                _chevron.Arrange(new Rect(Math.Max(x, plate.Right - padding - width), y, width, height));
            }
            else
            {
                _chevron!.Arrange(Rect.Empty);
            }
        }

        internal void Hide()
        {
            IsHidden = true;
            VisibleEntryCount = 0;
            Plate.Arrange(Rect.Empty);
            Grip.Arrange(Rect.Empty);
            _chevron!.Arrange(Rect.Empty);
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
                _chevron!.Render(context);
            }
        }

        internal UIElement? HitTest(Point point)
        {
            if (IsHidden)
            {
                return null;
            }

            if (IsTruncated && _chevron!.HitTest(point) is UIElement chevronHit)
            {
                return chevronHit;
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
            if (!visitor(Plate) || !visitor(Grip) || !visitor(_chevron!))
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
    /// One band's visuals: a group visual per group, and the chevron that offers the groups this band had
    /// no room for at all. The chevron belongs to the band, so a band that overflows never pushes anything
    /// onto another band.
    /// </summary>
    internal sealed class BandVisual
    {
        private readonly List<GroupVisual> _groups = new();
        private readonly Menu _menu = new();
        private DropDownButton? _overflow;
        private ToolBar? _owner;

        internal BandVisual(ToolBarBand band) => Band = band;

        internal ToolBarBand Band { get; }

        internal IReadOnlyList<GroupVisual> Groups => _groups;

        internal Rect Bounds { get; private set; }

        /// <summary>Whether the band had to drop a group whole.</summary>
        internal bool IsOverflowing { get; private set; }

        internal DropDownButton Overflow => _overflow!;

        internal void Build(ToolBar owner)
        {
            _owner = owner;
            _overflow = CreateChevron(owner, _menu, RebuildMenu);

            foreach (var group in Band.GroupsInternal)
            {
                var visual = new GroupVisual(group);
                visual.Build(owner);
                _groups.Add(visual);
            }
        }

        internal void Detach(ToolBar owner)
        {
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

            // Planned twice at most: the band's own chevron takes room, but only if the plan turns out to
            // drop a group whole. Deciding it up front would leave a gap whenever nothing was dropped.
            var plan = Plan(band.Width, margin, padding, spacing, showGrip);
            if (plan.AnyHidden)
            {
                plan = Plan(band.Width - _overflow.DesiredSize.Width - spacing, margin, padding, spacing, showGrip);
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
                double width = Math.Min(_overflow.DesiredSize.Width, band.Width);
                _overflow.Arrange(new Rect(
                    Math.Max(band.X, band.Right - margin - width), plateY + padding, width, entryHeight));
            }
            else
            {
                _overflow.Arrange(Rect.Empty);
            }
        }

        /// <summary>
        /// How many entries each group shows in the given width, or -1 for a group the band has no room for
        /// at all. Every group is first given the width it needs to stand collapsed, a grip beside a
        /// chevron, and only what is left over is handed out as entries from the left. Narrowing therefore
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
                minimums[i] = _groups[i].WidthFor(0, padding, spacing, showGrip, withChevron: true);
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
                        entries, padding, spacing, showGrip, withChevron: entries < group.Entries.Count);
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

        private void RebuildMenu()
        {
            _menu.Items.Clear();
            foreach (var group in _groups)
            {
                if (!group.IsHidden)
                {
                    continue;
                }

                FillMenu(_menu, group.Group.ItemsInternal);
                _menu.Items.Add(MenuSeparator.Instance);
            }

            if (_menu.Items.Count > 0 && _menu.Items[^1] is MenuSeparator)
            {
                _menu.Items.RemoveAt(_menu.Items.Count - 1);
            }
        }

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
