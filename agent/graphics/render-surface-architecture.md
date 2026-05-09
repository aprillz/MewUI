# 렌더 Surface 아키텍처 정리

## 목적

현재 렌더링 구조는 D2D, GDI, MewVG 백엔드가 같은 `IGraphicsFactory` / `IGraphicsContext`
계약을 구현하고 있지만, window target, offscreen target, bitmap target, texture target,
layered window present가 명확히 분리되어 있지 않다.

이 문서는 MewVG만의 문제가 아니라 MewUI 렌더링 공통 계층에서 정리해야 할 현재 상태와
정리 방향을 기록한다.

## 결론

정리 단위는 MewVG 코어가 아니다. `MewUI.Rendering`의 공통 surface/target/presenter
계약을 먼저 정리하고, D2D/GDI/MewVG가 그 계약을 각자 구현해야 한다.

MewVG는 GL/Metal 기반 구현체 중 하나일 뿐이다. D2D는 D2D/DXGI surface와 bitmap을,
GDI는 HDC/memory bitmap을, MewVG는 window drawable/FBO/texture/bitmap readback을
같은 모델 위에서 표현해야 한다.

## 현재 구조

### 공통 렌더링 계약

- `IRenderTarget`
  - `PixelWidth`
  - `PixelHeight`
  - `DpiScale`
- `WindowRenderTarget`
  - platform `IWindowSurface`를 감싼 internal window target
- `IBitmapRenderTarget`
  - `IRenderTarget`
  - `IPixelBufferSource`
  - `CopyPixels()`
  - `GetPixelSpan()`
  - `Clear()`
  - `IncrementVersion()`
  - `IsPremultiplied`
- `IGraphicsFactory`
  - resource 생성
  - context 생성
  - bitmap/offscreen target 생성
  - filter executor 생성
  - background render scope 제공
- `IWindowSurface`
  - platform window surface
  - `Kind`, native `Handle`, size, dpi
- `IWindowSurfacePresenter`
  - platform window surface로 최종 present

### 백엔드별 현황

#### GDI

- CPU memory bitmap / HDC 중심 구조에 가깝다.
- CPU read/write, layered window용 BGRA buffer 생성은 상대적으로 자연스럽다.
- GPU texture sampling, GPU filter pipeline 같은 capability는 없다.

#### D2D

- window 렌더링은 HWND/D2D device context 계열이 중심이다.
- offscreen은 `ID2D1Bitmap1` / DXGI surface 계열로 갈 수 있다.
- GPU-resident target과 CPU-readable bitmap은 비용과 의미가 다르다.
- layered window present에는 CPU readback 또는 compatible bitmap 경로가 필요하다.

#### MewVG

- Win32/X11는 GL context, FBO-backed bitmap target, worker GL context, shared texture 등을 사용한다.
- macOS는 Metal texture/render target 계열을 사용한다.
- 현재 layered window present는 MewUI/MewVG Win32 backend에서 별도 경로를 만들고,
  `RenderFrameToBitmap()` 재진입과 thread-static 상태로 현재 present target을 전달한다.
- `IBitmapRenderTarget`이라는 이름 아래 GPU FBO/texture target과 CPU pixel source 역할이 섞여 있다.

## 현재 문제

### 1. `IRenderTarget`이 target의 성격을 표현하지 못함

`IRenderTarget`은 크기와 DPI만 가진다. 이 target이 다음 중 무엇인지 알 수 없다.

- window drawable
- CPU bitmap
- GPU texture/FBO
- D2D bitmap
- Metal texture
- external native surface
- readback 가능한 offscreen target
- present 가능한 swap/window target

그 결과 backend 구현이 concrete type check와 별도 side-channel에 의존한다.

### 2. `IBitmapRenderTarget`의 의미가 과부하됨

이름상 bitmap target은 CPU pixel buffer처럼 보인다. 하지만 실제로는 backend에 따라 다음 의미를
동시에 가진다.

- CPU에서 읽고 쓸 수 있는 bitmap
- GPU offscreen render target
- filter intermediate surface
- image source로 sample 가능한 texture
- layered window present를 위해 readback 가능한 source

GDI에서는 이 의미들이 대체로 같은 물리 자원으로 수렴하지만, D2D/MewVG에서는 완전히 다르다.

### 3. `CreateBitmapRenderTarget`과 `CreateOffscreenRenderTarget`의 차이가 불명확함

현재 `CreateOffscreenRenderTarget`은 기본적으로 `CreateBitmapRenderTarget`으로 fallback한다.
주석은 GPU-resident transient target을 설명하지만 반환 타입은 여전히 `IBitmapRenderTarget`이다.

이 때문에 호출자는 "CPU bitmap이 필요하다"와 "GPU offscreen pass가 필요하다"를 명확히 표현하지
못한다.

### 4. `Layered`가 surface kind에 들어가 있음

`WindowSurfaceKind.Layered`는 render target 종류라기보다 present 정책에 가깝다.

Layered window 자체는 D2D/GDI/MewVG의 렌더링 primitive가 아니다. platform presenter가 요구하는
최종 출력 형식이다. 보통 요구사항은 다음에 가깝다.

- BGRA
- premultiplied alpha
- CPU-readable buffer 또는 platform API에 전달 가능한 native buffer

따라서 `Layered`를 render surface 종류처럼 취급하면 window policy와 rendering resource model이
섞인다.

### 5. `IGraphicsFactory` 책임이 너무 넓음

현재 factory는 다음 책임을 동시에 가진다.

- brush/font/image/context 생성
- bitmap/offscreen target 생성
- image filter executor 생성
- worker/background render scope 관리
- window resource release
- preferred window surface 선택
- window surface present

특히 MewVG backend에서 이 과부하가 크게 드러난다. window resource, GL/Metal context, offscreen
surface pool, layered presenter가 하나의 factory 주변에 모인다.

### 6. Present 경로와 draw 경로가 분리되지 않음

일반 window rendering, offscreen rendering, layered window rendering은 필요한 target과 완료 단계가
다르다.

- 일반 window: draw to window target, then swap/present
- offscreen GPU: draw to texture/FBO, keep as GPU image source
- CPU bitmap: draw or write pixels, expose CPU buffer
- layered window: draw to intermediate, convert/readback, call platform update API

현재는 이 차이가 공통 계약에 충분히 드러나지 않아서 backend-specific 우회가 생긴다.

### 7. 비동기 완료 모델이 명시되어 있지 않음

현재 코드에는 이미 여러 종류의 비동기성이 존재한다.

- worker thread offscreen render
  - `MewUI.Svg.Sample`의 `Task.Run` + `AcquireBackgroundRenderScope`
  - render 완료 후 dispatcher `BeginInvoke`로 UI thread cache swap
- GPU upload 비동기
  - OpenGL PBO + fence 기반 async upload
  - `PboFenceUploader`가 `glFenceSync` / `glClientWaitSync`로 이전 upload 완료를 확인
- deferred readback
  - OpenGL `OpenGLBitmapRenderTarget.RequestDeferredReadback`
  - Metal `MewVGMetalBitmapRenderTarget.RequestDeferredReadback(commandBuffer)`
  - CPU consumer가 `GetPixelSpan` / `CopyPixels`를 호출할 때 readback flush
- async GPU command completion
  - Metal command buffer 완료 이후 external texture release 또는 readback 가능 상태 전환
- external source synchronization
  - `IExternalLockedTexture.Acquire` / `Release`
  - release 시점이 backend별로 "flush 이후" 또는 "command buffer completion 이후"로 달라짐

따라서 새 surface 계약은 단순히 `CpuReadable` 여부만 표현하면 부족하다.
`CpuReadable`은 "즉시 읽을 수 있음"인지, "readback을 요청하면 나중에 가능"인지, "읽는 순간 동기화 barrier가 발생"하는지까지 구분해야 한다.

필요한 개념:

- operation completion
- deferred readback
- GPU fence / command buffer completion
- UI thread commit과 worker render 결과 handoff
- resource lifetime이 command completion 이후까지 연장되는 계약

### 8. SVG filter/pattern 경로가 surface 계약에 강하게 의존함

`MewUI.Svg`는 단순히 최종 canvas에 draw하는 소비자가 아니다. 내부적으로 offscreen surface를 만들고,
그 surface를 다시 image/filter source로 사용하는 복합 소비자다.

주요 경로:

- `SvgFilter.MewUI`
  - filter source layer를 `CreateOffscreenRenderTarget`으로 생성
  - source layer에 SVG element를 다시 렌더링
  - `CreateImageFromPixelSource(sourceLayer)`로 filter source image 생성
  - `DefaultFilterContext` / `ScratchRenderTargetPool`이 scratch target을 반복 대여
  - GPU executor는 GPU target을 선호하고, CPU fallback은 pixel read/write가 필요함
  - 결과 cache는 `IBitmapRenderTarget + IImage` 조합으로 유지됨
- `SvgPatternServer.MewUI`
  - pattern tile을 `CreateBitmapRenderTarget`에 렌더링
  - `CreateImageFromPixelSource(target)`로 tile image 생성
  - target과 image를 pattern server lifetime 동안 cache

따라서 SVG는 다음 capability를 동시에 요구한다.

- offscreen renderable
- image source로 재사용 가능
- filter intermediate로 재사용 가능
- 필요 시 CPU fallback에서 read/write 가능
- cache entry가 draw command flush 이후까지 안전하게 살아있을 수 있는 lifetime 계약

현재 `IBitmapRenderTarget` 하나로 이 요구사항을 모두 표현하고 있어, D2D/GDI/MewVG에서 의미가
다르게 해석된다.

### 9. Video.Sample은 external GPU texture 계약을 이미 요구함

`MewUI.Video.Sample`은 decoded video frame을 `IImage`로 만들어 `DrawImage`하는 소비자지만,
실제로는 다음 경로를 모두 사용한다.

- CPU fallback
  - decoded BGRA pixels를 `CreateImageFromPixelSource(frame)`으로 업로드
- D2D zero-copy
  - D3D11 texture에서 `IDXGISurface`를 얻고 D2D image/bitmap으로 wrap
- MewVG GL Win32 zero-copy
  - D3D11 texture를 `WGL_NV_DX_interop`으로 GL texture처럼 sample
  - `IExternalLockedTexture` acquire/release 필요
- MewVG Metal macOS zero-copy
  - VideoToolbox/CVPixelBuffer/IOSurface에서 Metal texture를 얻어 sample
- MewVG GL Linux zero-copy attempt
  - VAAPI surface를 dma_buf/EGLImage/GL texture로 import
  - YUV -> RGB 변환 문제가 별도 남음

즉 Video.Sample은 `RenderSurface`뿐 아니라 `ExternalTexture/ImageSource` 계약도 요구한다.
이 경로는 window present나 offscreen render target과 다르다. "그릴 대상"이 아니라 "sample할
외부 GPU resource"이며, 수명주기와 sync가 핵심이다.

현재 `IExternalLockedTexture`는 이미 다음 정보를 갖고 있다.

- native GPU handle
- pixel size
- alpha mode
- Y-flip 여부
- per-frame acquire/release

따라서 새 surface architecture는 external sample source도 일급 개념으로 포함해야 한다.

## 정리 원칙

### 1. Platform window 정책과 rendering resource를 분리한다

MewUI platform backend가 책임질 것:

- OS window 생성/파괴
- client rect / non-client rect 정규화
- hit-test
- resize/move/titlebar
- surface kind 선택에 필요한 platform capability 제공
- layered/native/composited present 정책 결정

MewUI rendering backend가 책임질 것:

- 주어진 render surface에 draw
- 필요한 offscreen surface 생성
- image/filter source로 사용할 resource 생성
- 가능한 경우 readback/copy/resolve 제공

### 2. Surface는 concrete type보다 capability로 본다

공통 surface 모델은 "무슨 클래스인가"보다 "무엇이 가능한가"를 표현해야 한다.

필요한 capability:

- `Renderable`: draw target으로 사용할 수 있음
- `Presentable`: window에 직접 present 가능
- `CpuReadable`: CPU pixel buffer로 읽을 수 있음
- `CpuWritable`: CPU가 직접 픽셀을 쓸 수 있음
- `GpuSampleable`: image/filter source로 GPU sampling 가능
- `ExternalHandle`: 외부 native handle을 감싸고 있음
- `ExternallySynchronized`: 외부 owner와 acquire/release 또는 fence sync가 필요함
- `FilterIntermediate`: filter graph 중간 결과로 재사용 가능함
- `CacheableImageSource`: surface/image view가 command flush 이후에도 cache 가능함
- `DeferredReadback`: CPU read가 deferred GPU readback을 flush할 수 있음
- `AsyncCompletion`: surface 작업 완료가 GPU fence/command buffer completion에 의존함
- `Alpha`: 의미 있는 alpha 채널을 가짐
- `Premultiplied`: premultiplied alpha 저장

### 3. Bitmap target과 offscreen texture target을 분리한다

`IBitmapRenderTarget`은 CPU pixel buffer 계약에 가깝게 좁히는 것이 좋다.

별도 개념이 필요하다.

- CPU bitmap surface
- GPU offscreen surface
- window surface target
- external/native surface wrapper
- readback/copy result

### 4. Layered window는 presenter 요구사항으로 표현한다

Layered window를 backend render target 종류로 두기보다, presenter가 요구하는 출력 조건으로 모델링한다.

예:

- `PresentMode.Layered`
- 요구 capability: `CpuReadable`, `Alpha`, `Premultiplied`
- 요구 format: BGRA8888

MewVG/D2D가 GPU target에 먼저 그린 뒤 readback할지, GDI가 memory bitmap에 직접 그릴지는 backend
구현 세부사항이다.

### 5. Context 생성과 presentation을 분리한다

`CreateContext(surface)`는 draw context 생성만 담당해야 한다.

최종 표시 단계는 별도 presenter/device 책임이어야 한다.

## 제안 모델

### Surface descriptor

```csharp
public readonly record struct RenderSurfaceDescriptor(
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    PixelFormat Format,
    SurfaceUsage Usage,
    SurfaceCapabilities RequiredCapabilities);
```

### Surface capability

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
    ExternalHandle = 1 << 5,
    ExternallySynchronized = 1 << 6,
    FilterIntermediate = 1 << 7,
    CacheableImageSource = 1 << 8,
    DeferredReadback = 1 << 9,
    AsyncCompletion = 1 << 10,
    Alpha = 1 << 11,
    Premultiplied = 1 << 12,
}
```

### Surface usage

```csharp
[Flags]
public enum SurfaceUsage
{
    None = 0,
    WindowBackBuffer = 1 << 0,
    Offscreen = 1 << 1,
    FilterIntermediate = 1 << 2,
    ImageSource = 1 << 3,
    ReadbackSource = 1 << 4,
    LayeredPresentSource = 1 << 5,
    FilterSource = 1 << 6,
    CachedImageSource = 1 << 7,
    ExternalSampleSource = 1 << 8,
    AsyncUploadSource = 1 << 9,
    DeferredReadbackSource = 1 << 10,
}
```

### Render surface

```csharp
public interface IRenderSurface : IDisposable
{
    int PixelWidth { get; }
    int PixelHeight { get; }
    double DpiScale { get; }
    PixelFormat Format { get; }
    SurfaceCapabilities Capabilities { get; }
}
```

### Optional capability interface

```csharp
public interface ICpuPixelSurface : IRenderSurface
{
    ReadOnlySpan<byte> GetReadOnlyPixelSpan();
    Span<byte> GetWritablePixelSpan();
    byte[] CopyPixels();
}

public interface IGpuSampleableSurface : IRenderSurface
{
    IImage CreateImageView();
}

public interface IExternalRenderSurface : IRenderSurface
{
    nint NativeHandle { get; }
}

public interface IExternalSampleSource : IDisposable
{
    int PixelWidth { get; }
    int PixelHeight { get; }
    PixelFormat Format { get; }
    BitmapAlphaMode AlphaMode { get; }
    bool YFlipped { get; }

    IDisposable AcquireForSampling();
}

public interface IRenderOperation : IDisposable
{
    bool IsCompleted { get; }
    void Wait();
    ValueTask WaitAsync(CancellationToken cancellationToken = default);
}

public interface IDeferredCpuReadableSurface : IRenderSurface
{
    bool HasPendingReadback { get; }
    IRenderOperation RequestReadback();
}
```

### Render device

```csharp
public interface IRenderDevice : IDisposable
{
    GraphicsBackend Backend { get; }

    IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor);
    IGraphicsContext CreateContext(IRenderSurface surface);

    IImage CreateImageView(IRenderSurface surface);
    IImage CreateImageFromExternalSource(IExternalSampleSource source);

    IRenderOperation FlushAsyncWork();
    bool TryReadPixels(IRenderSurface surface, PixelBuffer destination);
    bool TryCopySurface(IRenderSurface source, IRenderSurface destination);
}
```

### Window presenter

```csharp
public interface IWindowPresenter
{
    bool CanPresent(IWindowSurface windowSurface, IRenderSurface renderSurface, PresentOptions options);
    bool Present(Window window, IWindowSurface windowSurface, IRenderSurface renderSurface, PresentOptions options);
}
```

## 백엔드 매핑

### GDI 매핑

- `ICpuPixelSurface`
  - DIB section / memory bitmap
  - HDC-backed render target
- `WindowBackBuffer`
  - window HDC 또는 memory buffer 후 BitBlt
- `LayeredPresentSource`
  - BGRA premultiplied DIB section

GDI는 CPU surface 중심이므로 `CpuReadable`, `CpuWritable`, `Renderable`이 기본 capability가 된다.

### D2D 매핑

- `WindowBackBuffer`
  - HWND target 또는 device context target
- `Offscreen` / `FilterIntermediate`
  - `ID2D1Bitmap1` with target usage
- `ImageSource`
  - D2D bitmap/image
- `LayeredPresentSource`
  - GPU target 후 CPU staging/readback 또는 compatible bitmap path

D2D에서는 GPU surface와 CPU-readable surface가 다르므로 descriptor의 usage/capability가 중요하다.

### MewVG 매핑

- `WindowBackBuffer`
  - GL drawable, GLX/WGL surface, CAMetalLayer/Metal drawable
- `Offscreen` / `FilterIntermediate`
  - GL FBO texture, Metal texture
- `ImageSource`
  - GL texture, Metal texture
- `LayeredPresentSource`
  - GPU target 후 readback하거나 CPU-compatible target을 선택

MewVG backend 내부에서 GL/Metal context와 texture ownership을 관리하되, MewUI 상위 계층에는
`IRenderSurface` capability로만 노출한다.

## 주요 사용처

### MewUI.Svg

SVG는 새 surface 모델의 가장 중요한 검증 대상이다. SVG filter와 pattern은 다음 동작을 안정적으로
지원해야 한다.

- source layer를 offscreen render target에 렌더링
- source layer를 image/filter source로 wrap
- scratch filter target을 pool에서 재사용
- GPU executor가 가능한 backend에서는 readback 없이 GPU pipeline 유지
- CPU fallback에서는 pixel span/copy/readback 가능
- filter result cache가 command flush 전후 lifetime을 안전하게 유지
- pattern tile cache가 target/image pair를 장기간 보관

필요한 descriptor 예:

```csharp
new RenderSurfaceDescriptor(
    pixelWidth,
    pixelHeight,
    dpiScale,
    PixelFormat.Bgra8888,
    SurfaceUsage.Offscreen | SurfaceUsage.FilterSource | SurfaceUsage.FilterIntermediate | SurfaceUsage.ImageSource,
    SurfaceCapabilities.Renderable | SurfaceCapabilities.GpuSampleable | SurfaceCapabilities.FilterIntermediate);
```

CPU fallback까지 보장해야 하는 경우:

```csharp
SurfaceCapabilities.Renderable |
SurfaceCapabilities.CpuReadable |
SurfaceCapabilities.CpuWritable |
SurfaceCapabilities.CacheableImageSource
```

SVG 관점에서 `CreateBitmapRenderTarget`과 `CreateOffscreenRenderTarget`의 차이는 명확해야 한다.

- pattern tile: cache 가능한 image source가 필요함
- filter source: renderable + filter intermediate + image source가 필요함
- CPU executor fallback: readable/writable pixel access가 필요함

## 캐시 고려사항

Surface architecture에서 캐시는 flag 하나로 끝낼 수 없다. 캐시는 surface/image view의 소유권과
safe disposal boundary를 함께 다룬다.

필요한 공통 개념:

- cache key
  - pixel size
  - dpi scale
  - pixel format
  - source version
  - effective scale 또는 transform bucket
  - backend device identity
- owner
  - cache가 surface와 image view를 함께 소유하는지
  - caller가 image만 borrow하는지
- invalidation
  - source version 변경
  - DPI 변경
  - device lost
  - format/capability mismatch
- eviction
  - memory pressure
  - capacity exceeded
  - document/control dispose
- safe disposal
  - draw command가 아직 image/surface를 참조 중이면 즉시 dispose하지 않음
  - `IRenderOperation safeAfter` 이후 dispose

현재 코드에도 이미 같은 패턴이 있다.

- filter result cache
  - result target과 image를 함께 보관
  - command flush 전 dispose를 피하기 위해 pending disposal queue 사용
- pattern tile cache
  - tile target과 tile image를 장기 보관
- viewport snapshot cache
  - worker thread에서 만든 target/image를 UI thread에서 swap
- scratch target pool
  - zero-copy image가 scratch target을 참조할 수 있어 반환 시점을 늦춰야 함

공통 API는 도메인 이름을 쓰지 않는다. 예를 들어 SVG에서 온 cache라도 backend 관점에서는
`FilterResult`, `PatternTile`, `ViewportSnapshot`, `CachedImageSource` 같은 일반 cache kind로만 보인다.

캐시 서비스 후보:

```csharp
public interface IRenderResourceCache
{
    bool TryGet(RenderCacheKey key, out IRenderCacheEntry entry);
    IRenderCacheEntry Add(RenderCacheKey key, IRenderSurface surface, IImage image, IRenderOperation? safeToDisposeAfter = null);
    void Release(RenderCacheKey key);
    void ReleaseLater(IDisposable resource, IRenderOperation safeAfter);
    void Trim(RenderCacheTrimReason reason);
}
```

### MewUI.Video.Sample

Video.Sample은 surface target보다 external sample source 계약을 검증한다.

필요한 요구사항:

- external GPU resource를 `IImage`로 wrap
- backend별 native handle 해석
  - D2D: D3D11/DXGI surface
  - MewVG GL: GL texture id 또는 WGL/DX interop texture
  - MewVG Metal: MTLTexture/IOSurface-backed texture
- frame마다 acquire/release 또는 fence wait
- Y-flip 보정
- alpha mode 전달
- zero-copy 실패 시 CPU pixel source fallback
- video frame recycling과 image lifetime 분리

현재 `IExternalLockedTexture`는 이 요구사항의 초기 형태다. 새 모델에서는 이를 `IExternalSampleSource`
또는 `IExternalTextureSource` 계열로 승격하고, `IGraphicsFactory.CreateImageFromExternalTexture`를
device/image-source 생성 API로 이동시키는 것이 자연스럽다.

Video.Sample 관점에서 중요한 점은 external source가 render target이 아니라는 것이다. 이것은
`GpuSampleable` image source이며, 필요하면 backend가 intermediate RGB texture로 convert할 수 있어야
한다. 특히 VAAPI/NV12 같은 YUV source는 "native handle을 image로 wrap"만으로 끝나지 않고,
color conversion pass가 필요할 수 있다.

## 비동기 고려사항

Surface architecture는 동기 API만 전제하면 안 된다. 현재 코드도 이미 다음 패턴을 사용한다.

### Worker render와 UI commit

SVG sample은 worker thread에서 offscreen render를 수행하고, 결과 target/image를 UI thread로 넘긴다.
이때 필요한 계약은 다음과 같다.

- background render scope 획득
- worker thread에서 context 생성 가능 여부
- worker-created surface가 UI thread에서 sample 가능한지
- commit 전까지 target/image lifetime 유지
- 실패 또는 취소 시 partial target disposal

### Deferred readback

GPU target은 CPU buffer를 항상 최신 상태로 들고 있지 않다. OpenGL/Metal/D2D GPU target 모두
CPU read 시점에 readback barrier가 발생할 수 있다.

따라서 `CpuReadable`은 다음 상태로 세분화되어야 한다.

- immediate CPU memory
- deferred readback required
- readback operation pending
- readback complete

### Async upload

Video CPU fallback과 large pixel source upload는 OpenGL PBO + fence처럼 비동기로 진행될 수 있다.
이 경우 image creation은 완료되었지만 실제 texture upload completion은 나중에 발생한다.

필요한 계약:

- upload operation lifetime
- 이전 upload fence wait
- image가 sampling 가능한 시점
- producer frame recycling 가능 시점

### Command completion 기반 lifetime

Metal처럼 command buffer completion이 명확한 backend는 resource release 시점이 `EndFrame`이 아니라
command completion 이후가 될 수 있다.

따라서 cached image, external texture, scratch target 반환은 다음 중 어느 boundary에서 안전한지
명시해야 한다.

- draw command recorded
- frame flushed
- GPU command submitted
- GPU command completed
- CPU readback completed

## Migration 계획

### Phase 1: 용어와 계약 분리

- `IBitmapRenderTarget` 사용처를 분류한다.
  - CPU pixel buffer가 필요한 곳
  - offscreen draw target이 필요한 곳
  - image/filter source가 필요한 곳
  - layered present source가 필요한 곳
- `MewUI.Svg` 사용처를 별도 분류한다.
  - filter source layer
  - scratch filter intermediate
  - filter result cache
  - pattern tile cache
- `MewUI.Video.Sample` 사용처를 별도 분류한다.
  - CPU pixel source fallback
  - D2D DXGI external image
  - GL/Metal external locked texture
  - VAAPI dma_buf/EGLImage source
- `CreateBitmapRenderTarget`와 `CreateOffscreenRenderTarget` 호출 의도를 구분해서 문서화한다.
- layered window 경로에서 실제 요구 capability를 명시한다.

### Phase 2: 새 surface 계약 추가

- `IRenderSurface`
- `SurfaceCapabilities`
- `SurfaceUsage`
- `RenderSurfaceDescriptor`
- CPU/GPU/native optional capability interfaces
- external sample source/image source interface
- render operation / completion interface

기존 `IRenderTarget`은 바로 제거하지 않고 adapter로 유지한다.

### Phase 3: 백엔드별 adapter 구현

- GDI: 기존 bitmap target을 `ICpuPixelSurface`로 감싼다.
- D2D: bitmap/offscreen target을 surface descriptor 기반으로 생성한다.
- MewVG: OpenGL/Metal bitmap target을 GPU offscreen surface로 표현하고, CPU readback은 별도 capability로 둔다.
- SVG filter/pattern 경로를 새 descriptor 기반으로 이주시킨다.
- Video.Sample의 `IExternalLockedTexture` 경로를 새 external sample source API로 이주시킨다.

### Phase 4: presenter 분리

- `IWindowSurfacePresenter`를 새 `IWindowPresenter` 또는 `IRenderPresenter` 계열로 분리한다.
- layered present는 `WindowSurfaceKind.Layered` 중심이 아니라 `PresentMode.Layered`와 required capability 중심으로 전환한다.
- `Window.RenderFrameToBitmap()` 재진입 경로를 제거하고, presenter가 필요한 intermediate surface를 명시적으로 요청하도록 바꾼다.

### Phase 5: 기존 계약 축소

- `IBitmapRenderTarget`은 CPU bitmap 계약으로 좁힌다.
- GPU offscreen target은 `IRenderSurface + IGpuSampleableSurface`로 이동한다.
- `IGraphicsFactory`에서 window resource release, surface selection, present 책임을 분리한다.

## 비목표

- MewVG 코어가 Win32/macOS/X11 window policy를 알게 만들지 않는다.
- D2D/GDI/MewVG 중 하나의 backend 모델을 공통 모델로 강제하지 않는다.
- CPU bitmap과 GPU texture를 같은 이름으로 계속 숨기지 않는다.
- layered window를 renderer의 primitive로 만들지 않는다.

## 최종 방향

MewUI의 공통 렌더링 계층은 "어디에 그리는가"와 "어떻게 표시하는가"를 분리해야 한다.

- `RenderSurface`: 그릴 수 있는 자원
- `GraphicsContext`: surface에 draw command를 수행하는 객체
- `RenderDevice`: surface/resource/context 생성과 copy/readback 담당
- `WindowPresenter`: platform window에 최종 표시

이 구조가 잡히면 D2D/GDI/MewVG는 같은 개념을 서로 다른 native resource로 구현하게 되고,
layered window나 offscreen filter 같은 특수 경로도 backend-specific 우회가 아니라 capability 기반
선택으로 처리할 수 있다.
