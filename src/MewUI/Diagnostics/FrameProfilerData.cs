namespace Aprillz.MewUI.Diagnostics;

internal sealed class FrameProfilerData
{
    private ProfilerSample[] _samples = Array.Empty<ProfilerSample>();

    public FramePerformanceStats Stats { get; private set; }
    public ProfilerSample[] Samples => _samples;
    public int SampleCount { get; private set; }
    public int SampleOverflowCount { get; private set; }
    public int MarkerRegistryVersion { get; private set; }

    public void Set(FramePerformanceStats stats, int sampleCount, int sampleOverflowCount, int markerRegistryVersion)
    {
        Stats = stats;
        EnsureSampleCapacity(sampleCount);
        if (SampleCount > sampleCount)
        {
            Array.Clear(_samples, sampleCount, SampleCount - sampleCount);
        }

        SampleCount = sampleCount;
        SampleOverflowCount = sampleOverflowCount;
        MarkerRegistryVersion = markerRegistryVersion;
    }

    private void EnsureSampleCapacity(int sampleCount)
    {
        if (_samples.Length >= sampleCount)
        {
            return;
        }

        int capacity = Math.Max(128, _samples.Length);
        while (capacity < sampleCount)
        {
            capacity *= 2;
        }

        _samples = new ProfilerSample[capacity];
    }
}
