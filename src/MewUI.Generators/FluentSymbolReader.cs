using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

namespace Aprillz.MewUI.Generators;

/// <summary>
/// Turns owner type symbols and the compilation's extension methods into the equatable models the
/// generator pipeline caches.
/// </summary>
internal static class FluentSymbolReader
{
    private const string ELEMENT_TYPE = "Aprillz.MewUI.Controls.Element";
    private const string MEW_OBJECT_TYPE = "Aprillz.MewUI.Controls.MewObject";
    private const string MODEL_NAMESPACE = "Aprillz.MewUI.Controls";
    private const string EXCLUDED_ATTRIBUTE = "Aprillz.MewUI.FluentExcludedAttribute";
    private const string SETS_MEMBER_ATTRIBUTE = "Aprillz.MewUI.FluentSetsMemberAttribute";
    private const string OBSOLETE_ATTRIBUTE = "System.ObsoleteAttribute";
    private const string ASSEMBLY_PREFIX = "Aprillz.MewUI";
    private const int INHERITDOC_DEPTH = 4;

    // Object-rooted markup models of the controls namespace, the receivers the markup audit also reaches.
    private static readonly string[] _objectRootedOwners =
    [
        "Aprillz.MewUI.Controls.Menu",
        "Aprillz.MewUI.Controls.GridViewColumn`1",
        "Aprillz.MewUI.Controls.ToolBarGroup",
        "Aprillz.MewUI.Controls.ToolBarBand",
    ];

    private static readonly SymbolDisplayFormat _annotatedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Returns the owner model, or null when the type is not an owner.</summary>
    public static FluentOwnerModel? TryReadOwner(
        INamedTypeSymbol type,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (type.DeclaredAccessibility != Accessibility.Public
            || type.IsStatic
            || type.TypeKind != TypeKind.Class
            || type.ContainingType != null
            || !IsOwner(type))
        {
            return null;
        }

        var members = ImmutableArray.CreateBuilder<FluentMemberModel>();
        foreach (var member in type.GetMembers().OrderBy(static member => member.Name, System.StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = TryReadMember(member, type, compilation, cancellationToken);
            if (model != null)
            {
                members.Add(model);
            }
        }

        string ownerNamespace = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        string className = type.Name + "MarkupExtensions";
        string classMetadataName = ownerNamespace.Length == 0 ? className : ownerNamespace + "." + className;

        return new FluentOwnerModel(
            ownerNamespace,
            type.Name,
            Key(type),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Cref(type),
            type.IsSealed,
            TypeParameterList(type),
            ConstraintClauses(type),
            IsForeignType(compilation.GetTypeByMetadataName(classMetadataName)),
            new EquatableArray<string>(Chain(type)),
            new EquatableArray<string>(InstanceMethodNames(type)),
            new EquatableArray<FluentMemberModel>(members.ToImmutable()));
    }

    /// <summary>True when a type exists that the generated partial class cannot merge with.</summary>
    private static bool IsForeignType(INamedTypeSymbol? existing)
    {
        if (existing == null)
        {
            return false;
        }

        if (!existing.IsStatic || existing.TypeKind != TypeKind.Class)
        {
            return true;
        }

        foreach (var reference in existing.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax declaration
                || !declaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return existing.DeclaringSyntaxReferences.Length == 0;
    }

    private static bool IsOwner(INamedTypeSymbol type)
    {
        if (DerivesFrom(type, ELEMENT_TYPE))
        {
            return true;
        }

        if (type.ContainingNamespace.ToDisplayString() != MODEL_NAMESPACE)
        {
            return false;
        }

        if (DerivesFrom(type, MEW_OBJECT_TYPE))
        {
            return true;
        }

        string metadataName = type.ContainingNamespace.ToDisplayString() + "." + type.MetadataName;
        foreach (var candidate in _objectRootedOwners)
        {
            if (candidate == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string metadataName)
    {
        for (var candidate = type.BaseType; candidate != null; candidate = candidate.BaseType)
        {
            if (MetadataName(candidate) == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string MetadataName(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        return definition.ContainingNamespace.IsGlobalNamespace
            ? definition.MetadataName
            : definition.ContainingNamespace.ToDisplayString() + "." + definition.MetadataName;
    }

    private static FluentMemberModel? TryReadMember(
        ISymbol member,
        INamedTypeSymbol owner,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (member.DeclaredAccessibility != Accessibility.Public
            || member.IsStatic
            || member.IsOverride
            || member.IsImplicitlyDeclared
            || HasAttribute(member, OBSOLETE_ATTRIBUTE)
            || HasAttribute(member, EXCLUDED_ATTRIBUTE))
        {
            return null;
        }

        ITypeSymbol type;
        bool isEvent;
        if (member is IPropertySymbol property)
        {
            if (property.IsIndexer
                || property.SetMethod == null
                || property.SetMethod.IsInitOnly
                || property.SetMethod.DeclaredAccessibility != Accessibility.Public
                || property.ExplicitInterfaceImplementations.Length > 0)
            {
                return null;
            }

            type = property.Type;
            isEvent = false;
        }
        else if (member is IEventSymbol eventSymbol)
        {
            if (eventSymbol.ExplicitInterfaceImplementations.Length > 0)
            {
                return null;
            }

            type = eventSymbol.Type;
            isEvent = true;
        }
        else
        {
            return null;
        }

        if (type.TypeKind == TypeKind.Error || type is IPointerTypeSymbol || type.IsRefLikeType)
        {
            return null;
        }

        if (isEvent && type.IsReferenceType)
        {
            // The event field may be declared nullable; the handler passed to += is not.
            type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        bool isDelegate = type.TypeKind == TypeKind.Delegate;
        int arity = isDelegate && type is INamedTypeSymbol named && named.DelegateInvokeMethod != null
            ? named.DelegateInvokeMethod.Parameters.Length
            : -1;

        return new FluentMemberModel(
            member.Name,
            isEvent,
            type.ToDisplayString(_annotatedFormat),
            Key(type),
            type.SpecialType == SpecialType.System_Boolean,
            isDelegate,
            arity,
            IsHiddenBySameTypedBase(member, owner, type),
            ReadSummary(member, compilation, cancellationToken));
    }

    /// <summary>True when a base type declares a public settable member of the same name and type, which the base owner's generated extension already covers.</summary>
    private static bool IsHiddenBySameTypedBase(ISymbol member, INamedTypeSymbol owner, ITypeSymbol type)
    {
        for (var candidate = owner.BaseType; candidate != null; candidate = candidate.BaseType)
        {
            foreach (var baseMember in candidate.GetMembers(member.Name))
            {
                if (baseMember.DeclaredAccessibility != Accessibility.Public || baseMember.IsStatic)
                {
                    continue;
                }

                if (baseMember is IPropertySymbol baseProperty
                    && member is IPropertySymbol
                    && baseProperty.SetMethod != null
                    && baseProperty.SetMethod.DeclaredAccessibility == Accessibility.Public
                    && SymbolEqualityComparer.Default.Equals(baseProperty.Type, type))
                {
                    return true;
                }

                if (baseMember is IEventSymbol baseEvent
                    && member is IEventSymbol
                    && SymbolEqualityComparer.Default.Equals(baseEvent.Type, type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass != null && MetadataName(attribute.AttributeClass) == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string Key(ITypeSymbol type)
        => type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Cref(INamedTypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace('<', '{').Replace('>', '}');

    private static string TypeParameterList(INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        return "<" + string.Join(", ", type.TypeParameters.Select(static parameter => parameter.Name)) + ">";
    }

    private static string ConstraintClauses(INamedTypeSymbol type)
    {
        var builder = new StringBuilder();
        foreach (var parameter in type.TypeParameters)
        {
            var constraints = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
            {
                constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }

            if (parameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                constraints.Add("struct");
            }

            if (parameter.HasNotNullConstraint)
            {
                constraints.Add("notnull");
            }

            foreach (var constraintType in parameter.ConstraintTypes)
            {
                constraints.Add(constraintType.ToDisplayString(_annotatedFormat));
            }

            if (parameter.HasConstructorConstraint)
            {
                constraints.Add("new()");
            }

            if (constraints.Count > 0)
            {
                builder.Append(" where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", constraints));
            }
        }

        return builder.ToString();
    }

    private static ImmutableArray<string> Chain(INamedTypeSymbol type)
    {
        var chain = ImmutableArray.CreateBuilder<string>();
        for (var candidate = (INamedTypeSymbol?)type; candidate != null; candidate = candidate.BaseType)
        {
            chain.Add(Key(candidate));
        }

        foreach (var contract in type.AllInterfaces)
        {
            chain.Add(Key(contract));
        }

        return chain.ToImmutable();
    }

    private static ImmutableArray<string> InstanceMethodNames(INamedTypeSymbol type)
    {
        var names = new SortedSet<string>(System.StringComparer.Ordinal);
        for (var candidate = (INamedTypeSymbol?)type; candidate != null; candidate = candidate.BaseType)
        {
            foreach (var member in candidate.GetMembers())
            {
                if (member is IMethodSymbol method
                    && !method.IsStatic
                    && method.MethodKind == MethodKind.Ordinary
                    && method.DeclaredAccessibility == Accessibility.Public)
                {
                    names.Add(method.Name);
                }
            }
        }

        return names.ToImmutableArray();
    }

    /// <summary>Summary inner XML of a member, following inheritdoc crefs, with paramref flattened to code; empty when the member has none.</summary>
    private static string ReadSummary(ISymbol member, Compilation compilation, CancellationToken cancellationToken)
    {
        var current = member;
        for (int depth = 0; depth < INHERITDOC_DEPTH && current != null; depth++)
        {
            string xml = current.GetDocumentationCommentXml(cancellationToken: cancellationToken) ?? string.Empty;
            if (xml.Length == 0)
            {
                return string.Empty;
            }

            XElement root;
            try
            {
                root = XElement.Parse("<root>" + xml + "</root>", LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                return string.Empty;
            }

            var summary = root.Descendants("summary").FirstOrDefault();
            if (summary != null)
            {
                return NormalizeSummary(summary);
            }

            var inherit = root.Descendants("inheritdoc").FirstOrDefault();
            string? cref = inherit?.Attribute("cref")?.Value;
            if (cref == null)
            {
                return string.Empty;
            }

            current = DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, compilation);
        }

        return string.Empty;
    }

    private static string NormalizeSummary(XElement summary)
    {
        foreach (var reference in summary.Descendants().Where(static node => node.Name == "paramref" || node.Name == "typeparamref").ToList())
        {
            reference.ReplaceWith(new XElement("c", reference.Attribute("name")?.Value ?? string.Empty));
        }

        var builder = new StringBuilder();
        foreach (var node in summary.Nodes())
        {
            builder.Append(node.ToString(SaveOptions.DisableFormatting));
        }

        return CollapseWhitespace(builder.ToString());
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
            }
            else
            {
                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>Indexes every extension method of the source assembly and of the referenced MewUI assemblies.</summary>
    public static FluentExtensionIndex BuildIndex(Compilation compilation, CancellationToken cancellationToken)
    {
        var entries = ImmutableArray.CreateBuilder<FluentExtensionEntry>();
        Collect(compilation.Assembly.GlobalNamespace, includeInternal: true, entries, cancellationToken);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly.Name.StartsWith(ASSEMBLY_PREFIX, System.StringComparison.Ordinal))
            {
                Collect(assembly.GlobalNamespace, includeInternal: false, entries, cancellationToken);
            }
        }

        var ordered = entries
            .OrderBy(static entry => entry.Name, System.StringComparer.Ordinal)
            .ThenBy(static entry => entry.Display, System.StringComparer.Ordinal)
            .ToImmutableArray();

        var hintAttribute = compilation.GetTypeByMetadataName(SETS_MEMBER_ATTRIBUTE);
        bool hintsAvailable = hintAttribute != null
            && compilation.IsSymbolAccessibleWithin(hintAttribute, compilation.Assembly);

        return new FluentExtensionIndex(new EquatableArray<FluentExtensionEntry>(ordered), hintsAvailable);
    }

    private static void Collect(
        INamespaceSymbol container,
        bool includeInternal,
        ImmutableArray<FluentExtensionEntry>.Builder entries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                Collect(nested, includeInternal, entries, cancellationToken);
            }
            else if (member is INamedTypeSymbol type
                && type.IsStatic
                && type.TypeKind == TypeKind.Class
                && (includeInternal || type.DeclaredAccessibility == Accessibility.Public))
            {
                foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method.IsExtensionMethod
                        && method.MethodKind == MethodKind.Ordinary
                        && (includeInternal || method.DeclaredAccessibility == Accessibility.Public))
                    {
                        entries.Add(ReadEntry(method));
                    }
                }
            }
        }
    }

    private static FluentExtensionEntry ReadEntry(IMethodSymbol method)
    {
        var receiver = method.Parameters[0].Type;
        var roots = ImmutableArray.CreateBuilder<string>();
        var chain = ImmutableArray.CreateBuilder<string>();
        bool appliesToAny = false;
        if (receiver is ITypeParameterSymbol typeParameter)
        {
            if (typeParameter.ConstraintTypes.Length == 0)
            {
                appliesToAny = true;
            }

            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                AddRoot(constraint, roots, chain);
            }
        }
        else
        {
            AddRoot(receiver, roots, chain);
        }

        int valueCount = method.Parameters.Length - 1;
        string firstValueKey = string.Empty;
        bool firstIsDelegate = false;
        int firstArity = -1;
        bool restOptional = true;
        string? hintedMember = null;
        if (valueCount > 0)
        {
            var first = method.Parameters[1];
            if (first.Type is not ITypeParameterSymbol && !first.IsParams)
            {
                firstValueKey = Key(first.Type);
            }

            firstIsDelegate = first.Type.TypeKind == TypeKind.Delegate;
            if (firstIsDelegate && first.Type is INamedTypeSymbol named && named.DelegateInvokeMethod != null)
            {
                firstArity = named.DelegateInvokeMethod.Parameters.Length;
            }

            for (int index = 2; index < method.Parameters.Length; index++)
            {
                if (!method.Parameters[index].IsOptional && !method.Parameters[index].IsParams)
                {
                    restOptional = false;
                }
            }

            if (valueCount == 1)
            {
                hintedMember = HintedMember(first);
            }
        }

        return new FluentExtensionEntry(
            method.Name,
            method.ContainingType.ToDisplayString() + "." + method.Name,
            appliesToAny,
            receiver is ITypeParameterSymbol,
            new EquatableArray<string>(roots.ToImmutable()),
            new EquatableArray<string>(chain.ToImmutable()),
            valueCount,
            firstValueKey,
            firstIsDelegate,
            firstArity,
            restOptional,
            hintedMember);
    }

    private static void AddRoot(ITypeSymbol type, ImmutableArray<string>.Builder roots, ImmutableArray<string>.Builder chain)
    {
        roots.Add(Key(type));
        chain.Add(Key(type));
        if (type is INamedTypeSymbol named)
        {
            for (var candidate = named.BaseType; candidate != null; candidate = candidate.BaseType)
            {
                chain.Add(Key(candidate));
            }
        }

        foreach (var contract in type.AllInterfaces)
        {
            chain.Add(Key(contract));
        }
    }

    private static string? HintedMember(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass != null
                && MetadataName(attribute.AttributeClass) == SETS_MEMBER_ATTRIBUTE
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string memberName)
            {
                return memberName;
            }
        }

        return null;
    }
}
