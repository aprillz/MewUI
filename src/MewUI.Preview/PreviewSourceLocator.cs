using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Maps type full names to their source document via the assembly's portable PDB (on-disk or
/// embedded), so the IDE can match its active editor file to a preview target without a
/// syntax-analysis source of its own.
/// </summary>
internal static class PreviewSourceLocator
{
    internal readonly record struct SourceLocation(string Path, int Line);

    // Rank picks the document that best represents the type when partial/generated files split
    // it across documents: the build method wins, then a constructor, then any method body.
    private const int RANK_BUILD = 0;
    private const int RANK_CTOR = 1;
    private const int RANK_OTHER = 2;

    private static readonly Dictionary<Assembly, Dictionary<string, SourceLocation>?> _cache = new();

    internal static SourceLocation? TryLocate(Type type)
    {
        Dictionary<string, SourceLocation>? map;
        lock (_cache)
        {
            if (!_cache.TryGetValue(type.Assembly, out map))
            {
                map = BuildMap(type.Assembly);
                _cache[type.Assembly] = map;
            }
        }

        if (map != null && type.FullName is string fullName && map.TryGetValue(fullName, out var location))
        {
            return location;
        }
        return null;
    }

    private static Dictionary<string, SourceLocation>? BuildMap(Assembly assembly)
    {
        try
        {
            string location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
            {
                return null;
            }

            using var peStream = File.OpenRead(location);
            using var peReader = new PEReader(peStream);
            if (!peReader.TryOpenAssociatedPortablePdb(
                location,
                static path => File.Exists(path) ? File.OpenRead(path) : null,
                out var pdbProvider,
                out _))
            {
                return null;
            }

            using (pdbProvider)
            {
                return BuildMap(peReader.GetMetadataReader(), pdbProvider!.GetMetadataReader());
            }
        }
        catch (Exception ex)
        {
            PreviewTrace.Log($"pdb map unavailable for {assembly.GetName().Name}: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, SourceLocation> BuildMap(MetadataReader metadata, MetadataReader pdb)
    {
        var map = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var debugHandle in pdb.MethodDebugInformation)
        {
            var debugInfo = pdb.GetMethodDebugInformation(debugHandle);
            if (debugInfo.SequencePointsBlob.IsNil)
            {
                continue;
            }

            string? documentPath = null;
            int line = 0;
            foreach (var point in debugInfo.GetSequencePoints())
            {
                if (point.IsHidden || point.Document.IsNil)
                {
                    continue;
                }
                documentPath = pdb.GetString(pdb.GetDocument(point.Document).Name);
                line = point.StartLine;
                break;
            }
            if (documentPath == null)
            {
                continue;
            }

            var method = metadata.GetMethodDefinition(debugHandle.ToDefinitionHandle());
            string typeName = GetTypeFullName(metadata, method.GetDeclaringType());
            int rank = RankMethod(metadata.GetString(method.Name));

            if (!ranks.TryGetValue(typeName, out int existingRank)
                || rank < existingRank
                || (rank == existingRank && line < map[typeName].Line
                    && string.Equals(map[typeName].Path, documentPath, StringComparison.Ordinal)))
            {
                ranks[typeName] = rank;
                map[typeName] = new SourceLocation(documentPath, line);
            }
        }

        return map;
    }

    private static int RankMethod(string methodName)
    {
        if (string.Equals(methodName, "OnBuild", StringComparison.Ordinal))
        {
            return RANK_BUILD;
        }
        if (string.Equals(methodName, ".ctor", StringComparison.Ordinal))
        {
            return RANK_CTOR;
        }
        return RANK_OTHER;
    }

    private static string GetTypeFullName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        string name = metadata.GetString(type.Name);

        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            // Nested types use '+' to match System.Type.FullName.
            return GetTypeFullName(metadata, declaring) + "+" + name;
        }

        string namespaceName = metadata.GetString(type.Namespace);
        if (namespaceName.Length == 0)
        {
            return name;
        }
        return namespaceName + "." + name;
    }
}
