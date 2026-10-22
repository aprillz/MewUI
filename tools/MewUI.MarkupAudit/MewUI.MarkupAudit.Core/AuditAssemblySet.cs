using System.Reflection;
using System.Text.Json;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Input assemblies opened for metadata-only inspection; disposing releases the metadata context.</summary>
public sealed class AuditAssemblySet : IDisposable
{
    private const string NUGET_PACKAGES_VARIABLE = "NUGET_PACKAGES";
    private const string PACKAGE_LIBRARY_TYPE = "package";

    private readonly MetadataLoadContext _context;

    private AuditAssemblySet(MetadataLoadContext context, IReadOnlyList<Assembly> assemblies, IReadOnlyList<string> paths)
    {
        _context = context;
        Assemblies = assemblies;
        Paths = paths;
    }

    /// <summary>Loaded input assemblies in the order the paths were given.</summary>
    public IReadOnlyList<Assembly> Assemblies { get; }

    /// <summary>Full paths of the input assemblies, parallel to <see cref="Assemblies"/>.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Opens the given assemblies without executing them, resolving references from their directories and the running runtime.</summary>
    public static AuditAssemblySet Load(IReadOnlyList<string> assemblyPaths)
    {
        if (assemblyPaths.Count == 0)
        {
            throw new ArgumentException("At least one assembly path is required.", nameof(assemblyPaths));
        }

        var fullPaths = assemblyPaths.Select(Path.GetFullPath).ToArray();
        var resolverPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // One path per simple name: the context refuses two different builds of one identity, and inputs must win over sibling copies.
        foreach (var path in fullPaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Assembly not found: {path}", path);
            }

            resolverPaths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        var directories = fullPaths
            .Select(static path => Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Append(Path.GetDirectoryName(typeof(object).Assembly.Location)!);

        foreach (var directory in directories)
        {
            foreach (var candidate in Directory.EnumerateFiles(directory, "*.dll"))
            {
                resolverPaths.TryAdd(Path.GetFileNameWithoutExtension(candidate), candidate);
            }
        }

        // Class library outputs do not copy package assemblies, so their deps.json is followed into the NuGet cache.
        foreach (var path in fullPaths)
        {
            foreach (var candidate in EnumeratePackageAssemblies(path))
            {
                resolverPaths.TryAdd(Path.GetFileNameWithoutExtension(candidate), candidate);
            }
        }

        var resolver = new PathAssemblyResolver(resolverPaths.Values);
        var context = new MetadataLoadContext(resolver, typeof(object).Assembly.GetName().Name);
        var assemblies = new List<Assembly>(fullPaths.Length);

        try
        {
            foreach (var path in fullPaths)
            {
                assemblies.Add(context.LoadFromAssemblyPath(path));
            }
        }
        catch
        {
            context.Dispose();
            throw;
        }

        return new AuditAssemblySet(context, assemblies, fullPaths);
    }

    /// <summary>Finds a type by full name in the input assemblies, or null when no input declares it.</summary>
    public Type? FindType(string fullName)
    {
        foreach (var assembly in Assemblies)
        {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    public void Dispose() => _context.Dispose();

    private static IEnumerable<string> EnumeratePackageAssemblies(string assemblyPath)
    {
        var depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
        var packagesRoot = Environment.GetEnvironmentVariable(NUGET_PACKAGES_VARIABLE);
        if (string.IsNullOrEmpty(packagesRoot))
        {
            packagesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }

        if (!File.Exists(depsPath) || !Directory.Exists(packagesRoot))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
        if (!document.RootElement.TryGetProperty("targets", out var targets) ||
            !document.RootElement.TryGetProperty("libraries", out var libraries))
        {
            yield break;
        }

        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("runtime", out var runtime) ||
                    !libraries.TryGetProperty(library.Name, out var libraryInfo) ||
                    !libraryInfo.TryGetProperty("type", out var libraryType) ||
                    libraryType.GetString() != PACKAGE_LIBRARY_TYPE ||
                    !libraryInfo.TryGetProperty("path", out var relativePath))
                {
                    continue;
                }

                foreach (var file in runtime.EnumerateObject())
                {
                    var candidate = Path.Combine(packagesRoot, relativePath.GetString() ?? string.Empty, file.Name);
                    if (File.Exists(candidate))
                    {
                        yield return candidate;
                    }
                }
            }
        }
    }
}
