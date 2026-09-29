# Drag and Drop

`AllowDrop`을 켠 요소는 드래그를 받고, `CanDrag`를 켠 요소는 드래그를 시작합니다. 데이터는 `IDataObject`로 전달됩니다. MewUI 요소 사이의 드래그와 다른 앱에서 온 드래그(파일 관리자의 파일, 브라우저의 텍스트 등)는 같은 이벤트로 오고 같은 방법으로 읽습니다.

```text
다른 앱 ──(OS 드래그)──┐
                        ▼
MewUI 요소 (CanDrag) ──► 창 (AllowDrop) ── 포인터 아래 요소부터 창까지, AllowDrop을 켠 요소들
                                            DragEnter / DragOver / DragLeave / Drop
```

## 드롭 받기

다른 앱에서 오는 드래그를 받으려면 창에 `AllowDrop`을 켜고, 드래그에 참여할 요소마다 `AllowDrop`을 켭니다. 이벤트는 포인터 아래 요소부터 창까지 이어지는 경로에서 `AllowDrop`을 켠 요소들에 전달됩니다.

- `DragEnter`와 `DragLeave`는 포인터가 들어가거나 나간 요소마다 한 번씩 발생합니다.
- `DragOver`와 `Drop`은 가장 안쪽 요소에서 시작해, 처리기가 드래그를 수락하거나 `Handled`를 설정할 때까지 위로 올라갑니다.
- `Drop` 뒤에는 `DragLeave`가 오지 않습니다.

처리기는 `DragOver`에서 `Accepted`나 `Effect`를 설정해 수락합니다. `Effect`는 소스의 `AllowedEffects` 중 하나여야 하며, 그 밖의 값은 `None`으로 처리됩니다.

```csharp
list.AllowDrop = true;
list.DragOver += e =>
{
    e.Accepted = e.Data.Contains(StandardDataFormats.StorageItems);
    e.Effect = DragDropEffects.Copy;
};
list.Drop += e =>
{
    if (e.Data.TryGetData(DataFormats.StorageItems, out var paths))
    {
        AddFiles(paths);
    }
};
```

어떤 처리기도 `Accepted`나 `Effect`를 설정하지 않으면, 표준 형식(아래)을 실은 드래그는 수락되고 소스가 허용하면 효과는 `Copy`입니다. `Accepted = false`로 거절한 처리기의 결정은 그대로 유지됩니다. 플랫폼 형식만 실은 드래그는 처리기가 수락해야 합니다.

Windows에서는 UI 스레드가 STA일 때만 다른 앱의 드래그가 온전히 전달됩니다. `Main`에 `[STAThread]`를 붙이십시오. MTA 스레드에서는 파일 드롭만 `DragEnter`, `DragOver`, `DragLeave` 없이 `Drop`으로 옵니다.

## 데이터 읽기

`DataFormats`는 형식과 그 값의 타입을 함께 나타내고, `TryGetData`는 그 타입으로 값을 돌려줍니다.

### 표준 형식

표준 형식은 모든 플랫폼에서 같은 방식으로 읽힙니다.

| 형식 | 값 | 내용 |
|---|---|---|
| `DataFormats.StorageItems` | `IReadOnlyList<string>` | 로컬 파일과 폴더의 절대 경로 |
| `DataFormats.Uris` | `IReadOnlyList<string>` | 소스가 보낸 그대로의 절대 URI. 로컬 파일이 아닌 항목(`https://`, `smb://`, `zip:///...` 같은 압축 파일 안 항목)도 포함하며, 로컬 파일은 `file://` URI로 들어 있음 |
| `DataFormats.Text` | `string` | 일반 텍스트 |

압축 파일에서 끌어낸 파일은 로컬 경로가 없으므로 `Uris`에만 있고 `StorageItems`에는 없습니다.

### 플랫폼 형식

다른 앱에서 온 드래그는 소스가 제공한 모든 형식도 플랫폼 고유의 이름으로 함께 싣습니다. `DataFormats.FromPlatformName(name)`은 그 형식을 소스가 보낸 바이트 그대로 읽습니다.

```csharp
if (e.Data.TryGetData(DataFormats.FromPlatformName("text/html"), out var html))
{
    ShowHtml(Encoding.UTF8.GetString(html));
}
```

| 플랫폼 | 형식 이름 | 예 |
|---|---|---|
| Windows | 클립보드 형식 이름: 미리 정의된 형식은 상수 이름, 등록 형식은 등록 이름 | `CF_HDROP`, `CF_UNICODETEXT`, `HTML Format`, `UniformResourceLocatorW` |
| Linux (X11) | MIME 타입과 아톰 이름 | `text/uri-list`, `text/html`, `UTF8_STRING` |
| macOS | 붙여넣기 보드 형식 식별자 | `public.file-url`, `public.html`, `public.png` |

`Formats`는 표준 형식을 먼저, 그다음 플랫폼 형식을 소스가 보낸 순서대로 나열합니다. Windows에서는 내용 전체를 전역 메모리로 주는 형식만 나열됩니다. macOS에서는 파일, URL, 텍스트, HTML, 서식 있는 텍스트, 이미지 중 하나를 실은 드래그만 창에 도착합니다.

### 값을 읽을 수 있는 때

형식 목록은 드래그가 들어올 때 정해지고, 각 값은 처음 요청할 때 소스에서 읽습니다. 값은 `Drop`뿐 아니라 `DragEnter`, `DragOver`에서도 읽을 수 있습니다. 드래그가 끝나면 데이터 객체는 소스를 놓습니다. 그 전에 읽은 값은 남고, 읽지 않은 값은 더 읽을 수 없습니다. 필요한 값은 `Drop` 처리기 안에서 복사해 두십시오.

Linux에서는 값을 읽을 때마다 소스 앱에 요청하고 최대 2초까지 응답을 기다리므로, 필요한 형식만 읽으십시오. 소스가 나눠서 보내는 데이터(큰 이미지 등)는 읽을 수 없습니다.

### 문자열 키

형식 이름은 문자열로도 쓸 수 있고, `IDataObject`의 문자열 멤버도 그대로 있습니다.

```csharp
e.Data.Contains(StandardDataFormats.Uris);
e.Data.TryGetData<IReadOnlyList<string>>(StandardDataFormats.Uris, out var uris);
e.Data.GetData("text/uri-list"); // 플랫폼 형식: byte[]
```

## 드래그 시작하기

요소에 `CanDrag`를 켭니다. 마우스를 누른 채 드래그 임계값보다 멀리 움직이면 `DragStarting`이 발생합니다. `Data`를 설정하면 드래그가 시작되고, `null`로 두거나 `Cancel`을 설정하면 시작하지 않습니다.

```csharp
static readonly DataFormat<Card> CardFormat = DataFormats.Create<Card>("application/x-myapp.card");

card.CanDrag = true;
card.DragStarting += e =>
{
    var data = new DataObject();
    data.SetData(CardFormat, card.Model);
    data.SetText(card.Model.Title);
    e.Data = data;
    e.AllowedEffects = DragDropEffects.Move | DragDropEffects.Copy;
};
card.DragCompleted += e =>
{
    if (e.FinalEffect == DragDropEffects.Move)
    {
        RemoveCard(card.Model);
    }
};

board.Drop += e =>
{
    if (e.Data.TryGetData(CardFormat, out var moved))
    {
        Place(moved);
    }
};
```

`DataFormats.Create<T>(name)`은 앱 자체 데이터를 위한 형식을 정의하며, 값은 그 이름으로 저장한 .NET 객체입니다. `DragCompleted`는 대상이 고른 효과(드롭이 거절되었거나 빈 곳에서 놓았으면 `None`), 취소 여부, 드래그가 끝난 화면 위치를 알려 줍니다. `BeginDrag(data, allowedEffects, preview)`는 마우스 동작 없이 코드에서 드래그를 시작합니다.

MewUI에서 시작한 드래그는 앱 자신의 창들 사이에서 움직이며, 다른 앱은 받지 않습니다.

## 미리 보기

`DragStartingEventArgs.Preview`(또는 `BeginDrag`의 `preview` 인자)에 포인터를 따라다닐 `DragPreviewContent`를 설정합니다.

- `Element` 또는 `Image`: 보여 줄 내용. 부모가 없는 요소는 레이아웃을 거쳐 미리 보기로 표시되며, 너비는 `MaxWidth`로 제한됩니다.
- `Hotspot`: 미리 보기 위에서 포인터가 놓이는 위치. 기본값은 요소를 잡은 지점입니다.
- `Opacity`: 기본값 0.75.
- `Scope`: `WithinWindow`는 포인터 아래 창 안에서만 보이고, `CrossWindow`는 창과 바탕 화면을 넘나들며 포인터를 따라갑니다.
