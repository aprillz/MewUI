# NativeAOT size tools

MewUI treats pay-for-play NativeAOT output as a framework contract. Adding a feature must not silently make unrelated applications carry its implementation, constructed generic types, or metadata.

## Canonical probes

| Probe | Content | Purpose |
|---|---|---|
| Empty | `Window` only | Minimum platform, backend, application, and window graph |
| Text | One `TextBlock` | Text layout and rendering increment |
| Button | One empty `Button` | Control, style, input, and command increment without text content |
| Image | One raw-pixel `Image` | Image control and raster-source increment without encoded-image I/O |

The primary regression lane is Windows x64 with GDI. All probes use .NET 10, self-contained NativeAOT, full trimming, size optimization, invariant globalization, no debug symbols, and an ILC map. Do not compare results produced by different SDKs, runtime identifiers, backends, or publish properties.

## Architectural rules

1. `Application`, `Window`, graphics backends, and disposal paths must not directly construct optional features.
2. Registries must not eagerly enumerate every optional control or implementation.
3. Default and named styles must preserve per-control reachability.
4. Public interface and virtual members must be audited in the NativeAOT map.
5. A service abstraction is pay-for-play only when its concrete creation path is removable.
6. Size changes require executable A/B measurements and map evidence.

Each styled control owns its default-style registration in its own source declaration. Keep style factories `internal`, add an explicit static constructor when needed, and never rebuild a central factory table.

## Baselines

The committed JSON is a regression baseline, not a value to refresh after unexplained growth. A baseline change requires old and new reports, changed-probe map comparison, a concrete explanation, and explicit review of any raised budget.

The current SDK 10.0.301 Windows x64/GDI probes measure Empty at 3,022,336 bytes, Text at 3,412,480 bytes, Button at 3,479,040 bytes, and Image at 3,433,472 bytes. The Empty map retains no tracing types, LibJpeg, built-in encoded-image decoder, file-dialog style, optional default-style, or panel implementation methods.

## Commands

Run the four canonical probes on Windows GDI:

```powershell
./tools/aot-size/Measure-AotSize.ps1
```

Check growth against the committed observation baseline:

```powershell
./tools/aot-size/Measure-AotSize.ps1 `
  -BaselinePath ./tools/aot-size/baselines/win-x64-gdi.json
```

Compare two NativeAOT maps:

```powershell
./tools/aot-size/Compare-AotMaps.ps1 `
  -BaselineMap path/to/baseline.map.xml `
  -CurrentMap path/to/current.map.xml
```

Generated executables, maps, and reports are written below `.artifacts/aot-size/`.

Before a release, update `release-sizes.json` from matching Hello World and Gallery publishes and run:

```powershell
./tools/aot-size/Update-ReleaseSizeAssets.ps1
```

Commit the JSON and generated SVG together. README links stay unchanged. CI uses `-Check` to reject stale generated output.
