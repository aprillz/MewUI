using System.Collections.Immutable;

using Aprillz.MewUI.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MewUI.Generators.Test;

/// <summary>
/// Drives the generator over a stub of the binding surface and checks both the emitted chain and
/// that the augmented compilation still builds.
/// </summary>
[TestClass]
public sealed class BindingPathGeneratorTests
{
    private const string INTERCEPTOR_NAMESPACE = "Aprillz.MewUI.Generated";

    private const string BINDING_API = """

        namespace Aprillz.MewUI
        {
            public enum BindingMode { OneWay, TwoWay }

            public sealed class MewProperty<T> { }

            public sealed class ObservableValue<T> { public T Value { get; set; } }

            public static class BindingPath
            {
                public static BindingPath<TRoot, TRoot> From<TRoot>() where TRoot : class => null;
            }

            public sealed class BindingPath<TRoot, TValue> where TRoot : class
            {
                public BindingPath<TRoot, TNext> Then<TNext>(System.Func<TValue, TNext> getter) => null;

                public BindingPath<TRoot, TNext> Then<TNext>(
                    System.Func<TValue, ObservableValue<TNext>> selector) => null;
            }

            public static class MewPropertyBindingPathExtensions
            {
                public static BindingPath<TRoot, TNext> Then<TRoot, TOwner, TNext>(
                    this BindingPath<TRoot, TOwner> path, MewProperty<TNext> property)
                    where TRoot : class
                    where TOwner : Aprillz.MewUI.Controls.MewObject => null;
            }

            public static class InpcBindingPathExtensions
            {
                public static BindingPath<TRoot, TNext> ThenNotifying<TRoot, TOwner, TNext>(
                    this BindingPath<TRoot, TOwner> path,
                    System.Func<TOwner, TNext> getter,
                    System.Action<TOwner, TNext> setter = null,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(getter))]
                    string getterExpression = null)
                    where TRoot : class
                    where TOwner : class, System.ComponentModel.INotifyPropertyChanged => null;
            }
        }

        namespace Aprillz.MewUI.Controls
        {
            public class MewObject
            {
                public void SetBinding<TSource, T>(
                    Aprillz.MewUI.MewProperty<T> property,
                    TSource source,
                    Aprillz.MewUI.BindingPath<TSource, T> path,
                    Aprillz.MewUI.BindingMode? mode = null,
                    T fallbackValue = default)
                    where TSource : class
                {
                }

                public void SetBinding<TSource, T>(
                    Aprillz.MewUI.MewProperty<T> property,
                    TSource source,
                    System.Func<TSource, T> getter,
                    System.Action<TSource, T> setter = null,
                    Aprillz.MewUI.BindingMode? mode = null,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(getter))]
                    string getterExpression = null)
                    where TSource : class, System.ComponentModel.INotifyPropertyChanged
                {
                }
            }

            public static class BindingExtensions
            {
                public static TElement Bind<TElement, TSource, T>(
                    this TElement element,
                    Aprillz.MewUI.MewProperty<T> property,
                    TSource source,
                    System.Func<TSource, T> getter,
                    System.Action<TSource, T> setter = null,
                    Aprillz.MewUI.BindingMode? mode = null,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(getter))]
                    string getterExpression = null)
                    where TElement : MewObject
                    where TSource : class, System.ComponentModel.INotifyPropertyChanged => element;
            }

            public sealed class Label : MewObject
            {
                public static readonly Aprillz.MewUI.MewProperty<string> TextProperty = null;

                public string Text { get; set; }
            }
        }

        public class Notifier : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        }

        public class Profile : Notifier
        {
            public string DisplayName { get; set; }
        }

        public class Room
        {
            public Profile Owner { get; set; }
        }

        public class ViewModel : Notifier
        {
            public Profile Profile { get; set; }
            public object Current { get; set; }
            public Profile[] Items { get; set; }
            public Room Room { get; set; }
            public Aprillz.MewUI.ObservableValue<string> Caption { get; set; }
            public Aprillz.MewUI.Controls.Label Header { get; set; }
            public string Compute() => null;
        }
        """;

    [TestMethod]
    public void MemberChain_IsSplitIntoNotifyingSegments()
    {
        string generated = RunExpectingSuccess("x => x.Profile.DisplayName");

        StringAssert.Contains(generated, ".ThenNotifying(static value => value.Profile)");
        StringAssert.Contains(generated, ".ThenNotifying(static value => value.DisplayName,");
    }

    [TestMethod]
    public void NullConditionalChain_IsTreatedLikeAMemberChain()
    {
        string generated = RunExpectingSuccess("x => x.Profile?.DisplayName");

        StringAssert.Contains(generated, ".ThenNotifying(static value => value.Profile)");
        StringAssert.Contains(generated, ".ThenNotifying(static value => value.DisplayName,");
    }

    [TestMethod]
    public void NullForgivingChain_IsTreatedLikeAMemberChain()
    {
        string generated = RunExpectingSuccess("x => x.Profile!.DisplayName");

        StringAssert.Contains(generated, ".ThenNotifying(static value => value.Profile)");
    }

    [TestMethod]
    public void Cast_BecomesItsOwnSegment()
    {
        string generated = RunExpectingSuccess("x => ((Profile)x.Current).DisplayName");

        StringAssert.Contains(generated, ".Then(static value => (global::Profile)value)");
        StringAssert.Contains(generated, ".ThenNotifying(static value => value.DisplayName,");
    }

    [TestMethod]
    public void AsCast_BecomesItsOwnSegment()
    {
        string generated = RunExpectingSuccess("x => (x.Current as Profile).DisplayName");

        StringAssert.Contains(generated, ".Then(static value => value as global::Profile)");
    }

    [TestMethod]
    public void ConstantIndexer_BecomesANonObservingSegment()
    {
        string generated = RunExpectingSuccess("x => x.Items[0].DisplayName");

        StringAssert.Contains(generated, ".Then(static value => value[0])");
    }

    [TestMethod]
    public void NonNotifyingOwner_BecomesANonObservingSegment()
    {
        string generated = RunExpectingSuccess("x => x.Room.Owner.DisplayName");

        StringAssert.Contains(generated, ".Then(static value => value.Owner)");
    }

    [TestMethod]
    public void ObservableValueLeaf_UsesTheWrapperOverload()
    {
        string generated = RunExpectingSuccess("x => x.Profile.DisplayName");

        StringAssert.Contains(generated, "BindingPath.From<global::ViewModel>()");
    }

    [TestMethod]
    public void MewPropertyOwner_UsesTheMewPropertySegment()
    {
        string generated = RunExpectingSuccess("x => x.Header.Text");

        StringAssert.Contains(generated, ".Then(global::Aprillz.MewUI.Controls.Label.TextProperty)");
    }

    [TestMethod]
    public void ComputedGetter_IsReported()
    {
        var diagnostics = Run("x => x.Compute().Trim()", out _);

        Assert.AreEqual("MEWG001", diagnostics.Single(d => d.Id.StartsWith("MEWG")).Id);
    }

    [TestMethod]
    public void VariableIndexer_IsReported()
    {
        var diagnostics = Run("x => x.Items[Position].DisplayName", out _);

        Assert.AreEqual("MEWG001", diagnostics.Single(d => d.Id.StartsWith("MEWG")).Id);
    }

    [TestMethod]
    public void SingleMember_IsLeftToTheRuntime()
    {
        var diagnostics = Run("x => x.Profile.DisplayName", out string generated);
        Assert.IsFalse(diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        Run("x => x.Current", out string single);
        Assert.IsEmpty(single);
    }

    private static string RunExpectingSuccess(string getter)
    {
        var diagnostics = Run(getter, out string generated);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(
            errors,
            errors.Count == 0
                ? string.Empty
                : string.Join("\n", errors.Select(d => d.ToString())) + "\n\n" + generated);
        Assert.IsNotEmpty(generated);
        return generated;
    }

    private static ImmutableArray<Diagnostic> Run(string getter, out string generated)
    {
        string source = $$"""
            using Aprillz.MewUI.Controls;

            class Program
            {
                public const int Index = 0;

                public static int Position;

                static void Main()
                {
                    var label = new Label();
                    var model = new ViewModel();
                    label.Bind(Label.TextProperty, model, {{getter}});
                }
            }
            """ + BINDING_API;

        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFeatures(
            [
                new KeyValuePair<string, string>("InterceptorsNamespaces", INTERCEPTOR_NAMESPACE),
                new KeyValuePair<string, string>(
                    "InterceptorsPreviewNamespaces", INTERCEPTOR_NAMESPACE),
            ]);

        string platformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = platformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "BindingPathGeneratorProbe",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var driver = CSharpGeneratorDriver.Create(
            [new BindingPathGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var updated, out var generatorDiagnostics);

        var interceptors = updated.SyntaxTrees
            .FirstOrDefault(tree => tree.FilePath.Contains("BindingPathInterceptors"));
        generated = interceptors?.ToString() ?? string.Empty;

        return generatorDiagnostics.AddRange(updated.GetDiagnostics());
    }
}
