[![English](https://img.shields.io/badge/README.md-English-blue.svg)](README.md)

# MewUI 에이전트 스킬

Codex, Claude Code, GitHub Copilot이 MewUI 데스크톱 애플리케이션을 대신 만들도록
가르치는 스킬입니다. 스킬 본체는 [mewui](mewui/)입니다. 원하는 앱을 말하면
에이전트가 공개된 `Aprillz.MewUI*` NuGet 패키지로 동작하는 C# 코드를 작성하고,
실행하고, 게시합니다. MewUI 소스는 없어도 됩니다.

## 설치

### Claude Code

```text
/plugin marketplace add aprillz/MewUI
/plugin install mewui@aprillz
```

### GitHub Copilot

```text
copilot plugin marketplace add aprillz/MewUI
copilot plugin install mewui@aprillz
```

### Codex

`$skill-installer`를 실행하고 `aprillz/MewUI`의 `mewui` 스킬을 요청합니다.

### 그 밖의 에이전트

GitHub CLI로 스킬을 지원하는 어느 에이전트에나 같은 스킬을 설치할 수 있습니다.

```text
gh skill install aprillz/MewUI mewui --agent claude-code --scope user
```

`--agent`에는 `codex`, `github-copilot` 등도 쓸 수 있습니다. `--scope user`는 모든
프로젝트에서 쓰도록 한 번만 설치하고, 이 옵션을 빼면 현재 저장소에만 설치합니다.

이 저장소의 [mewui](mewui/) 디렉터리를 직접 복사해도 됩니다. 그 안의 `SKILL.md`가
사용하는 에이전트가 찾는 자리에 놓이면 됩니다.

| 에이전트 | 프로젝트 안 | 홈 디렉터리 |
| --- | --- | --- |
| Codex | `.agents/skills/mewui/SKILL.md` | `~/.agents/skills/mewui/SKILL.md` |
| Claude Code | `.claude/skills/mewui/SKILL.md` | `~/.claude/skills/mewui/SKILL.md` |
| GitHub Copilot | `.github/skills/mewui/SKILL.md` | `~/.copilot/skills/mewui/SKILL.md` |

## 사용

첫 요청부터 애플리케이션 전체를 맡길 수 있습니다. 새로 만드는 경우:

> 할 일을 관리하는 Windows MewUI 앱 만들어줘. 왼쪽에 할 일 목록, 오른쪽에 제목·
> 마감일·메모를 편집하는 창을 두고, 추가와 삭제 버튼, 목록에서 완료 표시하는 기능,
> 실행 파일 옆 JSON 파일로 저장까지 해줘.

이어서 같은 세션에서 계속 요청합니다.

- "다크/라이트 테마 전환 넣어줘"
- "목록을 텍스트로 필터링하게 해줘"
- "할 일 삭제할 때 확인 받게 해줘"
- "설정을 별도 대화 상자로 분리해줘"
- "NativeAOT로 플랫폼별 자체 포함 실행 파일 하나씩 게시해줘"

기존 앱을 옮기는 경우:

> 이 폴더의 WinForms 유틸리티를 MewUI로 포팅해줘. 창 레이아웃과 단축키, 설정 파일
> 형식은 그대로 두고, Windows와 Linux 양쪽에서 돌게 만들고, 실행되는 것까지 확인한
> 뒤에 알려줘.

에이전트가 대상 플랫폼과 렌더링 백엔드를 고르고, 프로젝트를 만들고, 빌드해서
실행해본 뒤 결과를 알려줍니다. 요청이 스킬 설명과 맞으면 알아서 불러오고, 직접
부르려면 Claude Code와 Copilot CLI에서는 `/mewui`, Codex에서는 `$mewui`를 씁니다.

