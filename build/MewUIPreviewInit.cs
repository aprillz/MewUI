// Injected into app compilations only during editor preview sessions (see MewUIPreview.targets).
// Runs before Main, so the preview bootstrap can intercept platform host resolution. The preview
// assembly is loaded by path: package-layout sessions copy it beside the app without a deps.json
// entry (it must never become a package dependency), so a static reference would not resolve.
namespace Aprillz.MewUI.Preview.Generated;

internal static class MewUIPreviewInit
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Aprillz.MewUI.Preview.dll");
        if (!System.IO.File.Exists(path))
        {
            System.Console.Error.WriteLine($"[mewui-preview] previewer assembly missing: {path}");
            return;
        }

        System.Reflection.Assembly.LoadFrom(path)
            .GetType("Aprillz.MewUI.Preview.PreviewBootstrap")?
            .GetMethod("TryRegister")?
            .Invoke(null, null);
    }
}
