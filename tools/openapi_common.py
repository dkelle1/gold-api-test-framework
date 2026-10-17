"""
Shared OpenAPI utilities for the generic Python tooling in tools/.

The tools know no concrete product: services, swagger location and output
directories come from a JSON config file (see docs/framework/python-tooling.md):

    OPENAPI_TOOLS_CONFIG=path/to/openapi-tools.json python3 tools/smoke_runner.py

Without the env var the demo-shop example config is used
(examples/demo-shop/api-tests/openapi-tools.json). Paths inside the config
are relative to the config file. Adding a microservice = one entry in
"services" — every generator (Postman/Bruno collections, k6 load tests,
smoke runner, schemathesis wrapper, mock server) picks it up automatically.

Stdlib only — no third-party dependencies.
"""

from __future__ import annotations

import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent


# ──────────────────────────────────────────────────────────────────────────────
# .env — the same file the .NET framework reads (see docs/framework/core.md)
# ──────────────────────────────────────────────────────────────────────────────

def _parse_env_value(value: str) -> str:
    if value[:1] == "'":
        end = value.find("'", 1)
        return value[1:end] if end > 0 else value[1:]
    if value[:1] == '"':
        out, i = [], 1
        while i < len(value) and value[i] != '"':
            if value[i] == "\\" and i + 1 < len(value):
                i += 1
                out.append({"n": "\n", "r": "\r", "t": "\t"}.get(value[i], value[i]))
            else:
                out.append(value[i])
            i += 1
        return "".join(out)
    comment = value.find(" #")
    return (value[:comment] if comment >= 0 else value).strip()


def load_dotenv() -> Path | None:
    """Apply a .env file to os.environ without overriding variables that are already set.

    File: $TEST_ENV_FILE, else the nearest .env from the current directory upwards,
    else <repo>/.env. Returns the file used, or None.
    """
    explicit = os.environ.get("TEST_ENV_FILE")
    if explicit:
        candidates = [Path(explicit)]
    else:
        cwd = Path.cwd().resolve()
        candidates = [d / ".env" for d in (cwd, *cwd.parents)] + [REPO_ROOT / ".env"]
    path = next((p for p in candidates if p.is_file()), None)
    if path is None:
        if explicit:
            raise SystemExit(f"TEST_ENV_FILE points to a missing file: {explicit}")
        return None

    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("export "):
            line = line[len("export "):].lstrip()
        key, sep, value = line.partition("=")
        if sep and key.strip():
            os.environ.setdefault(key.strip(), _parse_env_value(value.strip()))
    return path


DOTENV_PATH = load_dotenv()
DEFAULT_CONFIG = REPO_ROOT / "examples" / "demo-shop" / "api-tests" / "openapi-tools.json"
CONFIG_PATH = Path(os.environ.get("OPENAPI_TOOLS_CONFIG", DEFAULT_CONFIG)).resolve()


def configure_console() -> None:
    """Force UTF-8 stdout/stderr. The tools print box-drawing characters and
    arrows, which crash with UnicodeEncodeError on legacy Windows code pages
    (cp1250/cp1252) when the console is not already UTF-8."""
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


# ──────────────────────────────────────────────────────────────────────────────
# Service registry (loaded from the config file)
# ──────────────────────────────────────────────────────────────────────────────

@dataclass(frozen=True)
class Service:
    key: str                 # short identifier used on the CLI: "auth", "product", ...
    name: str                # display name
    swagger_path: Path       # absolute path of the offline swagger file
    default_base_url: str    # local default (e.g. docker-compose port mapping)
    env_var: str             # env var that overrides the base URL
    is_auth_issuer: bool = False  # service that issues JWT tokens
    auth_client: str = ""    # named OAuth2 client (config "authClients") whose token it gets

    @property
    def base_url(self) -> str:
        """Tool-specific variable (e.g. AUTH_BASE_URL), else the .NET test setting
        TEST_Services__<Name>__BaseUrl — one .env drives both — else the config default."""
        return (os.environ.get(self.env_var)
                or os.environ.get(f"TEST_Services__{self.name}__BaseUrl")
                or self.default_base_url).rstrip("/")


def _load_config(path: Path) -> dict:
    if not path.is_file():
        raise SystemExit(
            f"OpenAPI tools config not found: {path}. "
            "Set OPENAPI_TOOLS_CONFIG to your openapi-tools.json "
            "(see docs/framework/python-tooling.md).")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


CONFIG = _load_config(CONFIG_PATH)
CONFIG_DIR = CONFIG_PATH.parent


def _config_dir(key: str, default: str) -> Path:
    return (CONFIG_DIR / CONFIG.get("output", {}).get(key, default)).resolve()


SWAGGER_DIR = (CONFIG_DIR / CONFIG.get("swaggerDir", "swagger")).resolve()
COLLECTIONS_DIR = _config_dir("collections", "collections")
K6_DIR = _config_dir("k6", "perf/k6")
REPORTS_DIR = _config_dir("reports", "TestResults")
COLLECTION_NAME = CONFIG.get("collectionName", "ApiTests")

# Order matters: services are processed in config order — list the token
# issuer and dependencies (e.g. product before order) first.
SERVICES: dict[str, Service] = {
    entry["key"]: Service(
        key=entry["key"],
        name=entry.get("name", entry["key"]),
        swagger_path=SWAGGER_DIR / entry["swagger"],
        default_base_url=entry["baseUrl"],
        env_var=entry.get("envVar", f"{entry['key'].upper()}_BASE_URL"),
        is_auth_issuer=bool(entry.get("authIssuer", False)),
        auth_client=entry.get("authClient", ""))
    for entry in CONFIG.get("services", [])
}

# Named OAuth2 client-credentials clients — the counterpart of the .NET
# Authentication:Clients section. Every value can be overridden with the same
# environment variable the .NET tests read (one .env drives both).
AUTH_CLIENTS: dict[str, dict] = CONFIG.get("authClients", {})


def client_setting(client: str, key: str) -> str:
    """OAuth2 setting of a named client: TEST_Authentication__Clients__<client>__OAuth2__<Key>,
    then (TokenUrl, Scope, Audience only) the shared TEST_Authentication__OAuth2__<Key>,
    then the client's entry in "authClients" (camelCase key)."""
    value = os.environ.get(f"TEST_Authentication__Clients__{client}__OAuth2__{key}")
    if value is None and key in ("TokenUrl", "Scope", "Audience"):
        value = os.environ.get(f"TEST_Authentication__OAuth2__{key}")
    if value is None:
        value = AUTH_CLIENTS.get(client, {}).get(key[0].lower() + key[1:], "")
    if key == "TokenUrl" and value.startswith("/"):
        issuer = next((s for s in SERVICES.values() if s.is_auth_issuer), None)
        if issuer is not None:
            value = issuer.base_url + value  # relative: resolved against the auth issuer
    return value


def fetch_client_token(client: str) -> tuple[str | None, str]:
    """Client-credentials grant (RFC 6749 4.4) for a named client.
    Returns (access_token, token_url) or (None, error detail)."""
    if client not in AUTH_CLIENTS:
        return None, f"auth client '{client}' is not configured under \"authClients\""
    token_url = client_setting(client, "TokenUrl")
    form = {
        "grant_type": "client_credentials",
        "client_id": client_setting(client, "ClientId"),
        "client_secret": client_setting(client, "ClientSecret"),
    }
    for key in ("Scope", "Audience"):
        if value := client_setting(client, key):
            form[key.lower()] = value

    req = urllib.request.Request(
        token_url, data=urllib.parse.urlencode(form).encode(), method="POST",
        headers={"Content-Type": "application/x-www-form-urlencoded",
                 "Accept": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as exc:
        return None, f"{token_url}: HTTP {exc.code}: {exc.read().decode(errors='replace')[:300]}"
    except (urllib.error.URLError, OSError) as exc:
        return None, f"{token_url} unreachable: {exc}"
    try:
        token = json.loads(raw).get("access_token")
    except (json.JSONDecodeError, AttributeError):
        token = None
    return (token, token_url) if isinstance(token, str) and token else \
        (None, f"{token_url}: no access_token in {raw[:300]}")


# ──────────────────────────────────────────────────────────────────────────────
# Spec loading / endpoint extraction
# ──────────────────────────────────────────────────────────────────────────────

HTTP_METHODS = ("get", "post", "put", "patch", "delete", "head", "options")


@dataclass
class Endpoint:
    path: str
    method: str                       # upper-case: GET, POST, ...
    operation_id: str
    tag: str
    parameters: list[dict]            # raw OpenAPI parameter objects (resolved)
    request_body_schema: dict | None  # resolved JSON schema of request body
    request_body_ref: str | None      # original $ref name, e.g. "CreateProductRequest"
    responses: dict[int, dict | None] # status -> resolved response schema (or None)
    requires_auth: bool
    form_body_schema: dict | None = None  # resolved schema of an x-www-form-urlencoded body

    @property
    def success_status(self) -> int | None:
        codes = sorted(c for c in self.responses if 200 <= c < 300)
        return codes[0] if codes else None

    @property
    def path_params(self) -> list[dict]:
        return [p for p in self.parameters if p.get("in") == "path"]


def load_spec(service: Service) -> dict:
    with open(service.swagger_path, encoding="utf-8") as fh:
        return json.load(fh)


def resolve_ref(spec: dict, ref: str) -> dict:
    """Resolve a local '#/components/schemas/X' style reference."""
    node: dict = spec
    for part in ref.lstrip("#/").split("/"):
        node = node[part]
    return node


def resolve_schema(spec: dict, schema: dict | None) -> dict | None:
    """Follow $ref / oneOf one level so callers always see a concrete schema."""
    if schema is None:
        return None
    if "$ref" in schema:
        return resolve_schema(spec, resolve_ref(spec, schema["$ref"]))
    if "oneOf" in schema and schema["oneOf"]:
        resolved = dict(schema)
        resolved.update(resolve_schema(spec, schema["oneOf"][0]) or {})
        return resolved
    return schema


def ref_name(schema: dict | None) -> str | None:
    if schema and "$ref" in schema:
        return schema["$ref"].rsplit("/", 1)[-1]
    return None


def extract_endpoints(spec: dict) -> list[Endpoint]:
    endpoints: list[Endpoint] = []
    global_security = bool(spec.get("security"))

    for path, path_item in spec.get("paths", {}).items():
        for method in HTTP_METHODS:
            op = path_item.get(method)
            if op is None:
                continue

            params = [
                resolve_schema(spec, p) if "$ref" in p else p
                for p in (op.get("parameters") or []) + (path_item.get("parameters") or [])
            ]

            body_content = op.get("requestBody", {}).get("content", {})
            body_schema_raw = body_content.get("application/json", {}).get("schema")
            form_schema_raw = (body_content.get("application/x-www-form-urlencoded", {})
                               .get("schema"))

            responses: dict[int, dict | None] = {}
            for code_str, resp in (op.get("responses") or {}).items():
                try:
                    code = int(code_str)
                except ValueError:
                    continue
                resp_schema = (
                    resp.get("content", {}).get("application/json", {}).get("schema")
                )
                responses[code] = resolve_schema(spec, resp_schema) if resp_schema else None

            endpoints.append(Endpoint(
                path=path,
                method=method.upper(),
                operation_id=op.get("operationId")
                             or f"{method}_{path.strip('/').replace('/', '_')}",
                tag=(op.get("tags") or ["Default"])[0],
                parameters=params,
                request_body_schema=resolve_schema(spec, body_schema_raw),
                request_body_ref=ref_name(body_schema_raw),
                responses=responses,
                requires_auth=bool(op.get("security")) or global_security,
                form_body_schema=resolve_schema(spec, form_schema_raw),
            ))

    return endpoints


# ──────────────────────────────────────────────────────────────────────────────
# Example payload generation (schema → realistic JSON)
# ──────────────────────────────────────────────────────────────────────────────

# Name-based value heuristics, checked in order (first match wins).
# Keys are lowercase substrings of the property name.
_STRING_HINTS: list[tuple[str, str]] = [
    ("email", "qa.tester@example.com"),
    ("password", "P@ssw0rd_123!"),
    ("username", "qa_tester"),
    ("currency", "USD"),
    ("category", "Electronics"),
    ("description", "Generated by tools/openapi_common.py"),
    ("name", "Sample Name"),
    ("role", "User"),
    ("status", "Pending"),
]

_NUMBER_HINTS: list[tuple[str, float]] = [
    ("price", 19.99),
    ("amount", 19.99),
]

_INTEGER_HINTS: list[tuple[str, int]] = [
    ("stockquantity", 50),
    ("quantity", 1),
    ("id", 1),
]


def example_value(spec: dict, schema: dict | None, prop_name: str = "",
                  depth: int = 0) -> object:
    """Generate a realistic example value for a schema node."""
    if schema is None or depth > 8:
        return None
    schema = resolve_schema(spec, schema) or {}
    lname = prop_name.lower()

    if "enum" in schema and schema["enum"]:
        return schema["enum"][0]
    if "default" in schema:
        return schema["default"]
    if "example" in schema:
        return schema["example"]

    stype = schema.get("type")
    sformat = schema.get("format", "")

    if stype == "object" or (stype is None and "properties" in schema):
        return {
            name: example_value(spec, prop, name, depth + 1)
            for name, prop in (schema.get("properties") or {}).items()
        }
    if stype == "array":
        return [example_value(spec, schema.get("items"), prop_name, depth + 1)]
    if stype == "string":
        if sformat == "date-time":
            return "2026-01-01T12:00:00Z"
        if sformat == "uuid":
            return "00000000-0000-0000-0000-000000000001"
        for hint, value in _STRING_HINTS:
            if hint in lname:
                return value
        return "sample-string"
    if stype == "integer":
        for hint, value in _INTEGER_HINTS:
            if hint in lname:
                return value
        return 1
    if stype == "number":
        for hint, value in _NUMBER_HINTS:
            if hint in lname:
                return value
        return 10.5
    if stype == "boolean":
        return True
    return None


def example_body(spec: dict, endpoint: Endpoint) -> dict | None:
    """Generate an example JSON request body for an endpoint, or None."""
    if endpoint.request_body_schema is None:
        return None
    value = example_value(spec, endpoint.request_body_schema)
    return value if isinstance(value, dict) else None


def example_form(spec: dict, endpoint: Endpoint) -> dict[str, str] | None:
    """Example fields of an application/x-www-form-urlencoded body, or None."""
    if endpoint.form_body_schema is None:
        return None
    value = example_value(spec, endpoint.form_body_schema)
    if not isinstance(value, dict):
        return None
    return {k: v if isinstance(v, str) else json.dumps(v) for k, v in value.items()}


def example_param_value(spec: dict, param: dict) -> object:
    return example_value(spec, param.get("schema"), param.get("name", ""))


# ──────────────────────────────────────────────────────────────────────────────
# Auth flow discovery (which endpoint issues tokens, where the token lives)
# ──────────────────────────────────────────────────────────────────────────────

@dataclass
class AuthFlow:
    register_endpoint: Endpoint | None
    login_endpoint: Endpoint | None
    token_json_path: list[str]   # e.g. ["token", "accessToken"]


def _find_token_path(spec: dict, schema: dict | None,
                     path: list[str] | None = None, depth: int = 0) -> list[str] | None:
    """Walk a response schema looking for a string property named *accessToken*
    (or, failing that, *token*). Returns the JSON property path to it."""
    if schema is None or depth > 6:
        return None
    schema = resolve_schema(spec, schema) or {}
    path = path or []
    props = schema.get("properties") or {}
    for target in ("accesstoken", "token"):
        for name, prop in props.items():
            resolved = resolve_schema(spec, prop) or {}
            if name.lower() == target and resolved.get("type") == "string":
                return path + [name]
    for name, prop in props.items():
        found = _find_token_path(spec, prop, path + [name], depth + 1)
        if found:
            return found
    return None


def discover_auth_flow(spec: dict, endpoints: list[Endpoint]) -> AuthFlow:
    register = next((e for e in endpoints
                     if e.method == "POST" and "register" in e.path.lower()), None)
    login = next((e for e in endpoints
                  if e.method == "POST" and "login" in e.path.lower()), None)
    token_path: list[str] = []
    for ep in (login, register):
        if ep is None or ep.success_status is None:
            continue
        found = _find_token_path(spec, ep.responses.get(ep.success_status))
        if found:
            token_path = found
            break
    return AuthFlow(register_endpoint=register, login_endpoint=login,
                    token_json_path=token_path or ["token", "accessToken"])


# ──────────────────────────────────────────────────────────────────────────────
# Resource / dependency helpers
# ──────────────────────────────────────────────────────────────────────────────

def resource_of(path: str) -> str:
    """First concrete segment after /api/ — 'products' for /api/products/{id}."""
    segments = [s for s in path.strip("/").split("/") if not s.startswith("{")]
    if segments and segments[0] == "api" and len(segments) > 1:
        return segments[1].lower()
    return (segments[0] if segments else "root").lower()


def singular(resource: str) -> str:
    return resource[:-1] if resource.endswith("s") else resource


_FOREIGN_KEY_RE = re.compile(r"^(\w+?)Id$")


def foreign_key_resource(field_name: str) -> str | None:
    """'productId' → 'products' (the resource pool that can satisfy it)."""
    m = _FOREIGN_KEY_RE.match(field_name)
    if not m or m.group(1).lower() in ("", "id"):
        return None
    return m.group(1).lower() + "s"


def find_foreign_keys(value: object, path: tuple = ()) -> list[tuple[tuple, str, str]]:
    """Locate foreign-key fields at any depth of an example payload — nested
    objects and arrays included (e.g. order.items[0].productId).

    Returns [(path, field_name, pool)] where path addresses the field
    (dict keys / list indices) and pool is the resource that satisfies it."""
    found: list[tuple[tuple, str, str]] = []
    if isinstance(value, dict):
        for key, child in value.items():
            pool = foreign_key_resource(key)
            if pool and not isinstance(child, (dict, list)):
                found.append((path + (key,), key, pool))
            else:
                found.extend(find_foreign_keys(child, path + (key,)))
    elif isinstance(value, list):
        for index, child in enumerate(value):
            found.extend(find_foreign_keys(child, path + (index,)))
    return found


def set_at_path(value: object, path: tuple, new_value: object) -> None:
    """Assign new_value at a path produced by find_foreign_keys()."""
    for part in path[:-1]:
        value = value[part]  # type: ignore[index]
    value[path[-1]] = new_value  # type: ignore[index]


def find_payload_field(param_name: str, payload: object) -> tuple | None:
    """Find the payload field corresponding to a path param, searching nested
    objects too: exact (case-insensitive) name match first, then substring
    ('email' → 'customerEmail'). Returns the key path, or None."""
    name = param_name.lower()

    def walk(node: object, path: tuple, exact: bool) -> tuple | None:
        if not isinstance(node, dict):
            return None
        for key, child in node.items():
            if not isinstance(child, (dict, list)) and (
                    key.lower() == name if exact else name in key.lower()):
                return path + (key,)
        for key, child in node.items():
            hit = walk(child, path + (key,), exact)
            if hit:
                return hit
        return None

    return walk(payload, (), True) or walk(payload, (), False)


def get_at_path(value: object, path: tuple) -> object:
    for part in path:
        value = value[part]  # type: ignore[index]
    return value


CRUD_ORDER = {"POST": 0, "GET": 1, "PUT": 2, "PATCH": 3, "DELETE": 4}


def crud_sort_key(endpoint: Endpoint) -> tuple:
    """Sort endpoints into a natural CRUD execution order:
    create first, reads next, updates, deletes last; shallow paths first."""
    return (
        CRUD_ORDER.get(endpoint.method, 9),
        endpoint.path.count("{"),
        len(endpoint.path),
        endpoint.path,
    )


def load_all() -> list[tuple[Service, dict, list[Endpoint]]]:
    """Load every registered service spec with its extracted endpoints."""
    out = []
    for service in SERVICES.values():
        spec = load_spec(service)
        out.append((service, spec, extract_endpoints(spec)))
    return out
