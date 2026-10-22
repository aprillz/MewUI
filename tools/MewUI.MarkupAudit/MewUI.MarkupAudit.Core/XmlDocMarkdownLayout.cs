namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Page paths of xmldocmd output: <c>&lt;Assembly&gt;/&lt;Namespace&gt;/&lt;Type&gt;.md</c> for a type and <c>&lt;Type&gt;/&lt;Member&gt;.md</c> for its members; overloads share one page.</summary>
public static class XmlDocMarkdownLayout
{
    private const string PAGE_EXTENSION = ".md";

    /// <summary>Directory xmldocmd writes one assembly into.</summary>
    public static string AssemblyDirectory(string apiDirectory, string assemblyName) => Path.Combine(apiDirectory, assemblyName);

    /// <summary>Folder (and page stem) of a type: nested types are dotted and a generic arity becomes a "-N" suffix.</summary>
    public static string TypeFolderName(Type type)
    {
        var definition = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
        var fullName = definition.FullName ?? definition.Name;
        var withoutNamespace = string.IsNullOrEmpty(definition.Namespace) ? fullName : fullName[(definition.Namespace.Length + 1)..];
        return withoutNamespace.Replace('+', '.').Replace('`', '-');
    }

    /// <summary>Path of a type's own page.</summary>
    public static string TypePagePath(string apiDirectory, Type type)
    {
        return Path.Combine(NamespaceDirectory(apiDirectory, type), TypeFolderName(type) + PAGE_EXTENSION);
    }

    /// <summary>Path of the page of one member name declared by the type.</summary>
    public static string MemberPagePath(string apiDirectory, Type type, string memberName)
    {
        return Path.Combine(NamespaceDirectory(apiDirectory, type), TypeFolderName(type), memberName + PAGE_EXTENSION);
    }

    /// <summary>Forward-slash relative link from one page file to another.</summary>
    public static string RelativeLink(string fromPagePath, string toPagePath)
    {
        var fromDirectory = Path.GetDirectoryName(Path.GetFullPath(fromPagePath))!;
        return Path.GetRelativePath(fromDirectory, Path.GetFullPath(toPagePath)).Replace('\\', '/');
    }

    private static string NamespaceDirectory(string apiDirectory, Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        return Path.Combine(AssemblyDirectory(apiDirectory, assemblyName), type.Namespace ?? string.Empty);
    }
}
