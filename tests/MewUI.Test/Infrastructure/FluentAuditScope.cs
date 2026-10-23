#if NET10_0_OR_GREATER
using System.Collections.Concurrent;
using Aprillz.MewUI.MarkupAudit;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// Runs the markup audit over built assemblies and reduces the result to the rows of one assembly set,
/// so a test project can assert only on the assemblies it owns while the audit still sees the core types.
/// </summary>
internal sealed class FluentAuditScope
{
    private const string REPOSITORY_MARKER = "build/MewUI.Common.props";
    private const string DEFAULT_EXCLUDED_TYPE = "Aprillz.MewUI.Controls.ControlTemplate";
    private const string HINT_CODE_PREFIX = "hint-";
    private const int MAX_LISTED_ROWS = 50;

    private static readonly ConcurrentDictionary<string, Lazy<FluentAuditScope>> _cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly AuditResult _result;
    private readonly HashSet<string> _assemblyNames;
    private readonly HashSet<string> _receiverNames;

    private FluentAuditScope(AuditResult result, HashSet<string> assemblyNames, HashSet<string> receiverNames)
    {
        _result = result;
        _assemblyNames = assemblyNames;
        _receiverNames = receiverNames;
    }

    /// <summary>Counts of the audited ledger rows and inventoried extensions inside the scope, for the failure header.</summary>
    public string Summary
    {
        get
        {
            var propertyCount = _result.Properties.Count(IsInScope);
            var eventCount = _result.Events.Count(IsInScope);
            var extensionCount = _result.Inventory.Count(record => _assemblyNames.Contains(record.Assembly));
            return $"assemblies [{string.Join(", ", _assemblyNames.Order(StringComparer.Ordinal))}]: " +
                   $"{propertyCount} properties, {eventCount} events, {extensionCount} extension methods audited";
        }
    }

    /// <summary>Audits the core assembly alone.</summary>
    public static FluentAuditScope ForCore(Type coreType) => For([coreType.Assembly.Location], [coreType.Assembly.Location]);

    /// <summary>Audits an extension assembly together with the core assembly, scoped to the extension.</summary>
    public static FluentAuditScope ForExtension(Type coreType, Type extensionType)
        => For([coreType.Assembly.Location, extensionType.Assembly.Location], [extensionType.Assembly.Location]);

    /// <summary>Audits built extension assemblies that have no test project of their own, scoped to those assemblies.</summary>
    public static FluentAuditScope ForBuiltExtensions(Type coreType, IReadOnlyList<string> extensionPaths)
    {
        var inputs = new List<string> { coreType.Assembly.Location };
        inputs.AddRange(extensionPaths);
        return For(inputs, extensionPaths);
    }

    /// <summary>Resolves the first existing candidate path (relative to the repository root) of each requested extension, skipping absent ones.</summary>
    public static IReadOnlyList<string> FindBuiltExtensions(IReadOnlyList<string[]> candidateGroups, out IReadOnlyList<string> absent)
    {
        var root = FindRepositoryRoot();
        var found = new List<string>();
        var missing = new List<string>();
        foreach (var group in candidateGroups)
        {
            var existing = root == null
                ? null
                : group.Select(relative => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))).FirstOrDefault(File.Exists);
            if (existing == null)
            {
                missing.Add(group[0]);
            }
            else
            {
                found.Add(existing);
            }
        }

        absent = missing;
        return found;
    }

    /// <summary>Rows of (receiver, member) pairs that no extension, hint, or exclusion covers.</summary>
    public IReadOnlyList<string> MissingRows()
        => _result.Properties.Concat(_result.Events)
            .Where(entry => entry.IsMissing && IsInScope(entry))
            .Select(static entry => $"{entry.Receiver}.{entry.Member} ({entry.MemberKind} {entry.MemberType})")
            .ToArray();

    /// <summary>Rows of every <c>hint-*</c> finding.</summary>
    public IReadOnlyList<string> HintRows() => FindingRows(static code => code.StartsWith(HINT_CODE_PREFIX, StringComparison.Ordinal));

    /// <summary>Rows of <c>missing-summary</c> findings.</summary>
    public IReadOnlyList<string> SummaryRows() => FindingRows(static code => code == FindingCodes.MISSING_SUMMARY);

    /// <summary>Rows of <c>naming-prefix</c> findings.</summary>
    public IReadOnlyList<string> NamingRows() => FindingRows(static code => code == FindingCodes.NAMING_PREFIX);

    /// <summary>Rows of <c>return-type</c> findings.</summary>
    public IReadOnlyList<string> ReturnTypeRows() => FindingRows(static code => code == FindingCodes.RETURN_TYPE);

    /// <summary>Fails the test with the offending rows (at most 50, then a remainder count) when any exist.</summary>
    public void AssertNone(string check, IReadOnlyList<string> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var builder = new System.Text.StringBuilder();
        builder.Append(check).Append(": ").Append(rows.Count).Append(" offending row(s); ").Append(Summary).Append('\n');
        foreach (var row in rows.Take(MAX_LISTED_ROWS))
        {
            builder.Append("  ").Append(row).Append('\n');
        }

        if (rows.Count > MAX_LISTED_ROWS)
        {
            builder.Append("  ... and ").Append(rows.Count - MAX_LISTED_ROWS).Append(" more\n");
        }

        Assert.Fail(builder.ToString());
    }

    private static FluentAuditScope For(IReadOnlyList<string> inputPaths, IReadOnlyList<string> scopePaths)
    {
        var key = string.Join("|", inputPaths) + "=>" + string.Join("|", scopePaths);
        return _cache.GetOrAdd(key, _ => new Lazy<FluentAuditScope>(() => Build(inputPaths, scopePaths))).Value;
    }

    private static FluentAuditScope Build(IReadOnlyList<string> inputPaths, IReadOnlyList<string> scopePaths)
    {
        var result = MarkupAuditor.Run(new AuditOptions
        {
            AssemblyPaths = inputPaths,
            ExcludedTypes = [DEFAULT_EXCLUDED_TYPE],
        });

        var assemblyNames = new HashSet<string>(StringComparer.Ordinal);
        var receiverNames = new HashSet<string>(StringComparer.Ordinal);
        using (var set = AuditAssemblySet.Load(inputPaths))
        {
            var scopeFullPaths = scopePaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < set.Assemblies.Count; index++)
            {
                if (!scopeFullPaths.Contains(set.Paths[index]))
                {
                    continue;
                }

                var assembly = set.Assemblies[index];
                assemblyNames.Add(assembly.GetName().Name ?? string.Empty);
                foreach (var type in GetTypesOrPartial(assembly))
                {
                    if (type.IsPublic && type.IsClass && type.FullName != null)
                    {
                        receiverNames.Add(NormalizeTypeName(type.FullName));
                    }
                }
            }
        }

        return new FluentAuditScope(result, assemblyNames, receiverNames);
    }

    private static IEnumerable<Type> GetTypesOrPartial(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    // Ledger names print generics as Name<...> and nested types with dots; reflection uses Name`1 and '+'.
    private static string NormalizeTypeName(string name)
    {
        var generic = name.IndexOfAny(['`', '<']);
        var stripped = generic >= 0 ? name[..generic] : name;
        return stripped.Replace('+', '.');
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, REPOSITORY_MARKER)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private bool IsInScope(MemberLedgerEntry entry) => _receiverNames.Contains(NormalizeTypeName(entry.Receiver));

    private IReadOnlyList<string> FindingRows(Func<string, bool> codeFilter)
        => _result.Findings
            .Where(finding => codeFilter(finding.Code) && _assemblyNames.Contains(finding.Assembly))
            .Select(static finding => $"[{finding.Code}] {finding.Subject}: {finding.Message}")
            .ToArray();
}
#endif
