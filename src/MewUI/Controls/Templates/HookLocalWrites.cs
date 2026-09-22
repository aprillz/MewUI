namespace Aprillz.MewUI.Controls;

/// <summary>
/// Remembers the local values an app hook wrote to a recycled item container, so the container can
/// give the next item the values it had before the hook ran.
/// </summary>
/// <remarks>
/// The hook's writes are found by comparing the container's local values before and after the hook,
/// so the property write path carries no cost for this and objects without a hook pay nothing.
/// </remarks>
internal sealed class HookLocalWrites
{
    private readonly List<KeyValuePair<int, object?>> _before = [];
    private readonly List<KeyValuePair<int, object?>> _after = [];
    private readonly List<Baseline> _written = [];

    private readonly record struct Baseline(MewProperty Property, bool HadLocal, object? Value);

    /// <summary>Takes the local values the hook is about to change. Call right before the hook.</summary>
    public void BeginHook(MewObject target)
    {
        _before.Clear();
        target.PropertyStore.CollectLocalValues(_before);
    }

    /// <summary>Records every writable property whose local value the hook changed. Call right after the hook.</summary>
    public void EndHook(MewObject target)
    {
        _after.Clear();
        target.PropertyStore.CollectLocalValues(_after);

        foreach (var (id, value) in _after)
        {
            int index = IndexOf(_before, id);
            if (index < 0)
            {
                Remember(id, hadLocal: false, value: null);
            }
            else if (!Equals(_before[index].Value, value))
            {
                Remember(id, hadLocal: true, _before[index].Value);
            }
        }

        foreach (var (id, value) in _before)
        {
            if (IndexOf(_after, id) < 0)
            {
                Remember(id, hadLocal: true, value);
            }
        }
    }

    /// <summary>Puts back what the recorded writes replaced, then forgets them.</summary>
    public void Restore(MewObject target)
    {
        for (int index = _written.Count - 1; index >= 0; index--)
        {
            var baseline = _written[index];
            target.RestoreLocalValue(baseline.Property, baseline.HadLocal, baseline.Value);
        }

        _written.Clear();
    }

    private void Remember(int id, bool hadLocal, object? value)
    {
        if (MewPropertyRegistry.GetProperty(id) is not MewProperty property || property.IsReadOnly)
        {
            return;
        }

        _written.Add(new Baseline(property, hadLocal, value));
    }

    private static int IndexOf(List<KeyValuePair<int, object?>> values, int id)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index].Key == id)
            {
                return index;
            }
        }

        return -1;
    }
}
