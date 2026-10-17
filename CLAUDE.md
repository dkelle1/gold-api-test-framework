@AGENTS.md

## Claude Code specifics

- **Skills:** the playbooks listed above live in `.claude/skills/` and are available as `/add-api-tests`,
  `/onboard-service`, `/update-api-contract`, `/write-e2e-flow`, `/diagnose-test-failure`,
  `/extend-framework`. Use them for those tasks instead of improvising the workflow.
- **Guardrails** (`.claude/settings.json`): a `PreToolUse` hook (`.claude/hooks/protect_generated.py`)
  and `deny` rules block direct edits of generated files — when blocked, run the generator it names.
  Common build/test/generator commands are pre-approved; anything else asks.
- Nested `CLAUDE.md` files in `framework/`, `examples/demo-shop/` and `tools/` import those folders'
  `AGENTS.md` and load when you work there.
- Personal, uncommitted preferences go in `CLAUDE.local.md` (git-ignored), not in this file.
