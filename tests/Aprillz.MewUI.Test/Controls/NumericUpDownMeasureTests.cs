using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// Width stability of NumericUpDown while editing (issue #232): the control measures by its
/// formatted display text, so the in-flight edit text, including clearing it entirely, must
/// not change the desired width.
/// </summary>
[TestClass]
public sealed class NumericUpDownMeasureTests
{
    private static readonly Size PROBE = new(500, 300);

    [TestMethod]
    public void ClearingEditText_KeepsDesiredWidth()
    {
        var numeric = new NumericUpDown { Minimum = 0, Maximum = 100 };
        numeric.Measure(PROBE);
        double before = numeric.DesiredSize.Width;

        numeric.BeginEdit();
        var editor = FindDescendant<TextBox>(numeric);
        Assert.IsNotNull(editor, "the default template must expose the edit TextBox while editing");

        editor.Text = "";
        numeric.Measure(PROBE);

        Assert.AreEqual(before, numeric.DesiredSize.Width, 0.5,
            "clearing the edit text must not change the control's width");
    }

    [TestMethod]
    public void TypedEditText_WidthFollowsCommittedValueOnly()
    {
        var editing = new NumericUpDown { Minimum = 0, Maximum = 100 };
        editing.Measure(PROBE);
        editing.BeginEdit();
        var editor = FindDescendant<TextBox>(editing);
        Assert.IsNotNull(editor);

        editor.Text = "100";
        editing.Measure(PROBE);

        // Typing live-commits the parsed value, so the width matches a non-editing
        // control holding the same value; the edit text adds nothing on top.
        var committed = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 100 };
        committed.Measure(PROBE);

        Assert.AreEqual(committed.DesiredSize.Width, editing.DesiredSize.Width, 0.5,
            "while editing, the width must follow the committed value, not the edit text");
    }

    [TestMethod]
    public void StandaloneEmptyTextBox_KeepsDefaultWidth()
    {
        var textBox = new TextBox();
        textBox.Measure(PROBE);

        Assert.IsGreaterThan(50, textBox.DesiredSize.Width,
            "an empty standalone TextBox keeps its default measure width");
    }

    private static T? FindDescendant<T>(Element root) where T : Element
    {
        T? found = null;
        if (root is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                found ??= child as T ?? FindDescendant<T>(child);
                return found == null;
            });
        }
        return found;
    }
}
