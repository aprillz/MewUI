namespace Aprillz.MewUI.Automation;

/// <summary>Receives the automation tree's change notifications; each consumer of the tree registers one.</summary>
internal interface IAutomationEventSink
{
    void OnPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue);

    void OnStructureChanged(AutomationPeer peer);

    void OnFocusChanged(AutomationPeer peer);
}

/// <summary>Fans automation events out to the registered sinks. Used on the UI thread only.</summary>
internal static class AutomationEvents
{
    // Copied on change so a sink that unregisters while an event is delivered does not disturb the loop.
    private static IAutomationEventSink[] _sinks = [];

    internal static bool HasSinks => _sinks.Length > 0;

    internal static void AddSink(IAutomationEventSink sink)
    {
        if (Array.IndexOf(_sinks, sink) < 0)
        {
            _sinks = [.. _sinks, sink];
        }
    }

    internal static void RemoveSink(IAutomationEventSink sink)
    {
        int index = Array.IndexOf(_sinks, sink);
        if (index >= 0)
        {
            var remaining = new IAutomationEventSink[_sinks.Length - 1];
            Array.Copy(_sinks, 0, remaining, 0, index);
            Array.Copy(_sinks, index + 1, remaining, index, _sinks.Length - index - 1);
            _sinks = remaining;
        }
    }

    internal static void RaisePropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
    {
        foreach (var sink in _sinks)
        {
            sink.OnPropertyChanged(peer, property, oldValue, newValue);
        }
    }

    internal static void RaiseStructureChanged(AutomationPeer peer)
    {
        foreach (var sink in _sinks)
        {
            sink.OnStructureChanged(peer);
        }
    }

    internal static void RaiseFocusChanged(AutomationPeer peer)
    {
        foreach (var sink in _sinks)
        {
            sink.OnFocusChanged(peer);
        }
    }
}
