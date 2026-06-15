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
        }

        public static class BoxExtensions
        {
            public static Box Content(this Box box, object value) { box.Content = value; return box; }
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
