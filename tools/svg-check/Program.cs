using Aprillz.MewUI.SvgCheck;

// Reports whether each SVG stays inside what SimpleSvgSource reads, and why not when it does not.
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: svg-check <file-or-folder> [more...] [--quiet] [--notes]");
    return 2;
}

bool quiet = false;
bool notes = false;
var targets = new List<string>();
foreach (string argument in args)
{
    switch (argument)
    {
        case "--quiet": quiet = true; break;
        case "--notes": notes = true; break;
        default: targets.Add(argument); break;
    }
}

var files = new List<string>();
foreach (string target in targets)
{
    if (Directory.Exists(target))
    {
        files.AddRange(Directory.EnumerateFiles(target, "*.svg", SearchOption.AllDirectories));
    }
    else if (File.Exists(target))
    {
        files.Add(target);
    }
    else
    {
        Console.Error.WriteLine($"svg-check: not found: {target}");
        return 2;
    }
}

files.Sort(StringComparer.OrdinalIgnoreCase);

int outOfRange = 0;
int unreadable = 0;
var reasonTotals = new SortedDictionary<string, int>(StringComparer.Ordinal);

foreach (string file in files)
{
    SvgCheckResult result;
    try
    {
        result = SvgRangeChecker.Check(File.ReadAllText(file));
    }
    catch (Exception error)
    {
        unreadable++;
        Console.WriteLine($"{Path.GetFileName(file),-48} FAIL  {error.Message}");
        continue;
    }

    if (result.Reasons.Count > 0)
    {
        outOfRange++;
        foreach (var reason in result.Reasons)
        {
            reasonTotals.TryGetValue(reason.Key, out int total);
            reasonTotals[reason.Key] = total + reason.Value;
        }

        Console.WriteLine($"{Path.GetFileName(file),-48} OUT   {Describe(result.Reasons)}");
    }
    else if (!quiet)
    {
        string note = notes && result.Notes.Count > 0 ? $"   NOTE {string.Join(", ", result.Notes)}" : string.Empty;
        Console.WriteLine($"{Path.GetFileName(file),-48} OK{note}");
    }
}

Console.WriteLine();
Console.WriteLine($"{files.Count} file(s): {files.Count - outOfRange - unreadable} in range, {outOfRange} out of range, {unreadable} unreadable");
if (reasonTotals.Count > 0)
{
    Console.WriteLine($"reasons: {Describe(reasonTotals)}");
}

return outOfRange + unreadable > 0 ? 1 : 0;

static string Describe(IDictionary<string, int> reasons) =>
    string.Join(", ", reasons.Select(pair => $"{pair.Key}({pair.Value})"));
