using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Managed file/folder dialog prototype (pure MewUI). 2-panel: places sidebar + breadcrumb + file list.
/// Shown synchronously via <see cref="Window.ShowDialog"/>; result read from <see cref="Accepted"/>/<see cref="SelectedPaths"/>.
/// </summary>
internal sealed class ManagedFileDialogWindow : Window
{
    private readonly FileDialogMode _mode;
    private readonly FileSystemBrowser _browser = new();
    private readonly List<FileFilter> _filters;
    private readonly List<PlaceItem> _placeItems = new();

    private readonly GridView _grid;
    private readonly StackPanel _breadcrumb;
    private readonly TextBox _fileName;
    private readonly NavigationList _places;
    private SegmentButton _backButton = null!;

    private SegmentButton _forwardButton = null!;

    // Descriptor for a nav-cluster segment (glyph + tooltip + click action).
    private sealed record NavAction(string Id, GlyphKind Glyph, string Tip, Action Run);
    private readonly ContentControl _pathHost;
    private readonly TextBox _pathBox;
    private readonly ListBox _iconView;
    private readonly Border _viewHost;

    // Guards programmatic places selection (current-path sync) from re-triggering navigation.
    private bool _syncingPlaces;

    private bool _editingPath;
    private FileDialogViewMode _viewMode = FileDialogViewMode.Details;

    public ManagedFileDialogWindow(FileDialogMode mode, string? initialDirectory, IReadOnlyList<FileFilter>? filters, ManagedDialogExtras? extras = null)
    {
        _mode = mode;
        _filters = filters?.ToList() ?? new List<FileFilter> { new("All files", "*.*") };
        _browser.ActiveFilter = _filters[0];
        _browser.DirectoriesOnly = mode == FileDialogMode.SelectFolder;
        if (extras != null)
        {
            _browser.ShowHidden = extras.ShowHiddenFiles;
        }

        Title = ModeTitle(mode);
        this.Resizable(780, 560);

        _breadcrumb = new StackPanel().Horizontal().Spacing(2);
        _grid = BuildGrid();
        _iconView = BuildIconView();
        _viewHost = new Border { Child = _grid };
        _places = BuildPlaces();
        _fileName = new TextBox().TabIndex(6);

        var filterCombo = new ComboBox()
            .TabIndex(7)
            .Items(_filters.ConvertAll(f => f.Name).ToArray());
        filterCombo.SelectionChanged += _ =>
        {
            int index = filterCombo.SelectedIndex;
            if (index >= 0 && index < _filters.Count)
            {
                // Setting ActiveFilter re-filters the cached enumeration (no disk access); no Reload needed.
                _browser.ActiveFilter = _filters[index];
            }
        };
        filterCombo.SelectedIndex = filterCombo.ItemsSource.Count - 1;

        var bottom = new StackPanel()
            .Vertical()
            .Spacing(8);

        // Folder mode hides files, so no name/type rows; pickers get them on two lines.
        if (mode is FileDialogMode.OpenSingle or FileDialogMode.OpenMultiple or FileDialogMode.Save)
        {
            bottom.Add(new DockPanel().Spacing(8).Children(
                new TextBlock().Text("File name:").Width(70).CenterVertical().DockLeft(),
                _fileName.CenterVertical()));
            bottom.Add(new DockPanel().Spacing(8).Children(
                new TextBlock().Text("File type:").Width(70).CenterVertical().DockLeft(),
                filterCombo.CenterVertical()));
        }
        else
        {
            bottom.IsVisible(false);
        }

        var buttons = new StackPanel().Horizontal().Spacing(8).Right().Children(
            new Button().Content(AcceptText(mode)).Width(80).TabIndex(8).OnClick(OnAccept),
            new Button().Content("Cancel").Width(80).TabIndex(9).OnClick(OnCancel));

        // Joined nav cluster (back / forward / up): independent command segments wired via
        // PrepareContainer. Back/forward segments are captured so their enabled state can track history.
        var navItems = new[]
        {
            new NavAction("back", GlyphKind.ChevronLeft, "Back", () => _browser.GoBack()),
            new NavAction("forward", GlyphKind.ChevronRight, "Forward", () => _browser.GoForward()),
            new NavAction("up", GlyphKind.ChevronUp, "Up", () => _browser.GoParent()),
        };

        var navGroup = new ButtonGroup()
            .TabIndex(1)
            .ItemPadding(new Thickness(0))
            .Items(navItems, _ => string.Empty)
            .ItemTemplate<NavAction>(
                build: _ => new GlyphElement
                {
                    GlyphSize = 5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                bind: (view, item, _, _) => ((GlyphElement)view).Kind = item.Glyph)
            .PrepareContainer<NavAction>((seg, item, _) =>
            {
                seg.TabIndex(1).ToolTip(item.Tip);
                seg.Click += item.Run;
                seg.WithTheme((t, c) => c.Width(t.Metrics.BaseControlHeight));
                if (item.Id == "back") _backButton = seg;
                else if (item.Id == "forward") _forwardButton = seg;
            });

        FileDialogStyles.Ensure();
        _pathBox = new TextBox().TabIndex(2).StyleName(FileDialogStyles.NullTextBox).Padding(6, 0);
        _pathBox.OnKeyDown(e =>
        {
            if (e.Key == Key.Enter)
            {
                string target = _pathBox.Text ?? string.Empty;
                ExitPathEdit();
                _browser.Navigate(target);
                // Navigation rebuilt the breadcrumb, replacing the crumb ExitPathEdit re-homed onto.
                LastCrumbButton()?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ExitPathEdit();
                e.Handled = true;
            }
        });
        // Clicking away from the path box reverts to the breadcrumb.
        _pathBox.OnLostFocus(() => ExitPathEdit());

        // Address bar: breadcrumb by default; click the empty area to type a path and jump there.
        // Outer address-bar box stays; the inner editable TextBox is borderless (NullTextBox style).
        // Fixed height so toggling breadcrumb <-> text box does not change the toolbar height.
        // ContentControl (not Border) so IsFocusWithin drives the border: focusing a breadcrumb button
        // or the path box lights the box with an accent border, animated via the style's transition.
        // The base BorderBrush must come from the style so the Focused trigger can override it.
        _pathHost = new ContentControl
        {
            Content = _breadcrumb.CenterVertical(),
            Padding = new Thickness(0),
            StyleName = FileDialogStyles.AddressBar,
        };
        _pathHost.OnMouseDown(_ =>
        {
            if (!_editingPath)
            {
                EnterPathEdit();
            }
        });

        // Square icon-only segments, content-driven: height follows the control (BaseControlHeight, inner
        // ~26), and a 16px icon + 5px padding makes each segment's width match that, so no explicit size.
        var viewToggle = new SegmentedControl()
            .TabIndex(3)
            .DockRight()
            .ItemPadding(new Thickness(3, 3))
            .Items([FileDialogViewMode.Details, FileDialogViewMode.Icons], mode => mode.ToString())
            .ItemTemplate<FileDialogViewMode>(
                build: _ => new ViewModeIcon(),
                bind: (view, mode, _, _) => ((ViewModeIcon)view).IsGrid = mode == FileDialogViewMode.Icons)
            .PrepareContainer<FileDialogViewMode>((container, mode, _) =>
                container.TabIndex(3).ToolTip(mode == FileDialogViewMode.Icons ? "Grid" : "List")
                .WithTheme((t, c) => c.Width(t.Metrics.BaseControlHeight)))
            .SelectedIndex(_viewMode == FileDialogViewMode.Icons ? 1 : 0)
            .OnSelectionChanged(item => SetViewMode((FileDialogViewMode)item!));

        var top = new DockPanel()

            .Spacing(6)
            .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .DockLeft()
                    .Children(navGroup),
                viewToggle,
                _pathHost);

        Content = new DockPanel()
            .Margin(8)
            .Spacing(12)
            .Children(
                top.DockTop(),
                buttons.DockBottom(),
                bottom.DockBottom(),
                new SplitPanel() { MinFirst = 180, FirstLength = 180 }.Horizontal().First(_places.DockLeft()).Second(_viewHost));

        _browser.Changed += OnBrowserChanged;
        _browser.Navigating += OnBrowserNavigating;
        // Cancel any in-flight background enumeration on close so stale results aren't applied to a torn-down UI.
        Closed += () => _browser.Cancel();
        _browser.Navigate(initialDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public bool Accepted { get; private set; }

    public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();

    private int ActiveSelectedIndex =>
        _viewMode == FileDialogViewMode.Icons ? _iconView.SelectedIndex : _grid.SelectedIndex;

    private IReadOnlyList<int> ActiveSelectedIndices =>
        _viewMode == FileDialogViewMode.Icons ? _iconView.SelectedIndices : _grid.SelectedIndices;

    private static bool PathsEqual(string a, string b)
    {
        static string Normalize(string p) =>
            string.IsNullOrEmpty(p) ? p : p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string ModeTitle(FileDialogMode mode) => mode switch
    {
        FileDialogMode.OpenSingle => "Open File",
        FileDialogMode.OpenMultiple => "Open Files",
        FileDialogMode.Save => "Save File",
        FileDialogMode.SelectFolder => "Select Folder",
        _ => "File Dialog",
    };

    private static string AcceptText(FileDialogMode mode) => mode switch
    {
        FileDialogMode.Save => "_Save",
        FileDialogMode.SelectFolder => "_Select",
        _ => "_Open",
    };

    private GridView BuildGrid()
    {
        var grid = new GridView()
            .TabIndex(5)
            .RowHeight(26)
            .HeaderHeight(26)
            .CellPadding(new Thickness(8, 2))
            .Cached()
            .ZebraStriping()
            .Columns(
                new GridViewColumn<FileSystemEntry>()
                    .Header("Name")
                    .Width(340)
                    .Bind(
                        build: ctx => new DockPanel()
                            .Spacing(6)
                            .Children(
                                new FileIconElement(16).Register(ctx, "Icon").CenterVertical().DockLeft(),
                                new TextBlock().Register(ctx, "Text").CenterVertical().TextTrimming(TextTrimming.CharacterEllipsis)),
                        bind: (_, item, _, ctx) =>
                        {
                            var icon = ctx.Get<FileIconElement>("Icon");
                            icon.Kind = item.IsDirectory ? FileIconKind.Folder : FileIconKind.File;
                            icon.SetShellTarget(item.FullPath, item.IsDirectory);
                            ctx.Get<TextBlock>("Text").Text = item.Name;
                        }),
                new GridViewColumn<FileSystemEntry>()
                    .Header("Size")
                    .Width(80)
                    .Bind(
                        build: _ => new TextBlock().Margin(6, 0).CenterVertical().Right(),
                        bind: (view, item) => ((TextBlock)view).Text = item.IsDirectory ? string.Empty : FileSystemBrowser.FormatSize(item.Size)),
                new GridViewColumn<FileSystemEntry>()
                    .Header("Modified")
                    .Width(130)
                    .Bind(
                        build: _ => new TextBlock().Margin(6, 0).CenterVertical().TextTrimming(TextTrimming.CharacterEllipsis),
                        bind: (view, item) => ((TextBlock)view).Text = item.Modified.ToString("yyyy-MM-dd HH:mm")));

        grid.SelectionMode = _mode == FileDialogMode.OpenMultiple ? ItemsSelectionMode.Extended : ItemsSelectionMode.Single;
        grid.SelectionChanged += _ => OnViewSelectionChanged();
        grid.OnMouseDoubleClick(e =>
        {
            // Only navigate/open when a real data row is double-clicked - not the column header or the
            // empty area below the rows (both also raise the GridView's double-click).
            if (grid.TryGetCellIndexAt(e, out int row, out _, out bool isHeader) && !isHeader && row >= 0)
            {
                OnViewDoubleClick();
            }
        });
        return grid;
    }

    private ListBox BuildIconView()
    {
        var view = new ListBox()
            .TabIndex(5)
            .WrapPresenter(98, 88)
            .ZebraStriping(false)
            .Cached()
            .ItemTemplate<FileSystemEntry>(
                build: ctx => new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .Padding(6)
                    .Center()
                    .Children(
                        new FileIconElement(48).Register(ctx, "Icon").CenterHorizontal(),
                        new TextBlock().Register(ctx, "Text").Width(84).CenterHorizontal().TextAlignment(TextAlignment.Center).TextTrimming(TextTrimming.CharacterEllipsis)),
                bind: (_, item, _, ctx) =>
                {
                    var icon = ctx.Get<FileIconElement>("Icon");
                    icon.Kind = item.IsDirectory ? FileIconKind.Folder : FileIconKind.File;
                    icon.SetShellTarget(item.FullPath, item.IsDirectory);
                    ctx.Get<TextBlock>("Text").Text = item.Name;
                });

        view.SelectionMode = _mode == FileDialogMode.OpenMultiple ? ItemsSelectionMode.Extended : ItemsSelectionMode.Single;
        view.SelectionChanged += _ => OnViewSelectionChanged();
        view.OnMouseDoubleClick(_ => OnViewDoubleClick());
        return view;
    }

    private void SetViewMode(FileDialogViewMode mode)
    {
        if (_viewMode == mode)
        {
            return;
        }
        _viewMode = mode;
        _viewHost.Child = mode == FileDialogViewMode.Icons ? _iconView : _grid;
        RefreshActiveView();
    }

    private void RefreshActiveView()
    {
        var selectionMode = _mode == FileDialogMode.OpenMultiple ? ItemsSelectionMode.Extended : ItemsSelectionMode.Single;
        if (_viewMode == FileDialogViewMode.Icons)
        {
            _iconView.Items(_browser.Entries, e => e.Name);
            _iconView.SelectionMode = selectionMode;
        }
        else
        {
            _grid.ItemsSource(_browser.Entries);
            _grid.SelectionMode = selectionMode;
        }
    }

    private NavigationList BuildPlaces()
    {
        _placeItems.AddRange(PlacesProviders.Current.GetPlaces());

        // Headers (This PC / Favorites / Devices ...) are non-selectable rows via the kind selector, so they
        // are skipped by selection, hover, and keyboard navigation. The template styles them bold/dimmed/icon-less.
        var places = new NavigationList()
            .TabIndex(4)
            .Items(_placeItems,
                build: ctx => new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .Children(
                        new FileIconElement(16).Register(ctx, "Icon").CenterVertical(),
                        new TextBlock().Register(ctx, "Text").CenterVertical()),
                bind: (_, item, _, ctx) =>
                {
                    var icon = ctx.Get<FileIconElement>("Icon");
                    icon.IsVisible = !item.IsHeader;
                    icon.Kind = item.Kind;
                    icon.SetShellPlace(item.IsHeader ? null : item.Place, item.IsHeader ? null : item.Path);

                    var text = ctx.Get<TextBlock>("Text");
                    text.Text = item.Label;
                    text.FontWeight = item.IsHeader ? FontWeight.SemiBold : FontWeight.Normal;
                    text.WithTheme((t, c) => c.Foreground = item.IsHeader ? t.Palette.DisabledText : t.Palette.WindowText);
                },
                kind: p => p.IsHeader ? NavigationItemKind.Header : NavigationItemKind.Item);

        places.SelectionChanged += _ =>
        {
            if (_syncingPlaces)
            {
                return; // selection set programmatically to mirror the current path - don't navigate
            }
            int index = places.SelectedIndex;
            if (index >= 0 && index < _placeItems.Count && !_placeItems[index].IsHeader)
            {
                _browser.Navigate(_placeItems[index].Path);
            }
        };
        return places;
    }

    private void EnterPathEdit()
    {
        _editingPath = true;
        _pathBox.Text = _browser.CurrentDirectory;
        _pathHost.Content = _pathBox;
        _pathBox.Focus();
    }

    private void ExitPathEdit()
    {
        if (!_editingPath)
        {
            return;
        }
        _editingPath = false;

        // Capture intent before the swap: if the path box still holds focus (ESC / programmatic exit,
        // not a focus-out), re-home focus on the current crumb. The swap itself releases the path box's
        // focus during detach, so the re-home must be read here and applied after.
        bool reHome = _pathBox.IsFocused;

        _pathHost.Content = _breadcrumb;

        if (reHome)
        {
            LastCrumbButton()?.Focus();
        }
    }

    private Button? LastCrumbButton()
    {
        for (int i = _breadcrumb.Count - 1; i >= 0; i--)
        {
            if (_breadcrumb[i] is Button crumb)
            {
                return crumb;
            }
        }
        return null;
    }

    // A new navigation started: drop the stale file-list selection right away so an inaccessible or
    // still-loading drive doesn't leave the previously selected item highlighted (and feeding the name box).
    private void OnBrowserNavigating()
    {
        RefreshActiveView(); // entries were just cleared -> shows empty until the load completes
        _grid.SelectedIndex = -1;
        _iconView.SelectedIndex = -1;
    }

    private void OnBrowserChanged()
    {
        RefreshActiveView();
        _backButton.IsEnabled = _browser.CanGoBack;
        _forwardButton.IsEnabled = _browser.CanGoForward;
        RebuildBreadcrumb();
        SyncPlacesSelection();
    }

    // Right -> left: highlight the place whose path matches the current directory (or clear if none),
    // so the sidebar always reflects where you are, and a place can be re-clicked after leaving it.
    private void SyncPlacesSelection()
    {
        int match = -1;
        for (int i = 0; i < _placeItems.Count; i++)
        {
            if (!_placeItems[i].IsHeader && PathsEqual(_placeItems[i].Path, _browser.CurrentDirectory))
            {
                match = i;
                break;
            }
        }

        _syncingPlaces = true;
        _places.SelectedIndex = match;
        _syncingPlaces = false;
    }

    private void RebuildBreadcrumb()
    {
        _breadcrumb.Clear();
        string path = _browser.CurrentDirectory;
        var segments = new List<(string Label, string Path)>();

        var root = Path.GetPathRoot(path) ?? path;
        // Display the drive without the trailing separator ("C:\" -> "C:"); keep the full root for navigation.
        // Don't collapse a Unix root "/" to empty.
        string rootLabel = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (rootLabel.Length == 0)
        {
            rootLabel = root;
        }
        segments.Add((rootLabel, root));
        string remainder = path.Length > root.Length ? path[root.Length..] : string.Empty;
        string accumulated = root;
        foreach (var part in remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = Path.Combine(accumulated, part);
            segments.Add((part, accumulated));
        }

        bool first = true;
        foreach (var (label, segmentPath) in segments)
        {
            if (!first)
            {
                _breadcrumb.Add(new GlyphElement().Kind(GlyphKind.ChevronRight).GlyphSize(4).CenterVertical().WithTheme((t, c) => c.Foreground(t.Palette.DisabledText)));
            }
            first = false;

            string target = segmentPath;
            _breadcrumb.Add(new Button().Content(label, false).TabIndex(2).StyleName(BuiltInStyles.FlatButton)
                .OnClick(() => _browser.Navigate(target))
                .OnKeyDown(e =>
                {
                    // F2 is the Windows/Linux edit-in-place idiom. macOS uses Return, which here navigates,
                    // so Mac users reach text entry via the global Cmd+Shift+G instead.
                    if (e.Key == Key.F2 && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
                    {
                        EnterPathEdit();
                        e.Handled = true;
                    }
                }));
        }
    }

    private void OnViewSelectionChanged()
    {
        if (_mode is FileDialogMode.Save)
        {
            return;
        }

        int index = ActiveSelectedIndex;
        if (index < 0 || index >= _browser.Entries.Count)
        {
            return;
        }

        var entry = _browser.Entries[index];
        if (!entry.IsDirectory && _mode is FileDialogMode.OpenSingle or FileDialogMode.OpenMultiple)
        {
            _fileName.Text = entry.Name;
        }
    }

    private void OnViewDoubleClick()
    {
        int index = ActiveSelectedIndex;
        if (index < 0 || index >= _browser.Entries.Count)
        {
            return;
        }

        var entry = _browser.Entries[index];
        if (entry.IsDirectory)
        {
            _browser.Navigate(entry.FullPath);
        }
        else if (_mode is FileDialogMode.OpenSingle or FileDialogMode.OpenMultiple)
        {
            Accept([entry.FullPath]);
        }
    }

    private void OnAccept()
    {
        switch (_mode)
        {
            case FileDialogMode.SelectFolder:
            {
                int index = ActiveSelectedIndex;
                string folder = index >= 0 && index < _browser.Entries.Count && _browser.Entries[index].IsDirectory
                    ? _browser.Entries[index].FullPath
                    : _browser.CurrentDirectory;
                Accept([folder]);
                break;
            }

            case FileDialogMode.Save:
            {
                string name = _fileName.Text?.Trim() ?? string.Empty;
                if (name.Length == 0)
                {
                    return;
                }
                Accept([Path.Combine(_browser.CurrentDirectory, name)]);
                break;
            }

            case FileDialogMode.OpenMultiple:
            {
                var paths = new List<string>();
                foreach (int index in ActiveSelectedIndices)
                {
                    if (index >= 0 && index < _browser.Entries.Count && !_browser.Entries[index].IsDirectory)
                    {
                        paths.Add(_browser.Entries[index].FullPath);
                    }
                }
                if (paths.Count > 0)
                {
                    Accept(paths);
                }
                break;
            }

            default: // OpenSingle
            {
                int index = ActiveSelectedIndex;
                if (index >= 0 && index < _browser.Entries.Count && !_browser.Entries[index].IsDirectory)
                {
                    Accept([_browser.Entries[index].FullPath]);
                }
                break;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // A focused child consumes Escape first (path edit, open dropdown); an unhandled one cancels.
        base.OnKeyDown(e);

        if (!e.Handled && !_editingPath && IsEditPathShortcut(e))
        {
            EnterPathEdit();
            e.Handled = true;
            return;
        }

        if (!e.Handled && e.Key == Key.Escape)
        {
            e.Handled = true;
            OnCancel();
        }
    }

    // Switch the address bar to text entry, using each platform's convention.
    private static bool IsEditPathShortcut(KeyEventArgs e)
    {
        if (OperatingSystem.IsMacOS())
        {
            // Finder "Go to Folder".
            return e.MetaKey && e.ShiftKey && e.Key == Key.G;
        }

        // Windows + Linux/GTK location bar: Ctrl+L. Windows also allows Explorer's Alt+D.
        if (e.ControlKey && e.Key == Key.L)
        {
            return true;
        }
        return OperatingSystem.IsWindows() && e.AltKey && e.Key == Key.D;
    }

    private void OnCancel()
    {
        Accepted = false;
        SelectedPaths = Array.Empty<string>();
        Close();
    }

    private void Accept(IReadOnlyList<string> paths)
    {
        Accepted = true;
        SelectedPaths = paths;
        Close();
    }

    // View-mode toggle icon drawn directly as outlined rounded rectangles. Everything is snapped to the
    // device-pixel grid (work in integer pixels, divide back to DIP) so the strokes stay crisp at 100%/150%.
    // List = 3 stacked pills; grid = 2x2 tiles. Color follows the inherited Foreground (selection/theme).
    private sealed class ViewModeIcon : FrameworkElement
    {
        private bool _grid;

        public bool IsGrid
        {
            set
            {
                if (_grid == value)
                {
                    return;
                }
                _grid = value;
                InvalidateVisual();
            }
        }

        protected override Size MeasureContent(Size availableSize) => new(16, 16);

        protected override void OnRender(IGraphicsContext context)
        {
            double scale = GetDpi() / 96.0;
            if (scale <= 0)
            {
                scale = 1;
            }

            double extent = Math.Floor(Math.Min(Bounds.Width, Bounds.Height) * scale);
            if (extent < 2)
            {
                return;
            }

            double originX = Math.Round(Bounds.X * scale) + Math.Round((Bounds.Width * scale - extent) / 2);
            double originY = Math.Round(Bounds.Y * scale) + Math.Round((Bounds.Height * scale - extent) / 2);
            double stroke = Math.Max(1, scale * 1);
            double radius = Math.Max(1, extent * 0.12);
            var color = GetValue(Control.ForegroundProperty);

            void Outline(double x, double y, double width, double height, bool fill)
            {
                var rect = new Rect((originX + x) / scale, (originY + y) / scale, width / scale, height / scale);
                double r = Math.Min(radius, Math.Min(width, height) / 2) / scale;
                if (fill)
                {
                    context.FillRoundedRectangle(rect, r, r, color);
                }
                else
                {
                    context.DrawRoundedRectangle(rect, r, r, color, stroke, true);
                }
            }

            if (_grid)
            {
                double gap = Math.Max(1, Math.Round(2 * scale));
                double cell = Math.Floor((extent - gap) / 2);
                double offset = Math.Round((extent - (cell * 2 + gap)) / 2);
                Outline(offset, offset, cell, cell, false);
                Outline(offset + cell + gap, offset, cell, cell, false);
                Outline(offset, offset + cell + gap, cell, cell, false);
                Outline(offset + cell + gap, offset + cell + gap, cell, cell, false);
            }
            else
            {
                double gap = Math.Max(1, Math.Round(3 * scale));
                double rowHeight = Math.Floor((extent - 2 * gap) / 4);
                double offset = Math.Round((extent - (rowHeight * 3 + 2 * gap)) / 2);
                Outline(0, offset, extent, rowHeight, false);
                Outline(0, offset + rowHeight + gap, extent, rowHeight, false);
                Outline(0, offset + 2 * (rowHeight + gap), extent, rowHeight, false);
            }
        }
    }
}

internal enum FileDialogMode
{
    OpenSingle,
    OpenMultiple,
    Save,
    SelectFolder,
}

internal enum FileDialogViewMode
{
    Details,
    Icons,
}

/// <summary>
/// Concept styles for the managed dialog. <see cref="NullTextBox"/> is a borderless, transparent TextBox
/// so the address bar shows no box around the editable path.
/// </summary>
internal static class FileDialogStyles
{
    public const string NullTextBox = "null-textbox";
    public const string AddressBar = "address-bar";

    private static bool _registered;

    public static void Ensure()
    {
        if (_registered)
        {
            return;
        }
        _registered = true;

        Application.Current.StyleSheet.Define(NullTextBox, () => new Style(typeof(TextBox))
        {
            BasedOn = Style.ForType<TextBox>(),
            Setters =
            [
                Setter.Create(Control.BorderThicknessProperty, 0.0),
                Setter.Create(Control.BackgroundProperty,  Color.Transparent),
            ],
        });

        Application.Current.StyleSheet.Define(AddressBar, () => new Style(typeof(ContentControl))
        {
            Transitions = [Transition.Create(Control.BorderBrushProperty)],
            Setters =
            [
                Setter.Create(Control.BorderThicknessProperty, t => t.Metrics.ControlBorderThickness),
                Setter.Create(Control.BorderBrushProperty, t => t.Palette.ControlBorder),
                Setter.Create(Control.CornerRadiusProperty, t => t.Metrics.ControlCornerRadius),
                Setter.Create(Control.BackgroundProperty, Color.Transparent),
                Setter.Create(FrameworkElement.HeightProperty, t => t.Metrics.BaseControlHeight),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(Control.BorderBrushProperty, t => t.Palette.Accent)],
                },
            ],
        });
    }
}
