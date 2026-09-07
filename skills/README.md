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

A first request can be a whole application. Starting from nothing:

> Build a Windows MewUI app for tracking tasks. Put the task list on the left
> and an edit pane on the right with title, due date and notes. Add buttons to
> create and delete a task, mark one done from the list, and save everything to
> a JSON file next to the executable.

Then keep going in the same session:

- "Add a dark and light theme toggle"
- "Let me filter the list by text"
- "Ask for confirmation before deleting a task"
- "Move the settings into a separate dialog"
- "Publish it as one self-contained executable per platform with NativeAOT"

Or bring an existing application over:

> Port the WinForms utility in this folder to MewUI. Keep its window layout,
> keyboard shortcuts and settings file format, make it run on Windows and
> Linux, and show me it running before you report back.

The agent picks the platform and rendering backend, creates the project, builds
it, and runs it before reporting back. It loads the skill on its own when your
request matches; to call it explicitly, use `/mewui` in Claude Code and the
Copilot CLI, or `$mewui` in Codex.

