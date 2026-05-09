# 렌더 Surface 사용처 분류

## 목적

`render-surface-architecture.md`의 다음 단계로, 현재 코드셋에서 render target, bitmap target,
offscreen target, image source, external texture가 어떤 의미로 사용되는지 분류한다.

이 문서는 API를 바로 바꾸기 위한 문서가 아니라, 기존 호출의 의도를 고정하는 inventory다.
이 분류가 끝나야 `IRenderSurface`, `IRenderDevice`, `IWindowPresenter`, external sample source
분리가 안전하게 가능하다.

## 요약

현재 `IBitmapRenderTarget`은 최소 여섯 가지 용도로 쓰인다.

- CPU pixel buffer
- vector drawing이 가능한 offscreen target
- GPU-resident filter intermediate
- image source로 wrap 가능한 cached surface
- window frame을 bitmap으로 렌더링하기 위한 present source
- SVG pattern/filter cache backing store

`CreateImageFromPixelSource`도 단순 CPU image upload만 의미하지 않는다.

- `WriteableBitmap`/CPU pixel source upload
- backend offscreen target을 image view로 wrap
- GPU texture source zero-copy wrap
- cache target을 image로 유지

`CreateImageFromExternalTexture`는 video zero-copy 경로에서 이미 별도 추상화가 필요한 상태다.

추가로, 현재 코드는 비동기 완료 모델을 이미 사용한다.

- SVG sample의 worker render + UI thread commit
- OpenGL PBO + fence 기반 async upload
- OpenGL/Metal deferred readback
- Metal command buffer completion 이후 release/readback
- video frame ready event와 dispatcher invalidation

따라서 surface API는 "surface를 만들 수 있는가"뿐 아니라 "작업 완료 시점을 어떻게 표현하는가"도 포함해야 한다.

## 사용처 분류표

| 영역 | 현재 API | 현재 의미 | 필요한 capability | 향후 API 형태 |
| --- | --- | --- | --- | --- |
| `WriteableBitmapControl` | `CreateBitmapRenderTarget` | CPU-writable bitmap + optional vector draw target | `Renderable`, `CpuReadable`, `CpuWritable`, `CacheableImageSource` | `CreateSurface(Usage=Offscreen|ImageSource, Capabilities=CpuWritable|Renderable)` |
| `WriteableBitmapControl` | `CreateImageFromPixelSource` | versioned pixel target 위의 장기 image view | `ImageSource`, version tracking | `CreateImageView(surface)` 또는 `CreateImageFromPixelSource(source)` |
| WriteableBitmap samples | `CreateImageFromPixelSource` | `WriteableBitmap` 위의 image view | `CpuReadable` source upload 또는 dynamic image view | resource API 유지, 이후 `IImageSource`로 매핑 |
| `ColorPickerPopup` | `CreateImageFromPixelSource` | 생성된 bitmap 위의 image view | `CpuReadable`, `CacheableImageSource` | pixel-source image creation 유지 |
| `SvgDocument` raster export | `CreateBitmapRenderTarget` | SVG를 bitmap으로 렌더링 후 image 반환 | `Renderable`, `CpuReadable`, `ImageSource` | CPU bitmap surface 또는 export surface |
| `MewUI.Svg.Sample SvgView` | `CreateOffscreenRenderTarget` | worker-built cached SVG viewport | `Renderable`, `ImageSource`, preferably `GpuSampleable`, worker-safe | `CreateSurface(Usage=Offscreen|ImageSource, Capabilities=Renderable|GpuSampleable)` |
| `SvgFilter.MewUI` source layer | `CreateOffscreenRenderTarget` | filter source raster | `Renderable`, `ImageSource`, `FilterIntermediate` | `CreateSurface(Usage=FilterSource|FilterIntermediate)` |
| `ScratchRenderTargetPool` | `CreateOffscreenRenderTarget` | pooled filter intermediate | `Renderable`, `FilterIntermediate`, optional `GpuSampleable`, optional CPU fallback | filter surface pool backed by `IRenderDevice` |
| `DefaultFilterContext` | `CreateImageFromPixelSource` | scratch/source target을 image로 wrap | `ImageSource`, filter result lifetime | `CreateImageView(surface)` |
| `SvgPatternServer.MewUI` | `CreateBitmapRenderTarget` | cached pattern tile | `Renderable`, `ImageSource`, `CacheableImageSource` | `CreateSurface(Usage=CachedImageSource|ImageSource)` |
| Window layered present | `RenderFrameToBitmap(IBitmapRenderTarget)` | window frame을 presenter intermediate bitmap으로 렌더링 | `Renderable`, `LayeredPresentSource`, `CpuReadable`, `Premultiplied` | presenter-owned intermediate surface |
| Video CPU fallback | `CreateImageFromPixelSource(frame)` | decoded BGRA frame upload | `CpuReadable`, `ImageSource`, frame lifetime | `CreateImageFromPixelSource` 또는 `CreateImageFromCpuFrame` |
| Video D2D zero-copy | direct D2D helper | D3D11/DXGI surface를 D2D image로 wrap | `ExternalHandle`, `GpuSampleable`, backend-specific device match | external sample source / native image wrapper |
| Video MewVG GL/Metal zero-copy | `CreateImageFromExternalTexture` | acquire/release가 필요한 external GPU texture sample | `ExternalHandle`, `GpuSampleable`, `ExternallySynchronized` | `CreateImageFromExternalSource(IExternalSampleSource)` |
| VAAPI path | `CreateImageFromExternalTexture` | dma_buf/EGLImage GL texture source, possible YUV source | `ExternalHandle`, `GpuSampleable`, possible color conversion | external sample source + conversion surface |
| OpenGL PBO upload | internal async image path | CPU pixels를 PBO로 업로드하고 fence로 완료 확인 | `AsyncCompletion`, `ExternallySynchronized`, `GpuSampleable` | async upload operation + external sample source |
| GPU readback | `RequestDeferredReadback` / `GetPixelSpan` | GPU target의 CPU buffer를 필요 시점에 동기화 | `CpuReadable`, `DeferredReadback`, `AsyncCompletion` | `RequestReadback()` / `IRenderOperation` |

## 직접 호출 지점

### `CreateBitmapRenderTarget`

#### `src/MewUI/Controls/WriteableBitmapControl.cs`

용도:

- control의 persistent backing bitmap 유지
- `IGraphicsContext` drawing과 `GetPixelSpan` 직접 pixel write 모두 허용
- `IImage`를 한 번 만들고 version tracking에 의존

의미:

- 실제 CPU pixel surface 사용처다.
- GPU-only offscreen texture로 강제하면 안 된다.
- 향후 계약에는 `Renderable`이면서 `CpuWritable`인 surface가 필요하다.

#### `src/MewUI.Svg/SvgDocument.cs`

용도:

- 전체 SVG document를 bitmap-sized target에 rasterize
- target에서 image 생성 후 반환

의미:

- raster export/image creation 경로다.
- 호출자 기대에 따라 CPU-readable output이 필요할 수 있다.
- export 또는 CPU bitmap surface를 명시적으로 요청하는 형태가 맞다.

#### `src/MewUI.Svg/ThirdParty/Svg/Painting/SvgPatternServer.MewUI.cs`

용도:

- pattern child들을 tile target에 렌더링
- target을 image로 wrap
- target과 image를 pattern server lifetime 동안 cache

의미:

- 일반적인 "bitmap" 용도가 아니라 cacheable image-source surface다.
- sampling 가능하고 lifetime이 안전하다면 GPU-backed target도 가능하다.
- CPU writability는 필요하지 않다.

#### Backend 구현

- `Direct2DGraphicsFactory.CreateBitmapRenderTarget`
- `GdiGraphicsFactory.CreateBitmapRenderTarget`
- `MewVGGraphicsFactory.CreateBitmapRenderTarget`

의미:

- 같은 메서드 이름이 backend마다 다른 물리 자원을 반환한다.
- 향후 descriptor가 usage와 required capability를 전달해야 한다.

### `CreateOffscreenRenderTarget`

#### `src/MewUI/Rendering/Filters/ScratchRenderTargetPool.cs`

용도:

- image filter graph evaluation을 위한 scratch target pool
- GPU backend가 filter pass를 GPU에 유지할 수 있도록 `CreateOffscreenRenderTarget` 사용

의미:

- filter-surface pool로 분리하는 것이 맞다.
- 반환 target은 renderable이면서 image-source로 사용할 수 있어야 한다.
- CPU fallback에는 read/write access 또는 신뢰할 수 있는 readback 경로가 필요하다.

#### `src/MewUI.Svg/ThirdParty/Svg/Filter Effects/SvgFilter.MewUI.cs`

용도:

- filtered element의 source layer allocation
- SVG content를 source layer에 다시 렌더링
- source layer를 `IImage`로 wrap하고 filter DAG 평가

의미:

- 일급 filter source 사용처다.
- scale-aware sizing, image wrapping, filter intermediate compatibility, result cache lifetime이 모두 필요하다.

#### `samples/MewUI.Svg.Sample/Controls/SvgView.cs`

용도:

- worker thread에서 cached SVG viewport build
- `AcquireBackgroundRenderScope` 사용
- target에서 image를 만들고 UI thread에서 cache state swap

의미:

- worker-safe offscreen render/cache surface다.
- GL은 background context/share-list support가 필요하다.
- D2D/GDI/Metal은 threading 요구사항이 다르므로 thread scope는 일반 factory 책임으로 계속 두기 어렵다.

## 캐시 사용처

### Filter result cache

현재 위치:

- `SvgFilter.MewUI`

현재 동작:

- filter result를 `IBitmapRenderTarget + IImage` 조합으로 보관한다.
- cache hit 시 filter pipeline을 건너뛰고 cached image를 draw한다.
- replacement/eviction 시 draw command가 아직 image를 참조할 수 있어 pending disposal queue를 사용한다.

의미:

- cache entry는 surface와 image view를 함께 소유해야 한다.
- eviction은 즉시 dispose가 아니라 `safeAfter` operation 이후 dispose가 될 수 있다.
- cache key는 effective scale, region size, output size, source version/device identity를 포함해야 한다.

### Pattern tile cache

현재 위치:

- `SvgPatternServer.MewUI`

현재 동작:

- pattern tile target과 image를 server lifetime 동안 보관한다.
- tile size, viewBox, content units, content bounds를 key처럼 사용한다.

의미:

- persistent cached image source다.
- CPU writable은 필요하지 않다.
- backend/device invalidation 시 cache를 버릴 수 있어야 한다.

### Viewport snapshot cache

현재 위치:

- `MewUI.Svg.Sample SvgView`

현재 동작:

- worker thread에서 viewport target/image를 만들고 UI thread에서 swap한다.
- 이전 cache는 새 cache가 commit되면 dispose한다.

의미:

- cache entry 생성 thread와 소비 thread가 다를 수 있다.
- handoff 전까지 target/image ownership이 명확해야 한다.
- failed/cancelled build의 partial target/image disposal이 필요하다.

### Scratch target delayed return

현재 위치:

- `ScratchRenderTargetPool`
- `DefaultFilterContext`
- MewVG image wrapping path

현재 동작:

- scratch target을 image로 wrap한 뒤 draw command가 아직 참조할 수 있다.
- 즉시 pool에 반환하면 같은 target이 재사용되어 렌더링이 깨질 수 있다.

의미:

- scratch pool도 cache/lifetime 문제의 일부다.
- `Return(surface)` 대신 `Return(surface, safeAfter)` 형태가 필요하다.
- `IImage.TrySetPostReleaseCallback` 같은 특수 hook은 공통 release-later 계약으로 일반화하는 편이 낫다.

## 캐시 계약 요구사항

필요한 최소 계약:

- `RenderCacheKey`
- `IRenderCacheEntry`
- `IRenderResourceCache`
- `ReleaseLater(IDisposable resource, IRenderOperation safeAfter)`
- device lost / dpi changed / source invalidated trim reason

중요한 원칙:

- backend 공통 API에는 `Svg` 또는 `Video` 같은 도메인 이름을 넣지 않는다.
- 도메인별 cache는 common cache kind와 key로 일반화한다.
- cache entry가 image만 들고 surface를 잃어버리면 안 된다.
- GPU command completion 전 dispose가 가능한 backend는 없다.

#### `Direct2DGraphicsFactory.CreateOffscreenRenderTarget`

용도:

- shared device가 가능하면 GPU-only `Direct2DGpuBitmapRenderTarget` 반환 가능

의미:

- `IBitmapRenderTarget`이 이미 GPU render target으로도 쓰이고 있음을 보여준다.
- 향후에는 CPU bitmap이 아니라 `IRenderSurface + ID2DTextureSource` capability로 표현해야 한다.

### `CreateImageFromPixelSource`

#### Dynamic CPU image 경로

호출자:

- `WriteableBitmap`
- `ImageSource`
- `ColorPickerPopup`
- `MewUI.WriteableBitmapSample`

용도:

- CPU/versioned pixel source를 현재 backend가 그릴 수 있는 image로 변환

의미:

- 이 API 자체는 여전히 필요하다.
- source가 render target이 아닐 수도 있으므로, "render surface에서 image view 생성"과 분리해야 한다.

#### Surface image-view 경로

호출자:

- SVG filter source/result
- SVG pattern tile
- SVG sample viewport cache
- `WriteableBitmapControl` target image

용도:

- render target을 image source로 wrap

의미:

- `CreateImageView(IRenderSurface)` 또는 이에 준하는 API로 분리하는 것이 맞다.
- 현재는 `IPixelBufferSource` 경로 뒤에 `IGpuTextureSource` 같은 marker interface로 zero-copy가 숨어 있다.

### `CreateImageFromExternalTexture`

호출자:

- `samples/MewUI.Video.Sample/Controls/VideoView.cs`

provider:

- `WglDxInteropTexture`
- `VideoToolboxFrameTexture`
- `VaapiDmaBufTexture`
- `PboFenceUploader`

용도:

- externally-owned GPU resource를 image로 wrap
- backend가 `DrawImage` 중 sample
- `Acquire`/`Release`가 실제 GPU 사용을 감쌈

의미:

- render surface creation 경로가 아니다.
- external sample source/image-source creation으로 분리해야 한다.
- native handle kind, alpha mode, Y-flip, size, synchronization, optional color conversion이 명시되어야 한다.

## 비동기 사용처

### SVG sample worker render

현재 위치:

- `samples/MewUI.Svg.Sample/Controls/SvgView.cs`

현재 동작:

- `Task.Run`으로 worker thread에서 offscreen target 생성
- `AcquireBackgroundRenderScope`로 backend별 worker render 준비
- target에 SVG를 렌더링
- `CreateImageFromPixelSource(target)`로 image 생성
- dispatcher `BeginInvoke`로 UI thread에 cache swap

의미:

- surface는 생성 thread와 소비 thread가 다를 수 있다.
- GL backend는 share-listed context가 필요하다.
- render 완료 후 UI commit 전까지 target/image lifetime을 유지해야 한다.
- future API에는 worker render scope와 completion/ownership handoff가 명확해야 한다.

### OpenGL PBO async upload

현재 위치:

- `PboFenceUploader`
- `PboFenceUploaderPool`
- `MewVGGraphicsFactory.TryCreateAsyncUploadImage`

현재 동작:

- CPU pixel source를 PBO로 업로드
- `glFenceSync`로 upload completion 추적
- 다음 사용 시 `glClientWaitSync`로 이전 upload 완료 확인
- `IExternalLockedTexture` 형태로 MewVG image에 연결

의미:

- image creation과 GPU upload completion이 같은 시점이 아니다.
- source pixel lifetime과 texture sampling 가능 시점을 구분해야 한다.
- external sample source는 sync/fence 정보를 포함해야 한다.

### Deferred readback

현재 위치:

- `OpenGLBitmapRenderTarget.RequestDeferredReadback`
- `OpenGLBitmapRenderTarget.FlushFboReadbackIfNeeded`
- `MewVGMetalBitmapRenderTarget.RequestDeferredReadback`
- `Direct2DGpuBitmapRenderTarget.ReadbackToBuffer`

현재 동작:

- GPU target에 그린 뒤 즉시 CPU buffer를 갱신하지 않는다.
- CPU consumer가 `CopyPixels` 또는 `GetPixelSpan`을 호출할 때 readback을 flush한다.
- filter GPU path는 여러 중간 결과의 sync point를 줄이기 위해 deferred readback을 사용한다.

의미:

- `CpuReadable`은 "항상 CPU memory가 최신"이라는 뜻이 아니다.
- `DeferredReadback` capability와 `RequestReadback` operation이 필요하다.
- CPU fallback executor가 언제 readback barrier를 만드는지 명확해야 한다.

### Metal command completion

현재 위치:

- `MewVGMetalBitmapRenderTarget.RequestDeferredReadback(commandBuffer)`
- `MewVGMetalGraphicsContext`
- `MetalImageFilterExecutor`
- `IExternalLockedTexture` 문서의 command buffer completion release 설명

현재 동작:

- command buffer가 submit/complete되는 시점이 resource safety boundary가 된다.
- readback 또는 external texture release가 `EndFrame` 직후가 아니라 command completion 이후일 수 있다.

의미:

- `EndFrame`은 "CPU가 command를 기록했다"는 의미일 수 있고, "GPU가 작업을 끝냈다"는 의미가 아닐 수 있다.
- surface/image/scratch target 반환은 completion boundary를 알아야 한다.

### Video frame readiness

현재 위치:

- `VideoPlayback.FrameReady`
- `VideoView.OnPlaybackFrameReady`

현재 동작:

- decoder/playback thread가 frame ready event를 발생
- UI thread dispatcher가 `InvalidateVisual`을 예약
- render 시점에 current frame을 pull하고 image/external texture로 연결

의미:

- producer frame lifetime과 render frame lifetime이 분리되어 있다.
- zero-copy path에서는 frame recycling이 GPU sampling 완료보다 앞서면 안 된다.
- external sample source 계약은 frame owner와 renderer 사이의 lifetime handoff를 표현해야 한다.

## Presenter와 window frame 사용

현재 호출자:

- `GdiGraphicsFactory.Present`
- `Direct2DGraphicsFactory.Present`
- `MewVGGraphicsFactory.Present`

현재 동작:

- platform window surface가 `IWindowSurfacePresenter` 형태로 graphics factory에 전달된다.
- layered/composited presentation 경로는 bitmap/offscreen target을 allocate 또는 reuse한다.
- `Window.RenderFrameToBitmap(IBitmapRenderTarget)`가 visual tree를 해당 target에 렌더링한다.

의미:

- presentation이 현재 graphics factory에 결합되어 있다.
- intermediate target 요구사항은 presenter-specific이다.
  - renderable
  - alpha-capable
  - 보통 premultiplied BGRA
  - platform update API가 읽을 수 있거나, 그 buffer로 변환 가능
- 이 경로는 `IWindowPresenter`와 presenter-owned intermediate surface로 분리해야 한다.

## 숨겨진 capability 역할을 하는 backend marker interface

현재 marker/capability interface:

- `IGpuTextureSource`
- `IGLTextureSource`
- `IMetalTextureSource`
- `ID2DTextureSource`
- `IWin32HdcSource`
- `IExternalLockedTexture`

이들은 이미 `IRenderTarget` 바깥에서 capability-like 정보를 표현하고 있다.

문제:

- capability model이 암묵적이다.
- 호출자는 `IBitmapRenderTarget`을 요청하고, backend code가 concrete cast로 hidden property를 발견한다.
- cross-backend behavior가 concrete type과 marker availability에 의존한다.

향후 방향:

- 공통 개념은 explicit surface capability로 승격한다.
- API-specific detail은 backend-specific optional interface 뒤에 둔다.
- allocation 시점에 descriptor flag로 usage를 명시한다.

## Inventory에서 도출되는 Factory 책임 분리

이 inventory는 `IGraphicsFactory`를 더 작은 책임으로 나눌 근거를 제공한다.

### Resource factory에 남기거나 이동

- `CreateSolidColorBrush`
- `CreatePen`
- `CreateLinearGradientBrush`
- `CreateRadialGradientBrush`
- `CreateImageBrush`
- `CreateFont`
- `CreateImageFromFile`
- `CreateImageFromBytes`
- CPU `CreateImageFromPixelSource`

### Render device로 이동

- `CreateContext`
- `CreateBitmapRenderTarget`
- `CreateOffscreenRenderTarget`
- surface copy/readback/resolve
- render surface에서 image view 생성
- render/readback/upload operation completion 관리

### Image/filter device로 이동

- `CreateImageFilterExecutor`
- scratch filter target allocation policy
- SVG filter intermediate compatibility

### External image/source device로 이동

- `CreateImageFromExternalTexture`
- external GPU handle wrapping
- acquire/release synchronization
- Y-flip/alpha metadata
- optional color conversion

### Presenter로 이동

- `IWindowSurfacePresenter.Present`
- layered/composited present intermediate allocation
- platform-specific final update call

### Backend threading/scheduler service로 이동

- `AcquireBackgroundRenderScope`
- `AcquireConcurrentRenderUnit`
- GL worker context activation
- D2D multithread/no-op scope
- Metal/GDI no-op 또는 command scheduling semantics
- worker render completion과 UI commit handoff

## Migration 순서

### Step 1: 동작 변경 없이 descriptor 추가

추가:

- `RenderSurfaceDescriptor`
- `SurfaceUsage`
- `SurfaceCapabilities`
- `IRenderOperation` 또는 completion handle 초안

초기에는 기존 `IBitmapRenderTarget` 반환을 유지한다. descriptor는 새 overload 또는 internal helper path에 먼저 도입할 수 있다.

### Step 2: filter와 SVG 호출자 먼저 이관

대상:

- `ScratchRenderTargetPool`
- `SvgFilter.MewUI`
- `SvgPatternServer.MewUI`
- `MewUI.Svg.Sample SvgView`

이유:

- 이 call site들은 이미 offscreen/filter/cache 의도가 명확하다.
- 현재 overloaded target model에 가장 강한 압력을 주는 사용처다.

### Step 3: image view와 pixel source upload 분리

추가:

- `CreateImageView(IRenderSurface surface)`
- CPU/versioned source용 `CreateImageFromPixelSource(IPixelBufferSource source)`는 유지

render-target-backed image 경로를 `CreateImageView`로 이관한다.

이 단계에서 image view가 surface를 borrow하는지 retain하는지, GPU command completion 전 dispose가 가능한지까지 정해야 한다.

### Step 4: external sample source 도입

추가:

- `IExternalSampleSource` 또는 `IExternalLockedTexture`의 rename/evolution
- `CreateImageFromExternalSource`

Video.Sample과 PBO uploader 경로를 이관한다.

이 단계에서 acquire/release timing과 async upload fence를 명시한다.

### Step 5: presenter 분리

추가:

- `IWindowPresenter`
- presenter-owned intermediate allocation

layered/composited present logic을 `IGraphicsFactory` 구현에서 분리한다.

### Step 6: factory 축소

호출자 이관 후:

- 필요하면 compatibility `IGraphicsFactory` facade 유지
- 내부적으로 resource factory, render device, filter device, external source device, presenter,
  threading/scheduler service에 위임

## 열린 질문

- filter CPU fallback이 모든 scratch target에 `CpuWritable`을 요구해야 하는가, 아니면 CPU executor가 별도 CPU scratch surface를 요청해야 하는가?
- `CreateImageView(IRenderSurface)`는 surface ownership을 가져야 하는가, 아니면 borrow해야 하는가?
- cached image view가 GPU target을 command flush 이후까지 안전하게 붙잡는 lifetime 계약은 어디에 둘 것인가?
- `IRenderResourceCache`는 public API인가, rendering internal service인가?
- cache key의 backend device identity는 누가 제공하는가?
- `IExternalLockedTexture`를 더 일반적인 `IExternalSampleSource`로 rename/adapt할 것인가?
- video external source의 YUV-to-RGB conversion은 어디에 둘 것인가?
- `AcquireBackgroundRenderScope`는 render-device 책임인가, scheduler 책임인가?
- `EndFrame`이 반환할 completion handle이 필요한가, 아니면 별도 `FlushAsyncWork`가 충분한가?
- `CpuReadable` surface의 readback은 synchronous API로 유지할 것인가, `RequestReadback` + wait API로 분리할 것인가?
- worker render 결과를 UI thread에서 consume할 때 ownership transfer API가 필요한가?
