// Preview tool window surface, the WPF counterpart of tools/vscode-mewui/src/panel.ts:
// toolbar (project/target pickers, zoom, theme, refresh, restart, status), frame image, and
// input forwarding. Frames arrive as BGRA buffers and blit straight into a Bgra32
// WriteableBitmap; after each blit the session gets a FrameAck (flow control, plan.md 4.3.1).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Aprillz.MewUI.VisualStudio.Session;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace Aprillz.MewUI.VisualStudio
{
    internal sealed class PreviewToolWindowControl : UserControl
    {
        private const double VIEWPORT_PADDING = 16;

        private readonly ComboBox _projectCombo = new ComboBox { MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _startButton = new Button { Content = "Start", Padding = new Thickness(8, 2, 8, 2) };
        private readonly ComboBox _targetCombo = new ComboBox { MinWidth = 140, VerticalAlignment = VerticalAlignment.Center, IsEnabled = false };
        private readonly ComboBox _zoomCombo = new ComboBox { Width = 64, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _themeButton = new Button { Content = "Theme", Padding = new Thickness(8, 2, 8, 2), IsEnabled = false };
        private readonly Button _refreshButton = new Button { Content = "Refresh", Padding = new Thickness(8, 2, 8, 2), IsEnabled = false, ToolTip = "Rebuild the current target" };
        private readonly Button _restartButton = new Button { Content = "Restart", Padding = new Thickness(8, 2, 8, 2), IsEnabled = false, ToolTip = "Restart the preview session (full state reset)" };
        private readonly CheckBox _navigateCheck = new CheckBox { Content = "Go to code", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Jump to the target's declaration when the preview target changes" };
        private readonly TextBlock _stateText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
        private readonly Image _image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Focusable = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        private readonly ScrollViewer _surface = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        private readonly TextBox _detailText = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160, Visibility = Visibility.Collapsed, Foreground = Brushes.IndianRed, FontFamily = new FontFamily("Consolas"), BorderThickness = new Thickness(0, 1, 0, 0) };

        private readonly DispatcherTimer _viewportTimer;
        private PreviewSessionClient _session;
        private WriteableBitmap _bitmap;
        private double _lastFrameDpiScale = 1;
        private string _themeMode = string.Empty;
        private List<PreviewTargetInfo> _targets = new List<PreviewTargetInfo>();
        private string _activeTargetId = string.Empty;
        private bool _updatingCombos;
        // A manual dropdown pick wins over auto-match until the user moves to another editor file.
        private bool _manualOverride;
        // The editor file auto-match last followed; reactivating it (or our own navigation to it) is not a move.
        private string _lastEditorDocumentPath;
        // Only the latest project scan may fill the picker; an older one finishing late is dropped.
        private int _populateVersion;

        /// <summary>Session log sink (the package routes it to an output window pane).</summary>
        public Action<string> LogSink { get; set; }

        /// <summary>Returns the active document's full path, or null (wired to DTE by the package).</summary>
        public Func<string> ActiveDocumentPathProvider { get; set; }

        /// <summary>Returns the project file containing the active document, or null (wired to DTE by the package).</summary>
        public Func<string> ActiveDocumentProjectProvider { get; set; }

        /// <summary>Returns the solution root directory, or null (wired to DTE by the package).</summary>
        public Func<string> SolutionDirectoryProvider { get; set; }

        /// <summary>Returns the project files loaded in the solution; empty when unknown (wired by the package).</summary>
        public Func<IReadOnlyList<string>> SolutionProjectsProvider { get; set; }

        /// <summary>Opens a source file at a 1-based line in the editor (wired to DTE by the package).</summary>
        public Action<string, int?> NavigateToSource { get; set; }

        /// <summary>Persists the "Go to code" checkbox (wired to the VS settings store by the package).</summary>
        public Action<bool> SaveNavigatePreference { get; set; }

        /// <summary>Applies the persisted "Go to code" preference and starts persisting changes.</summary>
        public void InitializeNavigatePreference(bool navigateOnSelect)
        {
            _navigateCheck.IsChecked = navigateOnSelect;
            _navigateCheck.Checked += (_, __) => SaveNavigatePreference?.Invoke(true);
            _navigateCheck.Unchecked += (_, __) => SaveNavigatePreference?.Invoke(false);
        }

        public PreviewToolWindowControl()
        {
            _viewportTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _viewportTimer.Tick += (_, __) =>
            {
                _viewportTimer.Stop();
                PostViewport();
            };

            foreach (string zoom in new[] { "Fit", "50%", "100%", "150%", "200%" })
            {
                _zoomCombo.Items.Add(zoom);
            }
            _zoomCombo.SelectedIndex = 0;

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
            toolbar.Children.Add(_projectCombo);
            toolbar.Children.Add(Spaced(_startButton));
            toolbar.Children.Add(Spaced(_targetCombo));
            toolbar.Children.Add(Spaced(_zoomCombo));
            toolbar.Children.Add(Spaced(_themeButton));
            toolbar.Children.Add(Spaced(_refreshButton));
            toolbar.Children.Add(Spaced(_restartButton));
            toolbar.Children.Add(Spaced(_navigateCheck));
            toolbar.Children.Add(_stateText);

            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _surface.Content = _image;

            var root = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            DockPanel.SetDock(_detailText, Dock.Bottom);
            root.Children.Add(toolbar);
            root.Children.Add(_detailText);
            root.Children.Add(_surface);
            Content = root;

            _startButton.Click += (_, __) => RunOnUi(ToggleSessionAsync, "mewui/preview/toggle");
            _refreshButton.Click += (_, __) => _session?.RefreshTarget();
            _restartButton.Click += (_, __) => _session?.RestartProcess();
            _themeButton.Click += (_, __) => _session?.SetTheme(_themeMode == "dark" ? "light" : "dark");
            _zoomCombo.SelectionChanged += (_, __) =>
            {
                ApplyZoom();
                PostViewport();
            };
            _targetCombo.SelectionChanged += OnTargetSelected;
            _projectCombo.DropDownOpened += (_, __) => RunOnUi(PopulateProjectsAsync, "mewui/preview/projects");
            _surface.SizeChanged += (_, __) =>
            {
                ApplyZoom();
                _viewportTimer.Stop();
                _viewportTimer.Start();
            };

            HookInput();
            Loaded += (_, __) => RunOnUi(PopulateProjectsAsync, "mewui/preview/projects");
            ShowState("Not running. Pick a project and press Start.", isError: false);
        }

        private static FrameworkElement Spaced(FrameworkElement element)
        {
            element.Margin = new Thickness(6, 0, 0, 0);
            return element;
        }

        private static void RunOnUi(Func<Task> work, string faultEventName)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await work();
            }).FileAndForget(faultEventName);
        }

        private static void PostToUi(Action action)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                action();
            }).FileAndForget("mewui/preview/sessionevent");
        }

        // ---- session lifecycle -------------------------------------------------------------

        /// <summary>
        /// Follows the editor when the user moves to a different file: selects the target declared
        /// there, overriding a manual dropdown pick. Reactivating the file already followed (for
        /// example after "Go to code" opened it) changes nothing.
        /// </summary>
        public void OnEditorDocumentActivated(string fsPath)
        {
            if (fsPath == null
                || (_lastEditorDocumentPath != null && ProjectLocator.PathsEqual(_lastEditorDocumentPath, fsPath)))
            {
                return;
            }
            _lastEditorDocumentPath = fsPath;
            AutoMatchTarget(fsPath, fromEditor: true);
        }

        /// <summary>Feeds document saves to the buildRestart driver (no-op under watch).</summary>
        public void NotifySourceChanged(string fsPath) => _session?.NotifySourceChanged(fsPath);

        /// <summary>Stops the running session, if any, without waiting for its processes to exit.</summary>
        public void StopSession()
        {
            var session = _session;
            _session = null;
            session?.Stop();
            SetSessionUiEnabled(false);
        }

        /// <summary>
        /// Starts a session when the loaded solution names one project unambiguously (the active
        /// document's executable project, or the only executable project); a no-op when one is running.
        /// </summary>
        public async Task AutoStartAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_session != null)
            {
                return;
            }
            ProjectCandidates candidates = await PopulateProjectsAsync();
            if (candidates == null || _session != null)
            {
                return;
            }
            if (candidates.AutoStartProject != null)
            {
                SelectProject(candidates.AutoStartProject);
                StartSession(candidates.AutoStartProject);
            }
            else if (candidates.SolutionExecutables.Count > 1)
            {
                ShowState("Several executable projects in the solution. Pick one and press Start.", isError: false);
            }
            else
            {
                ShowState("No executable project in the loaded solution. Pick a project and press Start.", isError: false);
            }
        }

        private async Task ToggleSessionAsync()
        {
            if (_session != null)
            {
                StopSession();
                return;
            }

            string projectPath = (_projectCombo.SelectedItem as ProjectItemEntry)?.Path;
            if (projectPath == null)
            {
                await PopulateProjectsAsync();
                if (_session != null)
                {
                    return;
                }
                projectPath = (_projectCombo.SelectedItem as ProjectItemEntry)?.Path;
            }
            if (projectPath == null)
            {
                ShowState("No executable (.csproj with OutputType Exe/WinExe) project found.", isError: true);
                return;
            }
            StartSession(projectPath);
        }

        private void StartSession(string projectPath)
        {
            var session = new PreviewSessionClient(projectPath, new SessionOptions());
            session.Log += line => PostToUi(() => LogSink?.Invoke(line));
            session.StateChanged += (state, detail) => PostToUi(() => OnSessionState(session, state, detail));
            session.TargetsChanged += (targets, activeId) => PostToUi(() => OnTargets(session, targets, activeId));
            session.FrameReceived += (header, pixels) => PostToUi(() => OnFrame(session, header, pixels));
            session.StatusChanged += status => PostToUi(() => OnStatus(session, status));
            _session = session;
            SetSessionUiEnabled(true);
            // Seeds the viewport the session replays to the app on every connect.
            PostViewport();

            try
            {
                session.Start();
            }
            catch (Exception error)
            {
                StopSession();
                ShowState($"failed to start: {error.Message}", isError: true);
            }
        }

        private void SetSessionUiEnabled(bool running)
        {
            _startButton.Content = running ? "Stop" : "Start";
            _projectCombo.IsEnabled = !running;
            _targetCombo.IsEnabled = running;
            _themeButton.IsEnabled = running;
            _refreshButton.IsEnabled = running;
            _restartButton.IsEnabled = running;
            if (!running)
            {
                ShowState("Stopped.", isError: false);
            }
        }

        /// <summary>Rescans the projects off the UI thread and refills the picker; null when a newer scan superseded this one.</summary>
        private async Task<ProjectCandidates> PopulateProjectsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            int version = ++_populateVersion;
            IReadOnlyList<string> solutionProjects = SolutionProjectsProvider?.Invoke() ?? Array.Empty<string>();
            string solutionDirectory = SolutionDirectoryProvider?.Invoke();
            string activePath = ActiveDocumentPathProvider?.Invoke();
            string activeProject = ActiveDocumentProjectProvider?.Invoke();

            await TaskScheduler.Default;
            ProjectCandidates candidates = ProjectLocator.ResolveCandidates(
                solutionProjects, solutionDirectory, activePath, activeProject);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (version != _populateVersion)
            {
                return null;
            }

            string previous = (_projectCombo.SelectedItem as ProjectItemEntry)?.Path;
            _updatingCombos = true;
            _projectCombo.Items.Clear();
            foreach (string project in candidates.Projects)
            {
                _projectCombo.Items.Add(new ProjectItemEntry(project));
            }
            _updatingCombos = false;

            if (!SelectProject(previous ?? candidates.Preferred) && _projectCombo.Items.Count > 0)
            {
                _projectCombo.SelectedIndex = 0;
            }
            return candidates;
        }

        private bool SelectProject(string projectPath)
        {
            if (projectPath == null)
            {
                return false;
            }
            for (int index = 0; index < _projectCombo.Items.Count; index++)
            {
                if (ProjectLocator.PathsEqual(((ProjectItemEntry)_projectCombo.Items[index]).Path, projectPath))
                {
                    _projectCombo.SelectedIndex = index;
                    return true;
                }
            }
            return false;
        }

        private sealed class ProjectItemEntry
        {
            public ProjectItemEntry(string path) => Path = path;
            public string Path { get; }
            public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
        }

        // ---- session events ----------------------------------------------------------------

        private void OnSessionState(PreviewSessionClient session, SessionState state, string detail)
        {
            LogSink?.Invoke($"[session] {state}{(detail != null ? $": {detail}" : "")}");
            if (session != _session)
            {
                return;
            }
            switch (state)
            {
                case SessionState.Failed:
                case SessionState.Disconnected:
                    ShowState($"{state}{(detail != null ? $": {detail}" : "")}", isError: true);
                    break;
                case SessionState.Starting:
                    ShowState(detail != null ? "Restarting..." : "Starting...", isError: false);
                    break;
            }
        }

        private void OnTargets(PreviewSessionClient session, PreviewTargetInfo[] targets, string activeId)
        {
            if (session != _session)
            {
                return;
            }
            _targets = targets.ToList();
            _activeTargetId = activeId;
            SyncTargetCombo();
            string activePath = ActiveDocumentPathProvider?.Invoke();
            if (_lastEditorDocumentPath == null)
            {
                _lastEditorDocumentPath = activePath;
            }
            AutoMatchTarget(activePath);
        }

        /// <summary>
        /// Selects the target declared in the given file. Editor-driven calls (the user moved to
        /// another file) override a manual dropdown pick; refresh-driven calls must not, or a
        /// targets rebroadcast would silently revert the pick while its file is open.
        /// </summary>
        private void AutoMatchTarget(string fsPath, bool fromEditor = false)
        {
            if (_session == null || fsPath == null || !fsPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!fromEditor && _manualOverride)
            {
                return;
            }
            PreviewTargetInfo match = _targets.FirstOrDefault(target =>
                target.Available && target.SourcePath != null && ProjectLocator.PathsEqual(target.SourcePath, fsPath));
            if (match != null && match.Id != _activeTargetId)
            {
                LogSink?.Invoke($"[session] auto-selecting {match.Id} for {System.IO.Path.GetFileName(fsPath)}");
                _activeTargetId = match.Id;
                _manualOverride = false;
                _session.SelectTarget(match.Id);
                SyncTargetCombo();
            }
        }

        private void SyncTargetCombo()
        {
            _updatingCombos = true;
            _targetCombo.Items.Clear();
            foreach (PreviewTargetInfo target in _targets)
            {
                string label = target.Kind == "main" ? target.DisplayName : $"{target.DisplayName} ({target.Kind})";
                var item = new ComboBoxItem
                {
                    Content = target.Available ? label : $"{label} - unavailable",
                    Tag = target.Id,
                    IsEnabled = target.Available,
                    ToolTip = target.Available ? null : target.Reason,
                };
                if (target.Id == _activeTargetId)
                {
                    item.IsSelected = true;
                }
                _targetCombo.Items.Add(item);
            }
            _updatingCombos = false;
        }

        private void OnTargetSelected(object sender, SelectionChangedEventArgs args)
        {
            if (_updatingCombos || !(_targetCombo.SelectedItem is ComboBoxItem item))
            {
                return;
            }
            _activeTargetId = (string)item.Tag;
            _manualOverride = true;
            _session?.SelectTarget(_activeTargetId);

            // Reverse sync (opt-in): picking a target jumps the editor to its declaration.
            if (_navigateCheck.IsChecked == true)
            {
                PreviewTargetInfo target = _targets.FirstOrDefault(candidate => candidate.Id == _activeTargetId);
                if (target?.SourcePath != null)
                {
                    // The activation this navigation causes must not count as the user moving to that file.
                    _lastEditorDocumentPath = target.SourcePath;
                    NavigateToSource?.Invoke(target.SourcePath, target.SourceLine);
                }
            }
        }

        private void OnStatus(PreviewSessionClient session, StatusInfo status)
        {
            if (session != _session)
            {
                return;
            }
            ShowState(status.Message, status.HasError);
            _detailText.Text = status.ExceptionDetail ?? string.Empty;
            _detailText.Visibility = string.IsNullOrEmpty(status.ExceptionDetail) ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrEmpty(status.ThemeMode))
            {
                _themeMode = status.ThemeMode;
                _themeButton.Content = _themeMode == "dark" ? "Dark" : _themeMode == "light" ? "Light" : "System";
            }
        }

        private void OnFrame(PreviewSessionClient session, FrameHeader header, byte[] pixels)
        {
            if (session != _session)
            {
                return;
            }
            try
            {
                double dpiScale = header.DpiScale > 0 ? header.DpiScale : 1;
                bool recreate = _bitmap == null || _bitmap.PixelWidth != header.Width || _bitmap.PixelHeight != header.Height
                    || dpiScale != _lastFrameDpiScale;
                _lastFrameDpiScale = dpiScale;
                if (recreate)
                {
                    // At 96 x dpiScale the bitmap's natural size is the app's size in DIPs, matching WPF DIPs at 100%.
                    _bitmap = new WriteableBitmap(header.Width, header.Height, 96 * dpiScale, 96 * dpiScale, PixelFormats.Bgra32, null);
                    _image.Source = _bitmap;
                    ApplyZoom();
                }
                _bitmap.WritePixels(new Int32Rect(0, 0, header.Width, header.Height), pixels, header.Stride, 0);
            }
            finally
            {
                // Acknowledged even when the blit fails, or the server would hold every later frame.
                session.AckFrame(header.Seq);
            }
        }

        private void ShowState(string message, bool isError)
        {
            _stateText.Text = message;
            _stateText.Foreground = isError ? Brushes.IndianRed : SystemColors.ControlTextBrush;
            _stateText.ToolTip = message;
        }

        // ---- viewport / zoom ---------------------------------------------------------------

        private double ZoomFactor()
        {
            string zoom = _zoomCombo.SelectedItem as string ?? "Fit";
            if (zoom == "Fit")
            {
                return 1;
            }
            else
            {
                return int.Parse(zoom.TrimEnd('%')) / 100.0;
            }
        }

        private void PostViewport()
        {
            if (_session == null || _surface.ViewportWidth <= 0)
            {
                return;
            }
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            _session.SetViewport(
                Math.Max(1, _surface.ViewportWidth - VIEWPORT_PADDING),
                Math.Max(1, _surface.ViewportHeight - VIEWPORT_PADDING),
                96 * dpi.DpiScaleX * ZoomFactor());
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            PostViewport();
            ApplyZoom();
        }

        /// <summary>
        /// Zoom rides the requested DPI: the session re-renders the vectors at the zoomed scale
        /// and the image shows one frame pixel per device pixel, so zoom stays crisp. Fit shows
        /// the natural-scale render at its own size and only ever scales it down.
        /// </summary>
        private void ApplyZoom()
        {
            if (_bitmap == null)
            {
                return;
            }
            string zoom = _zoomCombo.SelectedItem as string ?? "Fit";
            if (zoom == "Fit")
            {
                _image.Width = double.NaN;
                _image.Height = double.NaN;
                _image.MaxWidth = Math.Max(1, _surface.ViewportWidth - VIEWPORT_PADDING);
                _image.MaxHeight = Math.Max(1, _surface.ViewportHeight - VIEWPORT_PADDING);
                _image.StretchDirection = StretchDirection.DownOnly;
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            }
            else
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                _image.MaxWidth = double.PositiveInfinity;
                _image.MaxHeight = double.PositiveInfinity;
                _image.Width = _bitmap.PixelWidth / dpi.DpiScaleX;
                _image.Height = _bitmap.PixelHeight / dpi.DpiScaleY;
                // The explicit size can exceed the natural size (a zoomed frame is denser than one app DIP per WPF DIP).
                _image.StretchDirection = StretchDirection.Both;
                RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
            }
        }

        // ---- input forwarding ---------------------------------------------------------------

        private void HookInput()
        {
            _image.MouseDown += (_, args) =>
            {
                _image.Focus();
                _image.CaptureMouse();
                _session?.SendInput(PreviewProtocol.POINTER_PRESSED, PointerBody(args, args.ChangedButton, args.ClickCount));
                args.Handled = true;
            };
            _image.MouseMove += (_, args) =>
                _session?.SendInput(PreviewProtocol.POINTER_MOVED, PointerBody(args, null, 1));
            _image.MouseUp += (_, args) =>
            {
                _image.ReleaseMouseCapture();
                _session?.SendInput(PreviewProtocol.POINTER_RELEASED, PointerBody(args, args.ChangedButton, args.ClickCount));
                args.Handled = true;
            };
            _image.MouseWheel += (_, args) =>
            {
                Point point = ToDip(args.GetPosition(_image));
                // WPF +Delta = wheel up; the wire convention is +Y = scroll-up intent in notches.
                _session?.SendInput(PreviewProtocol.SCROLL, new
                {
                    x = point.X,
                    y = point.Y,
                    deltaX = 0.0,
                    deltaY = args.Delta / 120.0,
                    modifiers = W3cInput.Modifiers(Keyboard.Modifiers),
                });
                args.Handled = true;
            };
            _image.KeyDown += (_, args) => OnImageKey(args, isDown: true);
            _image.KeyUp += (_, args) => OnImageKey(args, isDown: false);
            _image.TextInput += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Text) && !char.IsControl(args.Text[0]))
                {
                    _session?.SendInput(PreviewProtocol.TEXT_INPUT, new { text = args.Text });
                }
            };
        }

        private void OnImageKey(KeyEventArgs args, bool isDown)
        {
            Key key = args.Key == Key.System ? args.SystemKey : args.Key;
            // Unmapped keys are still sent: every key down resets the server's typed-text suppression.
            string code = W3cInput.KeyCode(key) ?? W3cInput.UNIDENTIFIED_KEY_CODE;
            _session?.SendInput(PreviewProtocol.KEY, new
            {
                code,
                isDown,
                modifiers = W3cInput.Modifiers(Keyboard.Modifiers),
            });

            // Mirror the native key-then-character sequence for keys WPF's TextInput skips.
            if (isDown && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
            {
                if (key == Key.Enter)
                {
                    _session?.SendInput(PreviewProtocol.TEXT_INPUT, new { text = "\r" });
                }
                else if (key == Key.Tab)
                {
                    _session?.SendInput(PreviewProtocol.TEXT_INPUT, new { text = "\t" });
                }
            }

            // Keep focus-navigation and scrolling keys inside the preview surface.
            if (key == Key.Tab || key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down || key == Key.Space)
            {
                args.Handled = true;
            }
        }

        private object PointerBody(MouseEventArgs args, MouseButton? button, int clickCount)
        {
            Point point = ToDip(args.GetPosition(_image));
            return new
            {
                x = point.X,
                y = point.Y,
                button = button != null ? W3cInput.Button(button.Value) : 0,
                buttons = W3cInput.Buttons(args),
                clickCount = clickCount > 0 ? clickCount : 1,
                modifiers = W3cInput.Modifiers(Keyboard.Modifiers),
            };
        }

        /// <summary>Converts a position on the displayed image to window DIPs of the previewed app.</summary>
        private Point ToDip(Point position)
        {
            double scale = _bitmap != null && _image.ActualWidth > 0 ? _bitmap.PixelWidth / _image.ActualWidth : 1;
            return new Point(
                position.X * scale / _lastFrameDpiScale,
                position.Y * scale / _lastFrameDpiScale);
        }
    }
}
