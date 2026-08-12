using Microsoft.CodeAnalysis;

namespace Aprillz.MewUI.Analyzers;

internal static class BindingDiagnostics
{
    public const string NonObservingThenId = "MEW1201";
    public const string NotifyingGetterShapeId = "MEW1202";

    public static readonly DiagnosticDescriptor NonObservingThen = new(
        id: NonObservingThenId,
        title: "Binding path segment does not observe a notifying owner",
        messageFormat: "'{0}' raises PropertyChanged; use ThenNotifying to observe '{1}'",
        category: "MewUI.Binding",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Then appends a segment that is read once and refreshed only when an upstream segment rebuilds. When the owner raises PropertyChanged, ThenNotifying subscribes to it so the binding follows later changes.");

    public const string GeneratorRequiredId = "MEW1203";

    public static readonly DiagnosticDescriptor GeneratorRequired = new(
        id: GeneratorRequiredId,
        title: "Dotted binding getter requires the binding path generator",
        messageFormat: "This build cannot split '{0}' into path segments",
        category: "MewUI.Binding",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A getter that walks more than one member is split into segments by the MewUI binding path generator, which needs Roslyn 4.12 or newer. Build with a newer SDK, or write the path as an explicit ThenNotifying chain.");

    public static readonly DiagnosticDescriptor NotifyingGetterShape = new(
        id: NotifyingGetterShapeId,
        title: "ThenNotifying getter must be a single member access",
        messageFormat: "ThenNotifying cannot infer a property name from this getter",
        category: "MewUI.Binding",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "ThenNotifying reads the observed property name from the getter expression, so the getter must be a single member access such as 'x => x.Name'. Split a multi-step path into separate segments, or move computation into a convert delegate.");
}
