namespace Aprillz.MewUI.Controls;

public static class WinFormsHostExtensions
{
    public static WinFormsHost Child(this WinFormsHost host, WF.Control? child)
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
