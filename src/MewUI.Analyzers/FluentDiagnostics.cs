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
}
