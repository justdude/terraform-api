#!/usr/bin/env python3
"""apitf — deterministic OpenAPI <-> Terraform helper. No LLM, pure transforms."""
import argparse, json, sys, io, copy
from pathlib import Path
from ruamel.yaml import YAML
from openapi_spec_validator import validate as oas_validate
from openapi_spec_validator.readers import read_from_filename

HTTP_METHODS = ["get", "put", "post", "delete", "patch", "options", "head", "trace"]
_yaml = YAML(); _yaml.preserve_quotes = True; _yaml.indent(mapping=2, sequence=4, offset=2)

def _is_yaml(p): return Path(p).suffix.lower() in (".yaml", ".yml")

def load(path):
    if _is_yaml(path):
        with open(path) as f: return _yaml.load(f)
    return json.loads(Path(path).read_text())

def dump(spec, path):
    """Canonical, deterministic write. Methods sorted by a fixed verb order."""
    spec = canonicalize(spec)
    if _is_yaml(path):
        buf = io.StringIO(); _yaml.dump(spec, buf); Path(path).write_text(buf.getvalue())
    else:
        Path(path).write_text(json.dumps(spec, indent=2, ensure_ascii=False) + "\n")

def canonicalize(spec):
    """Stable ordering of verbs within each path so diffs are minimal & reproducible."""
    spec = copy.deepcopy(spec)
    for route, methods in spec.get("paths", {}).items():
        ordered = {k: methods[k] for k in HTTP_METHODS if k in methods}
        # keep non-verb keys (parameters, $ref, summary) after verbs, in original order
        for k in list(methods.keys()):
            if k not in HTTP_METHODS: ordered[k] = methods[k]
        spec["paths"][route] = type(methods)(ordered) if hasattr(methods, "copy") else ordered
    return spec

def cmd_init(a):
    spec = {"openapi": "3.0.3",
            "info": {"title": a.title, "version": "1.0.0"},
            "paths": {}}
    dump(spec, a.file); print(f"created {a.file}")

def cmd_list(a):
    spec = load(a.file); n = 0
    for route, methods in spec.get("paths", {}).items():
        for verb in methods:
            if verb in HTTP_METHODS:
                print(f"{verb.upper():7} {route}"); n += 1
    print(f"-- {n} operations")

def cmd_add(a):
    spec = load(a.file)
    verb = a.method.lower()
    if verb not in HTTP_METHODS: sys.exit(f"bad method {a.method}")
    paths = spec.setdefault("paths", {})
    route = paths.setdefault(a.path, {})
    if verb in route and not a.force:
        print(f"exists: {verb.upper()} {a.path} (no change)"); dump(spec, a.file); return
    import re
    op = {"operationId": a.operation_id or (verb + a.path.replace("/", "_").replace("{","").replace("}","")),
          "summary": a.summary or f"{verb.upper()} {a.path}",
          "responses": {"200": {"description": "OK"}}}
    params = [{"name": tok, "in": "path", "required": True, "schema": {"type": "string"}}
              for tok in re.findall(r"\{([^}]+)\}", a.path)]
    if params: op["parameters"] = params
    route[verb] = op
    dump(spec, a.file); print(f"added: {verb.upper()} {a.path}")

def cmd_remove(a):
    spec = load(a.file); verb = a.method.lower()
    paths = spec.get("paths", {})
    if a.path in paths and verb in paths[a.path]:
        del paths[a.path][verb]
        if not any(v in HTTP_METHODS for v in paths[a.path]): del paths[a.path]
        print(f"removed: {verb.upper()} {a.path}")
    else:
        print(f"absent: {verb.upper()} {a.path} (no change)")
    dump(spec, a.file)

def cmd_validate(a):
    try:
        oas_validate(load(a.file)); print("VALID"); return 0
    except Exception as e:
        print("INVALID:", str(e).splitlines()[0]); return 1

def cmd_gen(a):
    """Emit Terraform JSON (.tf.json) for APIM bulk import. Built from a dict -> no HCL escaping bugs."""
    spec = load(a.file)
    fmt = "openapi" if _is_yaml(a.file) else "openapi+json"
    tf = {"resource": {"azurerm_api_management_api": {a.name: {
        "name": a.name, "resource_group_name": "${var.resource_group_name}",
        "api_management_name": "${var.apim_name}", "revision": "1",
        "display_name": spec.get("info", {}).get("title", a.name),
        "path": a.name, "protocols": ["https"],
        "import": {"content_format": fmt,
                   "content_value": "${file(\"%s\")}" % a.file}}}}}
    Path(a.out).write_text(json.dumps(tf, indent=2) + "\n")
    json.loads(Path(a.out).read_text())  # self-check well-formed
    print(f"wrote {a.out}")

def main():
    p = argparse.ArgumentParser(prog="apitf")
    sub = p.add_subparsers(required=True)
    s = sub.add_parser("init");     s.add_argument("file"); s.add_argument("--title", default="API"); s.set_defaults(fn=cmd_init)
    s = sub.add_parser("list");     s.add_argument("file"); s.set_defaults(fn=cmd_list)
    s = sub.add_parser("add");      s.add_argument("file"); s.add_argument("method"); s.add_argument("path")
    s.add_argument("--operation-id"); s.add_argument("--summary"); s.add_argument("--force", action="store_true"); s.set_defaults(fn=cmd_add)
    s = sub.add_parser("remove");   s.add_argument("file"); s.add_argument("method"); s.add_argument("path"); s.set_defaults(fn=cmd_remove)
    s = sub.add_parser("validate"); s.add_argument("file"); s.set_defaults(fn=cmd_validate)
    s = sub.add_parser("gen");      s.add_argument("file"); s.add_argument("--name", default="api"); s.add_argument("--out", default="api.tf.json"); s.set_defaults(fn=cmd_gen)
    a = p.parse_args(); rc = a.fn(a); sys.exit(rc or 0)

if __name__ == "__main__": main()
