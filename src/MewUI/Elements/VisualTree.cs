using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal static class VisualTree
{
    public static void Visit(Element? element, Action<Element> visitor)
    {
        if (element == null)
        {
            return;
        }

        visitor(element);

        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child => Visit(child, visitor));
        }
    }
}
