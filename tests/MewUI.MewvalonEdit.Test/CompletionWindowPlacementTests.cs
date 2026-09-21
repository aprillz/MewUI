using System.ComponentModel;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.CodeCompletion;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The completion window rides a popup owned by the editor, so its placement is bounded by the
/// window rather than by the editor: a caret on the last visible line still opens a full list
/// downward. The assertions read the placement decision, which is what the popup surface then
/// positions itself from.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CompletionWindowPlacementTests
{
    private const double EDITOR_HEIGHT = 150;

    private static readonly string[] WORDS =
        ["DateTime", "DateTimeKind", "Debug", "Decimal", "Delegate", "Dictionary", "Directory"];

    [TestMethod]
    public void CaretOnTheLastLineOpensPastTheEditorBottom()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, window) = CreateEditorInWindow(string.Join('\n', Enumerable.Repeat("line", 40)));
        editor.CaretOffset = editor.Document.TextLength;
        editor.TextArea.Caret.BringCaretToView();
        window.PerformLayout();

        var completion = OpenWindow(editor);

        Assert.IsTrue(completion.IsOpen);
        Assert.IsGreaterThan(0, completion.PlacedBounds.Height, "the popup reported no placement");
        Assert.IsGreaterThan(editor.Bounds.Bottom, completion.PlacedBounds.Bottom,
            $"the list stopped at the editor bottom (editor={editor.Bounds}, popup={completion.PlacedBounds})");
        completion.Close();
    }

    [TestMethod]
    public void InsightWindowAlsoOpensPastTheEditorBottom()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, window) = CreateEditorInWindow(string.Join('\n', Enumerable.Repeat("Method(", 40)));
        editor.CaretOffset = editor.Document.TextLength;
        editor.TextArea.Caret.BringCaretToView();
        window.PerformLayout();

        var insight = new OverloadInsightWindow(editor.TextArea)
        {
            Provider = new SingleOverloadProvider()
        };
        insight.Show();

        Assert.IsGreaterThan(0, insight.PlacedBounds.Height, "the popup reported no placement");
        Assert.IsGreaterThan(editor.Bounds.Bottom, insight.PlacedBounds.Bottom,
            $"the insight window stopped at the editor bottom (editor={editor.Bounds}, popup={insight.PlacedBounds})");
        insight.Close();
    }

    [TestMethod]
    public void TheWindowIsAsTallAsItsRowsAndShrinksWhenFilteringDoes()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        var completion = OpenWindow(editor);

        double whole = completion.PlacedBounds.Height;
        Assert.IsGreaterThan(WORDS.Length * 10.0, whole,
            $"the window collapsed to a fraction of a row: {completion.PlacedBounds}");

        editor.TextArea.PerformTextInput("De");

        Assert.IsLessThan(WORDS.Length, completion.CompletionList.VisibleItems.Count,
            "the query did not narrow the list");
        Assert.IsLessThan(whole, completion.PlacedBounds.Height,
            "the window kept the height of the unfiltered list");
        completion.Close();
    }

    [TestMethod]
    public void TheDescriptionOpensBesideTheListAndFollowsTheSelection()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        var completion = new CompletionWindow(editor.TextArea);
        for (int index = 0; index < 5; index++)
        {
            completion.CompletionList.CompletionData.Add(new CompletionData($"Described{index}", $"what {index} does"));
        }
        completion.CompletionList.CompletionData.Add(new CompletionData("Bare"));
        completion.Show();

        // A row well down the list, so lining up with it is not the same as lining up with the top.
        completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData[3];
        Assert.IsGreaterThan(completion.PlacedBounds.Y, completion.DescriptionBounds.Y,
            "the description sat at the top of the list rather than beside the selected row");
        Assert.IsTrue(completion.DescriptionPopup.IsOpen, "the described item showed no description");
        Assert.IsGreaterThan(0, completion.DescriptionBounds.Width, "the description panel measured to nothing");
        Assert.AreEqual(completion.PlacedBounds.Right + 4, completion.DescriptionBounds.X, 1.0,
            "the description did not open beside the list with its gap");
        Assert.AreEqual(completion.CompletionList.GetSelectedRowBounds().Y, completion.DescriptionBounds.Y, 1.0,
            "the description did not line up with the selected row");

        completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData[^1];
        Assert.IsFalse(completion.DescriptionPopup.IsOpen, "an item without a description kept the panel up");

        completion.Close();
    }

    [TestMethod]
    public void ADescriptionTemplateBuildsOnceAndRebindsAsTheSelectionMoves()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        int builds = 0;
        var completion = new CompletionWindow(editor.TextArea)
        {
            DescriptionTemplate = new DelegateTemplate<ICompletionData>(
                build: _ => { builds++; return new TextBlock(); },
                bind: static (view, item, _, _) => ((TextBlock)view).Text = $"{item.Text}: {item.Description}")
        };
        completion.CompletionList.CompletionData.Add(new CompletionData("First", "one"));
        completion.CompletionList.CompletionData.Add(new CompletionData("Second", "two"));
        completion.Show();

        completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData[0];
        var view = ((Border)completion.DescriptionPopup.Content!).Child;
        Assert.AreEqual("First: one", ((TextBlock)view!).Text, "the template did not draw the entry");

        completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData[1];

        Assert.AreEqual(1, builds, "the template rebuilt its view instead of rebinding it");
        Assert.AreSame(view, ((Border)completion.DescriptionPopup.Content!).Child);
        Assert.AreEqual("Second: two", ((TextBlock)view).Text, "the view kept the entry it was bound to before");
        completion.Close();
    }

    [TestMethod]
    public void ADescriptionThatIsNeitherTextNorElementShowsByItsText()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        var completion = new CompletionWindow(editor.TextArea);
        completion.CompletionList.CompletionData.Add(new CompletionData("Boxed", new Version(2, 5)));
        completion.Show();

        completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData[0];

        Assert.IsTrue(completion.DescriptionPopup.IsOpen, "the description was dropped for having no template");
        Assert.AreEqual("2.5", ((TextBlock)((Border)completion.DescriptionPopup.Content!).Child!).Text);
        completion.Close();
    }

    [TestMethod]
    public void AnEmptyListShowsWhatEmptyTemplateBuilds()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, window) = CreateEditorInWindow("");
        var completion = new CompletionWindow(editor.TextArea);
        completion.CompletionList.EmptyTemplate = new DelegateControlTemplate<ContentControl>(
            static (_, _) => new TextBlock { Text = "No suggestions" });
        completion.CompletionList.CompletionData.Add(new CompletionData("Described"));
        completion.Show();
        window.PerformLayout();

        double withRows = completion.PlacedBounds.Height;

        // A query nothing matches empties the list.
        editor.TextArea.PerformTextInput("zzz");
        window.PerformLayout();

        Assert.IsEmpty(completion.CompletionList.VisibleItems, "the query still matched something");
        Assert.AreNotEqual(withRows, completion.PlacedBounds.Height,
            "the window kept the height of the rows it no longer shows");
        completion.Close();
    }

    [TestMethod]
    public void ALongListStopsAtTheWindowCap()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        var completion = new CompletionWindow(editor.TextArea);
        for (int index = 0; index < 60; index++)
        {
            completion.CompletionList.CompletionData.Add(new CompletionData($"Item{index:D3}"));
        }
        completion.Show();

        Assert.AreEqual(completion.Root.MaxHeight, completion.PlacedBounds.Height,
            "the rows grew past the window, which caps its height and scrolls instead");
        completion.Close();
    }

    [TestMethod]
    public void TypingKeepsTheWindowOpen()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, _) = CreateEditorInWindow("");
        var completion = OpenWindow(editor);

        editor.TextArea.PerformTextInput("De");

        Assert.IsTrue(completion.IsOpen, "typing closed the completion window");
        completion.Close();
    }

    [TestMethod]
    public void ScrollingMovesTheWindowWithTheAnchor()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (editor, window) = CreateEditorInWindow(string.Join('\n', Enumerable.Repeat("line", 200)));
        editor.CaretOffset = editor.Document.GetLineByNumber(20).Offset;
        editor.TextArea.Caret.BringCaretToView();
        window.PerformLayout();

        var completion = OpenWindow(editor);
        double before = completion.PlacedBounds.Y;

        // One line, so the anchor stays in the viewport: leaving it is what closes the window.
        editor.LineDown();
        window.PerformLayout();

        Assert.IsTrue(completion.IsOpen, "scrolling closed the completion window");
        Assert.AreNotEqual(before, completion.PlacedBounds.Y, "the window did not follow its anchor");
        completion.Close();
    }

    private sealed class SingleOverloadProvider : IOverloadProvider
    {
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public int SelectedIndex { get; set; }
        public int Count => 1;
        public string CurrentIndexText => "1 of 1";
        public object? CurrentHeader => "Method(int value)";
        public object? CurrentContent => "the only overload";
    }

    private static (TextEditor editor, Window window) CreateEditorInWindow(string text)
    {
        var window = ScaledWindow.Create(1.0);
        var editor = new TextEditor
        {
            Text = text,
            SkipViewportCull = true,
            FontFamily = "Consolas",
            FontSize = 13,
            Height = EDITOR_HEIGHT,
            VerticalAlignment = VerticalAlignment.Top
        };
        window.Content = editor;
        window.PerformLayout();
        return (editor, window);
    }

    private static CompletionWindow OpenWindow(TextEditor editor)
    {
        var completion = new CompletionWindow(editor.TextArea);
        foreach (string word in WORDS)
        {
            completion.CompletionList.CompletionData.Add(new CompletionData(word));
        }
        completion.Show();
        return completion;
    }
}
