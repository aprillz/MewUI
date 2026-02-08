namespace Aprillz.MewUI;

internal readonly struct BindingSlot
{
    private static int _nextId;

    public int Id { get; }
    public string? Name { get; }

    public BindingSlot(string? name = null)
    {
        Id = Interlocked.Increment(ref _nextId);
        Name = name;
    }
}

