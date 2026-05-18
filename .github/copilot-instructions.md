# Copilot instructions

## Commit message rules

Structure rules (must follow):
- Line 1 (Subject):
  - One concise sentence in imperative mood (e.g., Add, Fix, Refactor).
  - Maximum ~50 characters.
  - Do NOT end with a period.
- Line 2:
  - Must be completely blank.
- Line 3+ (Body, optional):
  - Use bullet points only ("- " prefix).
  - Keep bullets short and factual.
  - Focus on WHY or IMPACT, not HOW.
  - Wrap lines at ~72 characters.
  - Omit the body if it adds no meaningful context.
  - If the change fixes a single, well-scoped issue with one clear cause,
    either:
    - use only ONE bullet point, or
    - omit the body entirely if the subject is sufficient.

Content rules:
- Use clear, technical English.
- Prefer minimal verbosity.
- Avoid listing multiple effects that stem from the same root cause.
- Avoid vague phrases such as "minor changes" or "misc updates".
- Do not mention tools (Copilot, GPT, IDE, etc.).
- Do not use emojis.

Project context:
- .NET / C#
- Native AOT–focused.
- Performance, startup time, binary size, and simplicity matter.
- UI framework with multiple backends (GDI, Direct2D, OpenGL, X11).

Heuristics:
- If the fix addresses one bug category, do NOT expand into
  secondary consequences unless they are independently significant.
- Prefer a strong subject line over an explanatory body.
- Mention impact only when it is concrete, direct, and visible from the
  change itself.
- Do not add inferred benefits such as reduced binary size, simplified
  interop, better maintainability, or improved performance unless the
  change directly measures or demonstrates them.
- If the change is a cleanup, removal, rename, or refactor, describe the
  actual change instead of its assumed benefits.

Output:
- Return ONLY the commit message text.