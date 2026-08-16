using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.MewvalonEdit.Rendering;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// How the editor attaches the generators it owns, and how anything added to a view learns of it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class BuiltinGeneratorTests
{
    [TestMethod]
    public void TheEditorKeepsItsSingleCharacterGeneratorInStepWithTheOptions()
    {
        var editor = new TextEditor { Text = "a b" };
        editor.Options.ShowSpaces = true;
        editor.Options.ShowTabs = false;

        var generator = editor.TextArea.TextView.ElementGenerators
            .OfType<SingleCharacterElementGenerator>()
            .Single();

        Assert.IsTrue(generator.ShowSpaces);
        Assert.IsFalse(generator.ShowTabs);

        editor.Options.ShowTabs = true;

        Assert.IsTrue(generator.ShowTabs, "The option change did not reach the attached generator.");
    }

    /// <summary>
    /// A generator built by hand carries its own settings and is never overwritten by the options.
    /// </summary>
    [TestMethod]
    public void AHandBuiltGeneratorKeepsItsOwnSettings()
    {
        var editor = new TextEditor { Text = "a b" };
        editor.Options.ShowSpaces = false;
        var generator = new SingleCharacterElementGenerator { ShowSpaces = true, ShowTabs = false };

        editor.TextArea.TextView.ElementGenerators.Add(generator);
        editor.Options.ShowTabs = true;

        Assert.IsTrue(generator.ShowSpaces);
        Assert.IsFalse(generator.ShowTabs);
    }

    [TestMethod]
    public void AGeneratorLearnsTheViewItIsAddedToAndRemovedFrom()
    {
        var editor = new TextEditor { Text = "a b" };
        var generator = new FoldingElementGenerator();

        editor.TextArea.TextView.ElementGenerators.Add(generator);
        CollectionAssert.AreEqual(new[] { editor.TextArea.TextView }, generator.TextViews.ToArray());

        editor.TextArea.TextView.ElementGenerators.Remove(generator);
        Assert.IsEmpty(generator.TextViews);
    }
}
