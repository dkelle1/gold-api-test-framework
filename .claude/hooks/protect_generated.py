#!/usr/bin/env python3
"""PreToolUse hook: block direct edits of generated files.

Claude Code passes the tool call as JSON on stdin. Exit code 2 blocks the call and
feeds stderr back to Claude; exit code 0 lets the normal permission flow continue.
Generated files must be regenerated from the committed OpenAPI contracts instead —
CI fails when they differ from what the generators produce.
"""

import json
import os
import re
import sys

GENERATED = [
    (re.compile(r"\.g\.cs$"),
     "generated test-data builder",
     "dotnet run --project framework/src/ApiTestFramework.Generator.Cli -- --config <product>/api-tests/generator.json "
     "(put custom logic in a partial class under Builders/Custom/)"),
    (re.compile(r"/Generated/[^/]+\.cs\.txt$"),
     "generated test scaffold",
     "the same Generator.Cli command; copy the scaffold into a real fixture (*.cs) instead of editing it"),
    (re.compile(r"/collections/(postman|bruno)/"),
     "generated Postman/Bruno collection",
     "python tools/generate_collections.py"),
    (re.compile(r"/perf/k6/[^/]+\.js$"),
     "generated k6 script",
     "python tools/generate_k6.py"),
]


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0  # not our business; let the normal flow decide

    tool_input = payload.get("tool_input") or {}
    path = tool_input.get("file_path") or tool_input.get("notebook_path") or ""
    if not path:
        return 0

    normalized = "/" + os.path.normpath(path).replace("\\", "/").lstrip("/")
    for pattern, kind, how in GENERATED:
        if pattern.search(normalized):
            print(
                f"Blocked: {path} is a {kind} and must not be edited by hand. "
                f"Change the OpenAPI contract (or the custom partial) and regenerate with: {how}. "
                "See AGENTS.md rule 4.",
                file=sys.stderr,
            )
            return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
