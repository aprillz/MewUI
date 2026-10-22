#if NET10_0_OR_GREATER
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Markup;

/// <summary>Enforces the fluent extension coverage and quality rules reported by the markup audit tool.</summary>
[TestClass]
public sealed class FluentExtensionAuditTests
{
    // Extensions without a test project; each group lists candidate build outputs relative to the repository root.
    private static readonly string[][] _builtExtensionCandidates =
    [
        ["extensions/MewUI.Skia/bin/Debug/net10.0/Aprillz.MewUI.Skia.dll"],
        [
            "extensions/MewUI.WebView2.Win32/bin/Debug/net10.0/Aprillz.MewUI.WebView2.Win32.dll",
            "extensions/MewUI.WebView2.Win32/bin/Debug/net10.0-windows/Aprillz.MewUI.WebView2.Win32.dll",
        ],
        ["extensions/MewUI.MewCharts/bin/Debug/net10.0/Aprillz.MewUI.MewCharts.dll"],
    ];

    private static FluentAuditScope CoreScope => FluentAuditScope.ForCore(typeof(Element));

    [TestMethod]
    public void Core_HasNoMissingExtensionPairs() => CoreScope.AssertNone("missing pairs", CoreScope.MissingRows());

    [TestMethod]
    public void Core_HasNoHintFindings() => CoreScope.AssertNone("hint-* findings", CoreScope.HintRows());

    [TestMethod]
    public void Core_HasNoMissingSummaries() => CoreScope.AssertNone("missing-summary findings", CoreScope.SummaryRows());

    [TestMethod]
    public void Core_HasNoNamingPrefixFindings() => CoreScope.AssertNone("naming-prefix findings", CoreScope.NamingRows());

    [TestMethod]
    public void Core_HasNoReturnTypeFindings() => CoreScope.AssertNone("return-type findings", CoreScope.ReturnTypeRows());

    [TestMethod]
    public void BuiltExtensions_HaveNoMissingExtensionPairs()
    {
        var scope = BuiltExtensionScope();
        scope.AssertNone("missing pairs", scope.MissingRows());
    }

    [TestMethod]
    public void BuiltExtensions_HaveNoHintFindings()
    {
        var scope = BuiltExtensionScope();
        scope.AssertNone("hint-* findings", scope.HintRows());
    }

    [TestMethod]
    public void BuiltExtensions_HaveNoMissingSummaries()
    {
        var scope = BuiltExtensionScope();
        scope.AssertNone("missing-summary findings", scope.SummaryRows());
    }

    [TestMethod]
    public void BuiltExtensions_HaveNoNamingPrefixFindings()
    {
        var scope = BuiltExtensionScope();
        scope.AssertNone("naming-prefix findings", scope.NamingRows());
    }

    [TestMethod]
    public void BuiltExtensions_HaveNoReturnTypeFindings()
    {
        var scope = BuiltExtensionScope();
        scope.AssertNone("return-type findings", scope.ReturnTypeRows());
    }

    private static FluentAuditScope BuiltExtensionScope()
    {
        var found = FluentAuditScope.FindBuiltExtensions(_builtExtensionCandidates, out var absent);
        if (found.Count == 0)
        {
            Assert.Inconclusive($"No built extension assembly was found; build these first: {string.Join(", ", absent)}");
        }

        return FluentAuditScope.ForBuiltExtensions(typeof(Element), found);
    }
}
#endif
