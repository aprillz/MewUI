using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class MergeChainStatementsTests
{
    private const string FluentApi = """

        public class Widget
        {
            public string Text { get; set; }
            public int Width { get; set; }
            public event System.Action Click;
        }

        public static class Board
        {
            public static void SetSlot(Widget widget, int slot) { }
        }

        public class Holder : Widget
        {
            public void Add(Widget child) { }
            public void AddRange(params Widget[] children) { }
            public void Attach(Widget child) { }
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
            [FluentReplacesMember("AddRange")]
            public static T Children<T>(this T holder, params Widget[] children) where T : Holder { holder.AddRange(children); return holder; }
            public static T Text<T>(this T widget, string value) where T : Widget { widget.Text = value; return widget; }
            public static T Width<T>(this T widget, int value) where T : Widget { widget.Width = value; return widget; }
            public static T OnClick<T>(this T widget, System.Action handler) where T : Widget { widget.Click += handler; return widget; }
            public static T Slot<T>(this T widget, int slot) where T : Widget { return widget; }
            public static T Ref<T>(this T widget, out T field) where T : class { field = widget; return widget; }
        }
        """;

    [TestMethod]
    public async Task MergesAssignmentWithFollowUpCall()
    {
        var source = """
            class C
            {
                Widget _w;
                void M()
                {
                    _w = new Widget().Te[||]xt("hi");
                    _w.Width(5);
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                Widget _w;
                void M()
                {
                    _w = new Widget()
                        .Text("hi")
                        .Width(5);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MergesLocalDeclaration_CapturesWithRefOut()
    {
        // A `var x = ...` local of a reference type is captured inline with `.Ref(out var x)`.
        var source = """
            class C
            {
                object M()
                {
                    var w = new Wi[||]dget();
                    w.Text("hi");
                    w.Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    new Widget()
                        .Ref(out var w)
                        .Text("hi")
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MergesEventSubscription_AsOnHandler()
    {
        // `_btn.Click += handler;` (an event subscription) folds in as `.OnClick(handler)`.
        var source = """
            class C
            {
                Widget _btn;
                void M()
                {
                    _btn = new Wi[||]dget().Text("x");
                    _btn.Click += () => Run();
                }
                void Run() { }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                Widget _btn;
                void M()
                {
                    _btn = new Widget()
                        .Text("x")
                        .OnClick(() => Run());
                }
                void Run() { }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MergesPropertyAssignmentAndAttachedSetter()
    {
        // A property assignment and a static attached setter configure the target just as a fluent call
        // does, so both fold into the chain in source order.
        var source = """
            class C
            {
                void M()
                {
                    var w = new Wi[||]dget();
                    w.Text = "hi";
                    Board.SetSlot(w, 2);
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
                        .Slot(2)
                        .Width(5);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MergesReplacedMemberCalls_IntoOneSetterCall()
    {
        // `Children` declares that it replaces `Add` and `AddRange`, and it takes the whole list at
        // once, so the consecutive calls collapse into a single `.Children(...)`.
        var source = """
            class C
            {
                void M(Widget a, Widget b, Widget c)
                {
                    var h = new Ho[||]lder();
                    h.Add(a);
                    h.AddRange(b, c);
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                void M(Widget a, Widget b, Widget c)
                {
                    new Holder()
                        .Ref(out var h)
                        .Children(a, b, c);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task KeepsReplacedMemberCallsApart_WhenAnotherCallSeparatesThem()
    {
        var source = """
            class C
            {
                void M(Widget a, Widget b)
                {
                    var h = new Ho[||]lder();
                    h.Add(a);
                    h.Text("hi");
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
                        .Children(a)
                        .Text("hi")
                        .Children(b);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task StopsAtUnhintedInstanceMethod()
    {
        // `Attach` has no extension declaring that it replaces it, so it is left alone.
        var source = """
            class C
            {
                void M(Widget a)
                {
                    var h = new Ho[||]lder();
                    h.Text("hi");
                    h.Attach(a);
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                void M(Widget a)
                {
                    new Holder()
                        .Ref(out var h)
                        .Text("hi");
                    h.Attach(a);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task StopsAtStatementConfiguringAnotherTarget()
    {
        // `other.Text = ...` is a valid conversion on its own, but it does not configure `w`, so it ends
        // the run of follow-ups instead of being folded in.
        var source = """
            class C
            {
                void M(Widget other)
                {
                    var w = new Wi[||]dget();
                    w.Text = "hi";
                    other.Text = "no";
                    w.Width(5);
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                void M(Widget other)
                {
                    new Widget()
                        .Ref(out var w)
                        .Text("hi");
                    other.Text = "no";
                    w.Width(5);
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MergesTopLevelStatements()
    {
        // Top-level statements are wrapped in GlobalStatementSyntax, not a block; collection stops at
        // the local function, and the trailing type declarations are never folded in.
        var source = """
            var w = new Wi[||]dget();
            w.Text("hi");
            w.Click += () => Run();

            void Run() { }
            """ + FluentApi;

        var fixedSource = """
            new Widget()
                .Ref(out var w)
                .Text("hi")
                .OnClick(() => Run());

            void Run() { }
            """ + FluentApi;

        await RunAsync(source, fixedSource, OutputKind.ConsoleApplication);
    }

    [TestMethod]
    public async Task NotOffered_WhenAnchorValueCannotCarryTheChain()
    {
        // `flag ? a : b` would bind as `flag ? a : b.Text("hi")` once a call is appended to it.
        var source = """
            class C
            {
                void M(bool flag, Widget a, Widget b)
                {
                    var [||]w = flag ? a : b;
                    w.Text("hi");
                }
            }
            """ + FluentApi;

        await RunAsync(source, source);
    }

    [TestMethod]
    public async Task NotOffered_WhenFollowUpDoesNotReturnTargetType()
    {
        // `w.ToString()` returns string, not Widget, so it cannot be chained back onto `w`.
        var source = """
            class C
            {
                void M()
                {
                    var w = new Wi[||]dget();
                    w.ToString();
                }
            }
            """ + FluentApi;

        await RunAsync(source, source);
    }

    private static async Task RunAsync(string source, string fixedSource, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var test = new CSharpCodeRefactoringTest<MergeChainStatementsRefactoring, DefaultVerifier>
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
