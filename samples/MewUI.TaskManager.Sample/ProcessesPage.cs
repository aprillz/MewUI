using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.TaskManager.Sample;

internal sealed class ProcessesPage : UserControl
{
    private readonly ObservableCollection<ProcessNode> _roots = [];
    private readonly ObservableCollection<ProcessNode> _flat = [];
    private readonly Dictionary<ProcessKey, ProcessNode> _nodes = [];
    private readonly TreeItemsView<ProcessNode> _tree;
    private readonly ItemsView<ProcessNode> _flatView;
    private readonly GridView _grid;
    private string _query = string.Empty;

    // Unsorted, processes stand under their parents; sorted by a column, every process is one row of a
    // flat list, as a parent's children would otherwise only be sorted among themselves.
    private bool _isFlat;

    public ProcessesPage(MonitorController monitor)
    {
        _tree = TreeItemsView.Create(
            _roots,
            node => node.Children,
            node => node.Name,
            node => node.Key);
        _flatView = ItemsView.Create<ProcessNode>(_flat, node => node.Name, node => node.Key);

        _grid = BuildGrid();
        _grid.SortChanged += change =>
        {
            bool flat = change.Direction != GridViewSortDirection.None;
            if (flat == _isFlat) return;
            _isFlat = flat;
            _grid.ItemsSource = flat ? _flatView : _tree;
        };
        _grid.ItemDoubleClicked += item =>
        {
            if (_isFlat || item is not ProcessNode node) return;
            for (int index = 0; index < _tree.Count; index++)
            {
                if (!ReferenceEquals(_tree.GetItem(index), node)) continue;
                if (_tree.GetHasChildren(index))
                    _tree.SetIsExpanded(index, !_tree.GetIsExpanded(index));
                break;
            }
        };
        monitor.Updated += UpdateProcesses;
        Build();
    }

    protected override Element OnBuild()
    {
        var search = new TextBox()
            .Width(300)
            .Placeholder("Type a name or PID to search")
            .OnTextChanged(text =>
            {
                _query = text.Trim();
                RebuildHierarchy(_nodes.Values.Select(node => node.LastSample).Where(sample => sample != null).Cast<ProcessSample>().ToArray());
            });

        // The row menu and the page buttons act on a process; a row hands its own over as the argument.
        var endTask = new Command("taskmanager.process.end", "End task");
        var openLocation = new Command("taskmanager.process.openLocation", "Open file location");
        var copyDetails = new Command("taskmanager.process.copy", "Copy details");
        Commands.Register(endTask, (ProcessNode node) => _ = TaskActions.EndProcessAsync(node, FindVisualRoot() as Window));
        Commands.Register(openLocation, (ProcessNode node) => TaskActions.OpenFileLocation(node.ExecutablePath), (ProcessNode node) => node.ExecutablePath != null);
        Commands.Register(copyDetails, (ProcessNode node) => TaskActions.Copy(TaskActions.Describe(node)));
        var rowMenu = new ContextMenu()
            .Item(endTask)
            .Separator()
            .Item(openLocation)
            .Item(copyDetails);
        _grid.PrepareContainer<ProcessNode>((row, _, _, _) => row.ContextMenu = rowMenu);

        var endTaskButton = TaskManagerView.CommandButton("dismiss_circle_regular", "End task").IsEnabled(false);
        endTaskButton.OnClick(() =>
        {
            if (_grid.SelectedItem is ProcessNode node) _ = TaskActions.EndProcessAsync(node, FindVisualRoot() as Window);
        });
        _grid.OnSelectionChanged(item => endTaskButton.IsEnabled = item is ProcessNode);

        var expandAll = new Command("taskmanager.process.expandAll", "Expand all");
        var collapseAll = new Command("taskmanager.process.collapseAll", "Collapse all");
        var openSelected = new Command("taskmanager.process.openSelectedLocation", "Open file location");
        var copySelected = new Command("taskmanager.process.copySelected", "Copy details");
        Commands.Register(expandAll, () => SetAllExpanded(true), () => !_isFlat);
        Commands.Register(collapseAll, () => SetAllExpanded(false), () => !_isFlat);
        Commands.Register(openSelected, () => TaskActions.OpenFileLocation((_grid.SelectedItem as ProcessNode)?.ExecutablePath), () => (_grid.SelectedItem as ProcessNode)?.ExecutablePath != null);
        Commands.Register(copySelected, () =>
        {
            if (_grid.SelectedItem is ProcessNode node) TaskActions.Copy(TaskActions.Describe(node));
        }, () => _grid.SelectedItem is ProcessNode);
        var more = TaskManagerView.MoreButton(new ContextMenu()
            .Item(expandAll)
            .Item(collapseAll)
            .Separator()
            .Item(openSelected)
            .Item(copySelected)
            .Separator()
            .Item(TaskManagerView.OpenSystemMonitorCommand(this)));

        return new Grid()
            .Rows("Auto, *")
            .Children(
                TaskManagerView.PageHeader("Processes", search.CenterVertical().Margin(0, 0, 8, 0), TaskManagerView.RunNewTaskButton(), endTaskButton, more),
                _grid.Row(1).Margin(20, 0, 20, 18));
    }

    private void SetAllExpanded(bool expanded)
    {
        // Expanding a row adds its children after it, so the walk goes on until the end moves no more.
        for (int index = 0; index < _tree.Count; index++)
        {
            if (_tree.GetHasChildren(index) && _tree.GetIsExpanded(index) != expanded) _tree.SetIsExpanded(index, expanded);
        }
    }

    private GridView BuildGrid()
    {
        var grid = new GridView
        {
            ItemsSource = _tree,
            ZebraStriping = false,
            ShowGridLines = false,
        };

        grid.Columns(
            new GridViewColumn<ProcessNode>()
                .Header("Name")
                .StarWidth(3, minWidth: 260)
                .SortBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Bind(
                    _ => new ProcessNameCell(_tree),
                    (cell, node, index, _) => cell.Bind(node, _isFlat ? -1 : index)),
            TextColumn("Status", 100, node => node.IsAccessible ? string.Empty : "Limited", node => node.IsAccessible),
            HeatColumn("CPU", 90, node => $"{node.CpuPercent:0.0}%", node => node.CpuPercent, node => node.CpuPercent / 100),
            HeatColumn("Memory", 120, node => FormatBytes(node.MemoryBytes), node => node.MemoryBytes, node => node.MemoryBytes / (double)_totalMemoryBytes),
            HeatColumn("Disk", 100, node => $"{node.DiskBytesPerSecond / (1024 * 1024):0.0} MB/s", node => node.DiskBytesPerSecond, node => node.DiskBytesPerSecond / DISK_HEAT_FULL_SCALE),
            TextColumn("PID", 85, node => node.ProcessId.ToString(), node => node.ProcessId));

        return grid;
    }

    private static GridViewColumn<ProcessNode> TextColumn<TKey>(
        string header,
        double width,
        Func<ProcessNode, string> text,
        Func<ProcessNode, TKey> sortKey) =>
        new GridViewColumn<ProcessNode>()
            .Header(header)
            .HeaderTextAlignment(TextAlignment.Right)
            .Width(width)
            .SortBy(sortKey)
            .Bind(
                _ => new TextBlock { TextAlignment = TextAlignment.Right }.Margin(8, 0).CenterVertical(),
                (view, node) => view.Text(text(node)));

    // Disk throughput that shades a cell fully; Task Manager scales the disk column the same way, by rate.
    private const double DISK_HEAT_FULL_SCALE = 100 * 1024 * 1024;

    /// <summary>
    /// A resource column shaded as a heat map, as Windows Task Manager does it: the cell takes the accent
    /// color, darker the larger the share of the machine's resource the process uses.
    /// </summary>
    private static GridViewColumn<ProcessNode> HeatColumn<TKey>(
        string header,
        double width,
        Func<ProcessNode, string> text,
        Func<ProcessNode, TKey> sortKey,
        Func<ProcessNode, double> share) =>
        new GridViewColumn<ProcessNode>()
            .Header(header)
            .HeaderTextAlignment(TextAlignment.Right)
            .Width(width)
            .SortBy(sortKey)
            .Bind(
                _ => new Border().Child(new TextBlock { TextAlignment = TextAlignment.Right }.Margin(8, 0).CenterVertical()),
                (cell, node) =>
                {
                    ((TextBlock)cell.Child!).Text = text(node);
                    cell.Background = Application.IsRunning
                        ? Application.Current.Theme.Palette.Accent.WithAlpha(HeatAlpha(share(node)))
                        : Color.Transparent;
                });

    /// <summary>A few steps of shade: the lightest for an idle process, the darkest for one taking a large share.</summary>
    private static byte HeatAlpha(double share) => share switch
    {
        < 0.001 => 22,
        < 0.005 => 38,
        < 0.02 => 58,
        < 0.05 => 82,
        < 0.15 => 110,
        < 0.3 => 140,
        _ => 172,
    };

    private long _totalMemoryBytes = 1;

    private void UpdateProcesses(IReadOnlyList<ProcessSample> samples, PerformanceSample performance)
    {
        var memory = performance.Resources.FirstOrDefault(resource => resource.Kind == ResourceKind.Memory);
        if (memory != null) _totalMemoryBytes = Math.Max(1, memory.CompositionTotal);
        RebuildHierarchy(samples);
    }

    private void RebuildHierarchy(IReadOnlyList<ProcessSample> samples)
    {
        var active = new HashSet<ProcessKey>();
        foreach (var sample in samples)
        {
            var key = new ProcessKey(sample.ProcessId, sample.StartTimeTicks);
            active.Add(key);
            if (!_nodes.TryGetValue(key, out var node))
            {
                node = new ProcessNode(key);
                _nodes.Add(key, node);
            }
            node.Update(sample);
            node.Children.Clear();
        }

        foreach (var key in _nodes.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            _nodes.Remove(key);
        }

        var byPid = _nodes.Values
            .GroupBy(node => node.ProcessId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(node => node.StartTimeTicks).First());
        var included = new HashSet<ProcessNode>();
        if (string.IsNullOrEmpty(_query))
        {
            included.UnionWith(_nodes.Values);
        }
        else
        {
            foreach (var match in _nodes.Values.Where(node =>
                node.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                node.ProcessId.ToString().Contains(_query, StringComparison.OrdinalIgnoreCase)))
            {
                var current = match;
                while (included.Add(current) &&
                    current.ParentProcessId != current.ProcessId &&
                    byPid.TryGetValue(current.ParentProcessId, out var parent))
                {
                    current = parent;
                }
            }
        }

        var roots = new List<ProcessNode>();

        foreach (var node in _nodes.Values.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!included.Contains(node)) continue;

            if (node.ParentProcessId != node.ProcessId &&
                byPid.TryGetValue(node.ParentProcessId, out var parent) &&
                included.Contains(parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        _roots.Clear();
        foreach (var root in roots.OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)) _roots.Add(root);
        _tree.Invalidate();

        // The flat list holds the matches alone: the parents a search brings in only give the tree its shape.
        _flat.Clear();
        foreach (var node in _nodes.Values)
        {
            if (string.IsNullOrEmpty(_query) ||
                node.Name.Contains(_query, StringComparison.OrdinalIgnoreCase) ||
                node.ProcessId.ToString().Contains(_query, StringComparison.OrdinalIgnoreCase))
            {
                _flat.Add(node);
            }
        }

        if (!string.IsNullOrEmpty(_query))
        {
            for (int index = 0; index < _tree.Count; index++)
            {
                if (_tree.GetHasChildren(index)) _tree.SetIsExpanded(index, true);
            }
        }
    }

    private static string FormatBytes(long value)
    {
        if (value < 1024) return $"{value} B";
        if (value < 1024 * 1024) return $"{value / 1024.0:0.0} KB";
        if (value < 1024L * 1024 * 1024) return $"{value / (1024.0 * 1024):0.0} MB";
        return $"{value / (1024.0 * 1024 * 1024):0.0} GB";
    }

    private sealed class ProcessNameCell : ContentControl
    {
        private readonly TreeItemsView<ProcessNode> _tree;
        private readonly Button _expander;
        private readonly GlyphElement _chevron;
        private readonly Image _icon;
        private readonly PathShape _fallbackIcon;
        private readonly TextBlock _name;
        private int _index;
        private long _iconRequest;

        public ProcessNameCell(TreeItemsView<ProcessNode> tree)
        {
            _tree = tree;
            _chevron = new GlyphElement().Kind(GlyphKind.ChevronRight);
            _expander = new Button()
                .StyleName("flat-button")
                .MinHeight(0)
                .Width(16)
                .Height(16)
                .Padding(0)
                .Content(_chevron)
                .OnClick(() => _tree.SetIsExpanded(_index, !_tree.GetIsExpanded(_index)));
            _icon = new Image().Size(18, 18).CenterVertical();
            _fallbackIcon = FluentIcons.Create("apps_regular").Size(18, 18);
            _name = new TextBlock().CenterVertical();
            Content = new StackPanel()
                .Horizontal()
                .Spacing(6)
                .CenterVertical()
                .Children(
                    _expander,
                    new Grid().Size(18, 18).Children(_icon, _fallbackIcon),
                    _name);
        }

        /// <summary>Shows <paramref name="node"/> at row <paramref name="index"/> of the tree, or unindented without an expander when the index is -1.</summary>
        public void Bind(ProcessNode node, int index)
        {
            _index = index;
            bool inTree = index >= 0;
            Margin = new Thickness(inTree ? _tree.GetDepth(index) * 18 : 0, 0, 0, 0);
            _expander.IsVisible = inTree && _tree.GetHasChildren(index);
            if (inTree)
            {
                _chevron.Kind(_tree.GetIsExpanded(index) ? GlyphKind.ChevronDown : GlyphKind.ChevronRight);
            }
            long request = ++_iconRequest;
            Task<ImageSource?>? realTask = string.IsNullOrWhiteSpace(node.ExecutablePath)
                ? null
                : ProcessIconCache.GetRealAsync(node.ExecutablePath);
            _icon.Source = realTask is { IsCompletedSuccessfully: true } && realTask.Result != null
                ? realTask.Result
                : ProcessIconCache.GetPlaceholder(node.ExecutablePath);
            _icon.IsVisible = _icon.Source != null;
            _fallbackIcon.IsVisible = _icon.Source == null;
            if (realTask is { IsCompleted: false })
            {
                _ = realTask.ContinueWith(task =>
                {
                    var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
                    if (dispatcher == null || !task.IsCompletedSuccessfully) return;
                    dispatcher.BeginInvoke(() =>
                    {
                        if (_iconRequest != request || task.Result == null) return;
                        _icon.Source = task.Result;
                        _icon.IsVisible = true;
                        _fallbackIcon.IsVisible = false;
                    });
                }, TaskScheduler.Default);
            }
            _name.Text = node.Name;
        }
    }
}

internal static class ProcessIconCache
{
    private const int IconSize = 18;
    private static readonly Dictionary<string, Task<ImageSource?>> s_realCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object s_gate = new();

    public static ImageSource? GetPlaceholder(string? executablePath)
    {
        if (!Application.IsRunning || string.IsNullOrWhiteSpace(executablePath)) return null;

        string iconPath = executablePath;
        bool isDirectory = false;
        if (OperatingSystem.IsMacOS())
        {
            int marker = executablePath.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                iconPath = executablePath[..(marker + 4)];
                isDirectory = true;
            }
        }

        return Application.Current.PlatformServices.ShellIconProvider.GetIcon(iconPath, isDirectory, IconSize);
    }

    public static Task<ImageSource?> GetRealAsync(string executablePath)
    {
        string iconPath = NormalizePath(executablePath);
        lock (s_gate)
        {
            if (s_realCache.TryGetValue(iconPath, out var cached)) return cached;
            var provider = Application.Current.PlatformServices.ShellIconProvider;
            var task = Task.Run(() => provider.GetRealIcon(iconPath, IconSize));
            s_realCache[iconPath] = task;
            return task;
        }
    }

    private static string NormalizePath(string executablePath)
    {
        if (!OperatingSystem.IsMacOS()) return executablePath;
        int marker = executablePath.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 ? executablePath[..(marker + 4)] : executablePath;
    }
}

internal readonly record struct ProcessKey(int ProcessId, long StartTimeTicks);

internal sealed class ProcessNode(ProcessKey key)
{
    public ProcessKey Key { get; } = key;
    public int ProcessId => Key.ProcessId;
    public long StartTimeTicks => Key.StartTimeTicks;
    public int ParentProcessId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? ExecutablePath { get; private set; }
    public double CpuPercent { get; private set; }
    public long MemoryBytes { get; private set; }
    public double DiskBytesPerSecond { get; private set; }
    public bool IsAccessible { get; private set; }
    public ProcessSample? LastSample { get; private set; }
    public ObservableCollection<ProcessNode> Children { get; } = [];

    public void Update(ProcessSample sample)
    {
        LastSample = sample;
        ParentProcessId = sample.ParentProcessId;
        Name = sample.Name;
        ExecutablePath = sample.ExecutablePath;
        CpuPercent = sample.CpuPercent;
        MemoryBytes = sample.MemoryBytes;
        DiskBytesPerSecond = sample.DiskBytesPerSecond;
        IsAccessible = sample.IsAccessible;
    }
}
