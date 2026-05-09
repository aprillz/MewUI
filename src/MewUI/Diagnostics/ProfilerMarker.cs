namespace Aprillz.MewUI.Diagnostics;

internal readonly struct ProfilerMarker
{
    public ProfilerMarker(int id, ProfilerSampleCategory category)
    {
        Id = id;
        Category = category;
    }

    public int Id { get; }
    public ProfilerSampleCategory Category { get; }

    public ProfilerScope Auto() => PerformanceProfiler.Sample(this);

    public static ProfilerMarker Register(string name, ProfilerSampleCategory category)
        => ProfilerMarkerRegistry.Register(name, category);
}
