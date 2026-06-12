#!/usr/bin/env python3
"""
Generic OpenAPI-driven smoke & contract runner. Pure Python stdlib — no deps.

Walks every endpoint of every registered service (tools/openapi_common.SERVICES)
in natural CRUD order, with real chained data:

  1. registers a user via the auth service and captures the JWT
  2. POSTs example payloads generated from the request schemas
  3. captures created ids and reuses them for GET/{id}, PUT/{id}, DELETE/{id}
  4. resolves cross-service foreign keys (order.productId ← created product)
  5. asserts the response status matches the spec's success code
  6. validates the response body against the spec's response schema
     (field types, unexpected nulls on non-nullable fields)
  7. additionally verifies protected endpoints return 401 without a token

Usage:
    python3 tools/smoke_runner.py                 # run everything
    python3 tools/smoke_runner.py --service product
    python3 tools/smoke_runner.py --list          # show the plan, no requests
    python3 tools/smoke_runner.py --json out.json # machine-readable report
    python3 tools/smoke_runner.py --no-auth-checks

Base URLs come from env vars (AUTH_BASE_URL, PRODUCT_BASE_URL, ORDER_BASE_URL)
with localhost docker-compose defaults. Exit code 0 = all green, 1 = failures.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field as dc_field
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from openapi_common import (  # noqa: E402
    SERVICES, Endpoint, Service,
    crud_sort_key, discover_auth_flow, example_body, example_param_value,
    foreign_key_resource, load_all, resolve_schema, resource_of,
)

TIMEOUT_SECONDS = 15


# ──────────────────────────────────────────────────────────────────────────────
# Minimal HTTP client (urllib)
# ──────────────────────────────────────────────────────────────────────────────

@dataclass
class HttpResponse:
    status: int
    body: object | None
    raw: str
    elapsed_ms: int


def http_request(method: str, url: str, body: dict | None = None,
                 token: str | None = None) -> HttpResponse:
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")

    started = time.monotonic()
    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT_SECONDS) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            status = resp.status
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", errors="replace")
        status = exc.code
    elapsed_ms = int((time.monotonic() - started) * 1000)

    parsed: object | None = None
    if raw.strip():
        try:
            parsed = json.loads(raw)
        except json.JSONDecodeError:
            parsed = None
    return HttpResponse(status=status, body=parsed, raw=raw, elapsed_ms=elapsed_ms)


# ──────────────────────────────────────────────────────────────────────────────
# Lightweight response-schema validation
# ──────────────────────────────────────────────────────────────────────────────

_TYPE_CHECKS = {
    "string": lambda v: isinstance(v, str),
    "integer": lambda v: isinstance(v, int) and not isinstance(v, bool),
    "number": lambda v: isinstance(v, (int, float)) and not isinstance(v, bool),
    "boolean": lambda v: isinstance(v, bool),
    "array": lambda v: isinstance(v, list),
    "object": lambda v: isinstance(v, dict),
}


def validate_against_schema(spec: dict, schema: dict | None, value: object,
                            path: str = "$", depth: int = 0) -> list[str]:
    """Best-effort structural validation: types match, non-nullable fields
    are not null, enums hold declared values. Returns a list of violations."""
    problems: list[str] = []
    if schema is None or depth > 8:
        return problems
    schema = resolve_schema(spec, schema) or {}
    stype = schema.get("type")

    if value is None:
        if not schema.get("nullable", False):
            problems.append(f"{path}: null but schema is not nullable")
        return problems

    if "enum" in schema and value not in schema["enum"]:
        problems.append(f"{path}: value {value!r} not in enum {schema['enum']}")

    if stype in _TYPE_CHECKS and not _TYPE_CHECKS[stype](value):
        problems.append(
            f"{path}: expected {stype}, got {type(value).__name__} ({value!r})")
        return problems

    if stype == "array" and isinstance(value, list):
        for i, item in enumerate(value[:5]):   # sample first items
            problems += validate_against_schema(
                spec, schema.get("items"), item, f"{path}[{i}]", depth + 1)
    elif (stype == "object" or "properties" in schema) and isinstance(value, dict):
        props = schema.get("properties") or {}
        for name, prop_schema in props.items():
            if name in value:
                problems += validate_against_schema(
                    spec, prop_schema, value[name], f"{path}.{name}", depth + 1)
            elif name in (schema.get("required") or []):
                problems.append(f"{path}.{name}: required property missing")
    return problems


# ──────────────────────────────────────────────────────────────────────────────
# Execution plan & state
# ──────────────────────────────────────────────────────────────────────────────

@dataclass
class CheckResult:
    service: str
    label: str
    passed: bool
    detail: str = ""
    elapsed_ms: int = 0


@dataclass
class RunState:
    token: str | None = None
    id_pool: dict[str, object] = dc_field(default_factory=dict)       # resource → created id
    payload_pool: dict[str, dict] = dc_field(default_factory=dict)    # resource → create payload
    results: list[CheckResult] = dc_field(default_factory=list)

    def record(self, service: Service, label: str, passed: bool,
               detail: str = "", elapsed_ms: int = 0) -> None:
        self.results.append(CheckResult(service.name, label, passed, detail, elapsed_ms))
        mark = "\033[32mPASS\033[0m" if passed else "\033[31mFAIL\033[0m"
        line = f"  [{mark}] {label} ({elapsed_ms}ms)"
        if detail and not passed:
            line += f"\n         {detail}"
        print(line)


def resolve_path(endpoint: Endpoint, spec: dict, state: RunState) -> str:
    """Substitute path params from captured ids / payload fields / examples."""
    path = endpoint.path
    resource = resource_of(endpoint.path)
    payload = state.payload_pool.get(resource, {})
    for param in endpoint.path_params:
        name = param["name"]
        placeholder = "{" + name + "}"
        if name.lower() == "id" and resource in state.id_pool:
            value = state.id_pool[resource]
        else:
            value = next(
                (payload[k] for k in payload if k.lower() == name.lower()), None)
            if value is None:
                value = next(
                    (payload[k] for k in payload if name.lower() in k.lower()), None)
            if value is None:
                value = example_param_value(spec, param)
        path = path.replace(placeholder, str(value))
    return path


def prepare_body(endpoint: Endpoint, spec: dict, state: RunState) -> dict | None:
    body = example_body(spec, endpoint)
    if body is None:
        return None
    for field_name in list(body):
        pool = foreign_key_resource(field_name)
        if pool and pool in state.id_pool:
            body[field_name] = state.id_pool[pool]
    return body


def acquire_token(state: RunState) -> bool:
    """Register a unique user via the auth issuer and capture the JWT."""
    auth_entry = next(((s, sp, eps) for s, sp, eps in load_all()
                       if s.is_auth_issuer), None)
    if auth_entry is None:
        print("no auth-issuer service registered — running unauthenticated")
        return True
    service, spec, endpoints = auth_entry
    flow = discover_auth_flow(spec, endpoints)
    register = flow.register_endpoint or flow.login_endpoint
    if register is None:
        print("auth service has no register/login endpoint — running unauthenticated")
        return True

    body = example_body(spec, register) or {}
    uniq = uuid.uuid4().hex[:10]
    for key in body:
        if "username" in key.lower():
            body[key] = f"smoke_{uniq}"
        elif "email" in key.lower():
            body[key] = f"smoke_{uniq}@example.com"

    url = service.base_url + register.path
    try:
        resp = http_request("POST", url, body)
    except (urllib.error.URLError, OSError) as exc:
        state.record(service, f"acquire token via {register.path}", False,
                     f"{url} unreachable: {exc}")
        return False

    node: object = resp.body
    for part in flow.token_json_path:
        node = node.get(part) if isinstance(node, dict) else None
    if resp.status in (200, 201) and isinstance(node, str) and node:
        state.token = node
        state.record(service, f"acquire token via POST {register.path}", True,
                     elapsed_ms=resp.elapsed_ms)
        return True

    state.record(service, f"acquire token via POST {register.path}", False,
                 f"HTTP {resp.status}: {resp.raw[:300]}", resp.elapsed_ms)
    return False


def run_endpoint(service: Service, spec: dict, endpoint: Endpoint,
                 state: RunState, auth_checks: bool,
                 create_endpoint: Endpoint | None = None) -> None:
    resource = resource_of(endpoint.path)

    # DELETE must not consume the pooled id — other services may still
    # reference it (order.productId). Create a fresh victim to delete.
    if (endpoint.method == "DELETE" and create_endpoint is not None
            and resource in state.id_pool):
        victim_body = prepare_body(create_endpoint, spec, state)
        try:
            victim = http_request(
                "POST", service.base_url + create_endpoint.path, victim_body,
                state.token if create_endpoint.requires_auth else None)
            if victim.status == (create_endpoint.success_status or 201) \
                    and isinstance(victim.body, dict) and "id" in victim.body:
                pooled = state.id_pool[resource]
                state.id_pool[resource] = victim.body["id"]
                try:
                    _execute(service, spec, endpoint, state, auth_checks)
                finally:
                    state.id_pool[resource] = pooled
                return
        except (urllib.error.URLError, OSError):
            pass  # fall through and delete the pooled id as a last resort

    _execute(service, spec, endpoint, state, auth_checks)


def _execute(service: Service, spec: dict, endpoint: Endpoint,
             state: RunState, auth_checks: bool) -> None:
    resource = resource_of(endpoint.path)
    path = resolve_path(endpoint, spec, state)
    body = prepare_body(endpoint, spec, state)
    url = service.base_url + path
    expected = endpoint.success_status or 200
    label = f"{endpoint.method} {path} → {expected}"

    try:
        resp = http_request(endpoint.method, url, body,
                            state.token if endpoint.requires_auth else None)
    except (urllib.error.URLError, OSError) as exc:
        state.record(service, label, False, f"unreachable: {exc}")
        return

    status_ok = resp.status == expected
    detail = "" if status_ok else f"got HTTP {resp.status}: {resp.raw[:300]}"
    state.record(service, label, status_ok, detail, resp.elapsed_ms)

    # contract validation against the declared response schema
    schema = endpoint.responses.get(expected)
    if status_ok and schema is not None and resp.body is not None:
        problems = validate_against_schema(spec, schema, resp.body)
        state.record(service, f"{endpoint.method} {path} matches response schema",
                     not problems, "; ".join(problems[:5]), resp.elapsed_ms)

    # capture created resource for downstream id-based endpoints
    if status_ok and endpoint.method == "POST" and isinstance(resp.body, dict):
        if "id" in resp.body:
            state.id_pool[resource] = resp.body["id"]
        if body:
            state.payload_pool[resource] = body

    # negative check: protected endpoints must reject anonymous calls
    if auth_checks and endpoint.requires_auth and endpoint.method == "GET":
        try:
            anon = http_request(endpoint.method, url, None, token=None)
            state.record(service, f"{endpoint.method} {path} without token → 401",
                         anon.status == 401,
                         "" if anon.status == 401 else f"got HTTP {anon.status}",
                         anon.elapsed_ms)
        except (urllib.error.URLError, OSError) as exc:
            state.record(service, f"{endpoint.method} {path} without token → 401",
                         False, f"unreachable: {exc}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--service", choices=list(SERVICES),
                        help="limit the run to one service")
    parser.add_argument("--list", action="store_true",
                        help="print the execution plan without sending requests")
    parser.add_argument("--json", metavar="FILE",
                        help="write a machine-readable JSON report")
    parser.add_argument("--no-auth-checks", action="store_true",
                        help="skip the anonymous-401 negative checks")
    args = parser.parse_args()

    plan = [(s, sp, sorted(eps, key=crud_sort_key)) for s, sp, eps in load_all()
            if not args.service or s.key == args.service]

    if args.list:
        for service, _, endpoints in plan:
            print(f"{service.name} ({service.base_url}) — {len(endpoints)} endpoints:")
            for ep in endpoints:
                auth = "JWT" if ep.requires_auth else "anon"
                print(f"  {ep.method:<6} {ep.path:<40} [{auth}] expect {ep.success_status}")
        return 0

    state = RunState()
    needs_token = any(ep.requires_auth for _, _, eps in plan for ep in eps)

    print("── smoke run ──────────────────────────────────────────────")
    if needs_token and not acquire_token(state):
        print("\ncannot acquire JWT — aborting (is the auth service running?)")
        _summarize(state, args.json)
        return 1

    for service, spec, endpoints in plan:
        if service.is_auth_issuer and not args.service:
            continue  # auth endpoints already exercised by acquire_token
        print(f"\n{service.name} @ {service.base_url}")
        for endpoint in endpoints:
            create_ep = next(
                (e for e in endpoints if e.method == "POST"
                 and resource_of(e.path) == resource_of(endpoint.path)), None)
            run_endpoint(service, spec, endpoint, state,
                         auth_checks=not args.no_auth_checks,
                         create_endpoint=create_ep)

    return _summarize(state, args.json)


def _summarize(state: RunState, json_file: str | None) -> int:
    passed = sum(1 for r in state.results if r.passed)
    failed = len(state.results) - passed
    print("\n── summary ────────────────────────────────────────────────")
    print(f"  {passed} passed, {failed} failed, {len(state.results)} total")

    if json_file:
        report = {
            "passed": passed,
            "failed": failed,
            "checks": [vars(r) for r in state.results],
        }
        Path(json_file).write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(f"  report written to {json_file}")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
