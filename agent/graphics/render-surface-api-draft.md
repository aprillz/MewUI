# 렌더 Surface API 초안

## 목적

이 문서는 `render-surface-architecture.md`와 `surface-usage-inventory.md`에서 도출한 최종 구조를
실제 API 후보로 구체화한다.

아직 구현 계획이 아니라 계약 초안이다. 기존 `IGraphicsFactory`, `IRenderTarget`,
`IBitmapRenderTarget`을 즉시 제거하지 않고, adapter와 facade를 통해 점진적으로 이관하는 것을 전제로 한다.

## 설계 목표

- D2D/GDI/MewVG가 같은 surface 모델을 구현한다.
- CPU bitmap, GPU offscreen target, window backbuffer, filter intermediate, external texture를 구분한다.
- `CreateImageFromPixelSource`와 render target image view 생성을 분리한다.
- layered/composited present를 graphics factory가 아니라 presenter 책임으로 분리한다.
- worker render, async upload, deferred readback, command completion을 표현한다.
- 기존 호출자를 한 번에 깨지 않고 단계적으로 이관한다.

## 핵심 타입 관계

```text
IGraphicsServices
  |
  +-- IGraphicsResourceFactory
  +-- IRenderDevice
  +-- IImageFilterDevice
  +-- IExternalImageDevice
  +-- IWindowPresenter
  +-- IBackendScheduler

IRenderDevice
  |
  +-- CreateSurface(RenderSurfaceDescriptor)
  +-- CreateContext(IRenderSurface)
  +-- CreateImageView(IRenderSurface)
  +-- Copy / Resolve / Readback

IRenderSurface
  |
  +-- CPU bitmap surface
  +-- GPU offscreen surface
  +-- window backbuffer surface
  +-- filter intermediate surface
  +-- presenter intermediate surface

IExternalSampleSource
  |
  +-- video decoder texture
  +-- WGL/DX interop texture
  +-- VideoToolbox/Metal texture
  +-- VAAPI dma_buf/EGLImage texture
  +-- PBO async upload texture
```

## Surface descriptor

```csharp
public readonly record struct RenderSurfaceDescriptor(
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    RenderPixelFormat Format,
    SurfaceUsage Usage,
    SurfaceCapabilities RequiredCapabilities,
    SurfaceLifetimeHint LifetimeHint = SurfaceLifetimeHint.Frame,
    string? DebugName = null);
```

### 설명

- `PixelWidth` / `PixelHeight`
  - 실제 drawable pixel size
- `DpiScale`
  - DIP에서 pixel로 변환되는 scale
- `Format`
  - BGRA/RGBA/alpha/premul 여부를 명확히 한다.
- `Usage`
  - surface를 왜 만드는지 표현한다.
- `RequiredCapabilities`
  - caller가 반드시 필요로 하는 기능이다.
- `LifetimeHint`
  - frame-local, pooled, cached, external 등 lifetime 기대치를 표현한다.
- `DebugName`
  - backend diagnostics용이다.

## Pixel format

```csharp
public enum RenderPixelFormat
{
    Unknown = 0,
    Bgra8888,
    Bgra8888Premultiplied,
    Rgba8888,
    Rgba8888Premultiplied,
    Alpha8,
}
```

초기에는 기존 구현과 맞추기 위해 `Bgra8888` / `Bgra8888Premultiplied` 중심으로 시작한다.
YUV/NV12는 render surface format으로 바로 넣기보다 external sample source의 format/plane metadata로
다루는 편이 안전하다.

## Surface usage

```csharp
[Flags]
public enum SurfaceUsage
{
    None = 0,

    WindowBackBuffer = 1 << 0,
    Offscreen = 1 << 1,
    ImageSource = 1 << 2,
    FilterIntermediate = 1 << 3,
    ReadbackSource = 1 << 4,
    PresenterIntermediate = 1 << 5,

    FilterSource = 1 << 6,
    CachedImageSource = 1 << 7,
    ExternalSampleSource = 1 << 8,

    AsyncUploadSource = 1 << 9,
    DeferredReadbackSource = 1 << 10,
}
```

### 사용 예

SVG filter source:

```csharp
SurfaceUsage.Offscreen |
SurfaceUsage.ImageSource |
SurfaceUsage.FilterIntermediate |
SurfaceUsage.FilterSource
```

Pattern tile:

```csharp
SurfaceUsage.Offscreen |
SurfaceUsage.ImageSource |
SurfaceUsage.CachedImageSource
```

Layered present intermediate:

```csharp
SurfaceUsage.Offscreen |
SurfaceUsage.PresenterIntermediate |
SurfaceUsage.ReadbackSource
```

## Surface capabilities

```csharp
[Flags]
public enum SurfaceCapabilities
{
    None = 0,

    Renderable = 1 << 0,
    Presentable = 1 << 1,

    CpuReadable = 1 << 2,
    CpuWritable = 1 << 3,

    GpuSampleable = 1 << 4,
    FilterIntermediate = 1 << 5,
    CacheableImageSource = 1 << 6,

    ExternalHandle = 1 << 7,
    ExternallySynchronized = 1 << 8,

    DeferredReadback = 1 << 9,
    AsyncCompletion = 1 << 10,

    Alpha = 1 << 11,
    Premultiplied = 1 << 12,
}
```

### 중요한 구분

`CpuReadable`은 두 가지 형태가 있다.

- immediate CPU memory
- GPU readback을 통해 읽을 수 있음

따라서 GPU target이 `CpuReadable | DeferredReadback`을 가질 수 있다. caller가 readback barrier를
피해야 한다면 `DeferredReadback` surface를 거부하거나 별도 async readback을 요청해야 한다.

## Lifetime hint

```csharp
public enum SurfaceLifetimeHint
{
    Frame = 0,
    Transient,
    Pooled,
    Cached,
    External,
}
```

용도:

- `Frame`
  - window backbuffer, presenter one-shot intermediate
- `Transient`
  - 일회성 offscreen pass
- `Pooled`
  - filter scratch pool
- `Cached`
  - pattern tile, viewport cache, filter result cache 같은 persistent image source
- `External`
  - externally-owned native resource wrapper

## Render surface

```csharp
public interface IRenderSurface : IDisposable
{
    int PixelWidth { get; }
    int PixelHeight { get; }
    double DpiScale { get; }

    RenderPixelFormat Format { get; }
    SurfaceUsage Usage { get; }
    SurfaceCapabilities Capabilities { get; }

    ulong Version { get; }
    bool IsDisposed { get; }
}
```

### 의미

`IRenderSurface`는 "그릴 수 있는 자원" 또는 "image source로 사용할 수 있는 자원"의 공통 기반이다.
모든 surface가 CPU pixels를 직접 노출해야 하는 것은 아니다.

## Optional capability interface

### CPU pixel surface

```csharp
public interface ICpuPixelSurface : IRenderSurface
{
    int StrideBytes { get; }
    ReadOnlySpan<byte> GetReadOnlyPixelSpan();
    Span<byte> GetWritablePixelSpan();
    byte[] CopyPixels();
    void IncrementVersion();
}
```

대상:

- GDI bitmap target
- software bitmap target
- `WriteableBitmapControl` backing store

### GPU sampleable surface

```csharp
public interface IGpuSampleableSurface : IRenderSurface
{
    bool YFlipped { get; }
    IDisposable RetainSampleHandle();
}
```

`RetainSampleHandle`은 backend-specific native handle lifetime을 고정하기 위한 최소 hook이다.
실제 handle은 backend-specific optional interface에서 제공한다.

### Backend native surface

```csharp
public interface INativeRenderSurface : IRenderSurface
{
    nint NativeHandle { get; }
}
```

이 interface는 common layer가 native handle을 직접 해석하기 위한 것이 아니다. diagnostics 또는
backend-specific bridge에서만 사용한다.

### Deferred CPU readable surface

```csharp
public interface IDeferredCpuReadableSurface : IRenderSurface
{
    bool HasPendingReadback { get; }
    IRenderOperation RequestReadback();
    bool TryFlushReadback();
}
```

## Cache lifetime

캐시는 surface architecture의 별도 축이다. `CacheableImageSource` capability만으로는 부족하다.
캐시는 key, owner, invalidation, eviction, safe disposal boundary를 함께 가져야 한다.

```csharp
public enum RenderCacheEntryKind
{
    Unknown = 0,
    ImageSource,
    FilterResult,
    PatternTile,
    ViewportSnapshot,
    UploadStaging,
}

public enum RenderCacheTrimReason
{
    Manual = 0,
    MemoryPressure,
    DeviceLost,
    DpiChanged,
    SourceInvalidated,
    CapacityExceeded,
}

public readonly record struct RenderCacheKey(
    RenderCacheEntryKind Kind,
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    RenderPixelFormat Format,
    ulong SourceVersion,
    ulong DeviceId,
    string? Scope = null);

public interface IRenderResourceCache
{
    bool TryGet(RenderCacheKey key, out IRenderCacheEntry entry);

    IRenderCacheEntry Add(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        IRenderOperation? safeToDisposeAfter = null);

    void Release(RenderCacheKey key);
    void ReleaseLater(IDisposable resource, IRenderOperation safeAfter);
    void Trim(RenderCacheTrimReason reason);
}

public interface IRenderCacheEntry : IDisposable
{
    RenderCacheKey Key { get; }
    IRenderSurface Surface { get; }
    IImage Image { get; }
    IRenderOperation? SafeToDisposeAfter { get; }
}
```

### 설명

- cache key는 backend/device identity를 포함해야 한다.
- GPU image view는 surface보다 오래 살 수 없으므로 cache entry가 둘을 함께 소유한다.
- eviction 시점에 GPU command가 아직 resource를 참조할 수 있으면 즉시 dispose하지 않는다.
- `ReleaseLater`는 existing pending-disposal queue를 일반화한 형태다.
- 초기 구현에서는 cache interface를 public으로 열지 않고 rendering internal service로 둘 수 있다.

캐시 대상은 도메인 이름이 아니라 일반 entry kind로 표현한다. 예를 들어 SVG pattern은
`PatternTile`, SVG viewport cache는 `ViewportSnapshot`, filter cache는 `FilterResult`로 표현하지만
백엔드는 이들이 SVG에서 왔는지 알 필요가 없다.

`TryFlushReadback`은 기존 synchronous API와의 호환을 위한 hook이다.
새 호출자는 가능하면 `RequestReadback` 결과를 operation으로 다룬다.

## Render operation

```csharp
public interface IRenderOperation : IDisposable
{
    bool IsCompleted { get; }
    void Wait();
    ValueTask WaitAsync(CancellationToken cancellationToken = default);
}
```

대상:

- GPU upload fence
- deferred readback
- command buffer completion
- surface copy/resolve
- worker render completion

초기 구현에서는 대부분 completed operation 또는 synchronous wait wrapper로 시작할 수 있다.
중요한 것은 완료 boundary를 타입으로 표현하는 것이다.

## Render device

```csharp
public interface IRenderDevice : IDisposable
{
    GraphicsBackend Backend { get; }

    IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor);

    IGraphicsContext CreateContext(IRenderSurface surface);

    IImage CreateImageView(IRenderSurface surface);

    bool TryCopySurface(IRenderSurface source, IRenderSurface destination);

    bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes);

    IRenderOperation RequestReadback(IRenderSurface source);

    IRenderOperation FlushAsyncWork();

    IRenderResourceCache? ResourceCache { get; }
}
```

### 설명

- `CreateSurface`
  - target allocation의 중심 API
- `CreateContext`
  - draw context 생성
- `CreateImageView`
  - render surface를 sampleable image로 wrap
- `ResourceCache`
  - backend가 제공하는 surface/image lifetime cache
- `TryCopySurface`
  - GPU/GPU, CPU/CPU, GPU/CPU 가능한 경로를 backend가 선택
- `TryReadPixels`
  - synchronous compatibility path
- `RequestReadback`
  - deferred/asynchronous readback path
- `FlushAsyncWork`
  - backend에 outstanding operation flush 요청

## Graphics resource factory

```csharp
public interface IGraphicsResourceFactory : IDisposable
{
    GraphicsBackend Backend { get; }

    ISolidColorBrush CreateSolidColorBrush(Color color);
    IPen CreatePen(Color color, double thickness = 1.0, StrokeStyle? strokeStyle = null);
    IPen CreatePen(IBrush brush, double thickness = 1.0, StrokeStyle? strokeStyle = null);

    ILinearGradientBrush CreateLinearGradientBrush(
        Point startPoint,
        Point endPoint,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod = SpreadMethod.Pad,
        GradientUnits units = GradientUnits.UserSpaceOnUse,
        Matrix3x2? gradientTransform = null);

    IRadialGradientBrush CreateRadialGradientBrush(
        Point center,
        Point gradientOrigin,
        double radiusX,
        double radiusY,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod = SpreadMethod.Pad,
        GradientUnits units = GradientUnits.UserSpaceOnUse,
        Matrix3x2? gradientTransform = null);

    IImageBrush CreateImageBrush(
        IImage image,
        Rect sourceRect,
        Rect destinationRect,
        TileMode tileMode = TileMode.Tile,
        double opacity = 1.0,
        Matrix3x2? transform = null,
        IDisposable[]? ownedResources = null);

    IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false);

    IImage CreateImageFromFile(string path);
    IImage CreateImageFromBytes(byte[] data);
    IImage CreateImageFromPixelSource(IPixelBufferSource source);
}
```

`CreateImageFromPixelSource`는 유지한다. 단, render surface를 image로 만드는 경로는
`IRenderDevice.CreateImageView`로 분리한다.

## Image filter device

```csharp
public interface IImageFilterDevice
{
    IImageFilterExecutor CreateExecutor();

    IRenderSurface RentScratchSurface(
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        RenderPixelFormat format,
        SurfaceCapabilities requiredCapabilities);

    void ReturnScratchSurface(IRenderSurface surface, IRenderOperation? safeAfter = null);
}
```

### 설명

기존 `ScratchRenderTargetPool` 책임을 이쪽으로 옮긴다.

`safeAfter`는 GPU command가 아직 surface/image를 참조할 수 있는 경우를 위해 둔다.
초기에는 null 또는 completed operation만 지원해도 된다.

## External sample source

```csharp
public enum ExternalSampleSourceKind
{
    Unknown = 0,
    OpenGLTexture,
    MetalTexture,
    D3D11Texture,
    DxgiSurface,
    DmaBuf,
    IOSurface,
    CpuUploadStaging,
}

public interface IExternalSampleSource : IDisposable
{
    ExternalSampleSourceKind Kind { get; }

    int PixelWidth { get; }
    int PixelHeight { get; }

    RenderPixelFormat Format { get; }
    BitmapAlphaMode AlphaMode { get; }
    bool YFlipped { get; }

    SurfaceCapabilities Capabilities { get; }

    IDisposable AcquireForSampling();

    nint NativeHandle { get; }
}
```

### 기존 `IExternalLockedTexture`와의 관계

`IExternalLockedTexture`는 초기에는 `IExternalSampleSource` adapter로 감싼다.

최종적으로는 다음 이유로 rename/evolution하는 편이 낫다.

- 모든 source가 texture라고 부르기 어렵다.
- D2D DXGI surface, dma_buf, IOSurface는 texture보다 external sample source에 가깝다.
- acquire/release 외에도 fence, color conversion, plane metadata가 필요할 수 있다.

## External image device

```csharp
public interface IExternalImageDevice
{
    bool CanCreateImage(IExternalSampleSource source);

    IImage CreateImageFromExternalSource(
        IExternalSampleSource source,
        ExternalImageOptions options = default);
}

public readonly record struct ExternalImageOptions(
    bool TakeOwnership = false,
    bool AllowColorConversion = true,
    string? DebugName = null);
```

## Window presenter

```csharp
public enum PresentMode
{
    Default = 0,
    Direct,
    Composited,
    Layered,
}

public readonly record struct PresentOptions(
    PresentMode Mode,
    double Opacity = 1.0,
    bool RequiresPremultipliedAlpha = false);

public interface IWindowPresenter
{
    bool CanPresent(Platform.IWindowSurface windowSurface, IRenderSurface renderSurface, PresentOptions options);

    IRenderSurface CreateIntermediateSurface(
        Platform.IWindowSurface windowSurface,
        PresentOptions options,
        IRenderDevice renderDevice);

    IRenderOperation Present(
        Window window,
        Platform.IWindowSurface windowSurface,
        IRenderSurface renderSurface,
        PresentOptions options);
}
```

### 설명

Layered/composited present는 renderer의 primitive가 아니라 presenter 정책이다.
presenter가 필요한 intermediate surface를 요청하고, render device가 가능한 최적 surface를 만든다.

## Backend scheduler

```csharp
public interface IBackendScheduler
{
    IDisposable AcquireBackgroundRenderScope();
    IDisposable AcquireConcurrentRenderUnit();

    bool IsCurrentThreadRenderCapable { get; }

    IRenderOperation CreateCompletedOperation();
}
```

### 설명

기존 `IGraphicsFactory.AcquireBackgroundRenderScope`와 `AcquireConcurrentRenderUnit`을 분리한다.

GL backend는 worker context activation을 수행하고, D2D/Metal/GDI는 no-op에 가깝게 구현할 수 있다.

## Compatibility facade

기존 `IGraphicsFactory`는 당분간 facade로 유지한다.

```csharp
public interface IGraphicsFactory :
    IGraphicsResourceFactory,
    IDisposable
{
    IRenderDevice RenderDevice { get; }
    IImageFilterDevice ImageFilterDevice { get; }
    IExternalImageDevice ExternalImageDevice { get; }
    IBackendScheduler BackendScheduler { get; }
    IRenderResourceCache? ResourceCache { get; }

    IGraphicsContext CreateContext(IRenderTarget target);
    IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0, bool hasAlpha = true);
    IBitmapRenderTarget CreateOffscreenRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0, bool hasAlpha = true);
}
```

초기 구현에서는 기존 메서드가 내부적으로 새 descriptor를 만들어 `IRenderDevice`로 위임한다.

## 기존 타입 이관 방향

### `IRenderTarget`

초기:

- 유지
- `IRenderSurface` adapter 추가

후기:

- window/layout 코드의 최소 target view로 남길지 검토
- rendering backend 내부는 `IRenderSurface` 중심으로 전환

### `IBitmapRenderTarget`

초기:

- 유지
- `ICpuPixelSurface` 또는 `IRenderSurface` adapter 구현
- GPU-backed 구현체도 compatibility를 위해 계속 반환 가능

후기:

- CPU bitmap 계약으로 축소
- GPU offscreen target은 `IRenderSurface`로 이동

### `IImage`

초기:

- 유지
- `CreateImageView(IRenderSurface)` 결과도 기존 `IImage` 반환

후기:

- 필요하면 `IImageView`를 별도로 도입
- 당장은 `IImage`를 유지하는 편이 migration 비용이 낮다.

### `IExternalLockedTexture`

초기:

- 유지
- `IExternalSampleSource` adapter 제공

후기:

- external source metadata가 늘어나면 rename/evolution

## 첫 번째 구현 slice 후보

### Slice 1: 타입 추가만 수행

추가:

- `RenderSurfaceDescriptor`
- `RenderPixelFormat`
- `SurfaceUsage`
- `SurfaceCapabilities`
- `SurfaceLifetimeHint`
- `IRenderSurface`
- `IRenderOperation`
- `IRenderResourceCache`

동작 변경 없음.

### Slice 2: adapter 추가

추가:

- `BitmapRenderTargetSurfaceAdapter`
- `RenderTargetSurfaceAdapter`
- completed render operation

동작 변경 최소화.

### Slice 3: `CreateImageView` 도입

대상:

- `SvgFilter.MewUI`
- `SvgPatternServer.MewUI`
- `MewUI.Svg.Sample SvgView`

`CreateImageFromPixelSource(target)`를 `CreateImageView(surface)`로 옮길 준비를 한다.

### Slice 4: filter scratch descriptor 적용

대상:

- `ScratchRenderTargetPool`
- `DefaultFilterContext`
- backend GPU filter executor

### Slice 4.5: cache lifetime 일반화

대상:

- filter result cache
- pattern tile cache
- SVG viewport cache
- MewVG scratch target delayed return

기존 pending-disposal queue를 `ReleaseLater(resource, safeAfter)` 형태로 일반화한다.

### Slice 5: external source adapter

대상:

- `IExternalLockedTexture`
- `PboFenceUploader`
- Video.Sample zero-copy path

### Slice 6: presenter 분리

대상:

- `GdiGraphicsFactory.Present`
- `Direct2DGraphicsFactory.Present`
- `MewVGGraphicsFactory.Present`
- platform `IWindowSurfacePresenter`

## 열린 결정 사항

- `CreateImageView(IRenderSurface)`가 surface ownership을 retain해야 하는가, borrow해야 하는가?
- `IRenderOperation`을 `EndFrame`에서 반환할 것인가, 별도 `FlushAsyncWork`로만 제공할 것인가?
- `TryReadPixels`는 synchronous compatibility로 유지하되, 새 code는 `RequestReadback`을 강제할 것인가?
- `IImageFilterDevice.RentScratchSurface`가 render device에 포함되어야 하는가, 별도 device가 맞는가?
- `IBackendScheduler`를 public API로 둘 것인가, backend internal service로 둘 것인가?
- external YUV source의 color conversion은 `IExternalImageDevice` 책임인가, video pipeline 책임인가?
- `IRenderResourceCache`는 public API인가, rendering internal service인가?
- cache key의 device identity는 누가 제공하는가?
