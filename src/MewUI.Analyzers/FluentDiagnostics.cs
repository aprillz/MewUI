using Microsoft.CodeAnalysis;

namespace Aprillz.MewUI.Analyzers;

internal static class FluentDiagnostics
{
    public const string InitializerToFluentId = "MEW1101";

    public static readonly DiagnosticDescriptor InitializerToFluent = new(
        id: InitializerToFluentId,
        title: "Object initializer can be a MewUI fluent chain",
        messageFormat: "'{0}' initializer can be converted to a fluent chain",
        category: "MewUI.Markup",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "MewUI exposes fluent setter extensions; an object initializer whose members have a matching setter can be rewritten as a fluent chain.");

    public const string MergeStatementsId = "MEW1105";

    public static readonly DiagnosticDescriptor MergeStatements = new(
        id: MergeStatementsId,
        title: "Configuration statements can be one MewUI fluent chain",
        messageFormat: "The statements configuring '{0}' can be merged into a fluent chain",
        category: "MewUI.Markup",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "A declaration or assignment followed by statements that only configure the same instance can be written as a single fluent chain, which keeps the element and its configuration together.");

    public const string StatementToFluentCallId = "MEW1106";

    public static readonly DiagnosticDescriptor StatementToFluentCall = new(
        id: StatementToFluentCallId,
        title: "Configuration statement can be a MewUI fluent call",
        messageFormat: "This statement can be written as a fluent call",
        category: "MewUI.Markup",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "A property assignment, an event subscription, or a static attached setter has an equivalent fluent setter extension, so it can be written as a call on the element.");
}
