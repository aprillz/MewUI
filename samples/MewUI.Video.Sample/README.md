# MewUI.Video.Sample

## FFmpeg dependency setup

This sample uses:

1. `FFmpeg.AutoGen` for managed bindings
2. `FFmpeg.AutoGen.Bindings.DynamicallyLoaded` for runtime loading
3. FFmpeg native DLLs downloaded with the upstream-style PowerShell script

## Recommended setup

From the sample project directory, run:

```powershell
.\FFmpeg\download-ffmpeg.ps1 -Version 7.1
```

This downloads a pinned Gyan shared build and places files here:

```text
MewUI.Video.Sample/
  FFmpeg/
    bin/
      x64/
        avcodec-61.dll
        avformat-61.dll
        avutil-59.dll
        swscale-8.dll
    include/
```

The project copies `FFmpeg/bin/x64/*.dll` into the build output, and the sample looks for native libraries in `FFmpeg/bin/x64` first.

## Why this path

This matches the layout used by the upstream `FFmpeg.AutoGen` download script pattern and avoids depending on a globally installed `ffmpeg.exe` alone.

## Notes

- Use `-Version 7.1` to stay aligned with the current `FFmpeg.AutoGen` package version in this sample.
- `ffmpeg.exe` being available on `PATH` does not guarantee the sample app can resolve the required FFmpeg DLL set correctly.
- If you need to refresh binaries, run the script again with `-Force`.

## macOS

Install FFmpeg via Homebrew:

```bash
brew install ffmpeg@7
brew link --overwrite --force ffmpeg@7
```

The sample searches `/opt/homebrew/lib` (Apple Silicon) and `/usr/local/lib` (Intel) automatically. Hardware-accelerated decode (D3D11VA / WGL_NV_DX_interop / Direct2D DXGI interop) is Windows-only — macOS falls back to software decode + CPU upload via the MewVG (Metal) backend.

Build/run:

```bash
dotnet run --project samples/MewUI.Video.Sample -r osx-arm64 -- path/to/video.mp4
```

## Linux (X11)

Install via your distro's package manager:

```bash
# Debian/Ubuntu
sudo apt install libavcodec-dev libavformat-dev libavutil-dev libswscale-dev
```

The sample searches `/usr/lib/x86_64-linux-gnu`, `/usr/lib/aarch64-linux-gnu`, `/usr/lib64`, `/usr/lib` automatically. Software decode + CPU upload via the MewVG (X11 GL) backend.