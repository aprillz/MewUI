[![English](https://img.shields.io/badge/README.md-English-blue.svg)](README.md)

# MewUI 에이전트 스킬

MewUI 에이전트 스킬은 Codex, Claude Code, GitHub Copilot이 공개
`Aprillz.MewUI*` NuGet 패키지만으로 완전한 MewUI 애플리케이션을 만들고
유지보수하도록 돕습니다. 프로젝트 생성, 공개 API 탐색, 타입 기반 상태와 바인딩,
컨트롤, `Window`/`UserControl` 재사용 뷰, Hot Reload와 프리뷰 친화적 구성,
윈도우리스 생명주기, 렌더링 백엔드, NativeAOT 게시를 패키지 수준에서 설명합니다.

스킬은 MewUI 소스 체크아웃을 요구하지 않습니다. 온라인 Gallery 소스에서 구성
패턴을 참고할 수 있지만, 선택한 패키지의 XML 문서와 패키지 전용 빌드 결과가
판단 기준입니다.

## 설치

릴리즈 압축 파일을 풀고 `mewui` 디렉터리를 통째로 다음 중 한 위치에 설치합니다.

| 에이전트 | 프로젝트 설치 | 개인 설치 |
| --- | --- | --- |
| Codex | `.agents/skills/mewui` | `~/.agents/skills/mewui` |
| Claude Code | `.claude/skills/mewui` | `~/.claude/skills/mewui` |
| GitHub Copilot | `.github/skills/mewui` | `~/.copilot/skills/mewui` |

Copilot은 프로젝트의 `.agents/skills`, `.claude/skills`와 개인
`~/.agents/skills`도 탐색합니다. 여러 에이전트를 함께 쓰는 팀은
`.agents/skills/mewui`에 공통 복사본 하나를 커밋할 수 있습니다. 제품별 동작이나
배포 범위를 달리해야 할 때만 제품 전용 위치를 사용합니다.

설치된 디렉터리 바로 아래에 `SKILL.md`와 `references/`가 있어야 합니다.
`agents/openai.yaml`은 OpenAI 호스트용 선택 메타데이터이며 Codex 전용의 별도
스킬이 아닙니다. 이 저장소에서 개발할 때는 [mewui](mewui/)를 선택한 위치에
복사하고, 저장소의 `agent/`와 `tests/` 디렉터리는 설치하지 않습니다.

공식 설치 위치 문서:

- [Codex 스킬](https://developers.openai.com/codex/skills)
- [Claude Code 스킬](https://code.claude.com/docs/en/skills)
- [GitHub Copilot 에이전트 스킬](https://docs.github.com/ko/copilot/concepts/agents/about-agent-skills)

## 사용

에이전트에게 MewUI 애플리케이션 생성이나 수정을 요청합니다. 수동 호출을 지원하는
호스트에서는 Codex의 `$mewui`, Claude Code와 GitHub Copilot CLI의 `/mewui`로
선택할 수 있습니다. 요청이 `SKILL.md`의 `description`과 일치하면 에이전트가
자동으로 불러올 수도 있습니다.

에이전트는 먼저 대상 플랫폼과 렌더링 백엔드를 선택하고, 패키지 전용 프로젝트를
만들어 컴파일한 뒤 지원되는 로컬 대상에서 실행해야 합니다. 배포 요청이 있으면
해당 RID와 NativeAOT 게시까지 검증합니다.

## 패키지 호환 정책

- 기존 애플리케이션은 업그레이드 요청이 없다면 현재의 호환 MewUI 패키지군을 유지합니다.
- 신규 애플리케이션은 현재 안정 NuGet 패키지를 사용합니다.
- 한 애플리케이션의 모든 `Aprillz.MewUI*` 패키지는 같은 버전을 사용합니다.
- 스킬은 특정 MewUI 릴리즈나 저장소 리비전에 고정되지 않습니다.
- API 차이는 선택한 패키지의 XML 문서와 실제 컴파일 결과로 판단합니다.
- Gallery 링크는 선택적인 온라인 예제이며 의존성이나 패키지 검증의 대체물이 아닙니다.

## 원본, 릴리즈와 검증

공식 원본은 [mewui](mewui/)입니다. 에이전트별 설치 디렉터리는 배포 대상이지
각각 유지보수하는 별도 원본이 아닙니다. `SKILL.md`는 진입점이고,
`references/`는 작업별 레시피를 담습니다.

스킬 릴리즈는 프레임워크 `v*`와 분리된 `skill-v*` 태그를 사용합니다. 프레임워크
버전마다 자동 릴리즈하지 않고, 지침·지원 작업·패키지 검증이 실질적으로 바뀔 때
릴리즈합니다.

패키지 전용 검증 프로젝트는
[`tests/MewUI.SkillTests`](../tests/MewUI.SkillTests/)에 있습니다. 이 프로젝트는
소스 체크아웃의 MewUI 프로젝트를 참조하면 안 됩니다. 검증 범위에는 복원과 빌드,
실제 애플리케이션 실행, 재사용 뷰 API, 플랫폼/백엔드 등록, 윈도우리스 시작 계약,
Windows NativeAOT를 포함한 게시 프로파일이 들어갑니다.
