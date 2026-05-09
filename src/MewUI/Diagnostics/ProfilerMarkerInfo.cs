namespace Aprillz.MewUI.Diagnostics;

internal readonly struct ProfilerMarkerInfo
{
    public ProfilerMarkerInfo(int id, string name, ProfilerSampleCategory category, Color color)
    {
        Id = id;
        Name = name;
        Category = category;
        Color = color;
    }

    public int Id { get; }
    public string Name { get; }
    public ProfilerSampleCategory Category { get; }
    public Color Color { get; }
}
