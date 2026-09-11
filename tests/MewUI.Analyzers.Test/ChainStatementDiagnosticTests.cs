using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class ChainStatementDiagnosticTests
{
    private const string FluentApi = """

        public class Widget
        {
            public string Text { get; set; }
            public int Width { get; set; }
            public event System.Action Click;
        }

        public class Holder : Widget
        {
            public void Add(Widget child) { }
        }

        [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
        internal sealed class FluentReplacesMemberAttribute : System.Attribute
        {
            public FluentReplacesMemberAttribute(string memberName) { MemberName = memberName; }
            public string MemberName { get; }
        }

        public static class WidgetExtensions
        {
            [FluentReplacesMember("Add")]
            public static T Children<T>(this T holder, params Widget[] children) where T : Holder { return holder; }
            public static T Text<T>(this T widget, string value) where T : Widget { widget.Text = value; return widget; }
            public static T Width<T>(this T widget, int value) where T : Widget { widget.Width = value; return widget; }
            public static T OnClick<T>(this T widget, System.Action handler) where T : Widget { widget.Click += handler; return widget; }
            public static T Ref<T>(this T widget, out T field) where T : class { field = widget; return widget; }
        }
        """;

    [TestMethod]
    public async Task ReportsMergeOnAnchor_AndNotOnTheStatementsItAbsorbs()
    {
        // The follow-ups belong to the anchor's merge, so MEW1106 must not also claim them: two fixes
        // over the same statement would collide in Fix All.
        var source = """
            class C
            {
                void M()
                {
                    var {|MEW1105:w|} = new Widget();
                    w.Text = "hi";
                    w.Width(5);
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                void M()
                {
                    new Widget()
                        .Ref(out var w)
                        .Text("hi")
                        .Width(5);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task ReportsStandaloneConversion_WhenNoAnchorClaimsTheStatement()
    {
        var source = """
            class C
            {
                void M(Widget w)
                {
                    {|MEW1106:w.Text = "hi";|}
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                void M(Widget w)
                {
                    w.Text("hi");
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task ReportsMergeForTopLevelStatements()
    {
        var source = """
            var {|MEW1105:w|} = new Widget();
            w.Text = "hi";
            """ + FluentApi;

        var fixedSource = """
            new Widget()
                .Ref(out var w)
                .Text("hi");
            """ + FluentApi;

        await RunAsync(source, fixedSource, OutputKind.ConsoleApplication);
    }

    [TestMethod]
    public async Task FixAllConvertsEveryStandaloneStatement()
    {
        var source = """
            class C
            {
                void M(Widget a, Widget b)
                {
                    {|MEW1106:a.Text = "one";|}
                    {|MEW1106:b.Click += Run;|}
                }
                void Run() { }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                void M(Widget a, Widget b)
                {
                    a.Text("one");
                    b.OnClick(Run);
                }
                void Run() { }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task FixesAnchorWhoseValueIsAlreadyAChain()
    {
        var source = """
            class C
            {
                void M(Widget a, Widget b)
                {
                    var {|MEW1105:h|} = new Holder().Text("hi");
                    h.Add(a);
                    h.Add(b);
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                void M(Widget a, Widget b)
                {
                    new Holder()
                        .Ref(out var h)
                        .Text("hi")
                        .Children(a, b);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    private static async Task RunAsync(
        string source, string fixedSource, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var test = new CSharpCodeFixTest<ChainStatementAnalyzer, ChainStatementCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.SolutionTransforms.Add((solution, projectId) => solution.WithProjectCompilationOptions(
            projectId, solution.GetProject(projectId)!.CompilationOptions!.WithOutputKind(outputKind)));

        await test.RunAsync();
    }
}
