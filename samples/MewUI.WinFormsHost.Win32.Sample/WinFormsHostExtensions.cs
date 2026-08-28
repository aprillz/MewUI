using WinForms = System.Windows.Forms;

namespace Aprillz.MewUI.Controls;

public static class WinFormsHostExtensions
{
    public static WinFormsHost Child(this WinFormsHost host, WinForms.Control? child)
    {
        host.Child = child;
        return host;
    }

    public static WinFormsHost ClipToAncestors(this WinFormsHost host, bool value)
    {
        host.ClipToAncestors = value;
        return host;
    }
}
