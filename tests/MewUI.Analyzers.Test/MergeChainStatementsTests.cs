using Aprillz.MewUI.Analyzers;

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

        public static class WidgetExtensions
        {
            public static T Text<T>(this T widget, string value) where T : Widget { widget.Text = value; return widget; }
            public static T Width<T>(this T widget, int value) where T : Widget { widget.Width = value; return widget; }
            public static T OnClick<T>(this T widget, System.Action handler) where T : Widget { widget.Click += handler; return widget; }
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

    private static async Task RunAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeRefactoringTest<MergeChainStatementsRefactoring, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync();
    }
}
