using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class FluentChainFormatTests
{
    // StringBuilder.Append returns StringBuilder, so this is a real fluent chain with no custom types
    // and the refactoring (purely syntactic) needs no semantic setup.

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

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Expand_RecursesIntoChainArguments()
    {
        var source = """
            class C
            {
                object M() => new System.Text.StringBuilder().Ap[||]pend("a").Append(new System.Text.StringBuilder().Append("x").Append("y"));
            }
            """;

        var fixedSource = """
            class C
            {
                object M() => new System.Text.StringBuilder()
                    .Append("a")
                    .Append(
                        new System.Text.StringBuilder()
                            .Append("x")
                            .Append("y")
                    );
            }
            """;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Collapse_FlattensNestedChainArguments()
    {
        var source = """
            class C
            {
                object M() => new System.Text.StringBuilder()
                    .Ap[||]pend("a")
                    .Append(
                        new System.Text.StringBuilder()
                            .Append("x")
                            .Append("y")
                    );
            }
            """;

        var fixedSource = """
            class C
            {
                object M() => new System.Text.StringBuilder().Append("a").Append(new System.Text.StringBuilder().Append("x").Append("y"));
            }
            """;

        await RunAsync(source, fixedSource);
    }

    private static async Task RunAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeRefactoringTest<FluentChainFormatRefactoring, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync();
    }
}
