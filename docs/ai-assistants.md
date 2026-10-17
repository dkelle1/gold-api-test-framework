# Working with AI coding assistants

The repository ships rules and playbooks for AI coding agents so that Claude Code, Codex, Copilot,
Cursor, Gemini and others follow the same conventions as human contributors. This page explains
what each file is for and how to keep them healthy.

## The files

| File | Read by | Purpose |
|------|---------|---------|
| `AGENTS.md` (root) | Codex, Cursor, Copilot (agent), Gemini (via `.gemini/settings.json`), Claude (via `CLAUDE.md`) | **Canonical rules**: commands, layout, architecture rules, conventions, definition of done, Always / Ask first / Never boundaries, git conventions |
| `framework/AGENTS.md`, `examples/demo-shop/AGENTS.md`, `tools/AGENTS.md` | same tools, when working in that folder | rules that only apply to that part of the repository |
| `CLAUDE.md` (root and the three folders) | Claude Code | `@AGENTS.md` import (works in every Claude Code version) + Claude-specific notes |
| `.claude/skills/<name>/SKILL.md` | Claude Code (`/name` or automatic); any agent as a playbook | step-by-step workflows for recurring tasks |
| `.claude/settings.json` | Claude Code | pre-approved safe commands, `deny` rules and the generated-file hook |
| `.claude/hooks/protect_generated.py` | Claude Code (`PreToolUse` hook) | blocks hand edits of generated files and tells the agent which generator to run |
| `.github/copilot-instructions.md` | GitHub Copilot (chat, review, agent) | short summary of the rules, pointing to `AGENTS.md` |
| `.github/instructions/*.instructions.md` | GitHub Copilot | folder-scoped rules (`applyTo`), pointing to the nested `AGENTS.md` |
| `.gemini/settings.json` | Gemini CLI | makes it load `AGENTS.md` as context |
| `docs/advanced-prompts.md` | humans | ready-made prompts for larger tasks |

**One source of truth:** rules are written once, in the `AGENTS.md` files. Every tool-specific file
either imports them (`CLAUDE.md`) or summarizes and points to them (Copilot). When a rule changes,
change `AGENTS.md` first, then the short summaries.

## Skills (playbooks)

| Skill | Use it for |
|-------|-----------|
| `add-api-tests` | tests for an endpoint: contract check → steps → promote scaffold → isolated data → run |
| `onboard-service` | a new service: contract, DTOs, routes, the three configs, generation, step class |
| `update-api-contract` | an API changed: merge into the curated contract, regenerate everything in one commit |
| `write-e2e-flow` | multi-service, multi-actor journeys built from existing steps |
| `diagnose-test-failure` | red CI or flaky tests: evidence, classification, fix without weakening tests |
| `extend-framework` | changes to `framework/` or `tools/`: product-agnostic, offline tests, docs |

In Claude Code: type `/add-api-tests POST /api/orders OrderService`, or just describe the task —
Claude picks the matching skill from its description. Other agents: point them at the file, e.g.
"follow `.claude/skills/write-e2e-flow/SKILL.md`".

Skills follow the [Agent Skills](https://agentskills.io) format: YAML front matter (`name`,
`description`, optional `argument-hint`) and a concise body under 500 lines; longer material goes into
files next to `SKILL.md` (see `add-api-tests/templates.md`).

## Guardrails that are enforced, not just written down

Instructions are context — an agent can still get them wrong. The rules that must never be broken are
enforced in `.claude/settings.json`:

- **Generated files** (`*.g.cs`, `Generated/*.cs.txt`, `examples/*/collections/**`,
  `examples/*/perf/k6/**`): `deny` rules plus a `PreToolUse` hook that explains how to regenerate.
  Generators run through Bash and are unaffected.
- **Secrets:** reading `.env` files is denied.
- **History:** force-pushes and direct pushes to `develop`/`main` are denied.
- **Pre-approved:** build, test, generator, Python tool, compose and read-only git/gh commands, so
  routine work does not stop for prompts. Anything else asks.

For every agent, the CI workflow is the final guard: the generated-code gate and the full test suite
run on every pull request.

## Personal settings

Do not edit the shared files for personal preferences. Use git-ignored `CLAUDE.local.md` and
`.claude/settings.local.json` (Claude Code), or your tool's user-level settings.

## Maintaining the rules

- Keep `AGENTS.md` and each `CLAUDE.md` short (well under 200 lines) and concrete: commands that run,
  paths that exist, rules that can be checked. Remove rules that no longer apply.
- When a review comment repeats ("tests must clean up", "don't edit generated code"), add it as a rule
  or, if it must always hold, as a hook / deny rule / CI check.
- When a workflow changes (new script, new config field), update the skill that describes it in the
  same PR. Skills reference docs instead of copying them.
- Claude Code users can audit all instruction files with `/doctor prompt-audit` (recent versions).
