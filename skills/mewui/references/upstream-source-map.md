# Use current upstream examples

Use official upstream source when internet access is available and a complete
package-level recipe needs additional composition context. The consumer project
must not reference or require a source checkout.

The source links in this document intentionally follow the official default
branch rather than a package version or commit hash. MewUI public API changes are
normally small between releases, so current Gallery composition is usually the
most useful starting point. The restored package API and a package-only compile
remain authoritative when they differ.

## Source map

| Task | Current official source | Reference points |
| --- | --- | --- |
| Startup and window shell | [Program.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/Program.cs), [GalleryView.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.cs) | Application builder, window creation, screen composition |
| Named views | [GalleryView.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.cs), [CustomWindowSample.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/CustomWindowSample.cs) | `UserControl.OnBuild`, reusable view ownership, one-shot window setup versus rebuildable composition |
| Panels and layout | [GalleryView.Panel.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Panel.cs), [GalleryView.Layout.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Layout.cs) | Panel choice, grid definitions, scrolling and sizing |
| Binding | [GalleryView.DataBinding.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.DataBinding.cs) | conversion validation, MewProperty, BindingPath, INPC, lifetime |
| Forms and selection | [GalleryView.Button.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Button.cs), [GalleryView.Input.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Input.cs), [GalleryView.Selection.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Selection.cs) | controls, editing, selection, tooltip and context menu |
| Input and drag-and-drop | [GalleryView.Input.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Input.cs), [GalleryView.DragDrop.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.DragDrop.cs) | focus, direct input, element and operating-system drops |
| Collections and navigation | [GalleryView.List.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.List.cs), [GalleryView.GridView.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.GridView.cs), [GalleryView.NavigationView.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.NavigationView.cs) | recycled templates, editable cells, sorting, navigation content |
| Commands and menus | [GalleryView.WindowMenu.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.WindowMenu.cs), [GalleryView.ToolBar.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.ToolBar.cs) | command scope, input-map scope, menus, toolbar groups and overflow |
| Styles and effects | [GalleryView.Styling.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Styling.cs), [GalleryView.Transitions.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Transitions.cs) | StyleSheet scope, BasedOn, Setter.Unset, transitions |
| Dialogs and resources | [GalleryView.MessageBox.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.MessageBox.cs), [GalleryView.FileDialog.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.FileDialog.cs), [GalleryView.ShowDialog.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.ShowDialog.cs), [GalleryView.Resources.cs](https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Gallery/GalleryView.Resources.cs) | owners, prompts, modal close, byte-backed images |

## Extraction rules

- Extract the public API interaction and its non-obvious lifetime or error rule, not the page's visual decoration.
- Remove page-card helpers, internal icon loaders, diagnostics, and host-specific resource download code.
- Confirm every referenced type and member in the restored package XML or assembly metadata.
- Rewrite the result as a self-contained consumer example and compile it with public `PackageReference` entries only.
- If current upstream source and the restored package disagree, adapt the recipe to the restored public package rather than unpublished source.
- `samples/MewUI.Gallery/TransformBox.cs` is a known sample-owned helper, not a public package control. Do not use `TransformBox` in package recipes.
