using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class FluentChainFormatTests
{
    // A node with a child list, to exercise nested-tree formatting with real (compilable) types.
    // Paint.Rgb is a static factory (a type-rooted call), used to check it is treated as a value.
    private const string NodeApi = """

        public class Node
        {
            public Node Child(string s) => this;
            public Node Add(params Node[] nodes) => this;
            public Node Color(Paint p) => this;
            public Node On(System.Action<int> handler) => this;
        }

        public class Paint
        {
            public static Paint Rgb(int r, int g, int b) => new Paint();
        }
        """;

    [TestMethod]
    public async Task Expand_BreaksEachCallOntoItsOwnLine()
    {
        var source = """
            class C
            {
                object M() => new System.Text.StringBuilder().Ap[||]pend("a").Append("b");
            }
            """;

        var fixedSource = """
            class C
            {
                object M() => new System.Text.StringBuilder()
                    .Append("a")
                    .Append("b");
            }
            """;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Expand_OffersOnSingleCallChain()
    {
        var source = """
            class C
            {
                object M() => new Node().Ch[||]ild("x");
            }
            """ + NodeApi;

        var fixedSource = """
            class C
            {
                object M() => new Node()
                    .Child("x");
            }
            """ + NodeApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Expand_RealignsMultiLineLambdaBlock()
    {
        var source = """
            class C
            {
                void M(int x)
                {
                    new Node().Ch[||]ild("x").On(e =>
            {
                Run(e);
            });
                }
                void Run(int e) { }
            }
            """ + NodeApi;

        var fixedSource = """
            class C
            {
                void M(int x)
                {
                    new Node()
                        .Child("x")
                        .On(e =>
                        {
                            Run(e);
                        });
                }
                void Run(int e) { }
            }
            """ + NodeApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Collapse_JoinsChainBackToOneLine()
    {
        var source = """
            class C
            {
                object M() => new System.Text.StringBuilder()
                    .Ap[||]pend("a")
                    .Append("b");
            }
            """;

        var fixedSource = """
            class C
            {
                object M() => new System.Text.StringBuilder().Append("a").Append("b");
            }
            """;

        await RunAsync(source, fixedSource, "MewUI.CollapseFluentChain");
    }

    [TestMethod]
    public async Task Expand_ExpandsEachChild_SeparatedByBlankLine()
    {
        // Each element of a child list is expanded as its own tree, with a blank line between siblings.
        var source = """
            class C
            {
                object M() => new Node().Ch[||]ild("root").Add(new Node().Child("a").Child("b"), new Node().Child("c").Child("d"));
            }
            """ + NodeApi;

        var fixedSource = """
            class C
            {
                object M() => new Node()
                    .Child("root")
                    .Add(
                        new Node()
                            .Child("a")
                            .Child("b"),

                        new Node()
                            .Child("c")
                            .Child("d")
                    );
            }
            """ + NodeApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Collapse_FlattensChildList()
    {
        var source = """
            class C
            {
                object M() => new Node()
                    .Ch[||]ild("root")
                    .Add(
                        new Node()
                            .Child("a")
                            .Child("b"),

                        new Node()
                            .Child("c")
                            .Child("d")
                    );
            }
            """ + NodeApi;

        var fixedSource = """
            class C
            {
                object M() => new Node().Child("root").Add(new Node().Child("a").Child("b"), new Node().Child("c").Child("d"));
            }
            """ + NodeApi;

        await RunAsync(source, fixedSource, "MewUI.CollapseFluentChain");
    }

    [TestMethod]
    public async Task Expand_KeepsTypeRootedCallInline()
    {
        // `Paint.Rgb(255, 0, 0)` is a static factory (rooted in a type), so it is a value and must
        // not be split onto its own lines.
        var source = """
            class C
            {
                object M() => new Node().Ch[||]ild("root").Color(Paint.Rgb(255, 0, 0));
            }
            """ + NodeApi;

        var fixedSource = """
            class C
            {
                object M() => new Node()
                    .Child("root")
                    .Color(Paint.Rgb(255, 0, 0));
            }
            """ + NodeApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Expand_ExpandsVariableRootedChild()
    {
        // A chain rooted in a value (a local) is an element and is expanded.
        var source = """
            class C
            {
                object M()
                {
                    var item = new Node();
                    return new Node().Ch[||]ild("root").Add(item.Child("a").Child("b"));
                }
            }
            """ + NodeApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var item = new Node();
                    return new Node()
                        .Child("root")
                        .Add(
                            item
                                .Child("a")
                                .Child("b")
                        );
                }
            }
            """ + NodeApi;

        await RunAsync(source, fixedSource);
    }

    private static async Task RunAsync(string source, string fixedSource, string equivalenceKey = "MewUI.ExpandFluentChain")
    {
        var test = new CSharpCodeRefactoringTest<FluentChainFormatRefactoring, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = equivalenceKey,
        };

        await test.RunAsync();
    }
}
