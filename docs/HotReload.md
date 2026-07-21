# Hot Reload

This document explains **Hot Reload** for MewUI apps written in C# markup. When you edit code,
`dotnet watch` (or your IDE's Hot Reload) applies the change to the running app, and MewUI rebuilds
**only the nodes whose build code actually changed**.

Hot Reload works only in debug (JIT) sessions. It is fully disabled in release/NativeAOT builds.

---

## 1. Enable (no setup required)

MewUI registers the Hot Reload handler in its own assembly, so **your app needs no declaration.**
Just run under `dotnet watch`:

```
dotnet watch run
```

> The old `#if DEBUG [assembly: MetadataUpdateHandler(...)]` manual declaration is no longer needed.
> If you still have it, remove it to avoid a duplicate registration.

### Opting out

Disable Hot Reload from your project with:

```xml
<PropertyGroup>
  <MewUIHotReload>false</MewUIHotReload>
</PropertyGroup>
```

---

## 2. What each edit does, and how it works

What you edit determines **how much is rebuilt** and **which state survives**.

| What you edit | When it shows | Rebuild scope | State preserved |
| --- | --- | --- | --- |
| Window `.OnBuild(...)` / UserControl `OnBuild()` body | on save | that one window/control | everything **outside** that node (siblings, other windows) |
| Event handler body (`.OnClick(...)`, etc.) | on next event | **none** | **all** state |
| Item template build function (`ItemTemplate`, etc.) | on save | the control using that template | everything outside that control |
| Default style definition | on next rebuild | (rides along a rebuild) | — |
| Signature / structural change (rude edit) | after app restart | everything | none |

### Why it works this way

**1) Change detection is per build-function body, not per type.**
The runtime only tells us *which types* changed. But in C# markup, build code and event handlers
usually live in the same type (same file), so looking at types alone would treat a handler edit as a
build change. Instead, MewUI compares whether **the registered build function's body actually
changed**. It ignores compiler noise (metadata-token reshuffling and the like), so editing only a
handler inside the same type does not change the build function's body and does not cause a rebuild.

**2) Editing a handler needs no rebuild.**
.NET replaces the method body in place. The event delegate wired up during the build is still there,
and the next event runs the replaced body. So the window is not rebuilt, and in-progress input,
scroll position, and selection are preserved.

**3) Only the shallowest affected node is rebuilt.**
When several nodes' build functions changed, rebuilding an ancestor re-creates its children, so only
the ancestor is rebuilt. Unchanged parts and other windows are left alone.

The same principles imply these limits:

- If a build function only calls a **helper in another type** and you edit just that helper, the build
  function's body is unchanged and the edit may not be detected. Edit the build function (or code in
  the same type) to apply it.
- Signature, base-type, or structural changes cannot be applied as a delta by the runtime (a rude
  edit) and require a restart.

---

## 3. Registering build code

Hot Reload rebuilds nodes whose build function is registered. The following two are registered
automatically:

- **A Window's `.OnBuild(...)`**: registers the callback and runs the initial build.
- **A UserControl's `OnBuild()` override**: registered when the control builds its content.

```csharp
var window = new Window()
    .OnBuild(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text("Edit code and save."),
                new Button()
                    .Content("Click")
                    .OnClick(() => Console.WriteLine("clicked"))   // editing this body -> no rebuild
            )));
```

Changing the `TextBlock` text rebuilds the window; changing the `OnClick` body applies on the next
click with no rebuild.

---

## 4. Full example

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Markup;
using Aprillz.MewUI.Controls;

var window = new Window()
    .OnBuild(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text($"Now: {DateTime.Now}"),
                new Button().Content("Click")
            )));

Application.Create()
    .UseWin32()
    .UseDirect2D()
    .Run(window);
```

Run with `dotnet watch run`, then edit the `TextBlock` text to see the change applied immediately.

---

## 5. Notes

- **State inside a rebuilt node is reset.** Keep state you need to preserve in a view-model/service
  outside the build function. (Handler edits do not rebuild, so their state is preserved.)
- Hot Reload works only in debug (JIT); it does nothing in release/NativeAOT builds.
- Changes that arrive before an `Application`/`Dispatcher` exists are ignored.
