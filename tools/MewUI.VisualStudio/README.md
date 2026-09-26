# MewUI.VisualStudio

MewUI 에디터 프리뷰의 Visual Studio 2022/2026 확장(VSIX). 세션/프로토콜 레이어는 `tools/vscode-mewui`의 session.ts/protocol.ts를 C#으로 포팅한 것으로, VS API 없이 동작한다 (`src/Session/`).

## 빌드

VS 확장성 워크로드 없이 NuGet `Microsoft.VSSDK.BuildTools`만으로 빌드된다. `dotnet build` 불가(VSSDK 타깃이 .NET Framework MSBuild 전용), VS 동봉 MSBuild를 사용:

```
"C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe" MewUI.VisualStudio.csproj -restore -p:Configuration=Release
```

산출물: `bin/Release/MewUI.VisualStudio.vsix` (AnyCPU 관리 코드, amd64/arm64 공용, VS 17.0-18.x 대상).

## 설치

```
"C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\VSIXInstaller.exe" bin\Release\MewUI.VisualStudio.vsix
```

사용: 보기 > 다른 창 > MewUI Preview. 툴윈도우 안에서 프로젝트 선택 후 Start. 창을 닫아도 세션은 유지되고(재열기 시 즉시 재부착) Stop 버튼이 세션을 종료한다.

## 게시

마켓플레이스 게시는 `tools/release/Publish-Extensions.ps1`이 수행한다. 게시자 계정 준비와 점검 목록은 `agent/preview-tooling/publishing.md`에 있다.
