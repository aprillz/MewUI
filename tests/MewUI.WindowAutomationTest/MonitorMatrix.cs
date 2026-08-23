namespace MewUI.WindowAutomationTest;

/// <summary>
/// Reads the machine's display layout and turns it into the DPI transitions worth exercising, so
/// the same tests cover whatever desks they are run on: every ordered pair of monitors whose scale
/// differs becomes one case, in both directions. A machine with a single scale yields no cases and
/// the scenarios report themselves inconclusive rather than passing vacuously.
/// </summary>
public static class MonitorMatrix
{
    private static readonly Lazy<IReadOnlyList<MonitorProbe>> _monitors = new(MonitorProbe.All);

    public static IReadOnlyList<MonitorProbe> Monitors => _monitors.Value;

    /// <summary>One line naming every display and its scale, for a failure message.</summary>
    public static string Describe()
        => Monitors.Count == 0
            ? "no displays"
            : string.Join(" | ", Monitors.Select(static monitor => monitor.Label));

    public static bool HasMixedScales
        => Monitors.Select(static monitor => monitor.Dpi).Distinct().Count() > 1;

    /// <summary>
    /// Ordered (from, to) pairs across differing scales. One pair per distinct scale transition:
    /// two monitors at the same scale add nothing, and a third display sharing a scale with one of
    /// the others would otherwise multiply identical cases.
    /// </summary>
    public static IEnumerable<object[]> Transitions()
    {
        var byScale = Monitors
            .GroupBy(static monitor => monitor.Dpi)
            .Select(static group => group.First())
            .OrderBy(static monitor => monitor.Dpi)
            .ToList();

        foreach (var from in byScale)
        {
            foreach (var to in byScale)
            {
                if (from.Dpi != to.Dpi)
                {
                    yield return [from, to];
                }
            }
        }
    }

    /// <summary>One monitor per distinct scale, for cases that care about a scale rather than a move.</summary>
    public static IEnumerable<object[]> DistinctScales()
    {
        foreach (var monitor in Monitors.GroupBy(static monitor => monitor.Dpi).Select(static group => group.First()).OrderBy(static monitor => monitor.Dpi))
        {
            yield return [monitor];
        }
    }

    /// <summary>Names a generated case after the scale it runs at.</summary>
    public static string ScaleName(System.Reflection.MethodInfo method, object?[]? data)
    {
        if (data is [MonitorProbe monitor, ..])
        {
            return $"{method.Name}({monitor.ScalePercent}%)";
        }

        return method.Name;
    }

    /// <summary>Names a generated case after the transition it drives.</summary>
    public static string TransitionName(System.Reflection.MethodInfo method, object?[]? data)
    {
        if (data is [MonitorProbe from, MonitorProbe to, ..])
        {
            return $"{method.Name}({from.ScalePercent}% -> {to.ScalePercent}%)";
        }

        return method.Name;
    }
}
