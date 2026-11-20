using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using SolutionEvents = Microsoft.VisualStudio.Shell.Events.SolutionEvents;
using Task = System.Threading.Tasks.Task;

namespace Aprillz.MewUI.VisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("MewUI Preview", "Live editor preview for MewUI windows and user controls.", "0.1")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(PreviewToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids.SolutionExplorer)]
    [Guid(PACKAGE_GUID)]
    public sealed class MewUIPreviewPackage : AsyncPackage
    {
        public const string PACKAGE_GUID = "6b4084d0-1b60-49a8-9255-07401588f550";
        public const string COMMAND_SET_GUID = "19af801b-62f4-4b49-a83b-023df968a6a6";
        public const int CMDID_PREVIEW_WINDOW = 0x0100;
        public const int CMDID_OPEN_PREVIEW_CONTEXT = 0x0101;
        private const string SETTINGS_COLLECTION = "MewUI.Preview";
        private const string NAVIGATE_PROPERTY = "NavigateOnSelect";
        private const uint PROJECT_FILE_FLAGS = (uint)__VSGETPROJFILESFLAGS.GPFF_SKIPUNLOADEDPROJECTS;

        private DTE2 _dte;
        private IVsSolution _solution;
        // COM event sources are advised through these wrappers; dropping the references
        // silently disconnects the sinks, so they live for the package lifetime.
        private DocumentEvents _documentEvents;
        private WindowEvents _windowEvents;
        private IVsOutputWindowPane _outputPane;
        private PreviewToolWindow _toolWindow;
        private PreviewToolWindowControl _control;
        private bool _servicesReady;
        // Set while "Go to code" opens a file, so the window activation it raises is not a user move.
        private bool _navigating;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var commandService = (OleMenuCommandService)await GetServiceAsync(typeof(IMenuCommandService));
            commandService?.AddCommand(new MenuCommand(
                (_, __) => ShowPreviewWindow(),
                new CommandID(new Guid(COMMAND_SET_GUID), CMDID_PREVIEW_WINDOW)));
            commandService?.AddCommand(new MenuCommand(
                (_, __) => ShowPreviewWindow(),
                new CommandID(new Guid(COMMAND_SET_GUID), CMDID_OPEN_PREVIEW_CONTEXT)));
        }

        /// <summary>Wires a preview tool window however it was created: menu command or layout restore at startup.</summary>
        internal void AttachToolWindow(PreviewToolWindow pane)
        {
            JoinableTaskFactory.RunAsync(() => AttachToolWindowAsync(pane)).FileAndForget("mewui/preview/attachwindow");
        }

        private void ShowPreviewWindow()
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                ToolWindowPane window = await ShowToolWindowAsync(typeof(PreviewToolWindow), 0, create: true, DisposalToken);
                if (window is PreviewToolWindow pane)
                {
                    await AttachToolWindowAsync(pane);
                }
            }).FileAndForget("mewui/preview/showwindow");
        }

        private async Task AttachToolWindowAsync(PreviewToolWindow pane)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var control = (PreviewToolWindowControl)pane.Content;
            if (_control == control)
            {
                return;
            }
            _toolWindow = pane;
            _control = control;

            if (!await EnsureServicesAsync())
            {
                return;
            }
            WireControl(control);

            // An open window is the intent to preview; one restored before the solution loads starts on the load event.
            if (KnownUIContexts.SolutionExistsAndFullyLoadedContext.IsActive)
            {
                await control.AutoStartAsync();
            }
        }

        private async System.Threading.Tasks.Task<bool> EnsureServicesAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (_servicesReady)
            {
                return true;
            }
            var dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
            var solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            var outputWindow = await GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            if (_servicesReady)
            {
                return true;
            }
            if (dte == null || outputWindow == null)
            {
                return false;
            }
            _dte = dte;
            _solution = solution;

            var paneGuid = new Guid(PACKAGE_GUID);
            outputWindow.CreatePane(ref paneGuid, "MewUI Preview", fInitVisible: 1, fClearWithSolution: 1);
            outputWindow.GetPane(ref paneGuid, out _outputPane);

            Events2 events = (Events2)_dte.Events;
            _documentEvents = events.DocumentEvents;
            _documentEvents.DocumentSaved += OnDocumentSaved;
            _windowEvents = events.WindowEvents;
            _windowEvents.WindowActivated += OnWindowActivated;
            SolutionEvents.OnBeforeCloseSolution += OnBeforeCloseSolution;
            KnownUIContexts.SolutionExistsAndFullyLoadedContext.UIContextChanged += OnSolutionLoadedChanged;
            _servicesReady = true;
            return true;
        }

        private void WireControl(PreviewToolWindowControl control)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            control.LogSink = line =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _outputPane?.OutputStringThreadSafe($"[{DateTime.Now:HH:mm:ss.fff}] {line}\n");
            };
            control.ActiveDocumentPathProvider = () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    return _dte?.ActiveDocument?.FullName;
                }
                catch (Exception)
                {
                    return null;
                }
            };
            control.ActiveDocumentProjectProvider = () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    string projectPath = _dte?.ActiveDocument?.ProjectItem?.ContainingProject?.FullName;
                    return string.IsNullOrEmpty(projectPath) ? null : projectPath;
                }
                catch (Exception)
                {
                    return null;
                }
            };
            control.SolutionDirectoryProvider = () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return GetSolutionDirectory();
            };
            control.SolutionProjectsProvider = () =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return GetSolutionProjectFiles();
            };
            var settingsStore = new ShellSettingsManager(this).GetWritableSettingsStore(SettingsScope.UserSettings);
            if (!settingsStore.CollectionExists(SETTINGS_COLLECTION))
            {
                settingsStore.CreateCollection(SETTINGS_COLLECTION);
            }
            control.SaveNavigatePreference = value =>
                settingsStore.SetBoolean(SETTINGS_COLLECTION, NAVIGATE_PROPERTY, value);
            control.InitializeNavigatePreference(
                settingsStore.GetBoolean(SETTINGS_COLLECTION, NAVIGATE_PROPERTY, defaultValue: true));

            control.NavigateToSource = (sourcePath, sourceLine) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _navigating = true;
                try
                {
                    _dte.ItemOperations.OpenFile(sourcePath);
                    if (sourceLine != null && _dte.ActiveDocument?.Selection is TextSelection selection)
                    {
                        selection.GotoLine(sourceLine.Value, Select: false);
                    }
                }
                catch (Exception)
                {
                    // The scanned path may no longer exist (renamed file); the selection itself still applies.
                }
                finally
                {
                    _navigating = false;
                }
            };
        }

        private void OnDocumentSaved(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                _control?.NotifySourceChanged(document.FullName);
            }
            catch (Exception)
            {
                // The document can be gone by the time the event reaches us; a missed save only skips one restart.
            }
        }

        private void OnWindowActivated(Window gotFocus, Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_navigating)
            {
                return;
            }
            string path;
            try
            {
                path = gotFocus?.Document?.FullName;
            }
            catch (Exception)
            {
                path = null;
            }
            if (path != null)
            {
                _control?.OnEditorDocumentActivated(path);
            }
        }

        private void OnBeforeCloseSolution(object sender, EventArgs args)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _control?.StopSession();
        }

        private void OnSolutionLoadedChanged(object sender, UIContextChangedEventArgs args)
        {
            if (!args.Activated)
            {
                return;
            }
            JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                if (_control != null && IsToolWindowOpen())
                {
                    await _control.AutoStartAsync();
                }
            }).FileAndForget("mewui/preview/autostart");
        }

        private bool IsToolWindowOpen()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _toolWindow?.Frame is IVsWindowFrame frame && frame.IsVisible() == VSConstants.S_OK;
        }

        private string GetSolutionDirectory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                string solutionPath = _dte?.Solution?.FullName;
                return string.IsNullOrEmpty(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private IReadOnlyList<string> GetSolutionProjectFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var projects = new List<string>();
            if (_solution == null)
            {
                return projects;
            }
            try
            {
                if (ErrorHandler.Failed(_solution.GetProjectFilesInSolution(PROJECT_FILE_FLAGS, 0, null, out uint count)) || count == 0)
                {
                    return projects;
                }
                var names = new string[count];
                if (ErrorHandler.Failed(_solution.GetProjectFilesInSolution(PROJECT_FILE_FLAGS, count, names, out uint fetched)))
                {
                    return projects;
                }
                string solutionDirectory = GetSolutionDirectory();
                for (int index = 0; index < fetched && index < names.Length; index++)
                {
                    string name = names[index];
                    if (string.IsNullOrEmpty(name) || !name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (Path.IsPathRooted(name))
                    {
                        projects.Add(Path.GetFullPath(name));
                    }
                    else if (solutionDirectory != null)
                    {
                        projects.Add(Path.GetFullPath(Path.Combine(solutionDirectory, name)));
                    }
                }
            }
            catch (Exception)
            {
                // A solution mid-load or with a malformed entry yields what was resolved so far.
            }
            return projects;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_servicesReady)
                {
                    SolutionEvents.OnBeforeCloseSolution -= OnBeforeCloseSolution;
                    KnownUIContexts.SolutionExistsAndFullyLoadedContext.UIContextChanged -= OnSolutionLoadedChanged;
                }
                _control?.StopSession();
                _control = null;
            }
            base.Dispose(disposing);
        }
    }
}
