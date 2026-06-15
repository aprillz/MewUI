# Copilot instructions

## Commit message rules

Structure rules (must follow):
- Line 1 (Subject):
  - One concise sentence in imperative mood (e.g., Add, Fix, Refactor).
  - Maximum ~50 characters when practical.
  - Do NOT end with a period.
- Line 2:
  - Must be completely blank when a body is present.
- Line 3+ (Body, optional):
  - Use bullet points only ("- " prefix).
  - Keep bullets short and factual.
  - Wrap lines at ~72 characters.
  - Omit the body unless it provides important context not already
    expressed by the subject.
  - Do not add a body merely because multiple lines or files changed.
  - For a single, well-scoped change, prefer the subject alone.

Content rules:
- Use clear, technical English.
- Prefer minimal verbosity.
- Describe only facts directly supported by the diff or explicitly
  provided context.
- Do not infer motivation, intent, benefits, behavioral effects, or
  architectural significance.
- State WHY or IMPACT only when it is explicitly evident from the diff,
  tests, issue context, or user-provided description.
- When WHY or IMPACT is not explicit, describe only WHAT changed.
- Use project-specific terminology exactly as it appears in the code.
- Avoid vague phrases such as "minor changes" or "misc updates".
- Do not mention tools (Copilot, GPT, IDE, etc.).
- Do not use emojis.

Change grouping rules:

- Identify the single primary change.
- Treat code movement, callback wiring, reference updates, and equivalent
  mechanical edits as part of the primary change.
- Do not list mechanical consequences as separate effects or outcomes.
- Avoid listing multiple observations that stem from the same root change.
- Mention a secondary change only when it is independently meaningful and
  would reasonably warrant mention without the primary change.
- If a property is converted from one property model to another, describe
  that conversion directly instead of enumerating the required callback,
  rendering, or lifecycle updates.

Project context:
- .NET / C#
- Native AOT–focused.
- Performance, startup time, binary size, and simplicity matter.
- UI framework with multiple backends (GDI, Direct2D, OpenGL, X11).

Heuristics:
- Prefer a strong subject line over an explanatory body.
- For cleanup, removal, rename, or refactoring, describe the actual
  structural change rather than an assumed purpose or benefit.
- Do not translate implementation details into claimed user-visible impact.
- Do not claim reduced binary size, simplified interop, improved
  maintainability, improved performance, or corrected behavior unless the
  change directly demonstrates it.
- When uncertain whether a detail is an independent change or a mechanical
  consequence, omit it.
- When the subject fully describes the change, output only the subject.

Output:
- Return ONLY the commit message text.
