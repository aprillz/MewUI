using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MewUI.Analyzers.Test;

/// <summary>
/// Establishes that an analyzer can tell whether the source generator ran in the same
/// compilation by probing for a marker type the generator emits unconditionally.
/// </summary>
[TestClass]
public sealed class GeneratorMarkerProbeTests
{
    private const string MARKER_TYPE_NAME = "Aprillz.MewUI.Generated.BindingPathGeneratorMarker";

    private const string MARKER_SOURCE = """
        namespace Aprillz.MewUI.Generated
        {
            internal static class BindingPathGeneratorMarker
            {
            }
        }
        """;

    private const string USER_SOURCE = """
        class C
        {
            void M() => System.Console.WriteLine("x");
        }
        """;

    [TestMethod]
    public async Task AnalyzerSeesMarker_WhenTheGeneratorRan()
    {
        var compilation = CreateCompilation();
        var driver = CSharpGeneratorDriver.Create(new MarkerGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        var diagnostics = await AnalyzeAsync(generated);

        Assert.AreEqual(MarkerProbeAnalyzer.MarkerFoundId, diagnostics.Single().Id);
    }

    [TestMethod]
    public async Task AnalyzerMissesMarker_WhenTheGeneratorDidNotRun()
    {
        var diagnostics = await AnalyzeAsync(CreateCompilation());

        Assert.AreEqual(MarkerProbeAnalyzer.MarkerMissingId, diagnostics.Single().Id);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Compilation compilation)
    {
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MarkerProbeAnalyzer());
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation()
    {
        string platformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = platformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        return CSharpCompilation.Create(
            "MarkerProbe",
            [CSharpSyntaxTree.ParseText(USER_SOURCE)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Generator]
    private sealed class MarkerGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
            => context.RegisterPostInitializationOutput(
                postInitialization => postInitialization.AddSource(
                    "BindingPathGeneratorMarker.g.cs", MARKER_SOURCE));
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class MarkerProbeAnalyzer : DiagnosticAnalyzer
    {
        public const string MarkerFoundId = "POC0001";
        public const string MarkerMissingId = "POC0002";

        private static readonly DiagnosticDescriptor _markerFound = new(
            MarkerFoundId,
            "Marker visible",
            "The generator marker is visible to the analyzer",
            "Poc",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor _markerMissing = new(
            MarkerMissingId,
            "Marker absent",
            "The generator marker is not visible to the analyzer",
            "Poc",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(_markerFound, _markerMissing);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(Probe, SyntaxKind.InvocationExpression);
        }

        private static void Probe(SyntaxNodeAnalysisContext context)
        {
            var marker = context.Compilation.GetTypeByMetadataName(MARKER_TYPE_NAME);
            context.ReportDiagnostic(Diagnostic.Create(
                marker is null ? _markerMissing : _markerFound,
                context.Node.GetLocation()));
        }
    }
}
