# MewUI 작업 관리자 샘플 계획

상태: 초기 구현 및 Windows/Linux/macOS 리소스 프로브 완료, GUI 형상 보정 진행 중

대상 체크아웃: `E:\Personal\Mew\MewUI`

예정 프로젝트: `samples/MewUI.TaskManager.Sample`

## 목표

MewUI와 MewCharts를 사용하여 Windows 11 작업 관리자의 GUI를 최대한 비슷하게 재현하는 크로스플랫폼 샘플을 만든다. 모든 작업 관리자 기능을 구현하는 것보다 시각적 재현, 특히 Performance 차트의 재현도를 우선한다.

## 화면 범위

다음 세 페이지만 구현한다.

- Processes
- Performance
- Settings

`NavigationView`를 공통 셸로 사용한다. Processes를 초기 페이지로 하고 Performance를 주 내비게이션 항목으로, Settings를 Footer에 고정한다.

## 시각적 기준

사용자가 제공하는 Windows 11 작업 관리자 캡처를 다음 형상의 최종 기준으로 사용한다.

- 창과 내비게이션 영역 비율
- 콘텐츠 여백과 간격
- 행, 헤더 및 카드 크기
- 글꼴 크기, 굵기 및 정렬
- 차트 종횡비, 격자 간격, 선 및 채움
- 라이트 및 다크 테마 색상
- 선택, 호버, 비활성화 및 접근 거부 상태

정확한 형상을 알 수 없으면 추측으로 확정하지 않고 전체 창 캡처를 요청한다. 캡처에는 가능하면 Windows 버전, 디스플레이 배율 및 창 크기를 함께 기록한다.

우선 필요한 캡처는 다음과 같다.

- 펼쳐진 프로세스 트리가 보이는 Processes 전체 화면
- CPU, Memory, Disk 및 Network를 각각 선택한 Performance 화면
- Settings 전체 화면
- 라이트 및 다크 테마 화면

## Fluent 아이콘

`samples/MewUI.Gallery/Resources/Icons.xaml`의 Fluent `PathGeometry` 자산을 사용한다. Gallery 어셈블리를 런타임에 참조하지 않도록 필요한 아이콘 경로만 샘플에 포함한다.

| 동작 또는 페이지 | Fluent 아이콘 |
| --- | --- |
| Processes | `apps_regular` |
| Performance | `data_line_regular` |
| Settings | `settings_regular` |
| 검색 | `search_regular` |
| 새 작업 실행 | `window_new_regular` |
| 더보기 | `more_regular` |
| 관리자 모드 | `shield_regular` |
| 접힌 프로세스 | `chevron_right_regular` |
| 펼친 프로세스 | `chevron_down_regular` |

각 아이콘의 Fill을 상속된 Foreground에 바인딩하여 선택, 비활성화 및 테마 변경을 반영한다.

## Processes

앱 및 백그라운드 프로세스 그룹 헤더를 만들지 않는다. 모든 프로세스를 하나의 계층형 그리드로 표시한다.

첫 번째 열에는 펼침 버튼, 프로세스 아이콘 및 프로세스 이름을 배치한다. 나머지 열에는 상태와 사용 가능한 CPU, 메모리, 디스크 및 네트워크 측정값을 표시한다. 정확한 표시 열과 너비는 참조 캡처를 따른다.

### 트리 그리드

현재 MewUI에는 전용 `TreeGridView`가 없지만 기존 API 조합으로 샘플 수준에서 구현할 수 있다.

- `TreeItemsView<ProcessNode>`를 `GridView.ItemsSource`로 사용한다.
- 첫 번째 셀 템플릿은 보이는 행 인덱스로 깊이와 펼침 상태를 얻는다.
- GridView의 열 레이아웃, 선택 및 가상화를 유지한다.
- TreeItemsView가 보이는 행의 평탄화, 깊이 및 펼침 상태를 관리한다.
- PID와 프로세스 시작 시각을 조합한 키로 펼침 및 선택 상태를 보존한다.
- PID가 재사용되어도 종료된 프로세스의 상태를 이어받지 않는다.

GridView 내장 정렬은 보이는 행을 평면 정렬하므로 트리 모드에서 사용하지 않는다. 헤더 클릭을 별도로 처리하여 루트 형제와 각 자식 컬렉션을 재귀적으로 정렬한다. 검색 결과에는 일치한 프로세스와 조상 경로를 함께 표시하고 검색을 해제하면 이전 펼침 상태를 복원한다.

다음 입력 동작을 지원한다.

- 오른쪽 키는 선택한 프로세스를 펼치거나 첫 번째 자식으로 이동한다.
- 왼쪽 키는 선택한 프로세스를 접거나 부모로 이동한다.
- 더블클릭은 펼침 상태를 전환한다.

부모 프로세스가 종료되면 살아 있는 자식을 현재 OS 정보에 맞게 새 부모 아래 또는 루트로 이동한다.

## Performance 차트

Performance 차트의 시각적 재현도를 가장 중요한 완료 기준으로 삼는다.

모든 자원 카드와 상세 차트에 MewCharts를 사용한다. 자원 카드와 선택된 상세 차트는 같은 샘플 버퍼를 공유하여 그래프 형상을 동기화한다.

CPU 상세 차트는 다음 특성을 재현한다.

- 새 샘플이 오른쪽에 들어오는 고정 60초 기록
- 0~100%로 고정된 세로 범위
- 포인트 마커가 없는 직선 연결
- 자원 색상의 가는 외곽선
- 같은 자원 색상의 옅은 반투명 영역 채움
- 가로 및 세로 격자와 플롯 테두리
- 범례와 Tooltip 제거
- `% Utilization`, `100%`, `60 seconds`, `0` 외부 레이블
- 샘플이 즉시 이동하도록 애니메이션을 끄거나 최소화

Memory, Disk 및 Network에도 자원별 색상과 단위를 사용하여 같은 시각 모델을 적용한다. 플랫폼 어댑터가 의미 있는 데이터를 제공할 때만 GPU를 표시한다. 사용할 수 없는 측정값을 임의 값으로 대체하지 않는다.

스크린샷 비교를 반복하여 다음 항목을 보정한다.

- 좌측 자원 카드와 상세 영역 비율
- 차트 가로세로 비율
- 격자 개수와 간격
- 선 두께와 채움 투명도
- 제목과 수치의 위치 및 기준선
- 카드 크기와 미니 차트 위치

## 다크 모드

시스템, 라이트 및 다크 테마와 실행 중 전환을 지원한다.

- MewUI 컨트롤은 활성 테마 팔레트를 사용한다.
- Fluent 아이콘 Fill은 Foreground를 상속한다.
- MewCharts Paint는 Foreground를 자동 상속하지 않으므로 테마 변경 시 선, 채움, 격자, 테두리 및 레이블 Paint를 명시적으로 교체한다.
- 자원 식별 색상은 유지하면서 테마별 명도와 채움 투명도를 조정한다.
- 양쪽 테마에서 선택, 호버, 비활성화 및 접근 거부 상태를 검증한다.
- Windows, Linux 및 macOS의 시스템 테마 변경을 반영한다.

## 크로스플랫폼 시스템 계층

플랫폼 구현을 다음 계약 뒤에 둔다.

- `IProcessService`
- `IPerformanceService`
- `IPrivilegeService`

Windows, Linux 및 macOS 어댑터를 제공한다. 부모 PID는 다음 방식으로 수집한다.

- Windows: Tool Help 프로세스 스냅샷
- Linux: `/proc/[pid]/stat`
- macOS: `libproc`의 `proc_listallpids`/`proc_pidinfo` 프로세스 정보

Windows에서는 반복적으로 예외를 발생시키는 `Process.StartTime`, `TotalProcessorTime`, `WorkingSet64` 조회 대신 `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`, `GetProcessTimes`, `K32GetProcessMemoryInfo`의 성공 여부를 사용한다. 보호 프로세스와 종료 중인 프로세스는 예외 기반 흐름 제어 없이 사용 불가능 상태로 표시한다.

지원하지 않거나 접근할 수 없는 측정값은 사용할 수 없음으로 보고한다. 하나의 프로세스나 측정값을 읽지 못해도 다른 값의 UI 갱신을 계속한다.

렌더링 조합은 다음과 같다.

- Windows: Win32 및 Direct2D
- Linux: X11 및 MewVG
- macOS: MacOS 및 MewVG

### Linux 테스트 환경

Linux 빌드와 GUI 검증에는 로컬 WSLg를 사용한다.

- 기본 배포판: Ubuntu 24.04, WSL2, x64
- 추가 설치 배포판: Rocky Linux, WSL2
- GUI 환경: WSLg, `DISPLAY=:0`, `WAYLAND_DISPLAY=wayland-0`
- MewUI 구성: X11 및 MewVG

기본 검증은 Ubuntu 24.04에서 수행하고 배포판별 차이를 확인해야 할 때 Rocky Linux를 추가로 사용한다. WSLg에서는 MewUI X11 창을 XWayland 경로로 실행하여 실제 창 생성, 입력, 크기 변경, 라이트 및 다크 테마와 차트 렌더링을 확인한다.

WSL에서 수집되는 프로세스 및 성능 정보는 Windows 호스트가 아니라 해당 Linux 배포판 환경을 대상으로 한다. WSLg의 시스템 테마 감지는 일반 Linux 데스크톱과 다를 수 있으므로 명시적인 Light 및 Dark 전환은 WSLg에서 검증하되 System 모드의 일반 Linux 동작을 WSLg 결과만으로 확정하지 않는다.

### macOS 테스트 환경

macOS 전용 API와 런타임 검증은 다음 원격 환경에서 수행한다.

- SSH: `al6uiz@wg.aprillz.net:19022`
- 원격 작업 경로: `~/Sandbox`
- 동기화된 MewUI 소스: `~/Dev/MewUI`
- 확인된 환경: macOS 15.7.3, arm64
- 네트워크 제약: 외부에서 접근 가능한 포트는 SSH `19022`뿐이다.

2026-08-11에 SSH 키 인증과 `~/Sandbox` 생성을 확인했다. `~/Dev/MewUI`는 별도 도구가 동기화하는 작업 사본이므로 원격 Git 저장소로 취급하지 않는다. 이 경로의 `.git` 포인터를 수정하거나 `git pull`, checkout 및 기타 Git 변경을 수행하지 않는다.

테스트 시점의 필요한 소스를 `~/Sandbox` 아래 테스트 사본으로 복제하고, 테스트 소스, 임시 프로젝트, 중간 출력 및 산출물을 모두 이 경로 아래에 둔다. 동기화 원본인 `~/Dev/MewUI`에는 빌드 출력이나 테스트 변경을 쓰지 않는다. 인증 비밀번호는 계획 문서, 저장소 파일, 스크립트 또는 로그에 기록하지 않는다.

### macOS GUI 세션 연동

현재 macOS 요구 수준은 리소스 정보 API 검증이므로 GUI 연동을 필수 테스트 환경으로 구성하지 않는다. 프로세스 열거, 부모 PID, CPU, 메모리, 디스크 및 네트워크 정보와 일반 권한 및 상승 권한의 접근 차이는 SSH 헤드리스 테스트로 검증한다.

2026-08-11 확인 결과 `al6uiz`의 콘솔 로그인, WindowServer 및 `gui/502` launchd 도메인은 활성화되어 있다. 향후 macOS 네이티브 창 렌더링, 인증 대화상자, 입력 또는 스크린샷 비교가 필요할 때만 다음 GUI 경로를 사용한다.

- 수동 관찰과 조작은 macOS Screen Sharing 또는 호환 VNC 클라이언트로 현재 콘솔 세션에 연결한다.
- 자동 실행은 `launchctl asuser 502` 또는 `launchctl bootstrap gui/502`로 기존 Aqua 세션에 테스트 앱을 연결한다.
- LaunchAgent plist, 제어 파일, 로그 및 스크린샷은 `~/Sandbox` 아래에 둔다.
- 자동 스크린샷에는 macOS Screen Recording 권한이 필요하다.
- 키보드 및 포인터 자동화에는 Accessibility 권한이 필요하다.
- GUI 세션이 없거나 잠긴 경우에는 빌드, 단위 테스트 및 API 프로브만 수행하고 시각 검증 성공으로 보고하지 않는다.

Screen Sharing 활성화와 개인정보 보호 권한 부여는 사용자가 Mac의 시스템 설정에서 명시적으로 수행한다. 테스트 자동화가 필요하더라도 보안 설정을 우회하거나 비대화식으로 권한을 강제하지 않는다.

GUI 검증이 실제로 필요해질 때의 권장 원격 공유 설정은 다음과 같다. 현재 망에서는 SSH 이외의 외부 포트가 차단되어 있으므로 VNC 주소나 TCP 5900에 직접 연결하지 않는다.

1. macOS 시스템 설정의 일반 > 공유에서 Remote Management를 끈다.
2. Screen Sharing을 켜고 접근 허용 사용자를 `al6uiz`로 제한한다.
3. Windows의 표준 VNC 클라이언트를 사용할 때만 VNC viewer 제어 옵션을 켜고 SSH 및 macOS 계정과 다른 전용 VNC 비밀번호를 설정한다.
4. TCP 5900을 외부에 직접 공개하지 않고 Windows에서 다음 SSH 터널을 연다.

   `ssh -N -L 5901:127.0.0.1:5900 -p 19022 al6uiz@wg.aprillz.net`

5. VNC 클라이언트는 `127.0.0.1:5901`에 연결한다.
6. 자동 스크린샷이 필요하면 테스트 실행 주체에 Screen & System Audio Recording 권한을, 자동 입력이 필요하면 Accessibility 권한을 사용자가 직접 부여한다.

2026-08-11 읽기 전용 확인에서 Screen Sharing 서비스는 로드되어 있고 Remote Management 서비스는 로드되지 않은 상태였다. Windows VNC 클라이언트의 실제 연결과 호환 인증은 별도 스모크가 필요하다.

## 권한 상승

권한 상승의 목적은 일반 권한으로 읽을 수 없는 프로세스 및 시스템 리소스와 성능 정보에 접근하는 것이다. 프로세스 종료나 임의의 관리자 작업 실행을 주목적으로 삼지 않는다.

일반 권한에서도 애플리케이션과 지원되는 모니터링 기능은 계속 동작해야 한다. 접근이 거부된 리소스만 제한 상태 또는 사용할 수 없음으로 표시하고, 사용자가 더 많은 리소스 정보를 확인하려고 명시적으로 요청할 때 동일 애플리케이션을 원래의 안전한 인수와 함께 상승된 권한으로 다시 시작한다. 시작 시 자동으로 권한 상승을 요청하지 않는다.

권한 상승 경로는 고정된 애플리케이션 재실행으로 제한하며 임의의 관리자 명령 실행 기능은 제공하지 않는다.

- Windows: `runas` 셸 동사
- Linux: `pkexec`
- macOS: 고정된 애플리케이션 실행 파일에 한정된 관리자 인증 경로

macOS의 서명된 privileged helper는 정식 배포 방식으로 별도 취급한다.

이미 상승된 프로세스를 감지하고 재시작 반복을 방지한다. 재실행 전 열려 있던 페이지를 복원하며 인증 취소는 정상 결과로 처리한다. Settings에서 현재 리소스 접근 수준과 제한된 리소스에 접근하기 위한 권한 상승 동작을 표시한다.

## Settings

다음 항목을 포함한다.

- 시스템, 라이트 및 다크 테마 선택
- 실시간 갱신 속도
- 관리자 모드 상태 및 재시작 동작
- 필요한 경우 플랫폼에서 사용할 수 없는 측정값의 표시 여부

최종 콘텐츠와 형상은 제공된 Windows 11 참조 캡처를 따른다.

## 구현 순서

1. 참조 캡처에서 레이아웃 치수, 색상, 타이포그래피 및 차트 토큰을 추출한다.
2. 샘플 프로젝트와 세 플랫폼의 플랫폼 및 렌더링 백엔드 등록을 구성한다.
3. Fluent 아이콘 집합과 공통 NavigationView 셸을 구현한다.
4. MewCharts용 작업 관리자 차트 팩토리와 동기화된 상세 및 미니 차트를 구현한다.
5. OS별 프로세스 및 성능 서비스와 `ProcessNode` 계층 모델을 구현한다.
6. 계층형 GridView, 트리 보존 정렬 및 검색을 구현한다.
7. Settings, 테마 전환 및 권한 상승 흐름을 구현한다.
8. 참조 스크린샷 비교와 플랫폼별 검증을 반복한다.

## 검증

- 라이트 및 다크 테마의 참조 크기 스크린샷 비교
- 차트 격자, 선, 채움, 레이블, 카드 및 콘텐츠 정렬 확인
- 대량 프로세스 트리와 반복적인 펼침 및 접기
- 계층을 유지하는 정렬과 검색
- 프로세스 생성, 종료, 부모 변경 및 PID 재사용
- 차트 갱신 중 테마 변경
- 일반, 상승, 취소 및 실패 권한 상승 경로
- Windows, Linux 및 macOS의 대표 빌드와 런타임 스모크
- `git diff --check`

## 2026-08-11 구현 보정

- 프로세스와 성능 정보 수집 전체를 백그라운드 작업에서 실행하고, 완성된 스냅샷의 UI 반영만 디스패처에서 수행한다. 이전 수집이 끝나기 전에는 다음 수집을 시작하지 않는다.
- 프로세스 아이콘은 확장자 기반 자리표시자 이후 실제 실행 파일 또는 macOS 앱 번들의 아이콘을 비동기로 읽고 경로별로 캐시한다.
- CPU 그래프는 `Overall utilization`과 `Logical processors` 보기 전환을 제공한다.
- 논리 프로세서 사용률은 Windows의 processor performance information, Linux의 `/proc/stat`, macOS의 `host_processor_info`에서 수집한다.
- 60초 창 밖의 마지막 표본 하나를 보존하여 선분이 차트의 왼쪽 경계에서 잘리도록 하고, 갱신 간격 때문에 그래프 시작점이 안쪽으로 밀리지 않게 한다.
- 기존 작업 트리의 관련 없는 변경이 포함되지 않았는지 확인
