using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal interface IVisualTreeHost
{
    void VisitChildren(Action<Element> visitor);
}

