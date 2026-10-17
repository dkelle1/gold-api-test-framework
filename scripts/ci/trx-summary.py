#!/usr/bin/env python3
"""Render one or more .trx files as a Markdown summary (for $GITHUB_STEP_SUMMARY).

Usage: trx-summary.py "<title>" results/*.trx
Stdlib only. Missing files are tolerated so the step never masks the real failure.
"""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
MAX_FAILURES = 30


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: trx-summary.py <title> [file.trx ...]", file=sys.stderr)
        return 2

    sys.stdout.reconfigure(encoding="utf-8")  # emoji on legacy Windows consoles
    title, files = sys.argv[1], [Path(p) for p in sys.argv[2:] if Path(p).is_file()]
    totals = {"total": 0, "passed": 0, "failed": 0, "notExecuted": 0}
    failures: list[tuple[str, str]] = []

    for path in files:
        root = ET.parse(path).getroot()
        counters = root.find("t:ResultSummary/t:Counters", NS)
        if counters is not None:
            for key in totals:
                totals[key] += int(counters.get(key, 0))
        for result in root.iterfind("t:Results/t:UnitTestResult", NS):
            if result.get("outcome") == "Failed":
                message = result.findtext("t:Output/t:ErrorInfo/t:Message", "", NS)
                failures.append((result.get("testName", "?"), message.strip().splitlines()[0] if message.strip() else ""))

    status = "❌" if totals["failed"] else ("⚠️" if not files else "✅")
    print(f"### {status} {title}\n")
    if not files:
        print("No test results found (the run may have failed before tests started).\n")
        return 0

    print("| Total | Passed | Failed | Skipped |")
    print("|------:|-------:|-------:|--------:|")
    print(f"| {totals['total']} | {totals['passed']} | {totals['failed']} | {totals['notExecuted']} |\n")

    if failures:
        print("<details open><summary>Failed tests</summary>\n")
        for name, message in failures[:MAX_FAILURES]:
            message = message.replace("|", "\\|")[:300]
            print(f"- `{name}` — {message}")
        if len(failures) > MAX_FAILURES:
            print(f"- … and {len(failures) - MAX_FAILURES} more (see the uploaded artifacts)")
        print("\n</details>\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
