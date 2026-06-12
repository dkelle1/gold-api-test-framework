#!/usr/bin/env python3
"""
Generate Postman and Bruno collections from the committed OpenAPI specs.

Usage:
    python3 tools/generate_collections.py            # both formats
    python3 tools/generate_collections.py --postman  # Postman only
    python3 tools/generate_collections.py --bruno    # Bruno only

Output:
    collections/postman/ApiTestFramework.postman_collection.json
    collections/postman/Local.postman_environment.json
    collections/bruno/  (bruno.json + one .bru file per request)

The generated collections are fully wired:
  - base URLs are environment variables ({{authBaseUrl}}, ...)
  - the Login/Register requests capture the JWT into {{accessToken}}
  - every protected request sends Bearer {{accessToken}} automatically
  - request bodies are realistic examples derived from the schemas

Deterministic output — safe to commit and regenerate (clean diffs).
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from openapi_common import (  # noqa: E402
    REPO_ROOT, SERVICES, AuthFlow, Endpoint, Service,
    discover_auth_flow, example_body, example_param_value, load_all,
)

POSTMAN_DIR = REPO_ROOT / "collections" / "postman"
BRUNO_DIR = REPO_ROOT / "collections" / "bruno"

COLLECTION_NAME = "ApiTestFramework"


def base_url_var(service: Service) -> str:
    return f"{service.key}BaseUrl"


def postman_url(service: Service, endpoint: Endpoint) -> dict:
    """Build a Postman URL object with path params as :variables."""
    raw_path = endpoint.path
    segments = []
    variables = []
    for seg in raw_path.strip("/").split("/"):
        if seg.startswith("{") and seg.endswith("}"):
            name = seg[1:-1]
            segments.append(f":{name}")
            param = next((p for p in endpoint.path_params if p.get("name") == name), None)
            variables.append({
                "key": name,
                "value": str(example_param_value_safe(param)),
                "description": f"Path parameter ({param.get('schema', {}).get('type', 'string') if param else 'string'})",
            })
        else:
            segments.append(seg)
    host_var = "{{" + base_url_var(service) + "}}"
    return {
        "raw": host_var + "/" + "/".join(segments),
        "host": [host_var],
        "path": segments,
        "variable": variables,
    }


def example_param_value_safe(param: dict | None) -> object:
    if param is None:
        return 1
    # spec not needed for primitive path params; fall back on simple types
    schema = param.get("schema") or {}
    if schema.get("type") == "integer":
        return 1
    name = param.get("name", "").lower()
    if "email" in name:
        return "qa.tester@example.com"
    if "category" in name:
        return "Electronics"
    return "sample"


def token_capture_script(token_path: list[str]) -> list[str]:
    accessor = "".join(f"?.{p}" for p in token_path)
    return [
        "const data = pm.response.json();",
        f"const token = data{accessor};",
        "if (token) {",
        "    pm.environment.set('accessToken', token);",
        "    console.log('accessToken captured');",
        "}",
    ]


def build_postman_request(service: Service, spec: dict, endpoint: Endpoint,
                          auth_flow: AuthFlow | None) -> dict:
    body = example_body(spec, endpoint)
    request: dict = {
        "method": endpoint.method,
        "header": [],
        "url": postman_url(service, endpoint),
        "description": f"`{endpoint.method} {endpoint.path}` — operationId: {endpoint.operation_id}",
    }
    if body is not None:
        request["header"].append({"key": "Content-Type", "value": "application/json"})
        # unique credentials on every send so register doesn't 409
        if auth_flow and endpoint in (auth_flow.register_endpoint,):
            for key in body:
                if "username" in key.lower():
                    body[key] = "user_{{$timestamp}}"
                if "email" in key.lower():
                    body[key] = "user_{{$timestamp}}@example.com"
        request["body"] = {
            "mode": "raw",
            "raw": json.dumps(body, indent=2),
            "options": {"raw": {"language": "json"}},
        }
    if not endpoint.requires_auth:
        request["auth"] = {"type": "noauth"}

    item: dict = {"name": f"{endpoint.method} {endpoint.path}", "request": request}

    if auth_flow and endpoint in (auth_flow.register_endpoint, auth_flow.login_endpoint):
        item["event"] = [{
            "listen": "test",
            "script": {"type": "text/javascript",
                       "exec": token_capture_script(auth_flow.token_json_path)},
        }]
    return item


def generate_postman() -> None:
    folders = []
    env_values = [{"key": "accessToken", "value": "", "type": "secret", "enabled": True}]

    for service, spec, endpoints in load_all():
        auth_flow = discover_auth_flow(spec, endpoints) if service.is_auth_issuer else None
        folders.append({
            "name": service.name,
            "description": spec.get("info", {}).get("description", ""),
            "item": [build_postman_request(service, spec, ep, auth_flow)
                     for ep in endpoints],
        })
        env_values.append({
            "key": base_url_var(service),
            "value": service.default_base_url,
            "type": "default",
            "enabled": True,
        })

    collection = {
        "info": {
            "name": COLLECTION_NAME,
            "description": "Generated from OpenAPI specs by tools/generate_collections.py — do not edit by hand.",
            "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
        },
        "auth": {
            "type": "bearer",
            "bearer": [{"key": "token", "value": "{{accessToken}}", "type": "string"}],
        },
        "item": folders,
    }
    environment = {
        "name": f"{COLLECTION_NAME} — Local",
        "values": env_values,
        "_postman_variable_scope": "environment",
    }

    POSTMAN_DIR.mkdir(parents=True, exist_ok=True)
    (POSTMAN_DIR / f"{COLLECTION_NAME}.postman_collection.json").write_text(
        json.dumps(collection, indent=2) + "\n", encoding="utf-8")
    (POSTMAN_DIR / "Local.postman_environment.json").write_text(
        json.dumps(environment, indent=2) + "\n", encoding="utf-8")
    print(f"[postman] wrote {POSTMAN_DIR.relative_to(REPO_ROOT)}/")


# ──────────────────────────────────────────────────────────────────────────────
# Bruno
# ──────────────────────────────────────────────────────────────────────────────

def bru_block(name: str, lines: dict[str, str] | list[str]) -> str:
    if isinstance(lines, dict):
        body = "\n".join(f"  {k}: {v}" for k, v in lines.items())
    else:
        body = "\n".join(f"  {line}" for line in lines)
    return f"{name} {{\n{body}\n}}\n"


def safe_filename(text: str) -> str:
    return "".join(c if c.isalnum() or c in "-_" else "-" for c in text).strip("-")


def build_bru_request(service: Service, spec: dict, endpoint: Endpoint,
                      auth_flow: AuthFlow | None, seq: int) -> str:
    url = "{{" + base_url_var(service) + "}}" + endpoint.path
    body = example_body(spec, endpoint)
    is_token_endpoint = auth_flow is not None and endpoint in (
        auth_flow.register_endpoint, auth_flow.login_endpoint)

    parts = [bru_block("meta", {
        "name": f"{endpoint.method} {endpoint.path}",
        "type": "http",
        "seq": str(seq),
    })]

    method_block: dict[str, str] = {"url": url}
    if body is not None:
        method_block["body"] = "json"
    method_block["auth"] = "inherit" if endpoint.requires_auth else "none"
    parts.append(bru_block(endpoint.method.lower(), method_block))

    if endpoint.path_params:
        parts.append(bru_block("params:path", {
            p["name"]: str(example_param_value_safe(p)) for p in endpoint.path_params
        }))

    if body is not None:
        if is_token_endpoint and endpoint is auth_flow.register_endpoint:
            for key in body:
                if "username" in key.lower():
                    body[key] = "user_{{uniq}}"
                if "email" in key.lower():
                    body[key] = "user_{{uniq}}@example.com"
        body_json = json.dumps(body, indent=2)
        indented = "\n".join("  " + line for line in body_json.splitlines())
        parts.append(f"body:json {{\n{indented}\n}}\n")

    if is_token_endpoint:
        accessor = "".join(f"?.{p}" for p in auth_flow.token_json_path)
        parts.append(
            "script:pre-request {\n"
            "  bru.setVar('uniq', Date.now().toString());\n"
            "}\n")
        parts.append(
            "script:post-response {\n"
            f"  const token = res.body{accessor};\n"
            "  if (token) {\n"
            "    bru.setEnvVar('accessToken', token);\n"
            "  }\n"
            "}\n")

    return "\n".join(parts)


def generate_bruno() -> None:
    if BRUNO_DIR.exists():
        shutil.rmtree(BRUNO_DIR)
    BRUNO_DIR.mkdir(parents=True)

    (BRUNO_DIR / "bruno.json").write_text(json.dumps({
        "version": "1",
        "name": COLLECTION_NAME,
        "type": "collection",
        "ignore": ["node_modules", ".git"],
    }, indent=2) + "\n", encoding="utf-8")

    # collection-level auth: every request with auth:inherit sends the bearer token
    (BRUNO_DIR / "collection.bru").write_text(
        bru_block("auth", {"mode": "bearer"}) + "\n" +
        bru_block("auth:bearer", {"token": "{{accessToken}}"}),
        encoding="utf-8")

    env_vars = {"accessToken": ""}
    for service in SERVICES.values():
        env_vars[base_url_var(service)] = service.default_base_url
    env_dir = BRUNO_DIR / "environments"
    env_dir.mkdir()
    (env_dir / "Local.bru").write_text(bru_block("vars", env_vars), encoding="utf-8")

    for service, spec, endpoints in load_all():
        auth_flow = discover_auth_flow(spec, endpoints) if service.is_auth_issuer else None
        service_dir = BRUNO_DIR / service.name
        service_dir.mkdir()
        for seq, endpoint in enumerate(endpoints, start=1):
            fname = safe_filename(f"{endpoint.method}-{endpoint.path.strip('/')}") + ".bru"
            (service_dir / fname).write_text(
                build_bru_request(service, spec, endpoint, auth_flow, seq),
                encoding="utf-8")

    print(f"[bruno]   wrote {BRUNO_DIR.relative_to(REPO_ROOT)}/")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--postman", action="store_true", help="generate Postman only")
    parser.add_argument("--bruno", action="store_true", help="generate Bruno only")
    args = parser.parse_args()

    both = not (args.postman or args.bruno)
    if args.postman or both:
        generate_postman()
    if args.bruno or both:
        generate_bruno()


if __name__ == "__main__":
    main()
