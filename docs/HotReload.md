# Hot Reload

This document explains the **opt-in Hot Reload flow** for MewUI apps using C# markup.
Hot Reload rebuilds **window** content by re-running a registered build callback.

---

## 1. Enable Hot Reload (app assembly)

Add a MetadataUpdateHandler in your app assembly (DEBUG only):

```csharp
#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(
    typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif
```

You can place this in `Program.cs` or a dedicated `AssemblyInfo.cs`.

---

## 2. Register a build callback

Hot Reload only rebuilds **windows** that have a build callback.
The C# markup helper `OnBuild(...)` sets it and also runs the initial build.

```csharp
var window = new Window()
    .OnBuild(w =>
    {
        w.Title("Hot Reload Demo");
        w.Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text("Edit code and Hot Reload."),
                new Button().Content("Click")
            ));
    });
```

---

## 3. Full example

```csharp
#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(
    typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif

using Aprillz.MewUI;
using Aprillz.MewUI.Markup;
using Aprillz.MewUI.Controls;

// 1) Register a build callback (DateTime updates after code change + Hot Reload)
var window = new Window()
    .OnBuild(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text($"Now: {DateTime.Now}"),
                new Button().Content("Click")
            )));

// 2) Select platform/backend and run
Application.Create()
    .UseWin32()
    .UseDirect2D()
    .Run(window);
```

---

## 4. Notes

- Hot Reload is **opt-in**. No build callback = no reload.
- The rebuild runs on the UI thread and simply re-invokes your build callback.
- Preserve state in your own view-model or services if needed.
- Hot Reload is **not supported on NativeAOT**.
- You can trigger a rebuild manually via `MewUiHotReload.RequestReload()`.
