using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class AssignmentToFluentCallTests
{
    private const string BoxApi = """

        public class Box
        {
            public object Content { get; set; }
            public event System.Action Click;
        }

        public static class Shelf
        {
            public static void SetSlot(Box box, int slot) { }
            public static void SetDepth(Box box, int depth) { }
        }

        public static class BoxExtensions
        {
            public static Box Content(this Box box, object value) { box.Content = value; return box; }
            public static Box OnClick(this Box box, System.Action handler) { box.Click += handler; return box; }
            public static Box Slot(this Box box, int slot) { return box; }
        }
        """;

    [TestMethod]
    public async Task ConvertsPropertyAssignmentToFluentCall()
    {
        var source = """
            class C
            {
                void M(Box box)
                {
                    box.Con[||]tent = new object();
                }
            }
            """ + BoxApi;

        var fixedSource = """
            class C
            {
                void M(Box box)
                {
                    box.Content(new object());
                }
            }
            """ + BoxApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task ConvertsEventSubscriptionToFluentCall()
    {
        var source = """
            class C
            {
                void M(Box box)
                {
                    box.Cli[||]ck += Run;
                }
                void Run() { }
            }
            """ + BoxApi;

        var fixedSource = """
            class C
            {
                void M(Box box)
                {
                    box.OnClick(Run);
                }
                void Run() { }
            }
            """ + BoxApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task ConvertsAttachedSetterToFluentCall()
    {
        // `Shelf.SetSlot(box, 2)` sets the property named after the `Set` prefix, so it is the same
        // conversion as an assignment once `Slot` resolves on the element.
        var source = """
            class C
            {
                void M(Box box)
                {
                    Shelf.SetS[||]lot(box, 2);
                }
            }
            """ + BoxApi;

        var fixedSource = """
            class C
            {
                void M(Box box)
                {
                    box.Slot(2);
                }
            }
            """ + BoxApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task NotOffered_WhenAttachedSetterHasNoFluentCounterpart()
    {
        // `Depth` has no extension, so the static setter stays as written.
        var source = """
            class C
            {
                void M(Box box)
                {
                    Shelf.SetD[||]epth(box, 2);
                }
            }
            """ + BoxApi;

        await RunAsync(source, source);
    }

    [TestMethod]
    public async Task NotOffered_WhenStatementIsAlreadyAFluentChain()
    {
        var source = """
            class C
            {
                void M(Box box)
                {
                    box.Con[||]tent(new object());
                }
            }
            """ + BoxApi;

        await RunAsync(source, source);
    }

    [TestMethod]
    public async Task NotOffered_WhenNoFluentSetterExists()
    {
        // `Tag` has no `.Tag(...)` extension, so there is nothing to convert to.
        var source = """
            class C
            {
                void M(Box box)
                {
                    box.Ta[||]g = new object();
                }
            }

            public class Box { public object Tag { get; set; } }
            """;

        await RunAsync(source, source);
    }

    private static async Task RunAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeRefactoringTest<AssignmentToFluentCallRefactoring, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync();
    }
}
