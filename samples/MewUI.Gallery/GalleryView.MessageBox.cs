using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement MessageBoxPage()
    {
        FrameworkElement PromptSample(string title, Func<Task<string>> showFunc)
        {
            var status = new ObservableValue<string>("Result: -");

            return Card(
                title,
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content($"Show")
                            .OnClick(async () =>
                            {
                                status.Value = await showFunc();
                            }),
                        new Label().BindText(status).FontSize(11)
                    )
            );
        }

        return CardGrid(
            PromptSample("Info (NotifyAsync)", async () =>
            {
                await MessageBox.NotifyAsync("This is an Info message box sample.", PromptIconKind.Info, owner: window);
                return "Result: closed";
            }),

            PromptSample("Warning (ConfirmAsync + Detail)", async () =>
            {
                var result = await MessageBox.ConfirmAsync(
                    "This is a Warning message box sample.",
                    icon: PromptIconKind.Warning,
                    detail: "System.InvalidOperationException: The operation failed.\n   at App.Module.Process() in Module.cs:line 42\n   at App.Main() in Program.cs:line 10\n\nThis is a multiline detail text that can be scrolled if the content is too long.",
                    owner: window);
                return $"Result: {result}";
            }),

            PromptSample("Error (AskYesNoAsync + Detail)", async () =>
            {
                var result = await MessageBox.AskYesNoAsync(
                    "A critical error occurred while saving the file.\nWould you like to retry?",
                    icon: PromptIconKind.Error,
                    detail: "A critical error occurred while saving the file.\nWould you like to retry?",
                    owner: window);
                return $"Result: {result}";
            }),

            PromptSample("Question (AskYesNoCancelAsync)", async () =>
            {
                var result = await MessageBox.AskYesNoCancelAsync(
                    "This is a Question message box sample.",
                    owner: window);
                return $"Result: {result}";
            }),

            PromptSample("Success (NotifyAsync + Detail)", async () =>
            {
                await MessageBox.NotifyAsync(
                    "Build completed successfully.",
                    PromptIconKind.Success,
                    detail: "Output: bin/Release/net8.0/MyApp.dll\nTime: 2.3s\nWarnings: 0\nErrors: 0",
                    owner: window);
                return "Result: closed";
            }),

            PromptSample("Shield (PromptAsync)", async () =>
            {
                var result = await MessageBox.PromptAsync(new MessageBoxOptions
                {
                    Message = "Connection to server timed out after 30 seconds.",
                    Icon = PromptIconKind.Shield,
                    Buttons = [new("Retry", MessageButtonRole.Accept), new("Ignore", MessageButtonRole.Destructive), new("Abort", MessageButtonRole.Reject)],
                    Detail = "Host: api.example.com:443\nAttempts: 3/3",
                    Owner = window
                });
                return $"Result: {result}";
            }),

            PromptSample("Crash (NotifyAsync + StackTrace)", async () =>
            {
                await MessageBox.NotifyAsync(
                    "An unhandled exception has occurred.",
                    PromptIconKind.Crash,
                    "System.NullReferenceException: Object reference not set to an instance of an object.\n"
                        + "   at Aprillz.MewUI.Controls.GridView.OnRender(IGraphicsContext context) in E:\\src\\MewUI\\Controls\\GridView.cs:line 387\n"
                        + "   at Aprillz.MewUI.Controls.Control.Render(IGraphicsContext context) in E:\\src\\MewUI\\Controls\\Control.cs:line 142\n"
                        + "   at Aprillz.MewUI.Elements.UIElement.RenderSubtree(IGraphicsContext context) in E:\\src\\MewUI\\Elements\\UIElement.cs:line 298\n"
                        + "   at Aprillz.MewUI.Controls.Window.RenderFrame(IGraphicsContext context) in E:\\src\\MewUI\\Controls\\Window.cs:line 510\n"
                        + "   at Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsContext.EndDraw() in E:\\src\\MewUI\\Rendering\\Direct2D\\Direct2DGraphicsContext.cs:line 89\n"
                        + "\n"
                        + "--- Inner Exception ---\n"
                        + "System.InvalidOperationException: Collection was modified; enumeration operation may not execute.\n"
                        + "   at System.Collections.Generic.List`1.Enumerator.MoveNext()\n"
                        + "   at Aprillz.MewUI.Controls.GridViewCore.BuildVisibleRows() in E:\\src\\MewUI\\Controls\\GridView.cs:line 215\n"
                        + "   at Aprillz.MewUI.Controls.GridViewCore.EnsureLayout() in E:\\src\\MewUI\\Controls\\GridView.cs:line 198",
                    owner: window
                );
                return $"Result: Closed";
            }),

            Card(
                "Native",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .Content("OK")
                            .OnClick(() => NativeMessageBox.Show("This is a native OK message box.", "MewUI Gallery")),
                        new Button()
                            .Content("OK / Cancel")
                            .OnClick(() => NativeMessageBox.Show("Do you want to continue?", "MewUI Gallery", NativeMessageBoxButtons.OkCancel, NativeMessageBoxIcon.Question)),
                        new Button()
                            .Content("Yes / No")
                            .OnClick(() => NativeMessageBox.Show("Are you sure?", "MewUI Gallery", NativeMessageBoxButtons.YesNo, NativeMessageBoxIcon.Warning)),
                        new Button()
                            .Content("Yes / No / Cancel")
                            .OnClick(() => NativeMessageBox.Show("Save changes before closing?", "MewUI Gallery", NativeMessageBoxButtons.YesNoCancel, NativeMessageBoxIcon.Information))
                    )
            )
        );
    }
}
