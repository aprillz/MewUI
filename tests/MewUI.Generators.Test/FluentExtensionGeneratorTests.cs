using System.Collections.Immutable;

using Aprillz.MewUI.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MewUI.Generators.Test;

/// <summary>
/// Drives the fluent extension generator over a stub of the control surface and checks the emitted
/// methods, the conflict skips and that the augmented compilation builds.
/// </summary>
[TestClass]
public sealed class FluentExtensionGeneratorTests
{
    private const string ENABLED_PROPERTY = "build_property.MewUIGenerateFluentExtensions";
    private const string NAMESPACE_PROPERTY = "build_property.MewUIFluentExtensionsNamespace";

    private const string CONTROL_API = """
        #nullable enable
        using System;

        namespace Aprillz.MewUI
        {
            public enum FontWeight { Normal, Bold }

            [AttributeUsage(AttributeTargets.Parameter)]
            internal sealed class FluentSetsMemberAttribute : Attribute
            {
                public FluentSetsMemberAttribute(string memberName) { }
            }

            [AttributeUsage(AttributeTargets.Property | AttributeTargets.Event)]
            internal sealed class FluentExcludedAttribute : Attribute
            {
                public FluentExcludedAttribute(string reason) { }
            }
        }

        namespace Aprillz.MewUI.Controls
        {
            public abstract class MewObject { }

            public abstract class Element : MewObject
            {
                /// <summary>Gets or sets the opacity, from 0 to 1.</summary>
                public double Opacity { get; set; }

                /// <summary>Gets or sets whether the element participates in hit testing.</summary>
                public bool IsHitTestVisible { get; set; }

                /// <summary>Occurs when the pointer enters.</summary>
                public event Action<string>? PointerEntered;

                [FluentExcluded("builder")]
                public string? Excluded { get; set; }

                public string? ReadOnly { get; }

                public string? Hidden { get; init; }

                protected virtual void OnPointerEntered() { }

                public void Focus() { }
            }

            public class Button : Element
            {
                /// <summary>Gets or sets the caption.</summary>
                public string? Caption { get; set; }

                /// <summary>Gets or sets the content of the button.</summary>
                public object? Content { get; set; }

                /// <summary>Gets or sets the predicate asked whether the button can be clicked.</summary>
                public Func<bool>? CanClick { get; set; }

                /// <summary>Occurs when the button is clicked.</summary>
                public event Action? Click;

                /// <summary>Gets or sets the font weight.</summary>
                public FontWeight FontWeight { get; set; }

            }

            public sealed class SplitButton : Button
            {
                /// <summary>Gets or sets the secondary caption.</summary>
                public string? SecondaryCaption { get; set; }

                /// <summary>Gets or sets a copy of the base caption with the same type.</summary>
                public new string? Caption { get; set; }

                /// <summary>Gets or sets a value whose name clashes with an inherited instance method.</summary>
                public new bool Focus { get; set; }
            }

            public sealed class Run : MewObject
            {
                /// <summary>Gets or sets the text.</summary>
                public string Text { get; set; } = string.Empty;
            }

            public sealed class GridViewColumn<TItem> where TItem : class
            {
                /// <summary>Gets or sets the header text.</summary>
                public string Header { get; set; } = string.Empty;

                /// <summary>Gets or sets the cell factory.</summary>
                public Func<TItem, Element>? CellFactory { get; set; }
            }

            public static class ControlExtensions
            {
                public static T Content<T>(this T button, string text, bool accessKey = true) where T : Button
                {
                    button.Content = text;
                    return button;
                }

                public static T OnPointerEntered<T>(this T element, Action<object> handler) where T : Element
                {
                    return element;
                }

                public static T Bold<T>(this T button, [FluentSetsMember("FontWeight")] bool bold = true) where T : Button
                {
                    return button;
                }

                public static SplitButton SecondaryCaption(this SplitButton button, string? text)
                {
                    button.SecondaryCaption = text;
                    return button;
                }
            }
        }

        namespace Other
        {
            public class NotAnOwner
            {
                public int Value { get; set; }
            }
        }
        """;

    [TestMethod]
    public void Disabled_EmitsNothing()
    {
        var result = Run(CONTROL_API, enabled: false);

        Assert.IsEmpty(result.GeneratedTrees);
        Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
    }

    [TestMethod]
    public void Enabled_EmitsSettersEventsAndWithForDelegateProperties()
    {
        var result = Run(CONTROL_API, enabled: true);

        Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors) + "\n\n" + result.AllGenerated);
        string button = result.Generated("Aprillz.MewUI.Controls.ButtonMarkupExtensions.g.cs");

        StringAssert.Contains(button, "public static T Caption<T>(this T target, string? value) where T : global::Aprillz.MewUI.Controls.Button");
        StringAssert.Contains(button, "/// <summary>Sets the caption.</summary>");
        StringAssert.Contains(result.Generated("Aprillz.MewUI.Controls.ElementMarkupExtensions.g.cs"), "public static T IsHitTestVisible<T>(this T target, bool value = true) where T : global::Aprillz.MewUI.Controls.Element");
        StringAssert.Contains(button, "public static T OnClick<T>(this T target, global::System.Action handler) where T : global::Aprillz.MewUI.Controls.Button");
        StringAssert.Contains(button, "target.Click += handler;");
        StringAssert.Contains(button, "public static T WithCanClick<T>(this T target, [global::Aprillz.MewUI.FluentSetsMember(\"CanClick\")] global::System.Func<bool>? value) where T : global::Aprillz.MewUI.Controls.Button");
        StringAssert.Contains(button, "/// <summary>Subscribes <paramref name=\"handler\"/> to <see cref=\"global::Aprillz.MewUI.Controls.Button.Click\"/>. Occurs when the button is clicked.</summary>");

        string element = result.Generated("Aprillz.MewUI.Controls.ElementMarkupExtensions.g.cs");
        StringAssert.Contains(element, "public static T Opacity<T>(this T target, double value) where T : global::Aprillz.MewUI.Controls.Element");
        Assert.DoesNotContain("Excluded<T>", element, "FluentExcluded members are skipped silently");
        Assert.DoesNotContain("ReadOnly<T>", element, "get-only properties are not settable");
        Assert.DoesNotContain("Hidden<T>", element, "init-only properties are not settable");
        Assert.IsFalse(result.GeneratedTrees.Any(tree => tree.Contains("NotAnOwner")), "types outside the owner roots get no class");
    }

    [TestMethod]
    public void Enabled_UsesConcreteReceiversForSealedAndGenericOwners()
    {
        var result = Run(CONTROL_API, enabled: true);

        Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
        string run = result.Generated("Aprillz.MewUI.Controls.RunMarkupExtensions.g.cs");
        StringAssert.Contains(run, "public static global::Aprillz.MewUI.Controls.Run Text(this global::Aprillz.MewUI.Controls.Run target, string value)");

        string column = result.Generated("Aprillz.MewUI.Controls.GridViewColumnMarkupExtensions.g.cs");
        StringAssert.Contains(column, "public static global::Aprillz.MewUI.Controls.GridViewColumn<TItem> Header<TItem>(this global::Aprillz.MewUI.Controls.GridViewColumn<TItem> target, string value) where TItem : class");
        StringAssert.Contains(column, "WithCellFactory<TItem>(this global::Aprillz.MewUI.Controls.GridViewColumn<TItem> target, [global::Aprillz.MewUI.FluentSetsMember(\"CellFactory\")] global::System.Func<TItem, global::Aprillz.MewUI.Controls.Element>? value) where TItem : class");
    }

    [TestMethod]
    public void Enabled_SkipsConflictsAndReportsThem()
    {
        var result = Run(CONTROL_API, enabled: true);

        Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
        string button = result.Generated("Aprillz.MewUI.Controls.ButtonMarkupExtensions.g.cs");
        string element = result.Generated("Aprillz.MewUI.Controls.ElementMarkupExtensions.g.cs");
        bool splitGenerated = result.GeneratedFileNames.Contains("Aprillz.MewUI.Controls.SplitButtonMarkupExtensions.g.cs");

        Assert.DoesNotContain("FontWeight<T>", button, "a hinted alias already sets the member");
        StringAssert.Contains(button, "public static T Content<T>(this T target, object? value)");
        Assert.DoesNotContain("OnPointerEntered<T>", element, "a same-shaped delegate overload would be ambiguous");
        Assert.IsFalse(splitGenerated, "every SplitButton member is skipped (hidden Caption, concrete-receiver twin SecondaryCaption, shadowing Focus), so no empty class is emitted");

        var skips = result.Diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("MEWG10", StringComparison.Ordinal))
            .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
            .ToList();
        CollectionAssert.Contains(skips, "MEWG101 global::Aprillz.MewUI.Controls.SplitButton.SecondaryCaption: 'SecondaryCaption' not generated, Aprillz.MewUI.Controls.ControlExtensions.SecondaryCaption already applies to this receiver with the same value type");
        CollectionAssert.Contains(skips, "MEWG102 global::Aprillz.MewUI.Controls.SplitButton.Focus: 'Focus' not generated, an instance method of that name would shadow it");
        CollectionAssert.Contains(skips, "MEWG101 global::Aprillz.MewUI.Controls.Element.PointerEntered: 'OnPointerEntered' not generated, Aprillz.MewUI.Controls.ControlExtensions.OnPointerEntered already applies to this receiver with the same value type");
        CollectionAssert.Contains(skips, "MEWG103 global::Aprillz.MewUI.Controls.Button.FontWeight: 'FontWeight' not generated, Aprillz.MewUI.Controls.ControlExtensions.Bold declares FluentSetsMember for this member");
        CollectionAssert.Contains(skips, "MEWG104 global::Aprillz.MewUI.Controls.SplitButton.Caption: 'Caption' not generated, a base type declares the member with the same type");
        Assert.IsTrue(result.Diagnostics.All(diagnostic => diagnostic.Severity == DiagnosticSeverity.Info), "skips are informational");
    }

    [TestMethod]
    public void Enabled_EmitsHintAttributesWhenTheCompilationHasNone()
    {
        // Strip the stub attributes and their uses so the compilation has no hint attributes at all.
        string source = CONTROL_API
            .Replace("[AttributeUsage(AttributeTargets.Parameter)]", "[System.Obsolete]")
            .Replace("[AttributeUsage(AttributeTargets.Property | AttributeTargets.Event)]", "[System.Obsolete]")
            .Replace("internal sealed class FluentSetsMemberAttribute", "internal sealed class UnusedSetsMember")
            .Replace("public FluentSetsMemberAttribute(string memberName)", "public UnusedSetsMember(string memberName)")
            .Replace("internal sealed class FluentExcludedAttribute", "internal sealed class UnusedExcluded")
            .Replace("public FluentExcludedAttribute(string reason)", "public UnusedExcluded(string reason)")
            .Replace("[FluentExcluded(\"builder\")]", string.Empty)
            .Replace("[FluentSetsMember(\"FontWeight\")] ", string.Empty);
        var result = Run(source, enabled: true);

        Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
        string hints = result.Generated("FluentHintAttributes.g.cs");
        StringAssert.Contains(hints, "internal sealed class FluentSetsMemberAttribute");
        StringAssert.Contains(hints, "internal sealed class FluentExcludedAttribute");
    }

    [TestMethod]
    public void NamespaceOverride_MovesEveryClass()
    {
        var result = Run(CONTROL_API, enabled: true, namespaceOverride: "Generated.Markup");

        Assert.IsEmpty(result.Errors, string.Join("\n", result.Errors));
        string button = result.Generated("Generated.Markup.ButtonMarkupExtensions.g.cs");
        StringAssert.Contains(button, "namespace Generated.Markup");
    }

    private static GeneratorRun Run(string source, bool enabled, string? namespaceOverride = null)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        string platformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = platformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "FluentExtensionGeneratorProbe",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var globals = new Dictionary<string, string>(StringComparer.Ordinal);
        if (enabled)
        {
            globals[ENABLED_PROPERTY] = "true";
        }

        if (namespaceOverride != null)
        {
            globals[NAMESPACE_PROPERTY] = namespaceOverride;
        }

        var driver = CSharpGeneratorDriver.Create(
            [new FluentExtensionGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: new StubOptionsProvider(globals));

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

        var generated = updated.SyntaxTrees
            .Where(tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .ToDictionary(tree => Path.GetFileName(tree.FilePath), tree => tree.ToString(), StringComparer.Ordinal);
        var errors = updated.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

        return new GeneratorRun(generated, errors, generatorDiagnostics);
    }

    private sealed class GeneratorRun(Dictionary<string, string> generated, List<string> errors, ImmutableArray<Diagnostic> diagnostics)
    {
        public List<string> Errors => errors;

        public ImmutableArray<Diagnostic> Diagnostics => diagnostics;

        public IEnumerable<string> GeneratedTrees => generated.Values;

        public IReadOnlyCollection<string> GeneratedFileNames => generated.Keys;

        public string AllGenerated => string.Join("\n\n", generated.Select(pair => "// " + pair.Key + "\n" + pair.Value));

        public string Generated(string fileName)
        {
            Assert.IsTrue(generated.TryGetValue(fileName, out var text), $"{fileName} was not generated; files: {string.Join(", ", generated.Keys)}");
            return text!;
        }
    }

    private sealed class StubOptionsProvider(Dictionary<string, string> globals) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new StubOptions(globals);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new StubOptions([]);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new StubOptions([]);
    }

    private sealed class StubOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
