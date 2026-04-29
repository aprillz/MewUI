# Contributing to MewUI

Thank you for your interest in contributing to MewUI.

This project is still evolving quickly, and APIs, internal structure, and behavior may continue to change on the way to v1.0. Because of that, some changes can be accepted directly as pull requests, while others need direction agreement before implementation.

## Before You Start

Before contributing, first determine which category your change belongs to.

### 1. Small Bug Fixes

The following types of changes can usually be submitted directly as pull requests:

- Fixing a clear regression
- Correcting behavior that clearly conflicts with current intent
- Small documentation, sample, or typo fixes
- Localized changes with limited design impact

### 2. Behavior Changes

For changes like the following, please start with an **Issue or Discussion** to confirm direction first:

- Changes to input handling
- Changes to existing behavior in scrolling, focus, layout, rendering, or similar areas
- Changes to defaults or expected UX behavior
- Changes that may work well for one scenario but affect other scenarios differently

For these changes, it is often more important to agree on **what behavior fits the project** than to start with the implementation itself.

### 3. New Architecture, Abstractions, or Public APIs

For changes like the following, the rule is: **do not open a pull request without prior agreement**.

- New backend abstractions
- New public enums, interfaces, or extension points
- Changes that affect lifecycle or composition patterns
- Structural changes that increase long-term maintenance cost

Even when the code diff looks small, these changes can have significant impact on project direction, maintenance cost, and compatibility expectations.

## Recommended Flow

1. Check whether a related Issue, PR, or Discussion already exists.
2. If it is a small bug fix, you can usually open a PR directly.
3. If it is a behavior change, first open an Issue or Discussion to define the problem and direction.
4. If it involves new architecture, abstractions, or public API, wait for prior agreement before opening a PR.
5. Keep each PR focused on one purpose.

## Issue Guidelines

Please use the provided **templates** when opening issues whenever possible.

- Use the bug report template for bug reports.
- Use the proposal template for behavior changes, API proposals, and design proposals.
- Keep one issue focused on one topic.
- If a similar issue already exists, add context to the existing thread instead of opening a duplicate.

### What to Include in a Bug Report

- Environment details (.NET version, OS, package version, backend, sample or app context)
- Reproduction steps
- Expected behavior
- Actual behavior
- A minimal repro, screenshots, or logs when available

### What to Include in a Proposal Issue

- The problem you are trying to solve
- Why the current approach is insufficient
- Whether this is a behavior change, API change, or structural change
- Expected user impact
- Possible alternatives or tradeoffs
- Whether a core change is truly necessary

## When to Start with a Discussion

In the following cases, a **Discussion** may be more appropriate than an Issue:

- Exploratory ideas
- Directional review
- Proposals where the problem statement is not yet fully defined
- Cases where design conversation should happen before implementation

## Pull Request Guidelines

When opening a PR:

- Clearly explain the problem being solved.
- Link the related Issue or Discussion.
- Describe the current behavior and the proposed behavior.
- Do not mix in unrelated refactoring.
- Keep the change scope as small and clear as possible.
- Update documentation or samples when relevant.

### When You Should Not Open a PR First

The following cases require prior agreement:

- A maintainer has asked for an Issue or Discussion first
- The change needs evaluation for project direction or scope fit
- The change affects public API surface or architecture
- The intended behavior must be defined before implementation

## What Maintainers May Do

To keep the project manageable, maintainers may:

- Ask contributors to move a proposal to an Issue or Discussion first
- Close proposals that do not fit the current direction
- Ask for a smaller scope
- Defer ideas to a later milestone
- Close or consolidate repeated requests that have already been addressed

## Style and Scope

- Follow the existing code style and repository structure.
- Prefer minimal changes over broad refactoring.
- Do not include changes that are unrelated to the problem being addressed.

## Conduct

Everyone participating in this repository is expected to follow the [Code of Conduct](.github/CODE_OF_CONDUCT.md).
