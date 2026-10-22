using Aprillz.MewUI.Analyzers;

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace MewUI.Analyzers.Test;

[TestClass]
public sealed class InitializerToFluentTests
{
    // A self-contained fluent API so resolution is exercised without depending on MewUI.
    // Tag has no extension setter, so it is the "not convertible" case.
    private const string FluentApi = """

        public class Widget
        {
            public string Text { get; set; }
            public int Width { get; set; }
            public object Tag { get; set; }
            public System.Action Click { get; set; }
            public object Content { get; set; }
        }

        public static class WidgetExtensions
        {
            public static T Text<T>(this T widget, string value) where T : Widget { widget.Text = value; return widget; }
            public static T Width<T>(this T widget, int value) where T : Widget { widget.Width = value; return widget; }
            public static T OnClick<T>(this T widget, System.Action handler) where T : Widget { widget.Click += handler; return widget; }
            public static T Content<T>(this T widget, object value) where T : Widget { widget.Content = value; return widget; }
        }

        public static class Factory
        {
            public static object Build() => null;
        }
        """;

    [TestMethod]
    public async Task ConvertsAndExpands_WhenEverySetterResolves()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { Text = "hi", Width = 5 };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .Text("hi")
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task DoesNotStrandParens_OnMultiLineInitializer()
    {
        // Repro: the type's trailing newline must not push the `()` onto the next line.
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|}
                    {
                        Text = "hi",
                        Width = 5
                    };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .Text("hi")
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task KeepsResidualInitializer_WhenSomeMembersDoNotResolve()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { Text = "hi", Tag = null, Width = 5 };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget() { Tag = null }
                        .Text("hi")
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MapsDelegateProperty_ToOnPrefixedSetter()
    {
        // `Click` (a delegate property) has no `.Click(...)` setter, so it maps to `.OnClick(...)`.
        var source = """
            class C
            {
                object M()
                {
                    System.Action handler = () => { };
                    var w = new {|MEW1101:Widget|} { Click = handler, Width = 5 };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    System.Action handler = () => { };
                    var w = new Widget()
                        .OnClick(handler)
                        .Width(5);
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task ConvertsValueThatIsItselfACall_WithoutThrowing()
    {
        // Repro: a converted member whose value is a call chain (`Factory.Build()`) must not crash the
        // fix. `Factory` is a type, so the argument stays inline.
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { Width = 5, Content = Factory.Build() };
                    return w;
                }
            }
            """ + FluentApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .Width(5)
                        .Content(Factory.Build());
                    return w;
                }
            }
            """ + FluentApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task NoDiagnostic_WhenNoMemberResolves()
    {
        var source = """
            class C { object M() => new Widget { Tag = null }; }
            """ + FluentApi;

        // No markup and identical fixed code: the analyzer must report nothing.
        await RunAsync(source, source);
    }

    // A stub of the hint contract: the attributes live in `Aprillz.MewUI` so the analyzer finds them by
    // metadata name, and the extensions mirror the real composite / shorthand / alias shapes.
    // `Command` (the composite) is constrained to `Button`; `SegmentButton` has its own one-argument
    // `Command`, so receiver lookup decides which applies.
    private const string HintApi = """

        public enum HorizontalAlignment { Left, Center, Right, Stretch }
        public enum VerticalAlignment { Top, Center, Bottom, Stretch }
        public enum CommandPresentationMode { None, TextAndIcon }
        public interface ICommand { }
        public class Command : ICommand { }
        public struct Thickness { }
        public class Brush { }

        public class Widget
        {
            public int Width { get; set; }
            public object Tag { get; set; }
            public HorizontalAlignment HorizontalAlignment { get; set; }
            public VerticalAlignment VerticalAlignment { get; set; }
            public Thickness NonUniformBorderThickness { get; set; }
        }

        public class Button : Widget
        {
            public ICommand Command { get; set; }
            public CommandPresentationMode CommandPresentationMode { get; set; }
        }

        public class SplitButton : Button { }

        public class SegmentButton : Widget
        {
            public ICommand Command { get; set; }
            public CommandPresentationMode CommandPresentationMode { get; set; }
        }

        public class Shape : Widget
        {
            public Brush Stroke { get; set; }
            public double StrokeThickness { get; set; }
        }

        public struct Rect { }
        public enum ImageViewBoxUnits { Pixels, RelativeToBoundingBox }

        public class Image : Widget
        {
            public Rect? ViewBox { get; set; }
            public ImageViewBoxUnits ViewBoxUnits { get; set; }
        }

        public static class WidgetExtensions
        {
            public static T Width<T>(this T widget, int value) where T : Widget { widget.Width = value; return widget; }
            public static T HorizontalAlignment<T>(this T widget, HorizontalAlignment value) where T : Widget { widget.HorizontalAlignment = value; return widget; }
            public static T VerticalAlignment<T>(this T widget, VerticalAlignment value) where T : Widget { widget.VerticalAlignment = value; return widget; }

            [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.HorizontalAlignment), global::HorizontalAlignment.Center)]
            public static T CenterHorizontal<T>(this T widget) where T : Widget { widget.HorizontalAlignment = global::HorizontalAlignment.Center; return widget; }

            [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), global::VerticalAlignment.Center)]
            public static T CenterVertical<T>(this T widget) where T : Widget { widget.VerticalAlignment = global::VerticalAlignment.Center; return widget; }

            [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.HorizontalAlignment), global::HorizontalAlignment.Center, order: 0)]
            [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), global::VerticalAlignment.Center, order: 1)]
            public static T Center<T>(this T widget) where T : Widget { widget.HorizontalAlignment = global::HorizontalAlignment.Center; widget.VerticalAlignment = global::VerticalAlignment.Center; return widget; }

            public static T BorderThickness<T>(this T widget, [Aprillz.MewUI.FluentSetsMember(nameof(Widget.NonUniformBorderThickness))] Thickness value) where T : Widget { widget.NonUniformBorderThickness = value; return widget; }

            public static T Command<T>(
                this T button,
                [Aprillz.MewUI.FluentSetsMember(nameof(Button.Command))] Command command,
                [Aprillz.MewUI.FluentSetsMember(nameof(Button.CommandPresentationMode))] CommandPresentationMode presentation = global::CommandPresentationMode.None)
                where T : Button { button.Command = command; button.CommandPresentationMode = presentation; return button; }

            public static SegmentButton Command(this SegmentButton button, ICommand command) { button.Command = command; return button; }
            public static T CommandPresentationMode<T>(this T button, CommandPresentationMode value) where T : SegmentButton { button.CommandPresentationMode = value; return button; }

            public static T Stroke<T>(this T shape, [Aprillz.MewUI.FluentSetsMember(nameof(Shape.Stroke))] Brush brush, double thickness) where T : Shape { shape.Stroke = brush; shape.StrokeThickness = thickness; return shape; }
            public static T StrokeThickness<T>(this T shape, double value) where T : Shape { shape.StrokeThickness = value; return shape; }

            public static Image ViewBox(
                this Image image,
                [Aprillz.MewUI.FluentSetsMember(nameof(Image.ViewBox))] Rect? viewBox,
                [Aprillz.MewUI.FluentSetsMember(nameof(Image.ViewBoxUnits))] ImageViewBoxUnits units = ImageViewBoxUnits.Pixels)
            { image.ViewBox = viewBox; image.ViewBoxUnits = units; return image; }

            [Aprillz.MewUI.FluentSetsConstant(nameof(Image.ViewBoxUnits), ImageViewBoxUnits.Pixels)]
            public static Image ViewBoxPixels(this Image image, [Aprillz.MewUI.FluentSetsMember(nameof(Image.ViewBox))] Rect? viewBox)
            { image.ViewBox = viewBox; image.ViewBoxUnits = ImageViewBoxUnits.Pixels; return image; }
        }

        namespace Aprillz.MewUI
        {
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            internal sealed class FluentSetsMemberAttribute : System.Attribute
            {
                public FluentSetsMemberAttribute(string memberName) { MemberName = memberName; }
                public string MemberName { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
            internal sealed class FluentSetsConstantAttribute : System.Attribute
            {
                public FluentSetsConstantAttribute(string memberName, object value, int order = 0) { MemberName = memberName; Value = value; Order = order; }
                public FluentSetsConstantAttribute(string memberName) { MemberName = memberName; }
                public string MemberName { get; }
                public object Value { get; }
                public int Order { get; }
            }
        }
        """;

    [TestMethod]
    public async Task Composite_ConsumesBothMembers_InContractOrder()
    {
        var source = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new {|MEW1101:SplitButton|} { Command = commandSave, CommandPresentationMode = CommandPresentationMode.TextAndIcon };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new SplitButton().Command(commandSave, CommandPresentationMode.TextAndIcon);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_ExpandsWithOtherSetters_OnMultiLineInitializer()
    {
        var source = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new {|MEW1101:SplitButton|}
                    {
                        Command = commandSave,
                        CommandPresentationMode = CommandPresentationMode.TextAndIcon,
                        Width = 5,
                    };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new SplitButton()
                        .Command(commandSave, CommandPresentationMode.TextAndIcon)
                        .Width(5);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_NotUsed_WhenOnlyFirstMemberIsAssigned()
    {
        // `Command` alone must not become `.Command(command)`: the omitted optional would overwrite
        // `CommandPresentationMode` with its default.
        var source = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new {|MEW1101:SplitButton|} { Command = commandSave, Width = 5 };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new SplitButton() { Command = commandSave }.Width(5);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_NotUsed_WhenOnlySecondMemberIsAssigned()
    {
        var source = """
            class C
            {
                object M()
                {
                    var b = new {|MEW1101:SplitButton|} { CommandPresentationMode = CommandPresentationMode.TextAndIcon, Width = 5 };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var b = new SplitButton() { CommandPresentationMode = CommandPresentationMode.TextAndIcon }.Width(5);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_NotUsed_WhenMembersAreReversed()
    {
        var source = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new {|MEW1101:SplitButton|} { CommandPresentationMode = CommandPresentationMode.TextAndIcon, Command = commandSave, Width = 5 };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new SplitButton() { CommandPresentationMode = CommandPresentationMode.TextAndIcon, Command = commandSave }.Width(5);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_NotUsed_WhenMembersAreNotContiguous()
    {
        // `Tag` (no setter) sits between the pair, so both stay behind; it also keeps them apart in the
        // residual initializer, so the result does not trigger MEW1101 again.
        var source = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new {|MEW1101:SplitButton|} { Command = commandSave, Tag = null, CommandPresentationMode = CommandPresentationMode.TextAndIcon, Width = 5 };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new SplitButton() { Command = commandSave, Tag = null, CommandPresentationMode = CommandPresentationMode.TextAndIcon }.Width(5);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_NotUsed_WhenValueDoesNotConvertToParameter()
    {
        // The property accepts any ICommand but the composite parameter is the `Command` class.
        var source = """
            class C
            {
                object M(ICommand anyCommand)
                {
                    var b = new {|MEW1101:SplitButton|} { Command = anyCommand, CommandPresentationMode = CommandPresentationMode.TextAndIcon, Width = 5 };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(ICommand anyCommand)
                {
                    var b = new SplitButton() { Command = anyCommand, CommandPresentationMode = CommandPresentationMode.TextAndIcon }.Width(5);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_NotUsed_WhenARequiredParameterIsUnmapped()
    {
        // `Stroke(brush, thickness)` leaves `thickness` unannotated, so it is not a candidate at all.
        var source = """
            class C
            {
                object M(Brush brush)
                {
                    var s = new {|MEW1101:Shape|} { Stroke = brush, StrokeThickness = 2, Width = 5 };
                    return s;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Brush brush)
                {
                    var s = new Shape() { Stroke = brush }
                        .StrokeThickness(2)
                        .Width(5);
                    return s;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_SelectsPerReceiver_DedicatedSingleArgOnUnrelatedReceiver()
    {
        // `SegmentButton` is not a `Button`, so the composite does not apply and its own one-argument
        // `Command` plus the same-name `CommandPresentationMode` setter are used instead.
        var source = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new {|MEW1101:SegmentButton|} { Command = commandSave, CommandPresentationMode = CommandPresentationMode.TextAndIcon };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new SegmentButton()
                        .Command(commandSave)
                        .CommandPresentationMode(CommandPresentationMode.TextAndIcon);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Alias_MapsMemberToDifferentlyNamedSetter()
    {
        var source = """
            class C
            {
                object M(Thickness thickness)
                {
                    var w = new {|MEW1101:Widget|} { NonUniformBorderThickness = thickness, Width = 5 };
                    return w;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Thickness thickness)
                {
                    var w = new Widget()
                        .BorderThickness(thickness)
                        .Width(5);
                    return w;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Shorthand_ReplacesSingleConstantAssignment()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { VerticalAlignment = VerticalAlignment.Center, Width = 5 };
                    return w;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .CenterVertical()
                        .Width(5);
                    return w;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Shorthand_MergesTwoContiguousConstants_InContractOrder()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    return w;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget().Center();
                    return w;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Shorthand_DoesNotMerge_WhenConstantsAreReversed()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    return w;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .CenterVertical()
                        .CenterHorizontal();
                    return w;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Shorthand_DoesNotMerge_WhenAnotherAssignmentSitsBetween()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { HorizontalAlignment = HorizontalAlignment.Center, Width = 5, VerticalAlignment = VerticalAlignment.Center };
                    return w;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .CenterHorizontal()
                        .Width(5)
                        .CenterVertical();
                    return w;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Shorthand_FallsBackToSameNameSetter_ForOtherConstant()
    {
        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { VerticalAlignment = VerticalAlignment.Top, Width = 5 };
                    return w;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .VerticalAlignment(VerticalAlignment.Top)
                        .Width(5);
                    return w;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Shorthand_DoesNotMatch_SameUnderlyingValueOfDifferentEnum()
    {
        // Declared with the wrong enum type (same number): it must neither match nor tie with
        // `CenterHorizontal`.
        var wrongShorthand = """

            public static class WrongShorthandExtensions
            {
                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.HorizontalAlignment), VerticalAlignment.Center)]
                public static T WrongCenterHorizontal<T>(this T widget) where T : Widget { return widget; }
            }
            """;

        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { HorizontalAlignment = HorizontalAlignment.Center, Width = 5 };
                    return w;
                }
            }
            """ + HintApi + wrongShorthand;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .CenterHorizontal()
                        .Width(5);
                    return w;
                }
            }
            """ + HintApi + wrongShorthand;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Shorthand_DoesNotMatch_NonConstantExpression()
    {
        var source = """
            class C
            {
                object M(VerticalAlignment alignment)
                {
                    var w = new {|MEW1101:Widget|} { VerticalAlignment = alignment, Width = 5 };
                    return w;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(VerticalAlignment alignment)
                {
                    var w = new Widget()
                        .VerticalAlignment(alignment)
                        .Width(5);
                    return w;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task MalformedHints_AreIgnored_WithoutCrash()
    {
        // Wrong constructor shape, duplicate / non-contiguous order, duplicate member, empty member
        // name, unknown member: none of these may become a candidate or disturb the valid ones.
        var malformed = """

            public static class MalformedExtensions
            {
                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment))]
                public static T BadShape<T>(this T widget) where T : Widget { return widget; }

                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.HorizontalAlignment), HorizontalAlignment.Center, order: 1)]
                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), VerticalAlignment.Center, order: 1)]
                public static T DuplicateOrder<T>(this T widget) where T : Widget { return widget; }

                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), VerticalAlignment.Center, order: 1)]
                public static T OrderNotFromZero<T>(this T widget) where T : Widget { return widget; }

                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), VerticalAlignment.Center, order: 0)]
                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), VerticalAlignment.Center, order: 1)]
                public static T DuplicateMember<T>(this T widget) where T : Widget { return widget; }

                [Aprillz.MewUI.FluentSetsConstant("NoSuchMember", VerticalAlignment.Center)]
                public static T UnknownMember<T>(this T widget) where T : Widget { return widget; }

                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), VerticalAlignment.Center)]
                public static T ConstantWithParameter<T>(this T widget, int value) where T : Widget { return widget; }

                public static T EmptyName<T>(this T widget, [Aprillz.MewUI.FluentSetsMember("")] int value) where T : Widget { return widget; }
            }
            """;

        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { VerticalAlignment = VerticalAlignment.Center, Width = 5 };
                    return w;
                }
            }
            """ + HintApi + malformed;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .CenterVertical()
                        .Width(5);
                    return w;
                }
            }
            """ + HintApi + malformed;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task AmbiguousShorthands_LeaveMemberInInitializer()
    {
        // Two differently named shorthands declare the same constant; overload resolution cannot pick
        // one, so the member stays residual (no fallback to the same-name setter).
        var ambiguous = """

            public static class AmbiguousExtensions
            {
                [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), VerticalAlignment.Center)]
                public static T MiddleVertical<T>(this T widget) where T : Widget { return widget; }
            }
            """;

        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { VerticalAlignment = VerticalAlignment.Center, Width = 5 };
                    return w;
                }
            }
            """ + HintApi + ambiguous;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget() { VerticalAlignment = VerticalAlignment.Center }.Width(5);
                    return w;
                }
            }
            """ + HintApi + ambiguous;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task NonImportedShorthand_IsNotACandidate()
    {
        var elsewhere = """

            namespace Elsewhere
            {
                public static class TopExtensions
                {
                    [Aprillz.MewUI.FluentSetsConstant(nameof(Widget.VerticalAlignment), VerticalAlignment.Top)]
                    public static T TopVertical<T>(this T widget) where T : Widget { return widget; }
                }
            }
            """;

        var source = """
            class C
            {
                object M()
                {
                    var w = new {|MEW1101:Widget|} { VerticalAlignment = VerticalAlignment.Top, Width = 5 };
                    return w;
                }
            }
            """ + HintApi + elsewhere;

        var fixedSource = """
            class C
            {
                object M()
                {
                    var w = new Widget()
                        .VerticalAlignment(VerticalAlignment.Top)
                        .Width(5);
                    return w;
                }
            }
            """ + HintApi + elsewhere;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Combined_PrefersShorthandOverComposite_WhenConstantMatches()
    {
        // Both `ViewBox(r, units)` and `ViewBoxPixels(r)` consume the two members; the combined
        // shorthand passes fewer arguments and wins.
        var source = """
            class C
            {
                object M(Rect rect)
                {
                    var i = new {|MEW1101:Image|} { ViewBox = rect, ViewBoxUnits = ImageViewBoxUnits.Pixels, Width = 5 };
                    return i;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Rect rect)
                {
                    var i = new Image()
                        .ViewBoxPixels(rect)
                        .Width(5);
                    return i;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Combined_FallsBackToComposite_WhenConstantDiffers()
    {
        var source = """
            class C
            {
                object M(Rect rect)
                {
                    var i = new {|MEW1101:Image|} { ViewBox = rect, ViewBoxUnits = ImageViewBoxUnits.RelativeToBoundingBox, Width = 5 };
                    return i;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Rect rect)
                {
                    var i = new Image()
                        .ViewBox(rect, ImageViewBoxUnits.RelativeToBoundingBox)
                        .Width(5);
                    return i;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Combined_NotUsed_WhenConstantMemberIsMissing()
    {
        // Neither the combined shorthand nor the composite applies to `ViewBox` alone.
        var source = """
            class C
            {
                object M(Rect rect)
                {
                    var i = new {|MEW1101:Image|} { ViewBox = rect, Width = 5 };
                    return i;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Rect rect)
                {
                    var i = new Image() { ViewBox = rect }.Width(5);
                    return i;
                }
            }
            """ + HintApi;

        await RunAsync(source, fixedSource);
    }

    [TestMethod]
    public async Task Composite_KeepsCrlfLineEndings()
    {
        var source = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new {|MEW1101:SplitButton|}
                    {
                        Command = commandSave,
                        CommandPresentationMode = CommandPresentationMode.TextAndIcon,
                        Width = 5,
                    };
                    return b;
                }
            }
            """ + HintApi;

        var fixedSource = """
            class C
            {
                object M(Command commandSave)
                {
                    var b = new SplitButton()
                        .Command(commandSave, CommandPresentationMode.TextAndIcon)
                        .Width(5);
                    return b;
                }
            }
            """ + HintApi;

        await RunAsync(source.Replace("\r\n", "\n").Replace("\n", "\r\n"), fixedSource.Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }

    private static async Task RunAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<InitializerToFluentAnalyzer, InitializerToFluentCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync();
    }
}
