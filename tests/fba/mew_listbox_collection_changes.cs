#:sdk Microsoft.NET.Sdk

#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property AllowUnsafeBlocks=true

#:project ../../src/MewUI/MewUI.csproj
#:project ../../src/MewUI.Platform.Win32/MewUI.Platform.Win32.csproj
#:project ../../src/MewUI.Backend.Gdi/MewUI.Backend.Gdi.csproj

using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

Win32Platform.Register();
GdiBackend.Register();

var window = new Window()
    .Title("ListBox collection change repro (#218, #219)")
    .Resizable(720, 700)
    .Content(
        new StackPanel()
            .Vertical()
            .Padding(20)
            .Spacing(16)
            .Children(
                new TextBlock()
                    .Text("Compare the rendered rows with the source values shown below each ListBox."),
                BuildDirectItemsCase(),
                BuildItemsSourceCase("List<int> ItemsSource (no collection notifications)", new List<int> { 1, 2, 3, 4, 5 }),
                BuildItemsSourceCase(
                    "ObservableCollection<int> (collection notifications)",
                    new ObservableCollection<int> { 1, 2, 3, 4, 5 })
            ));

Application.Run(window);

static FrameworkElement BuildDirectItemsCase()
{
    var items = new List<string> { "1", "2", "3", "4", "5" };
    ListBox listBox = null!;
    TextBlock sourceText = null!;

    void ApplyItems()
    {
        listBox.Items(items.ToArray());
        sourceText.Text = $"Source ({items.Count}): [{string.Join(", ", items)}]";
    }

    void Reset()
    {
        items.Clear();
        items.AddRange(["1", "2", "3", "4", "5"]);
        ApplyItems();
    }

    void RemoveFirst()
    {
        if (items.Count > 0)
        {
            items.RemoveAt(0);
        }

        ApplyItems();
    }

    void Clear()
    {
        items.Clear();
        ApplyItems();
    }

    var panel = BuildPanel(
        "ListBox.Items(...) direct replacement",
        new ListBox().Ref(out listBox),
        new TextBlock().Ref(out sourceText),
        Reset,
        RemoveFirst,
        Clear,
        invalidate: null);

    ApplyItems();
    return panel;
}

static FrameworkElement BuildItemsSourceCase<TCollection>(string title, TCollection items)
    where TCollection : IList<int>, IReadOnlyList<int>
{
    var view = ItemsView.Create(items, value => value.ToString());
    TextBlock sourceText = null!;
    int Count() => ((ICollection<int>)items).Count;

    void UpdateSourceText() =>
        sourceText.Text = $"Source ({Count()}): [{string.Join(", ", items)}]";

    void Reset()
    {
        items.Clear();
        for (var value = 1; value <= 5; value++)
        {
            items.Add(value);
        }

        // List<T> does not raise collection notifications. Force a known visual baseline so
        // RemoveAt(0) and Clear can be reproduced repeatedly without masking either operation.
        view.Invalidate();
        UpdateSourceText();
    }

    void RemoveFirst()
    {
        if (Count() > 0)
        {
            items.RemoveAt(0);
        }

        UpdateSourceText();
    }

    void Clear()
    {
        items.Clear();
        UpdateSourceText();
    }

    var panel = BuildPanel(
        title,
        new ListBox().ItemsSource(view),
        new TextBlock().Ref(out sourceText),
        Reset,
        RemoveFirst,
        Clear,
        view.Invalidate);

    UpdateSourceText();
    return panel;
}

static FrameworkElement BuildPanel(
    string title,
    ListBox listBox,
    TextBlock sourceText,
    Action reset,
    Action removeFirst,
    Action clear,
    Action? invalidate)
{
    var buttons = new List<Element>
    {
        new Button().Content("Reset").OnClick(reset),
        new Button().Content("RemoveAt(0)").OnClick(removeFirst),
        new Button().Content("Clear").OnClick(clear)
    };

    if (invalidate != null)
    {
        buttons.Add(new Button().Content("Invalidate").OnClick(invalidate));
    }

    return new StackPanel()
        .Vertical()
        .Spacing(8)
        .Children(
            new TextBlock()
                .Text(title)
                .Bold(),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(buttons.ToArray()),
            listBox.Height(140),
            sourceText
        );
}
