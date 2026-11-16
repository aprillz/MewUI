using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement MultiBindingNameCard()
    {
        var person = new MultiBindingDemoPerson { FirstName = "Ada", LastName = "Lovelace" };

        return Card(
            "Multi-binding (same source)",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Inputs: person.FirstName, person.LastName; Target: TextBlock.Text; Mode: OneWay"),
                    new TextBlock()
                        .Text("One target value is computed from two properties of the same viewmodel. Editing either name recomputes it.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("First name:"),
                    new TextBox()
                        .Width(280)
                        .Bind(TextBox.TextProperty, person, value => value.FirstName),
                    BindingDescription("Last name:"),
                    new TextBox()
                        .Width(280)
                        .Bind(TextBox.TextProperty, person, value => value.LastName),
                    new TextBlock()
                        .Bind(
                            TextBlock.TextProperty,
                            person,
                            static value => value.FirstName,
                            static value => value.LastName,
                            static (first, last) => $"{first} {last}".Trim())
                        .FontSize(ThemeFontSize.Large)),
            minWidth: 380);
    }

    private FrameworkElement MultiBindingSourcesCard()
    {
        var unitPrice = new ObservableValue<double>(12.5);
        var quantity = new Slider()
            .Width(280)
            .Minimum(1)
            .Maximum(10)
            .Value(3);
        var firstProfile = new InpcDemoProfile("A", "Alice");
        var secondProfile = new InpcDemoProfile("B", "Bob");
        var order = new InpcDemoRoot(firstProfile);

        return Card(
            "Multi-binding (different sources)",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    BindingDescription(
                        "Inputs: order.CurrentProfile.DisplayName (nested path), Slider.ValueProperty, ObservableValue<double>; Mode: OneWay"),
                    new TextBlock()
                        .Text("Each input may come from a different object and a different kind of source. Replacing the customer rewires the nested path.")
                        .TextWrapping(TextWrapping.Wrap),
                    BindingDescription("Quantity (Slider):"),
                    quantity,
                    BindingDescription("Unit price (ObservableValue<double>):"),
                    new Slider()
                        .Width(280)
                        .Minimum(1)
                        .Maximum(50)
                        .BindValue(unitPrice),
                    new TextBlock()
                        .Bind(
                            TextBlock.TextProperty,
                            BindingInput.From(order, static value => value.CurrentProfile.DisplayName),
                            BindingInput.From(quantity, RangeBase.ValueProperty),
                            BindingInput.From(unitPrice),
                            static (customer, count, price) =>
                                $"{customer}: {count:0} x {price:0.00} = {count * price:0.00}")
                        .FontSize(ThemeFontSize.Large),
                    new StackPanel()
                        .Horizontal()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content("Customer A")
                                .OnClick(() => order.CurrentProfile = firstProfile),
                            new Button()
                                .Content("Customer B")
                                .OnClick(() => order.CurrentProfile = secondProfile))),
            minWidth: 420);
    }
}

// The generated interceptors name this type, so it cannot be a private member of GalleryView.
internal sealed class MultiBindingDemoPerson : InpcDemoObject
{
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;

    public string FirstName
    {
        get => _firstName;
        set => SetField(ref _firstName, value);
    }

    public string LastName
    {
        get => _lastName;
        set => SetField(ref _lastName, value);
    }
}
