# NativeAOT 크기 관리

MewUI는 사용한 기능만 NativeAOT 결과에 포함되는 특성을 프레임워크 계약으로 취급합니다. 기능을 추가해도 무관한 애플리케이션에 그 구현, constructed generic type, 메타데이터가 자동으로 포함되어서는 안 됩니다.

## 표준 probe

모든 비교는 같은 SDK와 publish 속성을 사용합니다.

| Probe | 내용 | 목적 |
|---|---|---|
| Empty | `Window`만 사용 | 플랫폼, 백엔드, 애플리케이션, 창의 최소 그래프 |
| Text | `TextBlock` 하나 | 텍스트 레이아웃과 렌더링 증분 |
| Button | 내용 없는 `Button` 하나 | 텍스트 내용 없이 컨트롤, 스타일, 입력, 명령 증분 측정 |
| Image | raw pixel `Image` 하나 | 인코딩 이미지 I/O 없이 이미지 컨트롤과 래스터 소스 증분 측정 |

주 회귀 기준은 Windows x64/GDI입니다. Direct2D, MewVG, Linux x64, macOS arm64도 릴리스 감사에서 같은 probe를 사용합니다.

## Publish 계약

- `net10.0`
- self-contained NativeAOT
- `TrimMode=full`
- `IlcOptimizationPreference=Size`
- invariant globalization
- 디버그 심볼 및 NativeAOT PDB 제외
- 실행 파일 크기는 bytes로 기록하고 MiB 표시는 이진 단위를 사용
- 도달성 분석을 위해 `IlcGenerateMapFile=true`

SDK, RID, 백엔드, publish 속성이 다른 결과를 소스 회귀로 직접 비교하지 않습니다.

## 측정 항목

사용자에게 보이는 기준은 실행 파일 bytes입니다. 원인 분석에는 최소한 다음 map 항목을 사용합니다.

- `MethodCode`
- `ConstructedEEType`
- embedded metadata
- 메서드 및 constructed type 개수
- 새로 도달한 대형 메서드와 타입

미사용 컨트롤의 동작 코드는 제거되면서 최소 타입 메타데이터만 남을 수 있습니다. 무관한 probe에서 상당한 구현 코드, 제네릭 인스턴스 또는 메타데이터가 새로 남으면 회귀입니다.

## 구조 규칙

1. `Application`, `Window`, 그래픽 백엔드와 Dispose 경로에서 선택 기능 구현을 직접 생성하지 않습니다.
2. lazy lookup을 위해 모든 선택 컨트롤이나 구현을 전역 레지스트리에 미리 열거하지 않습니다.
3. 기본 스타일과 템플릿은 컨트롤별 도달성을 보존해야 합니다. 스타일 하나가 모든 컨트롤을 루팅해서는 안 됩니다.
4. 공개 인터페이스와 virtual 멤버는 NativeAOT map으로 확인합니다. 호출하지 않은 것처럼 보여도 dispatch 때문에 구현이 남을 수 있습니다.
5. 서비스 추상화는 구체 생성 경로까지 제거 가능할 때만 pay-for-play입니다. 직접 생성자를 generic dictionary로 감싸는 것만으로는 충분하지 않습니다.
6. 크기 최적화에는 A/B 실행 파일 측정과 map 근거가 필요합니다. 소스 크기나 IL 확인만으로 결론 내리지 않습니다.

## 기준선과 예산

저장소의 JSON은 추가 회귀를 막기 위한 관측 기준선이며 목표 최소 크기가 아닙니다. 회귀가 발생한 뒤 설명 없이 기준선을 갱신해서는 안 됩니다.

최초 표준 Windows GDI probe에서 Empty, Text, Button 실행 파일이 모두 4,291,584 bytes로 측정되었습니다. Text의 `MethodCode`는 Empty보다 139 bytes만 증가했고 Button은 실행 파일 증분이 없었습니다. Image 증분은 55,808 bytes였습니다. Empty/Text/Button가 같은 것은 해당 기능이 무료라는 뜻이 아니라 선택 기능 그래프가 이미 Empty에서 도달 가능하다는 근거입니다.

SDK 10.0.301로 2026년 7월 5일 기준 커밋 `df7f5b2a`를 다시 빌드한 Empty GDI는 2,987,008 bytes입니다. 현재 조사 값은 4,290,560 bytes로 1,303,552 bytes 증가했습니다. 임시 측정 빌드에서 `ManagedTextEngine`과 `ManagedTextRenderContext`만 제거하면 390,144 bytes가 감소했으며, 그 밖의 새 도달 코드와 타입 그래프가 913,408 bytes 남았습니다. 이는 조사 근거이며 지원되는 텍스트 비활성화 모드가 아닙니다.

기준선 변경에는 다음이 필요합니다.

1. 변경 전후 report;
2. 변경된 probe의 map 비교;
3. 요청된 기능 또는 도구 체인 변경에 증가분을 귀속한 설명;
4. 예산 상향에 대한 명시적 검토.

## 실행

```powershell
./tools/aot-size/Measure-AotSize.ps1

./tools/aot-size/Measure-AotSize.ps1 `
  -BaselinePath ./tools/aot-size/baselines/win-x64-gdi.json

./tools/aot-size/Update-ReleaseSizeAssets.ps1
```

## 기본 스타일 등록 패턴

기본 스타일은 중앙 테이블에서 모든 컨트롤을 참조하지 않습니다. 스타일을 가진 각 컨트롤은 자신의 원본 타입 선언과 명시적 정적 생성자에서 해당 팩토리만 `DefaultStyles`에 등록합니다. 따라서 NativeAOT 트리머는 실제로 도달 가능한 컨트롤의 스타일 구현만 유지할 수 있습니다.

프레임워크 named style도 팩토리가 실제로 요청될 때 자신이 상속할 기본 스타일만 보장합니다. 공개 `Style.ForType(Type)`과 `Style.DeriveFromDefault<T>()`는 동적 사용을 위해 요청된 컨트롤 계층의 정적 초기화를 실행합니다.

기본 스타일 컨트롤을 추가할 때는 다음 규칙을 따릅니다.

1. 컨트롤 선언 상단에 해당 팩토리 등록을 추가한다.
2. 정적 생성자가 없다면 명시적으로 추가한다.
3. 스타일 팩토리는 `internal`로 유지하며 중앙 팩토리 목록을 만들지 않는다.
4. 큰 선택 기능을 추가한다면 probe 또는 map 비교로 도달 범위를 확인한다.

SDK 10.0.301에서 컨트롤 소유 등록 적용 후 Windows x64/GDI 결과는 Empty 3,409,408 bytes, Text 3,788,800 bytes, Button 3,844,608 bytes, Image 3,465,728 bytes입니다. Empty는 기존 중앙 등록 결과보다 882,176 bytes(20.6%) 작고, Text의 379,392-byte 증가분이 분리되어 텍스트 엔진이 다시 pay-for-play로 동작합니다.

프레임워크 named style을 사용하는 컨트롤로 이동하고, encoded image decoding을 백엔드 디스패치 슬롯에서 제거하고, 창 아이콘 디코딩을 `IconSource` 뒤로 이동하고, 텍스트 서비스를 실제 생성된 경우에만 정리하도록 바꾼 뒤 결과는 Empty 3,037,184 bytes, Text 3,425,792 bytes, Button 3,492,864 bytes, Image 3,448,320 bytes입니다. Empty map에는 LibJpeg, 기본 이미지 디코더, 파일 다이얼로그 스타일, 선택 기본 스타일, Panel 구현 메서드가 없습니다.

플랫폼 tracing은 Debug 전용입니다. Release와 NativeAOT 빌드는 `TracingPlatformHost` 및 backend/dispatcher wrapper를 컴파일하지 않습니다. 컨트롤 소유 등록 초기화를 명시적으로 만들고 텍스트 서비스 생성을 경쟁 안전하게 만든 뒤 probe 크기는 Empty 3,022,336 bytes, Text 3,412,480 bytes, Button 3,479,040 bytes, Image 3,433,472 bytes이며 map에 tracing 타입이 없습니다.

report, 실행 파일, 복사된 map은 `.artifacts/aot-size/` 아래에 생성됩니다.
릴리스 전 동일 조건으로 Hello World와 Gallery를 게시해 `release-sizes.json`을 갱신하고 `Update-ReleaseSizeAssets.ps1`을 실행한 뒤 JSON과 생성된 SVG를 함께 커밋합니다. README 링크는 바뀌지 않습니다. 검증 작업에서는 `-Check`로 오래된 SVG를 거부합니다.
