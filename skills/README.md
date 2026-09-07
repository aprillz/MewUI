[![한국어](https://img.shields.io/badge/README.md-한국어-green.svg)](README.ko.md)

# MewUI Agent Skill

Teach Codex, Claude Code, or GitHub Copilot to build MewUI desktop applications
for you. The skill itself is [mewui](mewui/). Describe the app you want and the
agent writes working C# against the published `Aprillz.MewUI*` NuGet packages,
runs it, and publishes it. You do not need a copy of the MewUI source.

## Install

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

Run `$skill-installer` and ask it for the `mewui` skill from `aprillz/MewUI`.

### Other agents

The GitHub CLI installs the same skill for any agent that supports skills:

```text
gh skill install aprillz/MewUI mewui --agent claude-code --scope user
```

`--agent` also takes `codex`, `github-copilot`, and others. `--scope user`
installs it once for every project; leave it out to install into the current
repository only.

Or copy the [mewui](mewui/) directory of this repository yourself, so that its
`SKILL.md` lands where your agent looks:

| Agent | In your project | In your home directory |
| --- | --- | --- |
| Codex | `.agents/skills/mewui/SKILL.md` | `~/.agents/skills/mewui/SKILL.md` |
| Claude Code | `.claude/skills/mewui/SKILL.md` | `~/.claude/skills/mewui/SKILL.md` |
| GitHub Copilot | `.github/skills/mewui/SKILL.md` | `~/.copilot/skills/mewui/SKILL.md` |

## Use

Ask for what you want:

- "Build a MewUI app with a name field and a Save button"
- "Show these records in a grid and let me filter them"
- "Add a dark and light theme toggle"
- "Publish it as one Windows executable with NativeAOT"

The agent picks the platform and rendering backend, creates the project, builds
it, and runs it before reporting back. It loads the skill on its own when your
request matches; to call it explicitly, use `/mewui` in Claude Code and the
Copilot CLI, or `$mewui` in Codex.

