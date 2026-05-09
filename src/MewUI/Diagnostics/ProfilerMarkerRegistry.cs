namespace Aprillz.MewUI.Diagnostics;

internal static class ProfilerMarkerRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, int> IdByName = new(StringComparer.Ordinal);
    private static readonly List<ProfilerMarkerInfo> Markers = new();
    private static int _version;

    public static int Version => Volatile.Read(ref _version);

    public static ProfilerMarker Register(string name, ProfilerSampleCategory category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (Sync)
        {
            if (IdByName.TryGetValue(name, out int existing))
            {
                return new ProfilerMarker(existing, Markers[existing].Category);
            }

            int id = Markers.Count;
            IdByName.Add(name, id);
            Markers.Add(new ProfilerMarkerInfo(id, name, category, GetCategoryColor(category)));
            _version++;
            return new ProfilerMarker(id, category);
        }
    }

    public static ProfilerMarkerInfo GetInfo(int id)
    {
        lock (Sync)
        {
            if ((uint)id < (uint)Markers.Count)
            {
                return Markers[id];
            }
        }

        return new ProfilerMarkerInfo(-1, "(unknown)", ProfilerSampleCategory.Other, Color.FromArgb(255, 180, 180, 180));
    }

    public static ProfilerMarkerInfo[] Snapshot()
    {
        lock (Sync)
        {
            return Markers.ToArray();
        }
    }

    public static Color GetCategoryColor(ProfilerSampleCategory category)
        => category switch
        {
            ProfilerSampleCategory.Frame => Color.FromArgb(255, 170, 170, 170),
            ProfilerSampleCategory.Layout => Color.FromArgb(255, 118, 187, 255),
            ProfilerSampleCategory.Measure => Color.FromArgb(255, 84, 165, 255),
            ProfilerSampleCategory.Arrange => Color.FromArgb(255, 57, 130, 220),
            ProfilerSampleCategory.Animation => Color.FromArgb(255, 85, 210, 210),
            ProfilerSampleCategory.Render => Color.FromArgb(255, 130, 180, 35),
            ProfilerSampleCategory.Backend => Color.FromArgb(255, 210, 150, 40),
            ProfilerSampleCategory.DevTools => Color.FromArgb(255, 190, 120, 255),
            ProfilerSampleCategory.VSyncWait => Color.FromArgb(255, 210, 180, 0),
            ProfilerSampleCategory.GC => Color.FromArgb(255, 230, 70, 45),
            _ => Color.FromArgb(255, 150, 150, 150),
        };
}
