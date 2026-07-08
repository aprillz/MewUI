using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement TransitionsPage()
    {
        int fadeIndex = 0;
        string[] fadeItems = ["Hello, World!", "MewUI Transitions", "Fade Effect", "Smooth & Simple"];

        var fadeView = new TransitionContentControl
        {
            Transition = ContentTransition.CreateFade(durationMs: 300),
        };
        fadeView.Content = MakeTransitionBlock(fadeItems[0], 0);

        void NextFade()
        {
            fadeIndex = (fadeIndex + 1) % fadeItems.Length;
            fadeView.Content = MakeTransitionBlock(fadeItems[fadeIndex], fadeIndex);
        }

        // --- Fade (Image) --- diagnostic: does an image cross-fade, or snap?
        int fadeImgIndex = 0;
        var fadeImageView = new TransitionContentControl
        {
            Transition = ContentTransition.CreateFade(durationMs: 300),
        };
        fadeImageView.Content = MakeTransitionImage(0);

        void NextFadeImage()
        {
            fadeImgIndex = (fadeImgIndex + 1) % _transitionColors.Length;
            fadeImageView.Content = MakeTransitionImage(fadeImgIndex);
        }

        // --- Slide Left ---
        int slideLeftIndex = 0;
        string[] slideItems = ["Page 1", "Page 2", "Page 3", "Page 4"];

        var slideLeftView = new TransitionContentControl
        {
            Transition = ContentTransition.CreateSlide(SlideDirection.Left, durationMs: 300),
        };
        slideLeftView.Content = MakeTransitionBlock(slideItems[0], 0);

        void NextSlideLeft()
        {
            slideLeftIndex = (slideLeftIndex + 1) % slideItems.Length;
            slideLeftView.Content = MakeTransitionBlock(slideItems[slideLeftIndex], slideLeftIndex);
        }

        // --- Slide Up ---
        int slideUpIndex = 0;

        var slideUpView = new TransitionContentControl
        {
            Transition = ContentTransition.CreateSlide(SlideDirection.Up, durationMs: 300),
        };
        slideUpView.Content = MakeTransitionBlock(slideItems[0], 0);

        void NextSlideUp()
        {
            slideUpIndex = (slideUpIndex + 1) % slideItems.Length;
            slideUpView.Content = MakeTransitionBlock(slideItems[slideUpIndex], slideUpIndex);
        }

        // --- Scale ---
        int scaleIndex = 0;
        string[] scaleItems = ["Zoom A", "Zoom B", "Zoom C", "Zoom D"];

        var scaleView = new TransitionContentControl
        {
            Transition = ContentTransition.CreateScale(durationMs: 300),
        };
        scaleView.Content = MakeTransitionBlock(scaleItems[0], 0);

        void NextScale()
        {
            scaleIndex = (scaleIndex + 1) % scaleItems.Length;
            scaleView.Content = MakeTransitionBlock(scaleItems[scaleIndex], scaleIndex);
        }

        // --- Rotate ---
        int rotateIndex = 0;
        string[] rotateItems = ["Spin 1", "Spin 2", "Spin 3", "Spin 4"];

        var rotateView = new TransitionContentControl
        {
            Transition = ContentTransition.CreateRotate(durationMs: 400),
        };
        rotateView.Content = MakeTransitionBlock(rotateItems[0], 0);

        void NextRotate()
        {
            rotateIndex = (rotateIndex + 1) % rotateItems.Length;
            rotateView.Content = MakeTransitionBlock(rotateItems[rotateIndex], rotateIndex);
        }

        // --- Delay ---
        int delayIndex = 0;
        var delayView = new TransitionContentControl
        {
            Transition = ContentTransition.CreateFade(durationMs: 400, delayMs: 200),
        };
        delayView.Content = MakeTransitionBlock("Delayed Fade", 0);

        void NextDelay()
        {
            delayIndex = (delayIndex + 1) % fadeItems.Length;
            delayView.Content = MakeTransitionBlock(fadeItems[delayIndex], delayIndex);
        }

        // --- ProgressRing ---
        var ring = new ProgressRing { IsActive = false };

        return CardGrid(
            Card(
                "ProgressRing",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .HorizontalAlignment(HorizontalAlignment.Center)
                            .Child(
                                ring
                                    .Width(48)
                                    .Height(48)
                                    .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                            ),
                        new Button()
                            .Content("Toggle")
                            .OnClick(() => ring.IsActive = !ring.IsActive)
                    )
            ),


            Card(
                "Fade",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .Child(fadeView),
                        new Button()
                            .Content("Next")
                            .OnClick(NextFade)
                    )
            ),

            Card(
                "Fade (Image)",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .Child(fadeImageView),
                        new Button()
                            .Content("Next")
                            .OnClick(NextFadeImage)
                    )
            ),

            Card(
                "Slide Left",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .Child(slideLeftView),
                        new Button()
                            .Content("Next")
                            .OnClick(NextSlideLeft)
                    )
            ),

            Card(
                "Slide Up",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .Child(slideUpView),
                        new Button()
                            .Content("Next")
                            .OnClick(NextSlideUp)
                    )
            ),

            Card(
                "Scale",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .Child(scaleView),
                        new Button()
                            .Content("Next")
                            .OnClick(NextScale)
                    )
            ),

            Card(
                "Rotate",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .Child(rotateView),
                        new Button()
                            .Content("Next")
                            .OnClick(NextRotate)
                    )
            ),

            Card(
                "Fade + Delay (200ms)",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Height(60)
                            .Child(delayView),
                        new Button()
                            .Content("Next")
                            .OnClick(NextDelay)
                    )
            )
        );
    }

    private static readonly Color[] _transitionColors =
    [
        Color.FromArgb(255, 70, 130, 220),
        Color.FromArgb(255, 220, 90, 70),
        Color.FromArgb(255, 70, 190, 120),
        Color.FromArgb(255, 200, 160, 50),
    ];

    private static FrameworkElement MakeTransitionImage(int colorIndex)
    {
        var color = _transitionColors[colorIndex % _transitionColors.Length];
        const int w = 80, h = 50;
        var bgra = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            bgra[i * 4] = color.B;
            bgra[i * 4 + 1] = color.G;
            bgra[i * 4 + 2] = color.R;
            bgra[i * 4 + 3] = 255;
        }
        return new Image()
            .Source(ImageSource.FromBgraPixels(w, h, bgra))
            .StretchMode(Stretch.Fill);
    }

    private static FrameworkElement MakeTransitionBlock(string text, int colorIndex)
    {
        var color = _transitionColors[colorIndex % _transitionColors.Length];
        return new Border()
            .Background(color)
            .CornerRadius(6)
            .Padding(12, 8)
            .Child(
                new TextBlock()
                    .Text(text)
                    .Foreground(Color.White)
                    .Bold()
                    .VerticalAlignment(VerticalAlignment.Center)
                    .HorizontalAlignment(HorizontalAlignment.Center));
    }
}
