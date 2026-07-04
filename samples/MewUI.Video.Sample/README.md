# MewUI.Video.Sample

Cross-platform video playback demo built on top of `FFmpeg.AutoGen` 8.0.x with backend-specific zero-copy paths.

## Decode + present matrix

| Platform | HW decode | Zero-copy present | CPU fallback present |
|---|---|---|---|
| **Windows** | D3D11VA → NV12 in D3D11 texture | D3D11 VideoProcessor (NV12→BGRA) + Direct2D `IDXGISurface` interop, or WGL_NV_DX_interop for MewVG | sws_scale BGRA + sync upload |
| **macOS** | VideoToolbox → CVPixelBuffer (NV12/BGRA, IOSurface-backed) | `CVMetalTextureCache` → `MTLTexture` sampled by MewVG Metal | sws_scale + Metal upload |
| **Linux X11** | VAAPI primary, **CUDA NVDEC fallback** when VAAPI fails | VAAPI `scale_vaapi` (NV12→BGRA, GPU) → DRM_PRIME dma_buf → `EGL_LINUX_DMA_BUF_EXT` GL import | sws_scale + GL upload (PBO async) |
| **WSL2 + NVIDIA** | CUDA NVDEC (VAAPI unavailable through `/dev/dri/renderD128` shim) | software readback to NV12 → sws BGRA | same as Linux |

## FFmpeg dependency

The sample uses:
- `FFmpeg.AutoGen` 8.0.0.1 - managed bindings
- `FFmpeg.AutoGen.Bindings.DynamicallyLoaded` - runtime resolution

It searches the runtime DLLs in this order (per platform):

```
Windows: FFmpeg/bin/x64/*.dll → %PATH%
macOS:   /opt/homebrew/lib → /usr/local/lib
Linux:   /opt/ffmpeg-8.0.1 → /opt/ffmpeg → /usr/local → /usr/lib/x86_64-linux-gnu → /usr/lib64 → /usr/lib
```

Library names follow the FFmpeg 8 ABI: `avcodec-62`, `avformat-62`, `avutil-60`, `swscale-9` (or platform suffix `.so`/`.dylib`).

## Windows

From the sample directory:
```powershell
.\FFmpeg\download-ffmpeg.ps1
```

Downloads a pinned Gyan shared build (FFmpeg 8.0 by default) and extracts to `FFmpeg/bin/x64/` + `FFmpeg/include/`. The project copies `FFmpeg/bin/x64/*.dll` to the build output; the sample resolves them at startup before searching `%PATH%`.

Refresh: `.\FFmpeg\download-ffmpeg.ps1 -Force` (or `-Version 8.1` etc).

Backends:
- **Direct2D** (default) - D3D11 hardware decode + DXGI surface interop
- **MewVG.Win32** (`--vg`) - D3D11 decode + WGL_NV_DX_interop GL sample
- **GDI** (`--gdi`) - software present, CPU readback

## macOS

```bash
brew install ffmpeg@8
brew link --overwrite --force ffmpeg@8
dotnet run --project samples/MewUI.Video.Sample -r osx-arm64 -- path/to/video.mp4
```

VideoToolbox HW decode + Metal zero-copy via `CVMetalTextureCache`. No D3D paths apply.

## Linux (X11)

Distro FFmpeg works for most cases:
```bash
sudo apt install libavcodec-dev libavformat-dev libavutil-dev libswscale-dev
```

VAAPI HW decode + `scale_vaapi` filter for in-GPU NV12→BGRA + dma_buf zero-copy GL import.

## WSL2 with NVIDIA dGPU

VAAPI is **not usable** in WSL - `/dev/dri/renderD128` is a dxg shim and `vaInitialize` fails. The decoder falls back to CUDA NVDEC, but only when FFmpeg is built with `--enable-cuda-llvm --enable-cuvid --enable-nvdec`. Distro FFmpeg lacks these.

Custom build steps:
```bash
git clone --depth 1 https://github.com/FFmpeg/nv-codec-headers.git ~/build/nv-codec-headers
cd ~/build/nv-codec-headers && sudo make install

git clone --depth 1 --branch n8.0.1 https://git.ffmpeg.org/ffmpeg.git ~/build/ffmpeg-8.0.1
cd ~/build/ffmpeg-8.0.1
./configure --prefix=/opt/ffmpeg-8.0.1 --enable-shared --disable-static \
    --enable-nonfree --enable-cuda-llvm --enable-cuvid --enable-nvdec --enable-nvenc
make -j$(nproc) && sudo make install
```

Place the custom prefix at `/opt/ffmpeg-8.0.1` so the sample's library search finds it before the distro version.

Verify NVDEC active in the diagnostics overlay:
- `Decode path in use: hardware-backed, frameFormat=AV_PIX_FMT_CUDA`
- `decode: cuda/nvdec`

## Hardware decode fallback chain

`VideoDecoder.TryEnableHardwareDecode` probes in order:
- **Windows** → D3D11VA
- **macOS** → VideoToolbox
- **Linux** → VAAPI → CUDA NVDEC (when VAAPI fails)

If all fail the decoder runs in software (`AV_PIX_FMT_YUV420P` → sws_scale → BGRA → CPU upload).
