namespace Aprillz.MewUI.Controls;

/// <summary>Describes the active sort direction of a <see cref="GridView"/> column.</summary>
public enum GridViewSortDirection
{
    None = 0,
    Ascending = 1,
    Descending = 2,
}

/// <summary>Describes a change to the single-column sort applied by a <see cref="GridView"/>.</summary>
/// <param name="ColumnIndex">The sorted column index, or -1 when sorting was cleared.</param>
/// <param name="Direction">The new sort direction.</param>
public readonly record struct GridViewSortChange(int ColumnIndex, GridViewSortDirection Direction);

internal static class GridViewSortPermutation
{
    public static int[] Build(
        IItemsView source,
        Comparison<object?>? comparison,
        GridViewSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (direction is < GridViewSortDirection.None or > GridViewSortDirection.Descending)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        if (direction != GridViewSortDirection.None && comparison == null)
        {
            throw new ArgumentNullException(nameof(comparison));
        }

        var permutation = new int[source.Count];
        for (int i = 0; i < permutation.Length; i++)
        {
            permutation[i] = i;
        }

        if (direction == GridViewSortDirection.None || permutation.Length < 2)
        {
            return permutation;
        }

        Array.Sort(permutation, (left, right) =>
        {
            // Reverse the comparison arguments for descending order instead of negating the
            // result: a legal comparer may return int.MinValue, which cannot be negated.
            int primary = direction == GridViewSortDirection.Ascending
                ? comparison!(source.GetItem(left), source.GetItem(right))
                : comparison!(source.GetItem(right), source.GetItem(left));
            if (primary != 0)
            {
                return primary < 0 ? -1 : 1;
            }

            // Keep equal items in current source order for both directions.
            return left.CompareTo(right);
        });
        return permutation;
    }
}

internal abstract class GridViewSortedItemsView : ISelectableItemsView, IDisposable
{
    private int[] _viewToSource = [];
    private int[] _sourceToView = [];
    private Comparison<object?>? _comparison;
    private GridViewSortDirection _direction;
    private bool _disposed;

    protected GridViewSortedItemsView(
        ISelectableItemsView source,
        Comparison<object?>? comparison,
        GridViewSortDirection direction)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        AssignPermutation(GridViewSortPermutation.Build(source, comparison, direction));
        _comparison = comparison;
        _direction = direction;

        Source.Changed += OnSourceChanged;
        Source.SelectionChanged += OnSourceSelectionChanged;
    }

    public static GridViewSortedItemsView Create(
        ISelectableItemsView source,
        Comparison<object?>? comparison,
        GridViewSortDirection direction)
        => source is IMultiSelectableItemsView multi
            ? new Multi(multi, comparison, direction)
            : new Single(source, comparison, direction);

    protected ISelectableItemsView Source { get; }

    public int Count => Source.Count;

    public Func<object?, object?>? KeySelector => Source.KeySelector;

    public int SelectedIndex
    {
        get => ToViewIndex(Source.SelectedIndex);
        set
        {
            int clamped = Count == 0 ? -1 : Math.Clamp(value, -1, Count - 1);
            Source.SelectedIndex = ToSourceIndex(clamped);
        }
    }

    public object? SelectedItem
    {
        get => Source.SelectedItem;
        set => Source.SelectedItem = value;
    }

    public event Action<ItemsChange>? Changed;

    public event Action<int>? SelectionChanged;

    public object? GetItem(int index) => Source.GetItem(CheckedSourceIndex(index));

    public string GetText(int index) => Source.GetText(CheckedSourceIndex(index));

    public void Invalidate() => Source.Invalidate();

    public void SetSort(Comparison<object?>? comparison, GridViewSortDirection direction)
    {
        var previousSelection = CaptureMappedSelection();
        var next = GridViewSortPermutation.Build(Source, comparison, direction);

        AssignPermutation(next);
        _comparison = comparison;
        _direction = direction;

        NotifyMappedSelectionIfChanged(previousSelection);
        Changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, Count));
    }

    protected int ToSourceIndex(int viewIndex)
        => (uint)viewIndex < (uint)_viewToSource.Length ? _viewToSource[viewIndex] : -1;

    protected int ToViewIndex(int sourceIndex)
        => (uint)sourceIndex < (uint)_sourceToView.Length ? _sourceToView[sourceIndex] : -1;

    protected virtual object? CaptureMappedSelection() => null;

    protected virtual void NotifyMappedSelectionIfChanged(object? previousSelection)
    {
    }

    protected void RebuildForSelectionEvent()
    {
        // ItemsView updates its source collection and selection before raising Changed. Rebuild
        // here as well so a selection notification never exposes an index through a stale map.
        if (_direction != GridViewSortDirection.None)
        {
            AssignPermutation(GridViewSortPermutation.Build(Source, _comparison, _direction));
        }
        else if (_viewToSource.Length != Source.Count)
        {
            AssignPermutation(GridViewSortPermutation.Build(Source, null, GridViewSortDirection.None));
        }
    }

    protected virtual void OnSourceChanged(ItemsChange change)
    {
        var previousSelection = CaptureMappedSelection();
        AssignPermutation(GridViewSortPermutation.Build(Source, _comparison, _direction));
        NotifyMappedSelectionIfChanged(previousSelection);

        Changed?.Invoke(_direction == GridViewSortDirection.None
            ? change
            : new ItemsChange(ItemsChangeKind.Reset, 0, Count));
    }

    protected virtual void OnSourceSelectionChanged(int _)
    {
        RebuildForSelectionEvent();
        SelectionChanged?.Invoke(SelectedIndex);
    }

    private int CheckedSourceIndex(int viewIndex)
    {
        int sourceIndex = ToSourceIndex(viewIndex);
        if (sourceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewIndex));
        }
        return sourceIndex;
    }

    private void AssignPermutation(int[] viewToSource)
    {
        var sourceToView = new int[viewToSource.Length];
        for (int viewIndex = 0; viewIndex < viewToSource.Length; viewIndex++)
        {
            sourceToView[viewToSource[viewIndex]] = viewIndex;
        }

        _viewToSource = viewToSource;
        _sourceToView = sourceToView;
    }

    public virtual void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Source.Changed -= OnSourceChanged;
        Source.SelectionChanged -= OnSourceSelectionChanged;
    }

    private sealed class Single : GridViewSortedItemsView
    {
        public Single(
            ISelectableItemsView source,
            Comparison<object?>? comparison,
            GridViewSortDirection direction)
            : base(source, comparison, direction)
        {
        }
    }

    private sealed class Multi : GridViewSortedItemsView, IMultiSelectableItemsView
    {
        private readonly IMultiSelectableItemsView _source;

        public Multi(
            IMultiSelectableItemsView source,
            Comparison<object?>? comparison,
            GridViewSortDirection direction)
            : base(source, comparison, direction)
        {
            _source = source;
            _source.SelectedIndicesChanged += OnSourceSelectedIndicesChanged;
        }

        public ItemsSelectionMode SelectionMode
        {
            get => _source.SelectionMode;
            set => _source.SelectionMode = value;
        }

        public IReadOnlyList<int> SelectedIndices
        {
            get
            {
                var sourceIndices = _source.SelectedIndices;
                if (sourceIndices.Count == 0)
                {
                    return Array.Empty<int>();
                }

                var result = new int[sourceIndices.Count];
                int count = 0;
                for (int i = 0; i < sourceIndices.Count; i++)
                {
                    int index = ToViewIndex(sourceIndices[i]);
                    if (index >= 0)
                    {
                        result[count++] = index;
                    }
                }
                if (count != result.Length)
                {
                    Array.Resize(ref result, count);
                }
                Array.Sort(result);
                return result;
            }
        }

        public int AnchorIndex => ToViewIndex(_source.AnchorIndex);

        public event Action? SelectedIndicesChanged;

        public bool IsSelected(int index)
        {
            int sourceIndex = ToSourceIndex(index);
            return sourceIndex >= 0 && _source.IsSelected(sourceIndex);
        }

        public void SelectSingle(int index)
        {
            int sourceIndex = ToSourceIndex(index);
            if (sourceIndex >= 0)
            {
                _source.SelectSingle(sourceIndex);
            }
            else
            {
                _source.ClearSelection();
            }
        }

        public void ToggleSelected(int index)
        {
            int sourceIndex = ToSourceIndex(index);
            if (sourceIndex >= 0)
            {
                _source.ToggleSelected(sourceIndex);
            }
        }

        public void SetSelected(int index, bool selected)
        {
            int sourceIndex = ToSourceIndex(index);
            if (sourceIndex >= 0)
            {
                _source.SetSelected(sourceIndex, selected);
            }
        }

        public void SelectRange(int anchorIndex, int targetIndex, bool clearExisting)
        {
            if (Count == 0)
            {
                return;
            }

            anchorIndex = Math.Clamp(anchorIndex, 0, Count - 1);
            targetIndex = Math.Clamp(targetIndex, 0, Count - 1);
            int anchorSource = ToSourceIndex(anchorIndex);
            if (clearExisting)
            {
                _source.SelectSingle(anchorSource);
            }

            int low = Math.Min(anchorIndex, targetIndex);
            int high = Math.Max(anchorIndex, targetIndex);
            for (int viewIndex = low; viewIndex <= high; viewIndex++)
            {
                if (viewIndex != targetIndex)
                {
                    _source.SetSelected(ToSourceIndex(viewIndex), true);
                }
            }
            _source.SetSelected(ToSourceIndex(targetIndex), true);
        }

        public void ClearSelection() => _source.ClearSelection();

        protected override object CaptureMappedSelection() => SelectedIndices.ToArray();

        protected override void NotifyMappedSelectionIfChanged(object? previousSelection)
        {
            var previous = (int[]?)previousSelection ?? [];
            var current = SelectedIndices;
            if (!previous.AsSpan().SequenceEqual(current is int[] array ? array : current.ToArray()))
            {
                SelectedIndicesChanged?.Invoke();
            }
        }

        private void OnSourceSelectedIndicesChanged()
        {
            RebuildForSelectionEvent();
            SelectedIndicesChanged?.Invoke();
        }

        public override void Dispose()
        {
            _source.SelectedIndicesChanged -= OnSourceSelectedIndicesChanged;
            base.Dispose();
        }
    }
}
