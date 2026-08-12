# NativeAOT size tools

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
See [NativeAOT size discipline](../../docs/NativeAotSize.md) for the measurement contract and architectural rules.
