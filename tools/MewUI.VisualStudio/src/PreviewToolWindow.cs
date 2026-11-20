using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Aprillz.MewUI.VisualStudio
{
    [Guid("ab8e29a0-07fa-4cb2-b6c6-d6a8bb0204d6")]
    public sealed class PreviewToolWindow : ToolWindowPane
    {
        public PreviewToolWindow() : base(null)
        {
            Caption = "MewUI Preview";
            // Closing the pane only hides it, so a running session stays warm and reopening
            // the window reattaches instantly; Stop ends it explicitly.
            Content = new PreviewToolWindowControl();
        }

        public override void OnToolWindowCreated()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            base.OnToolWindowCreated();
            // Runs for every creation path, including a window the IDE restores with its layout at startup.
            FindPackage()?.AttachToolWindow(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (Content as PreviewToolWindowControl)?.StopSession();
            }
            base.Dispose(disposing);
        }

        private MewUIPreviewPackage FindPackage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package is MewUIPreviewPackage owner)
            {
                return owner;
            }
            var shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
            var packageGuid = new Guid(MewUIPreviewPackage.PACKAGE_GUID);
            if (shell != null && ErrorHandler.Succeeded(shell.IsPackageLoaded(ref packageGuid, out IVsPackage loaded)))
            {
                return loaded as MewUIPreviewPackage;
            }
            return null;
        }
    }
}
