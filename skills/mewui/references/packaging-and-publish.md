# Choose packages and publish

## Core platform packages

| Purpose | Package |
| --- | --- |
| All platforms and backends | `Aprillz.MewUI` |
| Windows | `Aprillz.MewUI.Windows` |
| Linux | `Aprillz.MewUI.Linux` |
| macOS | `Aprillz.MewUI.MacOS` |

Use a platform metapackage for an ordinary application. Add individual platform or backend packages only when the user intentionally controls the dependency graph.

## Optional packages

| Feature | Package |
| --- | --- |
| Docking | `Aprillz.MewUI.MewDock` |
| SVG | `Aprillz.MewUI.Svg` |
| Skia canvas | `Aprillz.MewUI.Skia` or a platform Skia metapackage |
| Charts | `Aprillz.MewUI.MewCharts` |
| WebView2 on Windows | `Aprillz.MewUI.WebView2.Win32` |

Verify that an optional package exists at the selected MewUI version before adding it. Do not add every extension preemptively.

## Choose a rendering backend

Platform registration and rendering registration are separate calls. Register exactly one
platform and one compatible backend in startup code:

| Target | Startup registration | Publish selection |
| --- | --- | --- |
| Windows Direct2D | `.UseWin32().UseDirect2D()` | `MewUIBackend=Direct2D` |
| Windows GDI | `.UseWin32().UseGdi()` | `MewUIBackend=Gdi` |
| Windows MewVG | `.UseWin32().UseMewVGWin32()` | `MewUIBackend=MewVG` |
| Linux X11 | `.UseX11().UseMewVGX11()` | no `MewUIBackend` value |
| macOS Metal | `.UseMacOS().UseMewVGMetal()` | no `MewUIBackend` value |

For a new Windows application, use Direct2D unless the task establishes a reason to
choose another backend. Choose GDI only for a specific GDI compatibility requirement.
Choose MewVG when the application intentionally uses the MewVG rendering stack or needs
the same renderer family as its Linux and macOS targets. Treat backend performance and
visual equivalence as workload-specific; measure the actual application before changing
a working backend.

`MewUIBackend` is a publish-time filter supplied by the Windows metapackage. It removes
the two unselected backend assemblies from publish output. It does not change startup
registration. If the property is omitted, the Windows metapackage publishes all included
backends. Therefore these three things must agree:

1. the platform metapackage or individual backend package;
2. the startup `Use...` calls;
3. the Windows `MewUIBackend` publish value.

After publishing, inspect `Aprillz.MewUI.Backend.*` files. An unknown or misspelled
filter value may fail to remove unwanted backends, so file inspection is part of
verification.

## Publish once from the command line

Use the runtime identifier and Windows backend matching the application:

```text
dotnet publish -c Release -r win-x64 --self-contained true -p:MewUIBackend=Direct2D
dotnet publish -c Release -r win-x64 --self-contained true -p:MewUIBackend=Gdi
dotnet publish -c Release -r win-x64 --self-contained true -p:MewUIBackend=MewVG
dotnet publish -c Release -r linux-x64 --self-contained true
dotnet publish -c Release -r osx-arm64 --self-contained true
```

Run the published application on its target operating system. A normal build or a
publish performed for another runtime does not verify target behavior.

## Keep repeatable NativeAOT settings in a publish profile

Once the command-line publish works, put repeatable settings in
`Properties/PublishProfiles/<name>.pubxml`. This example is a size-oriented Windows
Direct2D profile:

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishAot>true</PublishAot>
    <TrimMode>full</TrimMode>
    <OptimizationPreference>Size</OptimizationPreference>
    <MewUIBackend>Direct2D</MewUIBackend>
    <DebugType>none</DebugType>
    <DebugSymbols>false</DebugSymbols>
    <StripSymbols>true</StripSymbols>
  </PropertyGroup>
</Project>
```

Match `TargetFramework` to the application. For a single-target project, run:

```text
dotnet publish -p:PublishProfile=win-x64-aot
```

For a project that declares `TargetFrameworks`, also select the framework on the command line;
MSBuild's outer cross-targeting build does not choose it from the profile:

```text
dotnet publish -f net10.0 -p:PublishProfile=win-x64-aot
```

For Windows GDI or MewVG, change both startup registration and `MewUIBackend`. For
Linux or macOS, copy the profile, change the runtime identifier, remove
`MewUIBackend`, and publish on a compatible target runner. Do not assume NativeAOT
cross-compilation is available for every host and target pair.

## Apply size options deliberately

`PublishAot=true` and an actual publish are the compatibility gate. The remaining
properties are deployment choices:

- `TrimMode=full` enables full trimming and requires all optional packages and application code to be trim-safe.
- `OptimizationPreference=Size` asks NativeAOT to prefer smaller code over peak throughput. Compare startup, interaction latency, rendering throughput, and output size before keeping it.
- `DebugType=none`, `DebugSymbols=false`, and `StripSymbols=true` reduce release artifacts but make native crash diagnosis harder. Keep a diagnostic profile or retain symbols privately when supportability matters.
- `InvariantGlobalization=true` can reduce size but changes culture-sensitive formatting, parsing, collation, and resource behavior. Add it only after testing every supported locale; it is intentionally absent from the baseline profile above.
- Do not suppress trimming or AOT warnings globally. Identify the application or optional package producing each warning and verify the affected path.

Typed binding and templates are designed for AOT. Reflection, dynamic assembly loading,
untyped property paths, runtime code generation, and some third-party native libraries
need separate analysis. Re-run the exact publish profile whenever optional packages,
resources, serialization, interop, or backend selection changes.

## Publish verification

For each profile:

1. publish from a clean restore with warnings visible;
2. confirm the expected platform and backend files and absence of unselected backends;
3. record executable and total publish-directory size;
4. launch the published executable, not the build output;
5. exercise binding, templates, resources, dialogs, text, images, and application-specific interop;
6. repeat on every supported RID and keep unexecuted targets marked as unverified.
