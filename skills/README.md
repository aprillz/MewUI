[![한국어](https://img.shields.io/badge/README.md-한국어-green.svg)](README.ko.md)

# MewUI Agent Skill

The MewUI agent skill helps Codex, Claude Code, and GitHub Copilot create and
maintain complete MewUI applications from public `Aprillz.MewUI*` NuGet
packages. Its package-level guidance covers project setup, public API discovery,
typed state and binding, controls, reusable `Window` and `UserControl` views,
Hot Reload and preview-friendly composition, windowless lifecycle, rendering
backends, and NativeAOT publishing.

The skill does not require a MewUI source checkout. Online Gallery sources may
be consulted for composition patterns, but the selected package's XML
documentation and a package-only build are authoritative.

## Install

Extract the release archive and place its whole `mewui` directory in one
supported skills location:

| Agent | Project installation | Personal installation |
| --- | --- | --- |
| Codex | `.agents/skills/mewui` | `~/.agents/skills/mewui` |
| Claude Code | `.claude/skills/mewui` | `~/.claude/skills/mewui` |
| GitHub Copilot | `.github/skills/mewui` | `~/.copilot/skills/mewui` |

Copilot also discovers project skills in `.agents/skills` and `.claude/skills`,
and personal skills in `~/.agents/skills`. Teams using several agents can
therefore commit one shared copy at `.agents/skills/mewui`. Use a product-native
location only when its behavior or distribution scope needs to differ.

The installed directory must contain `SKILL.md` and `references/` directly.
`agents/openai.yaml` is optional OpenAI-host metadata, not a separate
Codex-only skill. For development from this repository, copy [mewui](mewui/)
to the selected location; do not install the repository's `agent/` or `tests/`
directories.

Official location references:

- [Codex skills](https://developers.openai.com/codex/skills)
- [Claude Code skills](https://code.claude.com/docs/en/skills)
- [GitHub Copilot agent skills](https://docs.github.com/en/copilot/concepts/agents/about-agent-skills)

## Use

Ask the agent to create or modify a MewUI application. Where manual skill
invocation is supported, select `$mewui` in Codex or `/mewui` in Claude Code and
GitHub Copilot CLI. Agents may also load it automatically when the request
matches the `description` in `SKILL.md`.

The agent should choose the platform and rendering backend first, create a
package-only project, compile it, run the supported local target, and perform
the matching RID/NativeAOT publish when deployment is requested.

## Package compatibility

- Existing applications keep their compatible MewUI package line unless an upgrade is requested.
- New applications use the current stable NuGet package.
- All `Aprillz.MewUI*` packages in one application stay on the same version.
- The skill is not tied to a fixed MewUI release or repository revision.
- Restored XML documentation and compilation against the selected packages resolve API differences.
- Gallery links are optional online examples, not dependencies or substitutes for package validation.

## Source, release, and validation

The canonical source is [mewui](mewui/). Agent-specific installation folders
are distribution targets, not independently maintained sources. `SKILL.md` is
the entry point and `references/` contains task-specific recipes.

Skill releases use independent `skill-v*` tags, separate from framework `v*`
releases. Release when guidance, supported workflows, or package validation
materially changes, not automatically for every framework version.

Package-only validation projects live in
[`tests/MewUI.SkillTests`](../tests/MewUI.SkillTests/). They must not reference
MewUI projects from the source checkout. Validation covers restore and build,
real application launch, reusable view APIs, platform/backend registration,
windowless startup contracts, and publish profiles including Windows NativeAOT.
