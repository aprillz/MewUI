#if NET10_0_OR_GREATER
using Aprillz.MewUI.Terminal;
using MewUI.Test.Infrastructure;

namespace MewUI.Terminal.Test;

/// <summary>Enforces the fluent extension coverage and quality rules for the Terminal assembly.</summary>
[TestClass]
public sealed class FluentExtensionAuditTests
{
    private static FluentAuditScope Scope => FluentAuditScope.ForExtension(typeof(Aprillz.MewUI.Controls.Element), typeof(TerminalView));

    [TestMethod]
    public void HasNoMissingExtensionPairs() => Scope.AssertNone("missing pairs", Scope.MissingRows());

    [TestMethod]
    public void HasNoHintFindings() => Scope.AssertNone("hint-* findings", Scope.HintRows());

    [TestMethod]
    public void HasNoMissingSummaries() => Scope.AssertNone("missing-summary findings", Scope.SummaryRows());

    [TestMethod]
    public void HasNoNamingPrefixFindings() => Scope.AssertNone("naming-prefix findings", Scope.NamingRows());

    [TestMethod]
    public void HasNoReturnTypeFindings() => Scope.AssertNone("return-type findings", Scope.ReturnTypeRows());
}
#endif
