namespace Aprillz.MewUI;

internal interface IVisualTreeHost
{
    void VisitChildren(Action<Element> visitor);
}

