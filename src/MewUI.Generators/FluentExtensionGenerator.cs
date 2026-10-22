using System.Collections.Generic;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Aprillz.MewUI.Generators;

/// <summary>
/// Emits the conventional fluent extensions (same-name setters and On-prefixed event subscriptions)
/// for every markup owner type declared in a compilation that opts in with the
/// MewUIGenerateFluentExtensions build property.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class FluentExtensionGenerator : IIncrementalGenerator
{
    private const string ENABLED_PROPERTY = "build_property.MewUIGenerateFluentExtensions";
    private const string NAMESPACE_PROPERTY = "build_property.MewUIFluentExtensionsNamespace";
    private const string HINTS_HINT_NAME = "FluentHintAttributes.g.cs";
    private const string CATEGORY = "MewUI.Markup";

    internal static readonly DiagnosticDescriptor SkippedConflict = new(
        id: "MEWG101",
        title: "Fluent extension not generated because an extension with the same name already applies",
        messageFormat: "{0}.{1}: '{2}' not generated, {3} already applies to this receiver with the same value type",
        category: CATEGORY,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SkippedInstanceMethod = new(
        id: "MEWG102",
        title: "Fluent extension not generated because an instance method has the same name",
        messageFormat: "{0}.{1}: '{2}' not generated, an instance method of that name would shadow it",
        category: CATEGORY,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SkippedAlias = new(
        id: "MEWG103",
        title: "Fluent extension not generated because a hinted extension already sets the member",
        messageFormat: "{0}.{1}: '{2}' not generated, {3} declares FluentSetsMember for this member",
        category: CATEGORY,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SkippedInherited = new(
        id: "MEWG104",
        title: "Fluent extension not generated because a base type declares the same member",
        messageFormat: "{0}.{1}: '{2}' not generated, a base type declares the member with the same type",
        category: CATEGORY,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor SkippedOwner = new(
        id: "MEWG105",
        title: "Fluent extension class not generated because a type with its name exists",
        messageFormat: "{0}: no extensions generated, the compilation already declares '{1}'",
        category: CATEGORY,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ReadOptions(provider.GlobalOptions));

        var index = context.CompilationProvider
            .Combine(options)
            .Select(static (pair, cancellationToken) => pair.Right.Enabled
                ? FluentSymbolReader.BuildIndex(pair.Left, cancellationToken)
                : FluentExtensionIndex.Empty);

        var owners = context.SyntaxProvider
            .CreateSyntaxProvider(IsCandidate, Transform)
            .Where(static owner => owner != null);

        context.RegisterSourceOutput(
            owners.Combine(index).Combine(options),
            static (production, input) => Emit(production, input.Left.Left!, input.Left.Right, input.Right));

        context.RegisterSourceOutput(
            index.Combine(options),
            static (production, input) =>
            {
                if (input.Right.Enabled && !input.Left.HintAttributesAvailable)
                {
                    production.AddSource(HINTS_HINT_NAME, SourceText.From(FluentHintAttributeSource.TEXT, Encoding.UTF8));
                }
            });
    }

    private static FluentGeneratorOptions ReadOptions(AnalyzerConfigOptions options)
    {
        bool enabled = options.TryGetValue(ENABLED_PROPERTY, out string? enabledText)
            && string.Equals(enabledText, "true", System.StringComparison.OrdinalIgnoreCase);
        string? namespaceOverride = options.TryGetValue(NAMESPACE_PROPERTY, out string? namespaceText)
            && !string.IsNullOrWhiteSpace(namespaceText)
            ? namespaceText.Trim()
            : null;
        return new FluentGeneratorOptions(enabled, namespaceOverride);
    }

    private static bool IsCandidate(SyntaxNode node, CancellationToken cancellationToken)
    {
        if (node is not ClassDeclarationSyntax declaration || declaration.Parent is TypeDeclarationSyntax)
        {
            return false;
        }

        bool visible = false;
        foreach (var modifier in declaration.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.StaticKeyword))
            {
                return false;
            }

            if (modifier.IsKind(SyntaxKind.PublicKeyword) || modifier.IsKind(SyntaxKind.PartialKeyword))
            {
                visible = true;
            }
        }

        return visible;
    }

    private static FluentOwnerModel? Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // A partial type is visited once per part; only the first part produces the model.
        var first = symbol.DeclaringSyntaxReferences[0];
        if (first.SyntaxTree != declaration.SyntaxTree || first.Span != declaration.Span)
        {
            return null;
        }

        return FluentSymbolReader.TryReadOwner(symbol, context.SemanticModel.Compilation, cancellationToken);
    }

    private static void Emit(
        SourceProductionContext production,
        FluentOwnerModel owner,
        FluentExtensionIndex index,
        FluentGeneratorOptions options)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (owner.ExtensionClassExists && options.NamespaceOverride == null)
        {
            production.ReportDiagnostic(Diagnostic.Create(SkippedOwner, Location.None, owner.Display, owner.ClassName));
            return;
        }

        var emitted = new List<FluentMemberModel>();
        var skipped = new List<string>();
        foreach (var member in owner.Members.Items)
        {
            string? reason = SkipReason(production, owner, member, index);
            if (reason == null)
            {
                emitted.Add(member);
            }
            else
            {
                skipped.Add(member.MethodName + ": " + reason);
            }
        }

        if (emitted.Count == 0)
        {
            // An empty public static class would still show up in the API surface and the reference docs.
            return;
        }

        string ownerNamespace = options.NamespaceOverride ?? owner.Namespace;
        string source = FluentExtensionEmitter.Emit(owner, ownerNamespace, emitted, skipped);
        string hintName = (ownerNamespace.Length == 0 ? string.Empty : ownerNamespace + ".") + owner.ClassName + ".g.cs";
        production.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
    }

    /// <summary>Reports why a member gets no extension and returns the reason, or null when it is generated.</summary>
    private static string? SkipReason(
        SourceProductionContext production,
        FluentOwnerModel owner,
        FluentMemberModel member,
        FluentExtensionIndex index)
    {
        string methodName = member.MethodName;
        if (member.IsHiddenBySameTypedBase)
        {
            production.ReportDiagnostic(Diagnostic.Create(SkippedInherited, Location.None, owner.Display, member.Name, methodName));
            return "a base type declares the member with the same type";
        }

        if (owner.InstanceMethodNames.Contains(methodName))
        {
            production.ReportDiagnostic(Diagnostic.Create(SkippedInstanceMethod, Location.None, owner.Display, member.Name, methodName));
            return "shadowed by an instance method of that name";
        }

        foreach (var entry in index.Entries.Items)
        {
            if (!entry.IsRelatedTo(owner))
            {
                continue;
            }

            if (entry.HintedMember == member.Name)
            {
                production.ReportDiagnostic(Diagnostic.Create(SkippedAlias, Location.None, owner.Display, member.Name, methodName, entry.Display));
                return "set through the hinted extension " + entry.Display;
            }

            // Covering every instance, a twin is ambiguous (one value) or hides the generated method
            // (optional extras); on a derived receiver only a twin generic over the receiver is ambiguous.
            bool shadows = entry.AppliesToOwner(owner)
                ? entry.ValueCount == 1 || entry.RestOptional
                : entry.ReceiverIsTypeParameter && entry.ValueCount == 1;
            if (entry.Name == methodName && entry.ValueCount >= 1 && shadows && Collides(entry, member))
            {
                production.ReportDiagnostic(Diagnostic.Create(SkippedConflict, Location.None, owner.Display, member.Name, methodName, entry.Display));
                return "already provided by " + entry.Display;
            }
        }

        return null;
    }

    /// <summary>True when a call with the member's value would be ambiguous or shadowed by the existing extension.</summary>
    private static bool Collides(FluentExtensionEntry entry, FluentMemberModel member)
    {
        if (entry.FirstValueKey == member.TypeKey)
        {
            return true;
        }

        // Two delegate overloads whose lambda shapes match cannot be told apart from an untyped lambda.
        return member.IsEvent
            && entry.FirstValueIsDelegate
            && entry.FirstValueDelegateArity >= 0
            && entry.FirstValueDelegateArity == member.DelegateArity;
    }
}
