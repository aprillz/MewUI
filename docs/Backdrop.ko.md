# 백드롭

## 요소 백드롭

`BackdropFilter`는 요소 뒤에 그려진 것에, 요소의 레이아웃 박스 안에서 필터를 적용합니다. 요소 자신의 배경과 내용은 그 위에 그려지므로, 반투명한 `Background`가 결과에 색을 입혀 반투명 유리처럼 보입니다.

```csharp
new Border()
    .CornerRadius(12)
    .Padding(12)
    .Background(Color.FromArgb(70, 255, 255, 255))
    .BackdropFilter(new BlurFilter(12, 12))
    .Child(new TextBlock().Text("Frosted glass"));
```

필터의 원본 입력은 요소 뒤의 픽셀입니다. 입력을 지정하지 않은 `BlurFilter`는 그 픽셀을 흐립니다. `BackdropFilter`를 `null`로 두면 백드롭이 없어집니다.

### 백드롭이 읽는 것

- 요소의 레이아웃 박스 안만 읽습니다. 블러는 박스 밖 자리에 박스 가장자리 픽셀을 늘려 쓰므로, 박스 밖의 내용은 백드롭에 나타나지 않고 박스 옆의 변경은 백드롭을 다시 그리게 하지 않습니다.
- 같은 창 안에서 요소 뒤에 그려진 것을 읽습니다. 뒤에 있는 다른 백드롭도 포함합니다. 창 뒤의 내용은 읽지 않습니다. 그것은 [시스템 백드롭](#시스템-백드롭)을 쓰세요.
- 반투명한 요소(`Opacity`가 1보다 작음)나 `CacheMode`가 지정된 요소 안에서는, 창이 아니라 그 요소가 그때까지 그린 것을 읽습니다.

백드롭은 요소의 `CornerRadius`와, 요소가 그려지는 둥근 클립에 맞춰 잘립니다.

`Opacity`가 1보다 작은 요소 자신은 뒤에 있는 것을 읽고, 백드롭과 함께 흐려집니다.

### 백드롭을 읽을 수 없을 때

요소 뒤를 읽을 수 없는 자리가 있습니다. 그 자리는 `BackdropFallbackBrush`로 채웁니다. 지정하지 않으면 그 자리에 아무것도 그리지 않고 요소 자신의 배경만 보입니다.

```csharp
border.BackdropFallbackBrush = new SolidColorBrush(Color.FromArgb(220, 40, 40, 48));
```

대체 브러시는 뒤가 비쳐 보이지 않으므로, 그것만으로도 요소의 내용이 잘 읽히는 색으로 정하세요.

### 비용

프레임은 백드롭 박스 아래에서 바뀐 부분만, 그 변경에서 블러가 닿는 거리까지만 다시 그립니다. 반투명한 요소나 캐시된 요소 안의 백드롭은 박스 안의 무엇이든 다시 그려지면 박스 전체를 다시 읽고 필터합니다. 박스가 크고 블러 반경이 클수록 비용이 큽니다.

## 시스템 백드롭

창이나 컨텍스트 메뉴는 투명한 부분을 플랫폼의 재질로 채우게 할 수 있습니다. 재질은 창 뒤의 내용을 흐린 것이나, 흐린 바탕 화면 배경입니다. 내용은 재질 위에 그려지므로, 반투명한 `Background`가 재질에 색을 입힙니다.

```csharp
new Window()
    .AllowsTransparency()
    .Background(Color.FromArgb(96, 32, 32, 32))
    .SystemBackdrop(SystemBackdrop.Acrylic)
    .BackdropFallbackBrush(Color.FromRgb(32, 32, 32));
```

창의 `SystemBackdrop`은 `AllowsTransparency`가 켜져 있을 때만 적용됩니다. 불투명한 창은 재질이 보일 자리를 덮기 때문입니다. 창을 보이기 전이든 후든 언제든 바꿀 수 있고, `SystemBackdrop.None`으로 없앱니다.

| 값 | 내용 뒤에 보이는 것 |
|---|---|
| `None` | 없음: 투명한 부분에 뒤의 내용이 그대로 보임 |
| `Blur` | 뒤의 내용을 흐린 것 |
| `Acrylic` | 뒤의 내용을 더 강하게 흐리고 채도를 높인 것 |
| `Mica` | 흐린 바탕 화면 배경. 창이 다른 창 위로 움직여도 바뀌지 않음 |

### 플랫폼별로 보이는 재질

플랫폼이 요청한 재질을 보일 수 없으면 보일 수 있는 가장 가까운 재질로 내립니다.

| 플랫폼 | Blur | Acrylic | Mica |
|---|---|---|---|
| Windows 11, Direct2D 백엔드 | Blur | Acrylic | Mica |
| Windows, MewVG 백엔드 | None | None | None |
| macOS | Blur | Acrylic | Acrylic |
| Linux KDE Plasma (X11) | Blur | Blur | Blur |
| 그 밖의 Linux 데스크톱, 브라우저, 프레임버퍼 | None | None | None |

이전 Windows 버전은 시스템 컴포지터가 제공하는 만큼 보이고, `Mica`부터 내립니다. MewVG 백엔드의 투명 창은 Windows에 픽셀을 레이어드 비트맵으로 넘기는데, 컴포지터의 재질은 그 비트맵을 비쳐 보이게 하지 못하고 가리므로 재질을 보이지 않습니다. Mica는 Windows 11 재질이라 macOS에는 대응하는 것이 없어 Acrylic으로 보입니다. macOS의 재질은 라이트·다크 모드를 따르는 시스템 재질입니다(popover, HUD window). KDE Plasma에서는 데스크톱의 블러 효과가 켜져 있을 때만 재질이 보입니다.

### 재질이 없을 때

`BackdropFallbackBrush`는 두 종류의 백드롭이 함께 쓰는 하나의 대체 수단입니다. 재질을 보일 수 없는 곳에서는 이 브러시가 `Background` 아래에서 창을 채우므로, 반투명한 색을 지정해도 내용이 읽힙니다.

```csharp
window.SystemBackdrop = SystemBackdrop.Mica;
window.BackdropFallbackBrush = Color.FromRgb(32, 32, 32);
```

대체 브러시가 없으면 재질 자리에 아무것도 그리지 않습니다.

### 컨텍스트 메뉴

```csharp
new ContextMenu()
    .SystemBackdrop(SystemBackdrop.Acrylic)
    .Item("Cut")
    .Item("Copy");
```

메뉴에서 연 하위 메뉴도 같은 재질을 따릅니다. 재질을 만드는 방식은 메뉴가 놓인 곳에 따라 다릅니다.

- 자신의 창에 있는 메뉴는 위 표대로 플랫폼의 재질을 메뉴 박스에만, 모서리에 맞춰 받습니다. 그림자는 재질 바깥에 남습니다.
- 브라우저처럼 소유자의 표면 안에 그려지는 메뉴는 그 표면에 있는 내용으로 같은 모양을 만듭니다. `Blur`는 블러이고, `Acrylic`은 더 강한 블러에 채도를 높인 것입니다. 표면은 바탕 화면 배경을 읽을 수 없으므로 `Mica`는 `Acrylic`으로 보입니다.

재질을 지정하면 테마의 불투명한 메뉴 배경은 재질이 비치도록 반투명한 색으로 바뀝니다. 메뉴에 직접 지정한 `Background`는 그대로 씁니다. 메뉴의 `BackdropFallbackBrush` 기본값은 테마의 메뉴 배경입니다.

### 비용

시스템 컴포지터는 창 뒤의 내용이 바뀔 때마다 재질을 다시 그리고, 창이 클수록 비용이 큽니다. 창 뒤가 바뀌지 않는 동안에는 비용이 들지 않습니다. 소유자의 표면 안에 그려지는 메뉴의 비용은 같은 크기의 요소 백드롭과 같습니다.
