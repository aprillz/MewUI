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

            public static class IndexedBindingPathExtensions
            {
                public static BindingPath<TRoot, TNext> ThenIndexed<TRoot, TOwner, TNext>(
                    this BindingPath<TRoot, TOwner> path,
                    System.Func<TOwner, TNext> getter)
                    where TRoot : class
                    where TOwner : class => null;
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

            public sealed class BindingInput<TValue> { }

            public static class BindingInput
            {
                public static BindingInput<TValue> From<TRoot, TValue>(
                    TRoot source, BindingPath<TRoot, TValue> path)
                    where TRoot : class => null;

                public static BindingInput<TValue> From<TSource, TValue>(
                    TSource source,
                    System.Func<TSource, TValue> getter,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(getter))]
                    string getterExpression = null)
                    where TSource : class, System.ComponentModel.INotifyPropertyChanged => null;
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

                public void SetBinding<TProp, T1, T2>(
                    Aprillz.MewUI.MewProperty<TProp> property,
                    Aprillz.MewUI.BindingInput<T1> first,
                    Aprillz.MewUI.BindingInput<T2> second,
                    System.Func<T1, T2, TProp> combine)
                {
                }

                public void SetBinding<TProp, T1, T2>(
                    Aprillz.MewUI.MewProperty<TProp> property,
                    Aprillz.MewUI.BindingInput<T1> first,
                    Aprillz.MewUI.BindingInput<T2> second,
                    TProp fallbackValue,
                    System.Func<T1, T2, TProp> combine)
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
                    T fallbackValue,
                    System.Action<TSource, T> setter = null,
                    Aprillz.MewUI.BindingMode? mode = null,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(getter))]
                    string getterExpression = null)
                    where TElement : MewObject
                    where TSource : class, System.ComponentModel.INotifyPropertyChanged => element;

                public static TElement Bind<TElement, TProp, TSource, T1, T2>(
                    this TElement element,
                    Aprillz.MewUI.MewProperty<TProp> property,
                    TSource source,
                    System.Func<TSource, T1> first,
                    System.Func<TSource, T2> second,
                    System.Func<T1, T2, TProp> combine,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(first))] string firstExpression = null,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(second))] string secondExpression = null)
                    where TElement : MewObject
                    where TSource : class, System.ComponentModel.INotifyPropertyChanged => element;

                public static TElement Bind<TElement, TProp, TSource, T1, T2>(
                    this TElement element,
                    Aprillz.MewUI.MewProperty<TProp> property,
                    TSource source,
                    System.Func<TSource, T1> first,
                    System.Func<TSource, T2> second,
                    TProp fallbackValue,
                    System.Func<T1, T2, TProp> combine,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(first))] string firstExpression = null,
                    [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(second))] string secondExpression = null)
                    where TElement : MewObject
                    where TSource : class, System.ComponentModel.INotifyPropertyChanged => element;

                public static TElement Bind<TElement, TSource, T>(
                    this TElement element,
                    Aprillz.MewUI.MewProperty<T> property,
                    TSource source,
                    System.Func<TSource, Aprillz.MewUI.ObservableValue<T>> selector,
                    Aprillz.MewUI.BindingMode? mode = null)
                    where TElement : MewObject
                    where TSource : class => element;

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

                public static readonly Aprillz.MewUI.MewProperty<int> CountProperty = null;

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
            public System.Collections.ObjectModel.ObservableCollection<string> Names { get; set; }
            public System.Collections.ObjectModel.ObservableCollection<Profile> Profiles { get; set; }
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
    public void ConstantIndexer_OnAPlainArrayBecomesANonObservingSegment()
    {
        string generated = RunExpectingSuccess("x => x.Items[0].DisplayName");

        StringAssert.Contains(generated, ".Then(static value => value[0])");
    }

    [TestMethod]
    public void ConstantIndexer_OnANotifyingCollectionBecomesAnIndexedSegment()
    {
        string generated = RunExpectingSuccess("x => x.Profiles[0].DisplayName");

        StringAssert.Contains(generated, ".ThenIndexed(static value => value[0])");
    }

    [TestMethod]
    public void NonNotifyingOwner_BecomesANonObservingSegment()
    {
        string generated = RunExpectingSuccess("x => x.Room.Owner.DisplayName");

        StringAssert.Contains(generated, ".Then(static value => value.Owner)");
    }

    [TestMethod]
    public void ObservableValueSelector_IsLeftToTheRuntime()
    {
        var diagnostics = Run("x => x.Caption", out string generated);

        Assert.IsFalse(diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            string.Join("\n", diagnostics));
        Assert.IsEmpty(generated, "The ObservableValue selector overload is handled by the runtime.");
    }

    [TestMethod]
    public void MewPropertyOwner_UsesTheMewPropertySegment()
    {
        string generated = RunExpectingSuccess("x => x.Header.Text");

        StringAssert.Contains(generated, ".Then(global::Aprillz.MewUI.Controls.Label.TextProperty)");
    }

    [TestMethod]
    public void InheritedMember_UsesTheReceiverTypeToChooseTheSegment()
    {
        // Count is declared on Collection<T>, which does not notify, but the receiver is an
        // ObservableCollection<T>, which does.
        string generated = RunExpectingSuccess("x => x.Names.Count", "Label.CountProperty");

        StringAssert.Contains(generated, ".ThenNotifying(static value => value.Count");
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

    [TestMethod]
    public void InputMemberChain_IsSplitIntoNotifyingSegments()
    {
        string generated = ExpectSuccess(
            CompileInput("x => x.Profile.DisplayName", out string output), output);

        StringAssert.Contains(generated, ".ThenNotifying(static value => value.Profile)");
        StringAssert.Contains(generated, "BindingInput<string> From0(");
        StringAssert.Contains(generated, "global::Aprillz.MewUI.BindingInput.From(source, _path0)");
    }

    [TestMethod]
    public void CombinedNestedGetter_GetsAPathForEveryLambda()
    {
        string generated = RunExpectingSuccess(
            "x => x.Profile.DisplayName, x => x.Current, static (name, current) => name");

        StringAssert.Contains(generated, "_path0_0 =");
        StringAssert.Contains(generated, "_path0_1 =");
        StringAssert.Contains(generated, "global::Aprillz.MewUI.BindingInput.From(source, _path0_0)");
        StringAssert.Contains(generated, "global::Aprillz.MewUI.BindingInput.From(source, _path0_1)");
        StringAssert.Contains(generated, "combine);");
    }

    [TestMethod]
    public void CombinedFallback_IsPassedThrough()
    {
        string generated = RunExpectingSuccess(
            "x => x.Profile.DisplayName, x => x.Current, \"none\", static (name, current) => name");

        StringAssert.Contains(generated, "string fallbackValue,");
        StringAssert.Contains(generated, "                fallbackValue,");
    }

    [TestMethod]
    public void CombinedSingleMembers_AreLeftToTheRuntime()
    {
        var diagnostics = Run(
            "x => x.Profile, x => x.Current, static (profile, current) => \"\"", out string generated);

        Assert.IsFalse(diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsEmpty(generated);
    }

    [TestMethod]
    public void CombinedComputedGetter_IsReported()
    {
        var diagnostics = Run(
            "x => x.Profile.DisplayName, x => x.Compute().Trim(), static (name, text) => name", out _);

        Assert.AreEqual(
            "MEWG001",
            diagnostics.Single(diagnostic => diagnostic.Id.StartsWith("MEWG")).Id);
    }

    [TestMethod]
    public void SingleGetterFallback_IsPassedThrough()
    {
        string generated = RunExpectingSuccess("x => x.Profile.DisplayName, \"none\"");

        StringAssert.Contains(generated, "string fallbackValue,");
        StringAssert.Contains(generated, "mode, fallbackValue);");
    }

    [TestMethod]
    public void InputSingleMember_IsLeftToTheRuntime()
    {
        var diagnostics = CompileInput("x => x.Current", out string generated);

        Assert.IsFalse(diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsEmpty(generated);
    }

    [TestMethod]
    public void InputComputedGetter_IsReported()
    {
        var diagnostics = CompileInput("x => x.Compute().Trim()", out _);

        Assert.AreEqual(
            "MEWG001",
            diagnostics.Single(diagnostic => diagnostic.Id.StartsWith("MEWG")).Id);
    }

    private static string RunExpectingSuccess(string getter, string property = "Label.TextProperty")
        => ExpectSuccess(Run(getter, out string generated, property), generated);

    private static string ExpectSuccess(ImmutableArray<Diagnostic> diagnostics, string generated)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(
            errors,
            errors.Count == 0
                ? string.Empty
                : string.Join("\n", errors.Select(d => d.ToString())) + "\n\n" + generated);
        Assert.IsNotEmpty(generated);
        return generated;
    }

    private static ImmutableArray<Diagnostic> Run(
        string getter,
        out string generated,
        string property = "Label.TextProperty")
        => Compile($"label.Bind({property}, model, {getter});", out generated);

    private static ImmutableArray<Diagnostic> CompileInput(string getter, out string generated)
        => Compile($"var input = Aprillz.MewUI.BindingInput.From(model, {getter});", out generated);

    private static ImmutableArray<Diagnostic> Compile(string statement, out string generated)
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
                    {{statement}}
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
