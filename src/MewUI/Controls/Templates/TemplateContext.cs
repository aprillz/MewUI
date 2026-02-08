namespace Aprillz.MewUI.Controls;

public sealed class TemplateContext : IDisposable
{
    private readonly Dictionary<string, UIElement> _namedElements = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _tracked = new();

    public void Register<T>(string name, T element) where T : UIElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(element);

        _namedElements[name] = element;
    }

    public T Get<T>(string name) where T : UIElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_namedElements.TryGetValue(name, out var element))
        {
            throw new KeyNotFoundException($"TemplateContext has no element named '{name}'.");
        }

        if (element is not T typed)
        {
            throw new InvalidCastException($"TemplateContext element '{name}' is '{element.GetType().Name}', not '{typeof(T).Name}'.");
        }

        return typed;
    }

    public void Track(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        _tracked.Add(disposable);
    }

    public void Reset()
    {
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            _tracked[i].Dispose();
        }
        _tracked.Clear();
    }

    public void Dispose() => Reset();
}

