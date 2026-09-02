---
name: mewui
description: Create, extend, debug, preview, or publish complete MewUI applications from public NuGet packages using fluent C# Markup. Use for project setup, Window and UserControl views, windowless lifecycle, Hot Reload, controls, layout, typed state and binding, collections, navigation, themes, dialogs, resources, custom controls, rendering backends, and NativeAOT. Do not use for MewUI framework implementation work or unrelated .NET UI frameworks.
---

# MewUI application development

Assume the agent has this installed skill and a consumer project, but no MewUI source checkout. Build with public `Aprillz.MewUI*` NuGet packages and public APIs only. When internet access is available, proactively use package-matched upstream examples for unfamiliar compound UI work after checking the package contract. Upstream source is secondary evidence, never a required local dependency.

For an existing application, preserve its compatible MewUI package line unless the task asks for an upgrade. For a new application, let NuGet select the current stable package and keep every `Aprillz.MewUI*` package on that same version. Do not pin this skill to a framework release or repository commit.

## Start here

For a new application, read [quickstart.md](references/quickstart.md) first and complete its package-only build before adding features. It contains the project command, complete startup code, expected result, and platform package map.

For an existing application, inspect its target framework, package versions, platform/backend registration, and current state ownership before editing it.

## Read by task

- Organize startup, state, reusable views, and window or windowless lifetime: [application-structure.md](references/application-structure.md)
- Use `OnBuild`, Hot Reload, and editor Preview safely: [views-hot-reload-and-preview.md](references/views-hot-reload-and-preview.md)
- Compose panels, forms, spacing, grids, and window sizing: [markup-and-layout.md](references/markup-and-layout.md)
- Add observable state, typed binding, conversion, and editing: [state-and-binding.md](references/state-and-binding.md)
- Build forms, buttons, selection controls, and tabs: [controls-and-interactions.md](references/controls-and-interactions.md)
- Handle focus, routed input, and drag-and-drop: [input-and-drag-drop.md](references/input-and-drag-drop.md)
- Display collections, typed templates, and navigation: [collections-and-navigation.md](references/collections-and-navigation.md)
- Connect reusable commands to menus, shortcuts, and toolbars: [commands-and-menus.md](references/commands-and-menus.md)
- Apply styles, theme values, and runtime theme switching: [styling-and-theme.md](references/styling-and-theme.md)
- Draw shapes, transform content, and animate content replacement: [graphics-and-transitions.md](references/graphics-and-transitions.md)
- Show prompts or modal windows, open files, load images, and run async work: [dialogs-resources-and-async.md](references/dialogs-resources-and-async.md)
- Create reusable application or library controls: [custom-controls.md](references/custom-controls.md)
- Select platform/extension packages and publish: [packaging-and-publish.md](references/packaging-and-publish.md)
- Confirm an uncertain public API or fluent overload from the restored package: [public-api-discovery.md](references/public-api-discovery.md)
- Locate package-matched upstream examples when online source inspection is useful: [upstream-source-map.md](references/upstream-source-map.md)
- Diagnose restore, startup, binding, build, and publish failures: [troubleshooting.md](references/troubleshooting.md)

Read only the references required for the requested application feature. The quickstart is mandatory only for a new project.

## Application rules

- Generate fluent C# Markup, not XAML.
- Use typed public APIs. Do not introduce `DataContext`, XAML binding strings, reflection-based property paths, `DependencyProperty`, or `StyledProperty`.
- Keep all `Aprillz.MewUI*` packages on one compatible version.
- Register exactly one platform and compatible rendering backend for each startup path.
- Keep long-lived binding state alive with its view or application state owner.
- Treat every `OnBuild` or fluent `Build` body as re-runnable development-time composition code.
- Prefer composition before deriving a custom control.
- Add optional packages only for requested features.
- Preserve an existing application's working initialization and structure unless the task requires migration.

## Completion workflow

1. Determine the target platform, framework, requested features, and existing package versions.
2. Create or update the project using the relevant complete recipe from this skill.
3. Restore packages before diagnosing missing APIs.
4. For uncertain APIs, follow the restored-package lookup rules in `public-api-discovery.md`, then compile against the selected package.
5. Build every affected target framework.
6. Run the application when the environment supports its platform and verify the requested interaction, not only window creation.
7. Run the actual publish command for trimming, NativeAOT, or runtime-specific requests.
8. Report package versions, changed files, commands, results, and platform behavior that remains unverified.

Do not claim success from code inspection alone. A new application is complete only when package restore and build succeed, and runtime behavior is checked when execution is available.
