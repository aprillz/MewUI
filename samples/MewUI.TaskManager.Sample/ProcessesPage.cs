using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.TaskManager.Sample;

internal sealed class ProcessesPage : UserControl
{
    private readonly ObservableCollection<ProcessNode> _roots = [];
    private readonly Dictionary<ProcessKey, ProcessNode> _nodes = [];
    private readonly TreeItemsView<ProcessNode> _tree;
    private readonly GridView _grid;
    private string _query = string.Empty;

    public ProcessesPage(MonitorController monitor)
    {
        _tree = TreeItemsView.Create(
            _roots,
            node => node.Children,
            node => node.Name,
            node => node.Key);

        _grid = BuildGrid();
        _grid.ItemDoubleClicked += item =>
        {
            if (item is not ProcessNode node) return;
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

        return new Grid()
            .Rows("Auto, *")
            .Children(
                new DockPanel()
                    .Padding(28, 18)
                    .Spacing(12)
                    .Children(
                        new StackPanel()
                            .DockRight()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                search,
                                new Button()
                                    .Content(new StackPanel().Horizontal().Spacing(7).Children(
                                        FluentIcons.Create("window_new_regular").Size(16, 16),
                                        new TextBlock().Text("Run new task"))),
                                new Button().Content(FluentIcons.Create("more_regular").Size(18, 18)).Width(38)),
                        new TextBlock()
                            .Text("Processes")
                            .FontSize(ThemeFontSize.Medium)
                            .SemiBold()
                            .CenterVertical()),
                _grid.Row(1).Margin(20, 0, 20, 18));
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
                    (cell, node, index, _) => cell.Bind(node, index)),
            TextColumn("Status", 100, node => node.IsAccessible ? string.Empty : "Limited", node => node.IsAccessible),
            TextColumn("CPU", 90, node => $"{node.CpuPercent:0.0}%", node => node.CpuPercent),
            TextColumn("Memory", 120, node => FormatBytes(node.WorkingSetBytes), node => node.WorkingSetBytes),
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

    private void UpdateProcesses(IReadOnlyList<ProcessSample> samples, PerformanceSample _)
        => RebuildHierarchy(samples);

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

        public void Bind(ProcessNode node, int index)
        {
            _index = index;
            Margin = new Thickness(_tree.GetDepth(index) * 18, 0, 0, 0);
            _expander.IsVisible = _tree.GetHasChildren(index);
            _chevron.Kind(_tree.GetIsExpanded(index) ? GlyphKind.ChevronDown: GlyphKind.ChevronRight);
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
    public long WorkingSetBytes { get; private set; }
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
        WorkingSetBytes = sample.WorkingSetBytes;
        IsAccessible = sample.IsAccessible;
    }
}
