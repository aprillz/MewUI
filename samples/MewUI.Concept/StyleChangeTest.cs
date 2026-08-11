using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept
{
    internal class StyleChangeTest
    {
        public static Window Create()
        {
            Window window = new Window()
            {
                Title = "Hello World",
                Background = Color.DarkGray,
                Content = new Border()
                {
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = Color.White,
                    BorderThickness = 30,
                    CornerRadius = 30,
                    BorderBrush = Color.WhiteSmoke,
                    Padding = 10,
                    Child = ButtonTest(),
                }
            };
            return window;
        }

        public static UIElement ButtonTest()
        {
            StackPanel stackPanel = new StackPanel()
            {
                Spacing = 10,
            };
            Button button = new Button()
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Content = new TextBlock()
                {
                    Text = "Click Me",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            Style styleA = new Style(typeof(Border))
            {
                Transitions =
                [
                    Transition.Create(Control.BackgroundProperty),
                    Transition.Create(FrameworkElement.WidthProperty),
                    Transition.Create(FrameworkElement.HeightProperty),
                ],
                Setters =
                [
                    Setter.Create(FrameworkElement.WidthProperty, 100),
                    Setter.Create(FrameworkElement.HeightProperty, 100),
                    Setter.Create(Control.BackgroundProperty, Color.LightBlue),
                ],
                Triggers =
                [
                    new StateTrigger
                    {
                        Match = VisualStateFlags.Hot,
                        Setters =
                        [
                            Setter.Create(Control.BackgroundProperty, Color.Black),
                            Setter.Create(FrameworkElement.WidthProperty, 200),
                            Setter.Create(FrameworkElement.HeightProperty, 200),
                        ],
                    },
                ]
            };
            Style styleB = new Style(typeof(Border))
            {
                Transitions =
                [
                    Transition.Create(Control.BackgroundProperty),
                    Transition.Create(FrameworkElement.WidthProperty),
                    Transition.Create(FrameworkElement.HeightProperty),
                ],
                        Setters =
                [
                    Setter.Create(FrameworkElement.WidthProperty, 200),
                    Setter.Create(FrameworkElement.HeightProperty, 200),
                    Setter.Create(Control.BackgroundProperty, Color.HotPink),
                ],
                Triggers =
                [
                    new StateTrigger
                    {
                        Match = VisualStateFlags.Hot,
                        Setters =
                        [
                            Setter.Create(Control.BackgroundProperty, Color.Black),
                            Setter.Create(FrameworkElement.WidthProperty, 200),
                            Setter.Create(FrameworkElement.HeightProperty, 200),
                        ],
                    },
                ]
            };
            StyleSheet styleSheet = new StyleSheet();
            styleSheet.Define("a", () => styleA);
            styleSheet.Define("b", () => styleB);
            Border border = new Border()
            {
                StyleName = "a",
                BorderThickness = 4,
                BorderBrush = Color.Red,
                StyleSheet = styleSheet,
            };
            button.Click += () => { border.StyleName = border.StyleName == "a" ? "b" : "a"; };
            stackPanel.Add(button);
            stackPanel.Add(border);
            return stackPanel;
        }
    }
}
