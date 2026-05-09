namespace Aprillz.MewUI.Diagnostics;

using Aprillz.MewUI.Controls;

internal readonly struct ProfilerSample
{
    public ProfilerSample(int parentIndex, int markerId, ProfilerSampleCategory category, long startTimestamp, long endTimestamp, int depth, UIElement? target)
    {
        ParentIndex = parentIndex;
        MarkerId = markerId;
        Category = category;
        StartTimestamp = startTimestamp;
        EndTimestamp = endTimestamp;
        Depth = depth;
        Target = target;
    }

    public int ParentIndex { get; }
    public int MarkerId { get; }
    public ProfilerSampleCategory Category { get; }
    public long StartTimestamp { get; }
    public long EndTimestamp { get; }
    public int Depth { get; }
    public UIElement? Target { get; }
}
