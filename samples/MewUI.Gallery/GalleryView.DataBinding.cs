using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement DataBindingPage() =>
        CardGrid(
            ObservableValueBindingCard(),
            ConvertedBindingCard(),
            BindingValidationCard(),
            BindingLifetimeCard(),
            MewPropertyBindingCard(),
            InpcBindingCard(),
            BindingPathCard(),
            InpcNestedPathCard(),
            CollectionPathCard()
        );

    private FrameworkElement ObservableValueBindingCard()
    {
        var source = new ObservableValue<string>("Alice");
        var nextValue = 1;

        return Card(
            "TwoWay",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<string>; Target: TextBox.Text; Mode: TwoWay"),
                    new TextBlock()
                        .Text("Typing updates source.Value. Changing source.Value updates the TextBox.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("Target TextBox (edit this):"),
                    new TextBox()
                        .Width(280)
                        .BindText(source),
                    new TextBlock()
                        .BindText(source, static value => $"source.Value = \"{value}\""),
                    new Button()
                        .Content("Change source.Value")
                        .OnClick(() => source.Value = $"Source value {nextValue++}")),
            minWidth: 380);
    }

    private FrameworkElement ConvertedBindingCard()
    {
        var source = new ObservableValue<double>(42);

        return Card(
            "Conversion",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<double>; Slider: TwoWay; Progress/Text: OneWay"),
                    new TextBlock()
                        .Text("The Slider writes the number back. The other targets only render it, and the TextBlock uses a converter.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("TwoWay target (Slider):"),
                    new Slider()
                        .Width(280)
                        .Minimum(0)
                        .Maximum(100)
                        .BindValue(source),
                    BindingDescription("OneWay target (ProgressBar):"),
                    new ProgressBar()
                        .Width(280)
                        .Minimum(0)
                        .Maximum(100)
                        .BindValue(source),
                    new TextBlock()
                        .BindText(source, static value => $"Converted text: {value:0.0}%")),
            minWidth: 380);
    }

    private FrameworkElement BindingValidationCard()
    {
        static int ParseWholeNumber(string text) =>
            int.TryParse(text, out var value)
                ? value
                : throw new FormatException("Enter a whole number.");

        var source = new ObservableValue<int>(42);
        var nextValidValue = 43;
        var target = new TextBox()
            .Width(280)
            .BindText(
                source,
                static value => value.ToString(),
                ParseWholeNumber);
        var status = new TextBlock()
            .Bind(
                TextBlock.TextProperty,
                target,
                Control.ValidationErrorsProperty,
                static errors => errors.Count == 0
                    ? "Valid: no binding errors"
                    : $"Invalid: {errors[0].Message}",
                mode: BindingMode.OneWay)
            .TextWrapping(TextWrapping.Wrap);

        return Card(
            "Validation",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<int>; Target: TextBox.Text; Mode: TwoWay"),
                    new TextBlock()
                        .Text("Type a non-numeric value. ConvertBack fails, the source stays unchanged, and the TextBox uses the Error border until the binding recovers.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("TwoWay target (try letters):"),
                    target,
                    new TextBlock()
                        .BindText(source, static value => $"source.Value = {value}"),
                    status,
                    new Button()
                        .Content("Restore from source")
                        .HorizontalAlignment(HorizontalAlignment.Left)
                        .OnClick(() => source.Value = nextValidValue++)),
            minWidth: 420);
    }

    private FrameworkElement BindingLifetimeCard()
    {
        var source = new ObservableValue<string>("Bound value 1");
        var state = new ObservableValue<string>("Binding is active");
        var target = new TextBlock();
        var version = 1;

        void BindTarget()
        {
            target.SetBinding(TextBlock.TextProperty, source, BindingMode.OneWay);
            state.Value = "Binding is active";
        }

        BindTarget();

        return Card(
            "Lifetime (ClearBinding)",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Source: ObservableValue<string>; Target: TextBlock.Text; Mode: OneWay"),
                    new TextBlock()
                        .Text("ClearBinding detaches the source and preserves the target's current value. Bind again to resynchronize it.")
                        .TextWrapping(TextWrapping.Wrap),
                    new TextBlock()
                        .BindText(source, static value => $"Source: {value}"),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(4)
                        .Children(
                            new TextBlock().Text("Target:"),
                            target),
                    new TextBlock()
                        .BindText(state, static value => $"State: {value}"),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Change Source")
                                .OnClick(() => source.Value = $"Bound value {++version}"),
                            new Button()
                                .Content("Clear Binding")
                                .OnClick(() =>
                                {
                                    target.ClearBinding(TextBlock.TextProperty);
                                    state.Value = "Binding cleared; target value preserved";
                                }),
                            new Button()
                                .Content("Bind Again")
                                .OnClick(BindTarget))),
            minWidth: 440);
    }

    private static TextBlock BindingDescription(string text) =>
        new TextBlock()
            .Text(text)
            .FontSize(ThemeFontSize.Small)
            .TextWrapping(TextWrapping.Wrap);

}

