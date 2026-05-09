using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

internal static class NonUniformBorderTest
{
    internal static Window Create()
    {
        var window = new Window()
            .Resizable(1000, 800)
            .Title("Non-Uniform Border Test");

        var root = new ScrollViewer
        {
            Content = BuildContent()
        };
        window.Content = root;
        return window;
    }

    private static UIElement BuildContent()
    {
        var root = new StackPanel().Vertical().Spacing(24).Padding(24);

        root.Add(MakeLabel("Non-Uniform Border Visual Test Cases"));

        // === Section 1: Uniform (regression check) ===
        root.Add(MakeSection("1. Uniform (Regression)", new[]
        {
            MakeBorder("No radius",
                thickness: 2, radius: new CornerRadius(0),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("Uniform radius=8",
                thickness: 2, radius: new CornerRadius(8),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("Uniform radius=20",
                thickness: 4, radius: new CornerRadius(20),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("Thick border=8",
                thickness: 8, radius: new CornerRadius(12),
                Color.FromRgb(200, 60, 60), Color.FromRgb(255, 230, 230)),
        }));

        // === Section 2: Non-uniform thickness ===
        root.Add(MakeSection("2. Non-Uniform Thickness", new[]
        {
            MakeBorder("Bottom only (0,0,0,3)",
                thickness: new Thickness(0, 0, 0, 3), radius: new CornerRadius(0),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("Top=1, Bottom=4",
                thickness: new Thickness(0, 1, 0, 4), radius: new CornerRadius(0),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("Left=6, others=1",
                thickness: new Thickness(6, 1, 1, 1), radius: new CornerRadius(0),
                Color.FromRgb(200, 60, 60), Color.FromRgb(255, 230, 230)),

            MakeBorder("All different (2,4,6,8)",
                thickness: new Thickness(2, 4, 6, 8), radius: new CornerRadius(0),
                Color.FromRgb(60, 160, 60), Color.FromRgb(230, 250, 230)),
        }));

        // === Section 3: Non-uniform corner radius ===
        root.Add(MakeSection("3. Non-Uniform CornerRadius", new[]
        {
            MakeBorder("TL=16 only",
                thickness: 2, radius: new CornerRadius(16, 0, 0, 0),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("Top corners=12",
                thickness: 2, radius: new CornerRadius(12, 12, 0, 0),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("Diagonal TL=16, BR=16",
                thickness: 2, radius: new CornerRadius(16, 0, 16, 0),
                Color.FromRgb(200, 60, 60), Color.FromRgb(255, 230, 230)),

            MakeBorder("All different (4,8,16,24)",
                thickness: 2, radius: new CornerRadius(4, 8, 16, 24),
                Color.FromRgb(60, 160, 60), Color.FromRgb(230, 250, 230)),
        }));

        // === Section 4: Non-uniform thickness + radius ===
        root.Add(MakeSection("4. Non-Uniform Thickness + Radius", new[]
        {
            MakeBorder("T=1,B=4 + Top corners=12",
                thickness: new Thickness(1, 1, 1, 4), radius: new CornerRadius(12, 12, 0, 0),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("L=6,R=2 + TL=20,BR=20",
                thickness: new Thickness(6, 2, 2, 2), radius: new CornerRadius(20, 0, 20, 0),
                Color.FromRgb(200, 60, 60), Color.FromRgb(255, 230, 230)),

            MakeBorder("All diff (2,4,6,8) + (4,8,16,24)",
                thickness: new Thickness(2, 4, 6, 8), radius: new CornerRadius(4, 8, 16, 24),
                Color.FromRgb(60, 160, 60), Color.FromRgb(230, 250, 230)),

            MakeBorder("Thick (8,2,8,2) + (16,16,16,16)",
                thickness: new Thickness(8, 2, 8, 2), radius: new CornerRadius(16),
                Color.FromRgb(160, 60, 160), Color.FromRgb(245, 230, 250)),
        }));

        // === Section 5: Radius clamping ===
        root.Add(MakeSection("5. Radius Clamping (radius > size/2)", new[]
        {
            MakeBorder("Pill shape (radius=999)",
                thickness: 2, radius: new CornerRadius(999),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250),
                width: 120, height: 40),

            MakeBorder("TL=100, TR=100 on 120x60",
                thickness: 2, radius: new CornerRadius(100, 100, 0, 0),
                Color.FromRgb(200, 60, 60), Color.FromRgb(255, 230, 230),
                width: 120, height: 60),

            MakeBorder("All=50 on 60x60 (circle-ish)",
                thickness: 3, radius: new CornerRadius(50),
                Color.FromRgb(60, 160, 60), Color.FromRgb(230, 250, 230),
                width: 60, height: 60),
        }));

        // === Section 6: ClipToBounds ===
        root.Add(MakeSection("6. ClipToBounds", new[]
        {
            MakeClipBorder("Uniform clip r=12",
                thickness: 2, radius: new CornerRadius(12),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeClipBorder("Non-uniform clip (12,12,0,0)",
                thickness: 2, radius: new CornerRadius(12, 12, 0, 0),
                Color.FromRgb(200, 60, 60), Color.FromRgb(255, 230, 230)),

            MakeClipBorder("Thick+radius clip (4,1,4,1)+(8,8,8,8)",
                thickness: new Thickness(4, 1, 4, 1), radius: new CornerRadius(8),
                Color.FromRgb(60, 160, 60), Color.FromRgb(230, 250, 230)),
        }));

        // === Section 7: No background (border only) ===
        root.Add(MakeSection("7. Border Only (no background)", new[]
        {
            MakeBorder("Uniform border only",
                thickness: 2, radius: new CornerRadius(8),
                Color.FromRgb(60, 120, 200), Color.Transparent),

            MakeBorder("Non-uniform border only (2,4,6,8)",
                thickness: new Thickness(2, 4, 6, 8), radius: new CornerRadius(12),
                Color.FromRgb(200, 60, 60), Color.Transparent),

            MakeBorder("Radius only, no bg (0,0,16,16)",
                thickness: 2, radius: new CornerRadius(0, 0, 16, 16),
                Color.FromRgb(60, 160, 60), Color.Transparent),
        }));

        // === Section 8: Background only (no border) ===
        root.Add(MakeSection("8. Background Only (no border)", new[]
        {
            MakeBorder("Uniform bg only",
                thickness: new Thickness(0), radius: new CornerRadius(8),
                Color.Transparent, Color.FromRgb(230, 240, 250)),

            MakeBorder("Non-uniform radius bg (12,0,12,0)",
                thickness: new Thickness(0), radius: new CornerRadius(12, 0, 12, 0),
                Color.Transparent, Color.FromRgb(255, 230, 230)),
        }));

        // === Section 9: Inner radius elliptical (non-uniform thickness + radius) ===
        root.Add(MakeSection("9. Elliptical Inner Radius", new[]
        {
            MakeBorder("L=12,T=2 + TL=16 (inner rx=4, ry=14)",
                thickness: new Thickness(12, 2, 2, 2), radius: new CornerRadius(16, 8, 8, 8),
                Color.FromRgb(60, 120, 200), Color.FromRgb(230, 240, 250)),

            MakeBorder("T=12,L=2 + TL=16 (inner rx=14, ry=4)",
                thickness: new Thickness(2, 12, 2, 2), radius: new CornerRadius(16, 8, 8, 8),
                Color.FromRgb(200, 60, 60), Color.FromRgb(255, 230, 230)),

            MakeBorder("Thick>radius (T=20, TL=10 -> inner ry=0)",
                thickness: new Thickness(2, 20, 2, 2), radius: new CornerRadius(10, 10, 8, 8),
                Color.FromRgb(60, 160, 60), Color.FromRgb(230, 250, 230)),
        }));

        return root;
    }

    private static Border MakeBorder(
        string label,
        Thickness thickness,
        CornerRadius radius,
        Color borderColor,
        Color bgColor,
        double width = 160,
        double height = 80)
    {
        return new Border
        {
            NonUniformBorderThickness = thickness,
            NonUniformCornerRadius = radius,
            BorderBrush = borderColor,
            Background = bgColor,
            Width = width,
            Height = height,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            }
        };
    }

    private static Border MakeClipBorder(
        string label,
        Thickness thickness,
        CornerRadius radius,
        Color borderColor,
        Color bgColor)
    {
        // Oversized child to test clipping
        return new Border
        {
            NonUniformBorderThickness = thickness,
            NonUniformCornerRadius = radius,
            BorderBrush = borderColor,
            Background = bgColor,
            ClipToBounds = true,
            Width = 160,
            Height = 80,
            Child = new Border
            {
                Background = Color.FromArgb(128, 255, 100, 100),
                Width = 200,
                Height = 120,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                }
            }
        };
    }

    private static UIElement MakeSection(string title, UIElement[] items)
    {
        var panel = new StackPanel().Vertical().Spacing(8);
        panel.Add(MakeLabel(title));

        var wrap = new WrapPanel().Spacing(12);
        foreach (var item in items)
            wrap.Add(item);
        panel.Add(wrap);

        return panel;
    }

    private static TextBlock MakeLabel(string text) => new TextBlock
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeight.Bold,
    };
}
