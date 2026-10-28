using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Real pointer input aimed at where a transformed control is drawn. The drawn position is computed here from
/// the decorator's documented matrix (transform about its origin), independently of the framework, and then
/// compared with <see cref="UIElement.PointToScreen"/> before the system pointer is sent there.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TransformedInputTests
{
    private const double DEGREES_TO_RADIANS = Math.PI / 180;
    // PointToScreen and the independent matrix both end in fractional device pixels; one pixel covers rounding at any scale.
    private const double SCREEN_TOLERANCE_PX = 1.0;
    // Injected moves reach the window in bursts a few steps behind the injector, so a value read mid-drag waits for the last one.
    private const int DRAG_SETTLE_MS = 300;

    private static bool? _inputDelivered;

    [TestMethod]
    public Task RotatedScaledButton_ClicksAtItsDrawnCenterOnly() => RunRotatedScaledButtonAsync(monitor: null);

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DynamicData(nameof(MonitorMatrix.DistinctScales), typeof(MonitorMatrix),
        DynamicDataDisplayName = nameof(MonitorMatrix.ScaleName),
        DynamicDataDisplayNameDeclaringType = typeof(MonitorMatrix))]
    public Task RotatedScaledButton_ClicksAtItsDrawnCenterOnly_AtEachScale(MonitorProbe monitor) => RunRotatedScaledButtonAsync(monitor);

    private static async Task RunRotatedScaledButtonAsync(MonitorProbe? monitor)
    {
        if (!RequireInput()) return;

        await RealAppSession.RunAsync(async () =>
        {
            await RequireDeliveredInputAsync();
            var button = new Button
            {
                Content = new TextBlock { Text = "Rotated" },
                Width = 90,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(90, 70, 0, 0),
            };
            var transform = Matrix3x2.CreateRotation((float)(-20 * DEGREES_TO_RADIANS)) * Matrix3x2.CreateScale(1.2f);
            var decorator = new TransformDecorator { Transform = transform, Mode = TransformMode.Render, Child = button };
            int clicks = 0;
            Point pressedLocal = default;
            button.Click += () => clicks++;
            button.MouseDown += args => pressedLocal = args.GetPosition(button);

            var window = NewWindow("TransformedButton", 640, 480, decorator);
            try
            {
                await ShowAsync(window, monitor);
                var toWindow = AboutOrigin(transform, decorator.Bounds, 0.5, 0.5);
                var local = new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
                var drawnCenter = DrawnScreenPoint(window, toWindow, button, local);
                var reported = button.PointToScreen(local);
                Log($"scale={window.DpiScale:F2} button.Bounds={button.Bounds} drawn center px={drawnCenter} PointToScreen={reported}");
                AssertNear(drawnCenter, reported, "PointToScreen of the button center follows the transform");

                // The untransformed layout center must fall outside the drawn button, or the second click proves nothing.
                var untransformedWindowPoint = new Point(button.Bounds.X + local.X, button.Bounds.Y + local.Y);
                var untransformedInButton = InverseToElement(toWindow, button, untransformedWindowPoint);
                double outside = DistanceOutside(untransformedInButton, button.Bounds.Size);
                Log($"untransformed center (window DIP)={untransformedWindowPoint} lies at button-local {untransformedInButton}, {outside:F1} DIP outside the drawn button");
                Assert.IsGreaterThan(10.0, outside, "the scene must put the layout center well outside the drawn button");

                var untransformedScreen = window.ClientToScreen(untransformedWindowPoint);
                RequireOnTop(window, untransformedScreen);
                await OsMouse.ClickAsync(untransformedScreen);
                await Task.Delay(150);
                int clicksAfterLayoutCenter = clicks;

                RequireOnTop(window, drawnCenter);
                await OsMouse.ClickAsync(drawnCenter);
                await Task.Delay(150);
                Log($"clicks: at layout center={clicksAfterLayoutCenter}, after drawn center={clicks}, pressed at button-local {pressedLocal}");

                Assert.AreEqual(0, clicksAfterLayoutCenter, $"a click at the untransformed center {untransformedScreen} px hit the button");
                Assert.AreEqual(1, clicks, $"a click at the drawn center {drawnCenter} px did not click the button");
                Assert.AreEqual(local.X, pressedLocal.X, 1.5, "GetPosition(button) x of the press at the drawn center");
                Assert.AreEqual(local.Y, pressedLocal.Y, 1.5, "GetPosition(button) y of the press at the drawn center");
            }
            finally
            {
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        });
    }

    [TestMethod]
    public async Task SliderRotated90_DragFollowsTheDrawnAxis()
    {
        if (!RequireInput()) return;

        await RealAppSession.RunAsync(async () =>
        {
            await RequireDeliveredInputAsync();
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                Width = 240,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var transform = Matrix3x2.CreateRotation((float)(90 * DEGREES_TO_RADIANS));
            var decorator = new TransformDecorator { Transform = transform, Child = slider };
            var window = NewWindow("TransformedSlider", 400, 400, decorator);
            try
            {
                await ShowAsync(window, monitor: null);
                var toWindow = AboutOrigin(transform, decorator.Bounds, 0.5, 0.5);
                var low = DrawnScreenPoint(window, toWindow, slider, HorizontalTrackPoint(slider, 0.2));
                var high = DrawnScreenPoint(window, toWindow, slider, HorizontalTrackPoint(slider, 0.8));
                AssertNear(low, slider.PointToScreen(HorizontalTrackPoint(slider, 0.2)), "PointToScreen of the 20% track point");
                Log($"slider.Bounds={slider.Bounds} padding={slider.Padding} 20% px={low} 80% px={high}");
                Assert.AreEqual(low.X, high.X, 2.0, "rotated 90 degrees, the drawn track runs vertically");
                Assert.IsGreaterThan(low.Y + 50, high.Y, "rotated 90 degrees clockwise, larger values are drawn lower");

                RequireOnTop(window, low);
                await OsMouse.MoveAsync(low);
                await OsMouse.PressAsync(low);
                double afterPress = slider.Value;
                const int STEPS = 12;
                for (int step = 1; step <= STEPS; step++)
                {
                    double fraction = (double)step / STEPS;
                    await OsMouse.MoveAsync(new Point(low.X + (high.X - low.X) * fraction, low.Y + (high.Y - low.Y) * fraction));
                }

                await Task.Delay(DRAG_SETTLE_MS);
                double beforeRelease = slider.Value;
                await OsMouse.ReleaseAsync(high);
                Log($"value after press={afterPress:F2} after drag={beforeRelease:F2} final={slider.Value:F2}");

                Assert.AreEqual(20, afterPress, 1.0, "pressing the drawn 20% point sets the value there");
                Assert.AreEqual(80, beforeRelease, 1.0, "dragging along the drawn axis to the 80% point follows it");
                Assert.AreEqual(80, slider.Value, 1.0, "the release keeps the value");
            }
            finally
            {
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        });
    }

    /// <summary>Opened from code, so the placement is checked even where the session cannot inject input.</summary>
    [TestMethod]
    public Task ComboBoxScaled_PopupOpensJustBelowTheDrawnBox() => RunScaledComboBoxAsync(openByClick: false);

    [TestMethod]
    public Task ComboBoxScaled_ClickAtTheDrawnCenterOpensItBelowTheDrawnBox() => RunScaledComboBoxAsync(openByClick: true);

    private static async Task RunScaledComboBoxAsync(bool openByClick)
    {
        if (openByClick && !RequireInput()) return;
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("The application loop did not start on this platform.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            if (openByClick)
            {
                await RequireDeliveredInputAsync();
            }

            // Centered across and above the middle, so the box drawn 1.5 times larger stays inside the decorator's bounds.
            var combo = new ComboBox
            {
                Width = 160,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 110, 0, 0),
            }.Items(["One", "Two", "Three"]);
            var transform = Matrix3x2.CreateScale(1.5f);
            var decorator = new TransformDecorator { Transform = transform, Child = combo };
            var window = NewWindow("TransformedComboBox", 420, 360, decorator);
            try
            {
                await ShowAsync(window, monitor: null);
                var toWindow = AboutOrigin(transform, decorator.Bounds, 0.5, 0.5);
                var drawnBottomLeft = DrawnScreenPoint(window, toWindow, combo, new Point(0, combo.Bounds.Height));
                var drawnBottomRight = DrawnScreenPoint(window, toWindow, combo, new Point(combo.Bounds.Width, combo.Bounds.Height));
                var drawnCenter = DrawnScreenPoint(window, toWindow, combo, new Point(combo.Bounds.Width / 2, combo.Bounds.Height / 2));

                bool openedByClick = false;
                if (openByClick)
                {
                    int presses = 0;
                    Point pressedLocal = default;
                    combo.MouseDown += args =>
                    {
                        presses++;
                        pressedLocal = args.GetPosition(combo);
                    };

                    RequireOnTop(window, drawnCenter);
                    await OsMouse.MoveAsync(drawnCenter);
                    await OsMouse.PressAsync(drawnCenter);
                    bool openAfterPress = combo.IsDropDownOpen;
                    await OsMouse.ReleaseAsync(drawnCenter);
                    bool openAfterRelease = combo.IsDropDownOpen;
                    await Task.Delay(300);
                    openedByClick = combo.IsDropDownOpen;
                    Log($"decorator.Bounds={decorator.Bounds} combo.Bounds={combo.Bounds} drawn center px={drawnCenter} presses={presses} pressed at combo-local {pressedLocal} open after press={openAfterPress} after release={openAfterRelease} after 300 ms={openedByClick}");
                    Assert.IsTrue(openedByClick, $"a click at the drawn center {drawnCenter} px did not open the drop-down");
                }
                else
                {
                    combo.IsDropDownOpen = true;
                    await Task.Delay(300);
                    Log($"decorator.Bounds={decorator.Bounds} combo.Bounds={combo.Bounds} opened from code, scale={window.DpiScale:F2}");
                }

                var list = (UIElement?)typeof(ComboBox).GetField("_popupList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(combo);
                Assert.IsNotNull(list, "the drop-down list was not created");
                var listTopLeft = list.PointToScreen(new Point(0, 0));
                var listTopRight = list.PointToScreen(new Point(list.Bounds.Width, 0));
                var surface = list.ResolveInputHostWindow();
                string measured = "framework";
                var osTopLeft = listTopLeft;
                if (OperatingSystem.IsWindows() && surface != null && !ReferenceEquals(surface, window))
                {
                    var origin = new Win32.POINT();
                    Win32.ClientToScreen(surface.Handle, ref origin);
                    osTopLeft = new Point(
                        origin.X + PopupChrome.ShadowPadding.Left * surface.DpiScale,
                        origin.Y + PopupChrome.ShadowPadding.Top * surface.DpiScale);
                    measured = "OS popup client origin + shadow padding";
                    AssertNear(osTopLeft, listTopLeft, "the list's PointToScreen agrees with the native popup window");
                }
                else if (OperatingSystem.IsLinux() && surface != null && !ReferenceEquals(surface, window))
                {
                    var origin = X11.RootPositionOf((nuint)surface.Handle);
                    osTopLeft = new Point(
                        origin.X + PopupChrome.ShadowPadding.Left * surface.DpiScale,
                        origin.Y + PopupChrome.ShadowPadding.Top * surface.DpiScale);
                    measured = "X popup window origin + shadow padding";
                    AssertNear(osTopLeft, listTopLeft, "the list's PointToScreen agrees with the X popup window");
                }
                else if (OperatingSystem.IsMacOS() && surface != null && !ReferenceEquals(surface, window))
                {
                    // Popup windows are borderless, so the frame's top-left is the content's.
                    var frame = MacScreens.ToScreenPixels(MacScreens.WindowFrameOfView(surface.Handle));
                    osTopLeft = new Point(
                        frame.X + PopupChrome.ShadowPadding.Left * surface.DpiScale,
                        frame.Y + PopupChrome.ShadowPadding.Top * surface.DpiScale);
                    measured = "NSWindow popup frame + shadow padding";
                    AssertNear(osTopLeft, listTopLeft, "the list's PointToScreen agrees with the popup NSWindow");
                }

                Log($"drawn box bottom-left px={drawnBottomLeft} bottom-right px={drawnBottomRight}; popup list top-left px={osTopLeft} ({measured}), top-right px={listTopRight}, popup on its own surface={surface != null && !ReferenceEquals(surface, window)}");

                Assert.AreEqual(drawnBottomLeft.X, osTopLeft.X, 3.0, "the popup's left edge lines up with the drawn box");
                Assert.AreEqual(drawnBottomLeft.Y, osTopLeft.Y, 3.0, "the popup opens just below the drawn box");
                Assert.AreEqual(drawnBottomRight.X, listTopRight.X, 3.0, "the popup is as wide as the drawn box");
            }
            finally
            {
                combo.IsDropDownOpen = false;
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        });
    }

    /// <summary>
    /// A Render-mode transform draws past the decorator's layout bounds without clipping, so a click on the part of the
    /// button drawn there has to reach the button as well.
    /// </summary>
    [TestMethod]
    public async Task ScaledButton_ClicksOnThePartDrawnOutsideTheDecoratorBounds()
    {
        if (!RequireInput()) return;

        await RealAppSession.RunAsync(async () =>
        {
            await RequireDeliveredInputAsync();
            var button = new Button { Content = new TextBlock { Text = "Scaled" } };
            var transform = Matrix3x2.CreateScale(1.5f);
            var decorator = new TransformDecorator
            {
                Width = 200,
                Height = 60,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Transform = transform,
                Child = button,
            };
            int clicks = 0;
            button.Click += () => clicks++;
            var window = NewWindow("ScaledOverflowButton", 420, 300, decorator);
            try
            {
                await ShowAsync(window, monitor: null);
                var toWindow = AboutOrigin(transform, decorator.Bounds, 0.5, 0.5);
                var inside = DrawnScreenPoint(window, toWindow, button, new Point(button.Bounds.Width / 2, button.Bounds.Height / 2));
                var overflow = DrawnScreenPoint(window, toWindow, button, new Point(button.Bounds.Width - 10, button.Bounds.Height / 2));
                var decoratorRight = window.ClientToScreen(new Point(decorator.Bounds.Right, 0)).X;
                Log($"decorator.Bounds={decorator.Bounds} button.Bounds={button.Bounds} inside px={inside} overflow px={overflow} decorator right edge px={decoratorRight}");
                Assert.IsGreaterThan(decoratorRight + 10, overflow.X, "the overflow point must lie outside the decorator's layout bounds");

                RequireOnTop(window, inside);
                await OsMouse.ClickAsync(inside);
                await Task.Delay(150);
                int clicksInside = clicks;

                RequireOnTop(window, overflow);
                await OsMouse.ClickAsync(overflow);
                await Task.Delay(150);
                Log($"clicks: inside the decorator={clicksInside}, after the drawn overflow={clicks}");

                Assert.AreEqual(1, clicksInside, "a click on the drawn button inside the decorator bounds");
                Assert.AreEqual(2, clicks, $"a click at {overflow} px, on the button where it is drawn past the decorator bounds, did not reach it");
            }
            finally
            {
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        });
    }

    /// <summary>The same click without a transform, so a failure above can be told apart from one the transform causes.</summary>
    [TestMethod]
    public async Task ComboBoxUntransformed_ClickOpensTheDropDown()
    {
        if (!RequireInput()) return;

        await RealAppSession.RunAsync(async () =>
        {
            await RequireDeliveredInputAsync();
            var combo = new ComboBox
            {
                Width = 160,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(40, 40, 0, 0),
            }.Items(["One", "Two", "Three"]);
            var window = NewWindow("UntransformedComboBox", 420, 360, combo);
            try
            {
                await ShowAsync(window, monitor: null);
                var center = combo.PointToScreen(new Point(combo.Bounds.Width / 2, combo.Bounds.Height / 2));
                RequireOnTop(window, center);
                await OsMouse.MoveAsync(center);
                await OsMouse.PressAsync(center);
                bool openAfterPress = combo.IsDropDownOpen;
                await OsMouse.ReleaseAsync(center);
                await Task.Delay(300);
                Log($"untransformed combo center px={center} open after press={openAfterPress} after release={combo.IsDropDownOpen}");
                Assert.IsTrue(combo.IsDropDownOpen, $"a click at the center {center} px did not open the drop-down");
            }
            finally
            {
                combo.IsDropDownOpen = false;
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        });
    }

    [TestMethod]
    public async Task ViewboxButton_ClicksAtItsDrawnCenter()
    {
        if (!RequireInput()) return;

        await RealAppSession.RunAsync(async () =>
        {
            await RequireDeliveredInputAsync();
            var button = new Button { Content = new TextBlock { Text = "Boxed" }, Width = 100, Height = 30 };
            var viewbox = new Viewbox
            {
                Width = 300,
                Height = 300,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = button,
            };
            int clicks = 0;
            button.Click += () => clicks++;
            var window = NewWindow("ViewboxButton", 400, 400, viewbox);
            try
            {
                await ShowAsync(window, monitor: null);
                var box = viewbox.Bounds;
                var expectedCenter = window.ClientToScreen(new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
                var drawnCenter = button.PointToScreen(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2));
                var drawnTopLeft = button.PointToScreen(new Point(0, 0));
                var drawnBottomRight = button.PointToScreen(new Point(button.Bounds.Width, button.Bounds.Height));
                double drawnWidthDip = (drawnBottomRight.X - drawnTopLeft.X) / window.DpiScale;
                Log($"viewbox.Bounds={box} button.Bounds={button.Bounds} drawn center px={drawnCenter} expected={expectedCenter} drawn width DIP={drawnWidthDip:F1}");
                AssertNear(expectedCenter, drawnCenter, "Uniform stretch centers the button in the viewbox");
                Assert.AreEqual(300, drawnWidthDip, 1.0, "Uniform stretch scales the 100 DIP button to the 300 DIP box");

                var layoutCenter = window.ClientToScreen(new Point(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2));
                RequireOnTop(window, layoutCenter);
                await OsMouse.ClickAsync(layoutCenter);
                await Task.Delay(150);
                int clicksAfterLayoutCenter = clicks;

                RequireOnTop(window, drawnCenter);
                await OsMouse.ClickAsync(drawnCenter);
                await Task.Delay(150);
                Log($"clicks: at layout center={clicksAfterLayoutCenter}, after drawn center={clicks}");

                Assert.AreEqual(0, clicksAfterLayoutCenter, $"a click at the unscaled layout center {layoutCenter} px hit the button");
                Assert.AreEqual(1, clicks, $"a click at the drawn center {drawnCenter} px did not click the button");
            }
            finally
            {
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        });
    }

    [TestMethod]
    public async Task VerticalSlider_DragUpIncreasesWithTheMinimumAtTheBottom()
    {
        if (!RequireInput()) return;

        await RealAppSession.RunAsync(async () =>
        {
            await RequireDeliveredInputAsync();
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                Height = 240,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var window = NewWindow("VerticalSlider", 300, 360, slider);
            try
            {
                await ShowAsync(window, monitor: null);
                var low = slider.PointToScreen(VerticalTrackPoint(slider, 0.1));
                var high = slider.PointToScreen(VerticalTrackPoint(slider, 0.9));
                Log($"slider.Bounds={slider.Bounds} padding={slider.Padding} 10% px={low} 90% px={high}");
                Assert.IsGreaterThan(high.Y + 100, low.Y, "the minimum end is at the bottom");

                RequireOnTop(window, low);
                await OsMouse.MoveAsync(low);
                await OsMouse.PressAsync(low);
                double afterPress = slider.Value;
                const int STEPS = 12;
                for (int step = 1; step <= STEPS; step++)
                {
                    double fraction = (double)step / STEPS;
                    await OsMouse.MoveAsync(new Point(low.X, low.Y + (high.Y - low.Y) * fraction));
                }

                await Task.Delay(DRAG_SETTLE_MS);
                double beforeRelease = slider.Value;
                await OsMouse.ReleaseAsync(high);
                Log($"value after press={afterPress:F2} after drag={beforeRelease:F2} final={slider.Value:F2}");

                Assert.AreEqual(10, afterPress, 1.0, "pressing near the bottom sets a low value");
                Assert.AreEqual(90, beforeRelease, 1.0, "dragging up raises the value");
                Assert.AreEqual(90, slider.Value, 1.0, "the release keeps the value");
            }
            finally
            {
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        });
    }

    private static bool RequireInput()
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("The application loop did not start on this platform.");
            return false;
        }

        if (!OsMouse.TryInitialize(out string reason))
        {
            Assert.Inconclusive(reason);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Clicks a plain untransformed target once per run; when the system delivers nothing there (a locked screen, a
    /// display asleep), the transformed cases are inconclusive rather than failed.
    /// </summary>
    private static async Task RequireDeliveredInputAsync()
    {
        if (_inputDelivered == null)
        {
            var target = new Border { Background = Color.FromRgb(40, 80, 120) };
            int presses = 0;
            target.MouseDown += _ => presses++;
            var window = NewWindow("InputDeliveryProbe", 240, 180, target);
            try
            {
                await ShowAsync(window, monitor: null);
                var center = target.PointToScreen(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2));
                await OsMouse.ClickAsync(center);
                await Task.Delay(150);
                _inputDelivered = presses > 0;
                Log($"input delivery probe at {center} px: presses={presses}");
            }
            finally
            {
                OsMouse.ReleaseIfHeld();
                window.Close();
            }
        }

        if (_inputDelivered != true)
        {
            Assert.Inconclusive("Injected pointer input did not reach a plain untransformed window in this session (locked screen, display asleep, or input blocked).");
        }
    }

    private static Window NewWindow(string title, double width, double height, UIElement content)
        => new()
        {
            Title = title,
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(width, height),
            Content = content,
        };

    private static async Task ShowAsync(Window window, MonitorProbe? monitor)
    {
        window.Show();
        if (OperatingSystem.IsWindows())
        {
            var target = monitor ?? MonitorMatrix.Monitors.FirstOrDefault(static probe => probe.IsPrimary) ?? MonitorMatrix.Monitors.FirstOrDefault();
            if (target != null)
            {
                MonitorProbe.SetWindowPos(window.Handle, 0, target.PixelBounds.Left + 120, target.PixelBounds.Top + 100, 0, 0, MonitorProbe.MOVE_ONLY);
            }

            TouchInjector.KeepOnTop(window);
        }
        else
        {
            window.MoveTo(120, 100);
        }

        window.Activate();
        await Task.Delay(500);
        if (monitor != null)
        {
            Assert.AreEqual(monitor.Dpi, window.Dpi, $"the window must sit on {monitor.Label}");
        }
    }

    /// <summary>The decorator's Render-mode matrix: the transform applied about a fractional origin of its bounds, in window DIPs.</summary>
    private static Matrix3x2 AboutOrigin(Matrix3x2 transform, Rect bounds, double originX, double originY)
    {
        float centerX = (float)(bounds.X + bounds.Width * originX);
        float centerY = (float)(bounds.Y + bounds.Height * originY);
        return Matrix3x2.CreateTranslation(-centerX, -centerY) * transform * Matrix3x2.CreateTranslation(centerX, centerY);
    }

    private static Point DrawnScreenPoint(Window window, Matrix3x2 toWindow, UIElement element, Point local)
    {
        var drawn = Vector2.Transform(new Vector2((float)(element.Bounds.X + local.X), (float)(element.Bounds.Y + local.Y)), toWindow);
        return window.ClientToScreen(new Point(drawn.X, drawn.Y));
    }

    /// <summary>Maps a drawn window point back into the element's own coordinates through the inverse matrix.</summary>
    private static Point InverseToElement(Matrix3x2 toWindow, UIElement element, Point windowPoint)
    {
        Assert.IsTrue(Matrix3x2.Invert(toWindow, out var inverse), "the decorator matrix must be invertible");
        var layout = Vector2.Transform(new Vector2((float)windowPoint.X, (float)windowPoint.Y), inverse);
        return new Point(layout.X - element.Bounds.X, layout.Y - element.Bounds.Y);
    }

    private static double DistanceOutside(Point local, Size size)
    {
        double outsideX = Math.Max(Math.Max(-local.X, local.X - size.Width), 0);
        double outsideY = Math.Max(Math.Max(-local.Y, local.Y - size.Height), 0);
        return Math.Max(outsideX, outsideY);
    }

    /// <summary>The local point SetValueFromPosition maps to <paramref name="fraction"/> on a horizontal slider.</summary>
    private static Point HorizontalTrackPoint(Slider slider, double fraction)
    {
        const double THUMB_SIZE = 14;
        var padding = slider.Padding;
        double track = slider.Bounds.Width - padding.Left - padding.Right - THUMB_SIZE;
        return new Point(padding.Left + THUMB_SIZE / 2 + fraction * track, slider.Bounds.Height / 2);
    }

    /// <summary>The local point SetValueFromPosition maps to <paramref name="fraction"/> on a vertical slider (minimum at the bottom).</summary>
    private static Point VerticalTrackPoint(Slider slider, double fraction)
    {
        const double THUMB_SIZE = 14;
        var padding = slider.Padding;
        double track = slider.Bounds.Height - padding.Top - padding.Bottom - THUMB_SIZE;
        return new Point(slider.Bounds.Width / 2, slider.Bounds.Height - padding.Bottom - THUMB_SIZE / 2 - fraction * track);
    }

    /// <summary>Fails early when another window covers the point on Windows: the click would land there instead.</summary>
    private static void RequireOnTop(Window window, Point screenPx)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var point = new Win32.POINT { X = (int)Math.Round(screenPx.X), Y = (int)Math.Round(screenPx.Y) };
        nint under = Win32.WindowFromPoint(point);
        if (under != window.Handle)
        {
            Assert.Fail($"Window 0x{under:X} covers ({point.X}, {point.Y}); the click would not reach the test window.");
        }
    }

    private static void AssertNear(Point expected, Point actual, string message)
    {
        Assert.AreEqual(expected.X, actual.X, SCREEN_TOLERANCE_PX, $"{message} (x, expected {expected}, actual {actual})");
        Assert.AreEqual(expected.Y, actual.Y, SCREEN_TOLERANCE_PX, $"{message} (y, expected {expected}, actual {actual})");
    }

    private static void Log(string message) => MeasureLog.Write($"[TransformedInput] {message}");

    private static class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern nint WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(nint hwnd, ref POINT point);
    }

    private static class X11
    {
        private const string LIB = "libX11.so.6";

        /// <summary>Where the server has the window's origin on the root, read over a connection of its own.</summary>
        public static (int X, int Y) RootPositionOf(nuint window)
        {
            nint display = XOpenDisplay(0);
            Assert.AreNotEqual((nint)0, display, "a second X connection could not be opened");
            try
            {
                _ = XTranslateCoordinates(display, window, XDefaultRootWindow(display), 0, 0, out int rootX, out int rootY, out _);
                return (rootX, rootY);
            }
            finally
            {
                _ = XCloseDisplay(display);
            }
        }

        [DllImport(LIB)] private static extern nint XOpenDisplay(nint displayName);
        [DllImport(LIB)] private static extern int XCloseDisplay(nint display);
        [DllImport(LIB)] private static extern nuint XDefaultRootWindow(nint display);
        [DllImport(LIB)]
        private static extern int XTranslateCoordinates(nint display, nuint source, nuint destination, int sourceX, int sourceY,
            out int destinationX, out int destinationY, out nuint child);
    }
}
