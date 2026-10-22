using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Markup;

/// <summary>
/// Code examples of docs/CSharpMarkup.md and docs/CSharpMarkup.ko.md. Each #region is copied into the guide's
/// matching snippet block by MewUI.MarkupAudit --snippets, so the guide only shows code that compiles.
/// </summary>
internal static class DocSnippets
{
    public static Button BasicPattern()
    {
        #region basic-pattern
        var button = new Button()
            .Content("Click Me")
            .Width(100)
            .OnClick(() => Console.WriteLine("Clicked!"));
        #endregion
        return button;
    }

    public static Border NamingAliases()
    {
        #region naming-aliases
        var border = new Border()
            .BorderThickness(new Thickness(1, 2, 3, 4))
            .CornerRadius(new CornerRadius(4, 8, 12, 16));
        #endregion
        return border;
    }

    public static StackPanel BindingExamples(DocSnippetViewModel viewModel)
    {
        #region binding
        var nameBox = new TextBox()
            .BindText(viewModel.Name);
        var countLabel = new Label()
            .BindText(viewModel.Count, count => $"{count} items");
        var levelSlider = new Slider()
            .BindValue(viewModel.Level, level => (double)level, value => (int)value);
        #endregion
        return new StackPanel().Children(nameBox, countLabel, levelSlider);
    }

    public static StackPanel Shortcuts()
    {
        #region shortcuts
        var title = new Label().Text("Title").Bold();
        var row = new StackPanel().Horizontal();
        var okButton = new Button().Content("OK").Center();
        #endregion
        return row.Children(title, okButton);
    }

    public static TextBox CaptureReference()
    {
        #region ref
        var textBox = new TextBox()
            .Ref(out var nameBox)
            .Text("Hello");
        #endregion
        return ReferenceEquals(textBox, nameBox) ? nameBox : textBox;
    }

    public static TextBox ApplyPattern()
    {
        #region apply
        var textBox = new TextBox()
            .OnTextChanged(text => Console.WriteLine(text))
            .Apply(box => box.MaxLength = 100);
        #endregion
        return textBox;
    }
}

/// <summary>Observable sources used by the binding examples.</summary>
internal sealed class DocSnippetViewModel
{
    public ObservableValue<string> Name { get; } = new(string.Empty);

    public ObservableValue<int> Count { get; } = new();

    public ObservableValue<int> Level { get; } = new();
}
