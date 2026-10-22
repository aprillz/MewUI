using System.Reflection;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Member kind names of the public surface enumeration.</summary>
public static class SurfaceKinds
{
    public const string TYPE = "type";
    public const string CONSTRUCTOR = "constructor";
    public const string METHOD = "method";
    public const string PROPERTY = "property";
    public const string EVENT = "event";
    public const string FIELD = "field";
}

/// <summary>One public type or one public member declared by it, with the id its XML documentation is keyed by.</summary>
public sealed record PublicMember(
    Type DeclaringType,
    MemberInfo Member,
    string Kind,
    string DocumentationId)
{
    /// <summary>Display name: the type's short name for a type, else the member name with the signature for methods.</summary>
    public string DisplayName => Member switch
    {
        Type type => TypeFormatter.FormatType(type),
        MethodInfo method => FormatInvocable(method.Name, method.GetParameters(), method.IsGenericMethodDefinition ? method.GetGenericArguments() : Array.Empty<Type>()),
        ConstructorInfo constructor => FormatInvocable(TypeFormatter.FormatType(DeclaringType), constructor.GetParameters(), Array.Empty<Type>()),
        _ => Member.Name,
    };

    private static string FormatInvocable(string name, ParameterInfo[] parameters, Type[] typeArguments)
    {
        var generic = typeArguments.Length > 0 ? $"<{string.Join(", ", typeArguments.Select(static argument => argument.Name))}>" : string.Empty;
        var list = string.Join(", ", parameters.Select(static parameter => TypeFormatter.FormatType(parameter.ParameterType)));
        return $"{name}{generic}({list})";
    }
}

/// <summary>Enumerates the public surface of metadata-only assemblies the way CS1591 sees it: visible types and the members they declare.</summary>
public static class PublicSurface
{
    private const string COMPILER_GENERATED_ATTRIBUTE = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
    private const string DELEGATE_TYPE = "System.Delegate";
    private const string OPERATOR_PREFIX = "op_";
    private const string ENUM_VALUE_FIELD = "value__";

    /// <summary>Visible types of an assembly whose namespace matches the prefix (null matches all), ordered by namespace and name.</summary>
    public static IEnumerable<Type> EnumerateTypes(Assembly assembly, string? namespacePrefix, IList<string> warnings)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            warnings.Add($"Some types of {assembly.GetName().Name} could not be loaded: {exception.Message}");
            types = exception.Types.OfType<Type>().ToArray();
        }

        return types
            .Where(type => type.IsVisible && !IsCompilerGenerated(type.GetCustomAttributesData()) && MatchesNamespace(type.Namespace, namespacePrefix))
            .OrderBy(static type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(static type => type.FullName, StringComparer.Ordinal);
    }

    /// <summary>True when the namespace equals the prefix or lies below it; a null prefix matches everything.</summary>
    public static bool MatchesNamespace(string? typeNamespace, string? namespacePrefix)
    {
        if (string.IsNullOrEmpty(namespacePrefix))
        {
            return true;
        }

        var actual = typeNamespace ?? string.Empty;
        return actual == namespacePrefix || actual.StartsWith(namespacePrefix + ".", StringComparison.Ordinal);
    }

    /// <summary>The type itself followed by its public declared members; delegate types list no members.</summary>
    public static IEnumerable<PublicMember> EnumerateMembers(Type type)
    {
        yield return new PublicMember(type, type, SurfaceKinds.TYPE, DocumentationIdBuilder.ForType(type));

        if (TypeAnalysis.DerivesFrom(type, DELEGATE_TYPE))
        {
            yield break;
        }

        const BindingFlags declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(declared).OrderBy(static constructor => constructor.GetParameters().Length))
        {
            if (!IsCompilerGenerated(constructor.GetCustomAttributesData()))
            {
                yield return new PublicMember(type, constructor, SurfaceKinds.CONSTRUCTOR, DocumentationIdBuilder.ForConstructor(constructor));
            }
        }

        foreach (var field in type.GetFields(declared).OrderBy(static field => field.Name, StringComparer.Ordinal))
        {
            if (field.Name != ENUM_VALUE_FIELD && !IsCompilerGenerated(field.GetCustomAttributesData()))
            {
                yield return new PublicMember(type, field, SurfaceKinds.FIELD, DocumentationIdBuilder.ForField(field));
            }
        }

        foreach (var property in type.GetProperties(declared).OrderBy(static property => property.Name, StringComparer.Ordinal))
        {
            if (!IsCompilerGenerated(property.GetCustomAttributesData()) &&
                (property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true))
            {
                yield return new PublicMember(type, property, SurfaceKinds.PROPERTY, DocumentationIdBuilder.ForProperty(property));
            }
        }

        foreach (var eventInfo in type.GetEvents(declared).OrderBy(static eventInfo => eventInfo.Name, StringComparer.Ordinal))
        {
            if (!IsCompilerGenerated(eventInfo.GetCustomAttributesData()) && eventInfo.AddMethod?.IsPublic == true)
            {
                yield return new PublicMember(type, eventInfo, SurfaceKinds.EVENT, DocumentationIdBuilder.ForEvent(eventInfo));
            }
        }

        var methods = type.GetMethods(declared)
            .Where(static method => !method.Name.Contains('<') && (!method.IsSpecialName || method.Name.StartsWith(OPERATOR_PREFIX, StringComparison.Ordinal)))
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .ThenBy(static method => method.GetParameters().Length);

        foreach (var method in methods)
        {
            if (!IsCompilerGenerated(method.GetCustomAttributesData()))
            {
                yield return new PublicMember(type, method, SurfaceKinds.METHOD, DocumentationIdBuilder.ForMethod(method));
            }
        }
    }

    private static bool IsCompilerGenerated(IList<CustomAttributeData> attributes)
    {
        return attributes.Any(static attribute => attribute.AttributeType.FullName == COMPILER_GENERATED_ATTRIBUTE);
    }
}
