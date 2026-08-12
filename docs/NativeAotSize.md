# NativeAOT size discipline

MewUI treats pay-for-play NativeAOT output as a framework contract. Adding a feature must not silently make unrelated applications carry its implementation, constructed generic types, or metadata.

## Canonical probes

All comparisons use the same SDK and publish properties. The checked-in probe project defines four cumulative reachability tests:

| Probe | Content | Purpose |
|---|---|---|
| Empty | `Window` only | Minimum platform, backend, application, and window graph |
| Text | One `TextBlock` | Text layout and rendering increment |
| Button | One empty `Button` | Control, style, input, and command increment without text content |
| Image | One raw-pixel `Image` | Image control and raster-source increment without encoded-image I/O |

The primary regression lane is Windows x64 with GDI. Direct2D, MewVG, Linux x64, and macOS arm64 use the same probes for release audits.

## Publish contract

- Target framework: `net10.0`
- Self-contained NativeAOT
- `TrimMode=full`
- `IlcOptimizationPreference=Size`
- Invariant globalization
- No debug symbols or NativeAOT PDB
- Main executable size uses bytes; displayed MiB uses binary units
- `IlcGenerateMapFile=true` for reachability analysis

Do not compare results produced by different SDKs, runtime identifiers, backends, or publish properties as source regressions.

## What is measured

Executable bytes are the release-facing metric. A map audit explains changes using at least:

- `MethodCode`
- `ConstructedEEType`
- embedded metadata
- method and constructed-type counts
- newly reachable large methods and types

An unused control may retain minimal type metadata while its behavior is trimmed. A regression exists when an unrelated probe starts retaining substantial implementation code, generic instantiations, or metadata.

## Architectural rules

1. `Application`, `Window`, graphics backends, and their disposal paths must not directly construct optional feature implementations.
2. A process-wide registry must not eagerly enumerate every optional control or implementation merely to provide lazy lookup.
3. Default styles and templates should preserve per-control reachability. Adding one style must not root all controls.
4. Public interface and virtual members must be audited in the NativeAOT map; an unused-looking member can keep its implementation reachable through dispatch.
5. Feature-service abstractions count only when the concrete creation path is also removable. Wrapping a direct constructor in a generic dictionary is not pay-for-play.
6. Size optimizations require an A/B executable measurement and map evidence. Source size or IL inspection alone is insufficient.

### Default-style registration pattern

Each styled control owns its default-style factory registration directly in the control's source declaration and has an explicit static constructor. `DefaultStyles` stores only factories registered by reachable control types; it must never rebuild a central table that references every control.

Framework named styles are also lazy. Their factory explicitly ensures only the default style it derives from when the named style is first requested. Public `Style.ForType(Type)` and `Style.DeriveFromDefault<T>()` preserve dynamic scenarios by running the requested control hierarchy's retained static initializers.

When adding a default-styled control:

1. add the registration at the top of the control declaration;
2. add an explicit static constructor when the control does not already have one;
3. keep its style factory `internal` and do not add it to a central factory list;
4. add a probe or map audit if the control introduces a substantial optional subsystem.

## Baselines and budgets

The committed JSON is a pay-for-play regression baseline and must not be silently refreshed after a regression.

The initial canonical Windows GDI probes measured Empty, Text, and Button at the same 4,291,584-byte executable size. Text added only 139 bytes of `MethodCode` over Empty, while Button added no executable-size increment. Image added 55,808 bytes. Equal Empty/Text/Button results are evidence that those optional graphs are already reachable from Empty, not evidence that they are free.

After moving default-style registration to the owning control types, the same SDK 10.0.301 probes measure Empty at 3,409,408 bytes, Text at 3,788,800 bytes, Button at 3,844,608 bytes, and Image at 3,465,728 bytes. Empty is 882,176 bytes (20.6%) smaller than the central-registry baseline. Text now has an explicit 379,392-byte increment over Empty, so the text engine is pay-for-play again.

After also moving framework named styles to their consuming controls, removing encoded-image decoding from backend dispatch slots, moving window-icon decoding behind `IconSource`, and making text-service disposal conditional on creation, the probes measure Empty at 3,037,184 bytes, Text at 3,425,792 bytes, Button at 3,492,864 bytes, and Image at 3,448,320 bytes. The Empty map contains no LibJpeg, built-in image decoder, file-dialog style, optional default-style, or panel implementation methods.

Platform tracing is Debug-only. Release and NativeAOT builds do not compile `TracingPlatformHost` or its backend/dispatcher wrappers. After making control-owned registration initialization explicit and text-service creation race-safe, the probes measure Empty at 3,022,336 bytes, Text at 3,412,480 bytes, Button at 3,479,040 bytes, and Image at 3,433,472 bytes, with no tracing types in their maps.

The July 5 reference commit `df7f5b2a` produces a 2,987,008-byte Empty GDI executable when rebuilt with SDK 10.0.301. The current investigation measured 4,290,560 bytes, an increase of 1,303,552 bytes. Removing only `ManagedTextEngine` and `ManagedTextRenderContext` in a temporary measurement build saved 390,144 bytes; 913,408 bytes remained attributable to other newly reachable code and type graphs. These figures are investigation evidence, not a supported text-disable mode.

Baseline changes require:

1. the old and new reports;
2. map comparison for the changed probes;
3. an explanation assigning the growth to a requested feature or toolchain change;
4. explicit review of any raised budget.

## Commands

```powershell
./tools/aot-size/Measure-AotSize.ps1

./tools/aot-size/Measure-AotSize.ps1 `
  -BaselinePath ./tools/aot-size/baselines/win-x64-gdi.json

./tools/aot-size/Update-ReleaseSizeAssets.ps1
```

The report, executable, and copied map are stored under `.artifacts/aot-size/`.
Before a release, update `release-sizes.json` from matching Hello World and Gallery publishes, run `Update-ReleaseSizeAssets.ps1`, and commit the JSON and generated SVG together. README links stay unchanged. Validation jobs use `-Check` to reject a stale SVG.
