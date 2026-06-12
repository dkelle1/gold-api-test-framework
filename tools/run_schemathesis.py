#!/usr/bin/env python3
"""
Property-based fuzz & contract testing wrapper around Schemathesis.

Schemathesis reads each OpenAPI spec and auto-generates hundreds of test
cases per endpoint (boundary values, malformed payloads, wrong types),
then verifies the API never 500s, always matches its declared response
schemas, and honours its status codes.

This wrapper handles what Schemathesis can't know on its own:
  - registers a fresh user against the auth service and injects the JWT
  - maps each committed swagger file to its live base URL
  - writes JUnit XML reports (CI-friendly) to TestResults/schemathesis/

Setup:
    pip install -r tools/requirements.txt

Usage:
    python3 tools/run_schemathesis.py                  # fuzz every service
    python3 tools/run_schemathesis.py --service product
    python3 tools/run_schemathesis.py --max-examples 200
    python3 tools/run_schemathesis.py --checks not_a_server_error

Base URLs honour AUTH_BASE_URL / PRODUCT_BASE_URL / ORDER_BASE_URL env vars.
Exit code is non-zero if any service run finds failures.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import urllib.request
import uuid
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from openapi_common import (  # noqa: E402
    REPO_ROOT, SERVICES, discover_auth_flow, example_body, extract_endpoints,
    load_spec,
)

REPORT_DIR = REPO_ROOT / "TestResults" / "schemathesis"


def find_cli() -> list[str] | None:
    """Locate the schemathesis CLI (or fall back to `python -m schemathesis`)."""
    for name in ("schemathesis", "st"):
        path = shutil.which(name)
        if path:
            return [path]
    try:
        import schemathesis  # noqa: F401
        return [sys.executable, "-m", "schemathesis"]
    except ImportError:
        return None


def acquire_token() -> str:
    """Register a unique user via the auth issuer; return the JWT."""
    auth_service = next(s for s in SERVICES.values() if s.is_auth_issuer)
    spec = load_spec(auth_service)
    endpoints = extract_endpoints(spec)
    flow = discover_auth_flow(spec, endpoints)
    register = flow.register_endpoint or flow.login_endpoint
    if register is None:
        raise SystemExit("auth spec has no register/login endpoint")

    body = example_body(spec, register) or {}
    uniq = uuid.uuid4().hex[:10]
    for key in body:
        if "username" in key.lower():
            body[key] = f"fuzz_{uniq}"
        elif "email" in key.lower():
            body[key] = f"fuzz_{uniq}@example.com"

    url = auth_service.base_url + register.path
    req = urllib.request.Request(
        url, data=json.dumps(body).encode(), method="POST",
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=15) as resp:
        payload = json.loads(resp.read())

    node: object = payload
    for part in flow.token_json_path:
        node = node.get(part) if isinstance(node, dict) else None
    if not isinstance(node, str) or not node:
        raise SystemExit(f"could not extract token from auth response: {payload}")
    return node


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--service", choices=list(SERVICES),
                        help="fuzz a single service")
    parser.add_argument("--max-examples", type=int, default=50,
                        help="generated cases per endpoint (default: 50)")
    parser.add_argument("--checks", default="all",
                        help="schemathesis checks to run (default: all)")
    parser.add_argument("--extra", nargs=argparse.REMAINDER, default=[],
                        help="extra args passed through to schemathesis")
    args = parser.parse_args()

    cli = find_cli()
    if cli is None:
        print("schemathesis is not installed.\n"
              "    pip install -r tools/requirements.txt", file=sys.stderr)
        return 2

    services = [s for s in SERVICES.values()
                if not args.service or s.key == args.service]

    needs_auth = any(not s.is_auth_issuer for s in services)
    token = None
    if needs_auth:
        print("acquiring JWT from auth service ...")
        token = acquire_token()
        print("token acquired")

    REPORT_DIR.mkdir(parents=True, exist_ok=True)
    overall_rc = 0

    for service in services:
        junit = REPORT_DIR / f"{service.key}-schemathesis.xml"
        cmd = cli + [
            "run", str(service.swagger_path),
            "--base-url", service.base_url,
            "--checks", args.checks,
            "--hypothesis-max-examples", str(args.max_examples),
            "--junit-xml", str(junit),
        ]
        if token and not service.is_auth_issuer:
            cmd += ["--header", f"Authorization: Bearer {token}"]
        cmd += args.extra

        print(f"\n── fuzzing {service.name} @ {service.base_url} "
              f"({args.max_examples} examples/endpoint) ──")
        rc = subprocess.call(cmd)
        if rc != 0:
            overall_rc = 1
        print(f"junit report: {junit.relative_to(REPO_ROOT)}")

    return overall_rc


if __name__ == "__main__":
    sys.exit(main())
