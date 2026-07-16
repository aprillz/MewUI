# Copilot instructions

## Commit message rules

Commit messages must be written in English.

### Structure

* Line 1 is the subject.

  * Write one concise sentence in imperative mood, such as `Add`, `Fix`,
    `Refactor`, `Remove`, `Replace`, `Convert`, or `Move`.
  * Keep it within approximately 50 characters when practical.
  * Do not end it with a period.
* When a body is present, line 2 must be completely blank.
* The body starts on line 3.

  * Use bullet points only with the `- ` prefix.
  * Keep bullets short and factual.
  * Wrap lines at approximately 72 characters.
  * Omit the body unless it provides important context not already
    expressed by the subject.
  * Do not add a body merely because multiple files or lines changed.
  * Prefer the subject alone for a single, well-scoped change.

### Content

* Use clear, technical English.
* Prefer minimal verbosity.
* Describe only facts directly supported by the diff or explicitly provided
  context.
* Do not infer motivation, intent, benefits, behavioral effects, or
  architectural significance.
* State reasons or impact only when they are explicitly evident from the
  diff, tests, issue context, or user-provided description.
* When reasons or impact are not explicit, describe only what changed.
* Use project-specific terminology exactly as it appears in the code.
* Avoid vague phrases such as `minor changes` or `misc updates`.
* Do not mention tools such as Copilot, GPT, or an IDE.
* Do not use emojis.

### Change interpretation

* Interpret the diff as a before-and-after transformation of the code, not
  as an independent list of added and removed lines.
* Identify the single primary semantic change across the entire diff.
* Describe the final conceptual API model rather than compatibility
  overloads or adapters used to implement it.
* When a broader type replaces a narrower model while the previous API is
  retained for compatibility, describe the change as a generalization in
  the subject and record the compatibility mechanism in the body.
* Prefer a before-and-after type description when it identifies the API
  change precisely. For example, prefer
  `Generalize NavigationView icons from PathGeometry to Element` over
  `Add Element icon overloads to NavigationView`.
* Determine the final code structure before choosing verbs such as `Add`,
  `Remove`, `Replace`, `Convert`, `Move`, or `Rename`.
* When removed and added members represent the same concept in a different
  form, describe the change as a conversion, replacement, move, or rename
  rather than as separate removal and addition operations.
* Use `Remove` only when the concept or capability no longer exists in the
  final code.
* Do not claim that a property, API, behavior, or capability was removed
  when an equivalent concept remains under a different property model,
  visibility, owner, name, type, or implementation.
* Prefer describing the before-and-after model over listing deleted and
  added symbols.

### Change grouping

* Describe the final high-level change, not the individual edits or
  implementation steps used to produce it.
* Treat code movement, declaration changes, callback wiring, initialization
  changes, reference updates, visibility changes, renames, removals, and
  cleanup made for the same purpose as part of the primary change.
* When several edits result from one cause, property-model conversion, API
  change, or structural change, combine them into one description.
* Treat changes across multiple files as one change when they form a single
  conceptual modification.
* Do not enumerate constructors, callbacks, extension methods, backing
  fields, registrations, or references when they are mechanical
  consequences of the primary change.
* Mention a secondary change only when it is independently meaningful and
  would remain worth mentioning without the primary change.
* When uncertain whether an edit is independent or merely a mechanical
  consequence, omit it.

### Subject and body selection

* Prefer a strong subject over an explanatory body.
* Use the narrowest accurate subject that still represents the whole diff.
* Do not use a vague verb such as `Update` when a more precise verb is
  supported by the diff.
* Do not combine conflicting descriptions such as `Remove X and add X`
  when the actual change is a conversion or replacement.
* Do not repeat the subject in the body by decomposing it into smaller
  implementation details.
* Omit the body when it only restates the subject or explains how the
  primary change was implemented.
* Include a body only when it adds independently meaningful context,
  constraints, or a secondary change.
* Treat public API compatibility, ownership or lifetime requirements, and
  user-visible behavior needed for release notes as independently
  meaningful context when they are not already clear from the subject.
* Do not omit the body when release notes would otherwise require
  reconstructing the API contract or compatibility details from the diff.

### Issue references

* When the available repository context identifies a related issue or pull
  request, append its number to the subject as `(#NNN)`.
* Do not invent an issue or pull request number when none can be determined
  from the available context.
* Do not generate standalone reference footers such as `Refs #NNN` or
  closing keywords such as `Fixes #NNN`.

### Project context

* .NET / C#
* Native AOT focused
* Performance, startup time, binary size, and simplicity matter
* UI framework with CLR properties and framework-specific properties such
  as `MewProperty`
* Multiple backends, including GDI, Direct2D, OpenGL, and X11

### Heuristics

* For cleanup, removal, rename, conversion, or refactoring, describe the
  actual structural change rather than an assumed purpose or benefit.
* Do not translate implementation details into claimed user-visible impact.
* Do not claim reduced binary size, simplified interop, improved
  maintainability, improved performance, or corrected behavior unless the
  change directly demonstrates it.
* Combine multiple observations that stem from the same root change.
* When the subject fully describes the change, output only the subject.

### Output

* Return only the commit message text.
* Do not include explanations, analysis, code fences, quotation marks, or
  any additional text.
