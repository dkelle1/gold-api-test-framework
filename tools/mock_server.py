#!/usr/bin/env python3
"""
Generic OpenAPI mock server. Pure Python stdlib — no deps.

Serves every service registered in tools/openapi_common.SERVICES on its
default port (5300/5100/5200), with behaviour derived entirely from the spec:

  - endpoints marked with a security requirement reject requests without an
    `Authorization: Bearer ...` header (401)
  - POST stores an in-memory resource (id auto-increment) and returns a
    schema-conformant body merged with the request payload
  - GET /resource returns the stored list; GET /resource/{id} returns the
    item or 404; PUT updates or 404s; DELETE removes (204) or 404s
  - the auth issuer's register/login endpoints return a fake JWT in the
    shape its response schema declares

Use it to develop and debug the test framework, collections, k6 scripts and
the smoke runner without running SQL Server + Redis + three .NET services:

    python3 tools/mock_server.py            # all services, Ctrl+C to stop
    python3 tools/mock_server.py --service product
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

sys.path.insert(0, str(Path(__file__).resolve().parent))
from openapi_common import (  # noqa: E402
    SERVICES, Endpoint, Service,
    example_value, extract_endpoints, load_spec, resolve_schema, resource_of,
)


def port_of(service: Service) -> int:
    return urlparse(service.default_base_url).port or 80


def path_regex(path: str) -> re.Pattern:
    pattern = re.sub(r"\{[^/}]+\}", r"([^/]+)", path)
    return re.compile(f"^{pattern}/?$")


def merge_request_fields(response: object, request: dict) -> object:
    """Overlay request payload values onto a schema-example response so the
    mock echoes back what was sent (case-insensitive name matching, applied
    recursively so nested response objects pick up flat request fields)."""
    if not isinstance(response, dict):
        return response
    lowered = {k.lower(): v for k, v in request.items()}
    merged = {}
    for key, value in response.items():
        if isinstance(value, dict):
            merged[key] = merge_request_fields(value, request)
        elif key.lower() in lowered and type(lowered[key.lower()]) is type(value):
            merged[key] = lowered[key.lower()]
        else:
            merged[key] = value
    return merged


class MockState:
    """Per-service in-memory resource store."""

    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.stores: dict[str, dict[int, dict]] = {}
        self.next_id = 1

    def create(self, resource: str, item: dict) -> dict:
        with self.lock:
            item = dict(item)
            item["id"] = self.next_id
            self.next_id += 1
            self.stores.setdefault(resource, {})[item["id"]] = item
            return item


def make_handler(service: Service, spec: dict, endpoints: list[Endpoint],
                 state: MockState):
    routes = [(ep, path_regex(ep.path)) for ep in endpoints]

    class Handler(BaseHTTPRequestHandler):
        server_version = "OpenApiMock/1.0"

        def log_message(self, fmt, *args):  # quiet by default
            pass

        def _send(self, status: int, body: object | None = None) -> None:
            payload = json.dumps(body).encode() if body is not None else b""
            self.send_response(status)
            if payload:
                self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def _read_body(self) -> dict:
            length = int(self.headers.get("Content-Length") or 0)
            if not length:
                return {}
            try:
                parsed = json.loads(self.rfile.read(length))
                return parsed if isinstance(parsed, dict) else {}
            except json.JSONDecodeError:
                return {}

        def _handle(self) -> None:
            path = urlparse(self.path).path
            method = self.command.upper()

            match_ep: Endpoint | None = None
            match_groups: tuple = ()
            for ep, rx in routes:
                m = rx.match(path)
                if m and ep.method == method:
                    match_ep, match_groups = ep, m.groups()
                    break
            if match_ep is None:
                self._send(404, {"error": f"no mock route for {method} {path}"})
                return

            if match_ep.requires_auth:
                auth = self.headers.get("Authorization") or ""
                if not auth.startswith("Bearer ") or len(auth) <= len("Bearer "):
                    self._send(401)
                    return

            resource = resource_of(match_ep.path)
            success = match_ep.success_status or 200
            response_schema = match_ep.responses.get(success)
            body = self._read_body()

            # auth issuer endpoints: return the declared response shape
            if service.is_auth_issuer:
                example = example_value(spec, response_schema) if response_schema else {}
                self._send(success, merge_request_fields(example, body))
                return

            has_id_param = "{id" in match_ep.path.lower()
            item_id = None
            if has_id_param and match_groups:
                try:
                    item_id = int(match_groups[0])
                except ValueError:
                    self._send(400, {"error": "invalid id"})
                    return

            store = state.stores.setdefault(resource, {})

            if method == "POST":
                example = example_value(spec, response_schema) if response_schema else {}
                item = merge_request_fields(example, body)
                item = state.create(resource, item if isinstance(item, dict) else {})
                self._send(success, item)
            elif method in ("PUT", "PATCH"):
                if item_id not in store:
                    self._send(404)
                    return
                store[item_id] = merge_request_fields(store[item_id], body)
                self._send(success, store[item_id])
            elif method == "DELETE":
                if item_id not in store:
                    self._send(404)
                    return
                del store[item_id]
                self._send(success)
            else:  # GET
                if has_id_param:
                    if item_id not in store:
                        self._send(404)
                        return
                    self._send(success, store[item_id])
                else:
                    schema = resolve_schema(spec, response_schema) or {}
                    if schema.get("type") == "array":
                        self._send(success, list(store.values()))
                    else:
                        self._send(success, list(store.values()))

        do_GET = do_POST = do_PUT = do_PATCH = do_DELETE = _handle

    return Handler


def serve(services: list[Service]) -> list[ThreadingHTTPServer]:
    servers = []
    for service in services:
        spec = load_spec(service)
        endpoints = extract_endpoints(spec)
        handler = make_handler(service, spec, endpoints, MockState())
        httpd = ThreadingHTTPServer(("127.0.0.1", port_of(service)), handler)
        thread = threading.Thread(target=httpd.serve_forever, daemon=True)
        thread.start()
        servers.append(httpd)
        print(f"[mock] {service.name} listening on http://127.0.0.1:{port_of(service)} "
              f"({len(endpoints)} endpoints)")
    return servers


def main() -> None:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--service", choices=list(SERVICES), action="append",
                        help="serve only the given service(s); default: all")
    args = parser.parse_args()

    selected = ([SERVICES[k] for k in args.service] if args.service
                else list(SERVICES.values()))
    servers = serve(selected)
    print("[mock] Ctrl+C to stop")
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        for httpd in servers:
            httpd.shutdown()


if __name__ == "__main__":
    main()
