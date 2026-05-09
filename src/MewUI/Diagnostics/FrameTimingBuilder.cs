using System.Diagnostics;

namespace Aprillz.MewUI.Diagnostics;

internal struct FrameTimingBuilder
{
    public bool Enabled;
    public long FrameIndex;
    public long SourceId;
    public long FrameStart;
    public long FrameEnd;
    public long BeginFrameTicks;
    public long AnimationTicks;
    public long RenderBodyTicks;
    public long DevToolsTicks;
    public long EndFrameTicks;
    public long PresentTicks;
    public long StartAllocatedBytes;
    public int StartGen0Collections;
    public int StartGen1Collections;
    public int StartGen2Collections;

    public static double ToMilliseconds(long ticks)
        => ticks * 1000.0 / Stopwatch.Frequency;
}
