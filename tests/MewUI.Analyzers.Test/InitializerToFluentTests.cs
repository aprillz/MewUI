using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class InitializerToFluentTests
{
    // A self-contained fluent API so resolution is exercised without depending on MewUI.
    // Tag has no extension setter, so it is the "not convertible" case.
    private const string FluentApi = """

        public class Widget
        {
            public string Text { get; set; }
            public int Width { get; set; }
            public object Tag { get; set; }
            public System.Action Click { get; set; }
            public object Content { get; set; }
        }

        public static class WidgetExtensions
        {
            public static T Text<T>(this T widget, string value) where T : Widget { widget.Text = value; return widget; }
            public static T Width<T>(this T widget, int value) where T : Widget { widget.Width = value; return widget; }
            public static T OnClick<T>(this T widget, System.Action handler) where T : Widget { widget.Click += handler; return widget; }
            public static T Content<T>(this T widget, object value) where T : Widget { widget.Content = value; return widget; }
        }

        public static class Factory
        {
            public static object Build() => null;
        }
        """;

    [TestMethod]
    public async Task ConvertsAndExpands_WhenEverySetterResolves()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { Text = "hi", Width = 5 };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .Text("hi")
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task DoesNotStrandParens_OnMultiLineInitializer()
    {
        // Repro: the type's trailing newline must not push the `()` onto the next line.
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|}
                    {
                        Text = "hi",
                        Width = 5
                    };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .Text("hi")
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task KeepsResidualInitializer_WhenSomeMembersDoNotResolve()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { Text = "hi", Tag = null, Width = 5 };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget() { Tag = null }
                        .Text("hi")
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MapsDelegateProperty_ToOnPrefixedSetter()
    {
        // `Click` (a delegate property) has no `.Click(...)` setter, so it maps to `.OnClick(...)`.
        var source = """
            class C
            {
                object M()
                {
                    System.Action handler = () => { };
                    var w = new {|MEW1101:Widget|} { Click = handler, Width = 5 };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    System.Action handler = () => { };
                    var w = new Widget()
                        .OnClick(handler)
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task ConvertsValueThatIsItselfACall_WithoutThrowing()
    {
        // Repro: a converted member whose value is a call chain (`Factory.Build()`) must not crash the
        // fix. `Factory` is a type, so the argument stays inline.
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { Width = 5, Content = Factory.Build() };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .Width(5)
                        .Content(Factory.Build());
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task NoDiagnostic_WhenNoMemberResolves()
    {
        var source = """
            class C { object M() => new Widget { Tag = null }; }
            """ + FluentApi;

        // No markup and identical fixed code: the analyzer must report nothing.
        await RunAsync(source, source);
    }

    private static async Task RunAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<InitializerToFluentAnalyzer, InitializerToFluentCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync();
    }
}
