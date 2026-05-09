using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept
{
    internal class ImageResourceTest
    {
        private static void Run()
        {
            var imageFolder = @"E:\97. Map\MapOutput\NewMap\5";
            var statusText = new ObservableValue<string>("Loading...");
            UniformGrid imagePanel = null!;

            var root = new Window()
                .Resizable(1200, 800)
                .OnBuild(x => x
                    .Title("Async Image Loader")
                    .Content(
                        new DockPanel()
                            .Margin(8)
                            .Spacing(8)
                            .Children(
                                new StackPanel()
                                    .Horizontal()
                                    .Spacing(8)
                                    .DockTop()
                                    .Children(
                                        new Label()
                                            .BindText(statusText),

                                        new Button()
                                            .Content("Load")
                                            .OnClick(() => LoadImagesAsync(imageFolder)),

                                        new Button()
                                            .Content("Clear")
                                            .OnClick(() =>
                                            {
                                                imagePanel.Clear();
                                                GC.Collect();
                                                GC.WaitForPendingFinalizers();
                                                GC.Collect();
                                            })
                                    ),

                                new ScrollViewer()
                                    .VerticalScroll(ScrollMode.Auto)
                                    .Content(
                                        new UniformGrid()
                                            .Ref(out imagePanel)
                                            .Left()
                                            .Columns(8)
                                            .Spacing(4)
                                    )
                            )
                    )
                );

            Application.Run(root);

            async void LoadImagesAsync(string folder)
            {
                var files = Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories)
                    .OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var n) ? n : int.MaxValue)
                    .ToArray();

                var images = new Image[files.Length];
                for (int i = 0; i < files.Length; i++)
                {
                    var img = new Image()
                        .Size(64, 64)
                        .StretchMode(Stretch.Uniform);
                    images[i] = img;
                    imagePanel.Add(img);
                }

                statusText.Value = $"Loading 0 / {files.Length}...";

                int loaded = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    var index = i;
                    var source = await Task.Run(() =>
                    {
                        var src = ImageSource.FromBytes(File.ReadAllBytes(files[index]));
                        src.EnsureDecode(); // Important
                        return src;
                    });

                    images[index].Source = source;

                    loaded++;
                    if (loaded % 10 == 0)
                    {
                        statusText.Value = $"Loading {loaded} / {files.Length}...";
                    }
                }

                statusText.Value = $"Done. {files.Length} images loaded.";
            }
        }
    }
}
