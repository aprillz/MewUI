namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// The areas one frame has to repaint, kept apart for as long as joining them would repaint more than
/// it saves. Two changes at opposite corners of a surface are two small areas, not one that spans it.
/// The limits come from the measurements in agent/retained-redesign/damage-cost.md: joining pays while
/// the joined box stays within 1.7 times the area of the two and adds little area of its own; on the
/// backend that paints per pixel, joining eight scattered boxes cost up to sixteen times more.
/// </summary>
internal sealed class DamageRegion
{
    /// <summary>More areas than this are joined, cheapest pair first; each one is a separate replay.</summary>
    internal const int MAX_AREAS = 8;

    private const double JOIN_AREA_RATIO = 1.7;

    // Added area, in layout units squared, up to which joining two areas costs less than replaying them
    // apart. It is the lower of the measured limits, so it holds for every backend.
    private const double JOIN_ADDED_AREA_LIMIT = 20_000;

    private readonly List<Rect> _areas = [];

    internal IReadOnlyList<Rect> Areas => _areas;

    internal bool IsEmpty => _areas.Count == 0;

    /// <summary>Sum of the areas, which is what a frame that repaints them pays for.</summary>
    internal double TotalArea
    {
        get
        {
            double total = 0;
            for (int index = 0; index < _areas.Count; index++)
            {
                total += _areas[index].Width * _areas[index].Height;
            }

            return total;
        }
    }

    internal void Clear() => _areas.Clear();

    internal void Add(Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        // Joining can bring the grown area next to another one, so it repeats until nothing joins.
        bool joined = true;
        while (joined)
        {
            joined = false;
            for (int index = 0; index < _areas.Count; index++)
            {
                if (ShouldJoin(_areas[index], area))
                {
                    area = _areas[index].Union(area);
                    _areas.RemoveAt(index);
                    joined = true;
                    break;
                }
            }
        }

        _areas.Add(area);
        while (_areas.Count > MAX_AREAS)
        {
            JoinCheapestPair();
        }
    }

    private static bool ShouldJoin(Rect first, Rect second)
    {
        if (first.IntersectsWith(second))
        {
            // Overlapping areas would repaint the overlap twice and erase what the first one painted.
            return true;
        }

        double separate = (first.Width * first.Height) + (second.Width * second.Height);
        var union = first.Union(second);
        double joinedArea = union.Width * union.Height;
        return joinedArea <= separate * JOIN_AREA_RATIO && joinedArea - separate <= JOIN_ADDED_AREA_LIMIT;
    }

    private void JoinCheapestPair()
    {
        int bestFirst = 0;
        int bestSecond = 1;
        double bestAdded = double.MaxValue;
        for (int first = 0; first < _areas.Count; first++)
        {
            for (int second = first + 1; second < _areas.Count; second++)
            {
                var union = _areas[first].Union(_areas[second]);
                double added = (union.Width * union.Height) -
                    (_areas[first].Width * _areas[first].Height) -
                    (_areas[second].Width * _areas[second].Height);
                if (added < bestAdded)
                {
                    bestAdded = added;
                    bestFirst = first;
                    bestSecond = second;
                }
            }
        }

        var joined = _areas[bestFirst].Union(_areas[bestSecond]);
        _areas.RemoveAt(bestSecond);
        _areas.RemoveAt(bestFirst);
        Add(joined);
    }
}
