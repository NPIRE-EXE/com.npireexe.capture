#!/usr/bin/env python3
# Copyright © 2026 NPIRE EXE. All rights reserved.
"""NPIRE EXE Unity package tool. Each package is its own git repo with the package at the repo root.

  new <short-name> "<Display Name>" [--public]   (template repo only) create ../com.npireexe.<short-name>
  meta [dir]                                     generate missing .meta files
  check [dir]                                    validate against PACKAGING.md, exit 1 on failure
  unitypackage [dir] [--out <folder>]            build <name>-<version>.unitypackage (default folder: dist)
  notes [dir]                                    print GitHub release notes for the current version

[dir] defaults to the current folder. Package repos carry a copy of this file as .github/npire_pkg.py.
"""
import datetime
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import uuid
from pathlib import Path

TEMPLATE_REPO = Path(__file__).resolve().parent.parent
TEMPLATE = TEMPLATE_REPO / "_template" / "com.npireexe.template"
LICENSES = TEMPLATE_REPO / "licenses"
ORG = "NPIRE-EXE"

COPYRIGHT = re.compile(r"Copyright:? © \d{4} NPIRE EXE\.")
SEMVER = re.compile(r"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")
NAME = re.compile(r"^com\.npireexe\.[a-z0-9]+(-[a-z0-9]+)*$")
# Unity ignores hidden folders and folders ending in ~, so they need no .meta.
SKIP_META = re.compile(r"(^|/)(\.|[^/]*~(/|$))")
# Repo housekeeping that is not part of the Unity package.
NOT_ASSETS = {"dist"}

IMPORTER_TAIL = "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
META_BY_EXT = {
    ".cs": "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
           "  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    ".asmdef": "AssemblyDefinitionImporter:\n" + IMPORTER_TAIL,
}
TEXT_META = "TextScriptImporter:\n" + IMPORTER_TAIL
PACKAGE_JSON_META = "PackageManifestImporter:\n" + IMPORTER_TAIL
FOLDER_META = "folderAsset: yes\nDefaultImporter:\n" + IMPORTER_TAIL


def manifest_of(pkg):
    return json.loads((pkg / "package.json").read_text(encoding="utf-8"))


def asset_paths(pkg):
    for p in sorted(pkg.rglob("*")):
        rel = p.relative_to(pkg).as_posix()
        if p.suffix == ".meta" or SKIP_META.search(rel) or rel.split("/")[0] in NOT_ASSETS:
            continue
        yield p


def guid_of(path):
    m = re.search(r"^guid: ([0-9a-f]{32})$", Path(str(path) + ".meta").read_text(encoding="utf-8"), re.M)
    return m.group(1) if m else None


def write_meta(path):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        return False
    if path.is_dir():
        body = FOLDER_META
    elif path.name == "package.json":
        body = PACKAGE_JSON_META
    else:
        body = META_BY_EXT.get(path.suffix, TEXT_META)
    meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n{body}", encoding="utf-8", newline="\n")
    return True


def cmd_meta(pkg):
    made = [p for p in asset_paths(pkg) if write_meta(p)]
    print(f"{pkg.name}: {len(made)} .meta file(s) created")


def check_package(pkg):
    failures = []
    fail = failures.append

    if not (pkg / "package.json").exists():
        return ["missing package.json"]
    manifest = manifest_of(pkg)
    name, version = manifest.get("name", ""), manifest.get("version", "")
    if not NAME.match(name):
        fail(f"name '{name}' must look like com.npireexe.<short-name>")
    if pkg.name.startswith("com.") and name != pkg.name:
        fail(f"package.json name '{name}' must match folder name")
    if not SEMVER.match(version):
        fail(f"version '{version}' is not semver")
    for field in ("displayName", "description", "unity", "author", "license"):
        if not manifest.get(field):
            fail(f"package.json missing '{field}'")

    for doc in ("README.md", "CHANGELOG.md", "LICENSE.md"):
        path = pkg / doc
        if not path.exists():
            fail(f"missing {doc}")
        elif not COPYRIGHT.search(path.read_text(encoding="utf-8")):
            fail(f"{doc} missing NPIRE EXE copyright line")
    license_text = (pkg / "LICENSE.md").read_text(encoding="utf-8") if (pkg / "LICENSE.md").exists() else ""
    if manifest.get("license") == "MIT" and "MIT License" not in license_text:
        fail("package.json says MIT but LICENSE.md is not the MIT license")
    changelog = pkg / "CHANGELOG.md"
    if changelog.exists() and f"## [{version}]" not in changelog.read_text(encoding="utf-8"):
        fail(f"CHANGELOG.md has no '## [{version}]' entry")

    for folder, platforms in (("Runtime", None), ("Editor", ["Editor"])):
        if not (pkg / folder).exists():
            continue
        asmdefs = list((pkg / folder).glob("*.asmdef"))
        if not asmdefs:
            fail(f"{folder}/ has no .asmdef")
        for asmdef in asmdefs:
            if platforms and json.loads(asmdef.read_text(encoding="utf-8")).get("includePlatforms") != platforms:
                fail(f"{asmdef.name} must set includePlatforms to {platforms}")

    guids = {}
    for path in asset_paths(pkg):
        rel = path.relative_to(pkg).as_posix()
        if path.suffix == ".cs":
            text = path.read_text(encoding="utf-8")
            if not COPYRIGHT.search(text[:600]):
                fail(f"{rel} missing copyright header")
            if not re.search(r"^namespace NPIREEXE\.", text, re.M):
                fail(f"{rel} must be inside an NPIREEXE.* namespace")
        if not Path(str(path) + ".meta").exists():
            fail(f"{rel} has no .meta (run: npire_pkg.py meta)")
            continue
        guid = guid_of(path)
        if not guid:
            fail(f"{rel}.meta has no guid")
        elif guid in guids:
            fail(f"{rel}.meta guid duplicates {guids[guid]}")
        else:
            guids[guid] = rel
    for meta in pkg.rglob("*.meta"):
        if not Path(str(meta)[:-5]).exists():
            fail(f"orphan {meta.relative_to(pkg).as_posix()}")
    return failures


def cmd_check(pkg):
    targets = [pkg] if (pkg / "package.json").exists() else sorted(p.parent for p in pkg.glob("*/package.json"))
    failed = False
    for target in targets:
        failures = check_package(target)
        if failures:
            failed = True
            for f in failures:
                print(f"FAIL {target.name}: {f}")
        else:
            print(f"{target.name} {manifest_of(target)['version']}: OK")
    sys.exit(1 if failed or not targets else 0)


def cmd_unitypackage(pkg, out_dir):
    manifest = manifest_of(pkg)
    folder = manifest["displayName"].removeprefix("NPIRE EXE").strip() or manifest["name"]
    root = f"Assets/NPIRE EXE/{folder}"

    def add(tar, name, data):
        info = tarfile.TarInfo(name)
        info.size, info.mode = len(data), 0o644
        tar.addfile(info, io.BytesIO(data))

    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / f"{manifest['name']}-{manifest['version']}.unitypackage"
    with tarfile.open(out, "w:gz") as tar:
        # Parent folders get stable GUIDs so every NPIRE package agrees on "Assets/NPIRE EXE".
        for folder_path in ("Assets/NPIRE EXE", root):
            guid = uuid.uuid5(uuid.NAMESPACE_URL, f"npireexe:{folder_path}").hex
            add(tar, f"{guid}/pathname", folder_path.encode())
            add(tar, f"{guid}/asset.meta", f"fileFormatVersion: 2\nguid: {guid}\n{FOLDER_META}".encode())
        for path in asset_paths(pkg):
            if path.name == "package.json":
                continue
            guid = guid_of(path)
            rel = path.relative_to(pkg).as_posix()
            add(tar, f"{guid}/pathname", f"{root}/{rel}".encode())
            add(tar, f"{guid}/asset.meta", Path(str(path) + ".meta").read_bytes())
            if path.is_file():
                add(tar, f"{guid}/asset", path.read_bytes())
    print(out)


def cmd_notes(pkg):
    manifest = manifest_of(pkg)
    version = manifest["version"]
    changelog = (pkg / "CHANGELOG.md").read_text(encoding="utf-8")
    m = re.search(rf"^## \[{re.escape(version)}\][^\n]*\n(.*?)(?=^## \[|\Z)", changelog, re.M | re.S)
    repo = os.environ.get("GITHUB_REPOSITORY", f"{ORG}/{manifest['name']}")
    deps = manifest.get("dependencies", {})
    print((m.group(1).strip() if m else "") + "\n")
    print("## Install with Package Manager (recommended)\n")
    print("Add to `Packages/manifest.json` under `dependencies`:\n")
    print(f'```json\n"{manifest["name"]}": "https://github.com/{repo}.git#v{version}"\n```\n')
    print("## Or import the .unitypackage\n")
    print(f"Download `{manifest['name']}-{version}.unitypackage` below and open it in Unity.")
    if deps:
        print("Install these first from Package Manager: " + ", ".join(f"`{k}` {v}" for k, v in deps.items()) + ".")
    print("Don't use both install methods in the same project.")


def cmd_new(short, display, public):
    if not TEMPLATE.exists():
        sys.exit("new only runs from the unity-package-template repo")
    name = f"com.npireexe.{short}"
    dest = TEMPLATE_REPO.parent / name
    if dest.exists():
        sys.exit(f"{dest} already exists")
    shutil.copytree(TEMPLATE, dest)
    ns = "NPIREEXE." + "".join(part.capitalize() for part in short.split("-"))
    license_id = "MIT" if public else "SEE LICENSE IN LICENSE.md"
    replacements = {
        "com.npireexe.template": name,
        "NPIREEXE.Template": ns,
        "Template Display Name": display,
        "YYYY-MM-DD": datetime.date.today().isoformat(),
        "__LICENSE_ID__": license_id,
        "__LICENSE_LINE__": "Licensed under the MIT License." if public else "All rights reserved.",
    }
    for path in dest.rglob("*"):
        if path.is_file():
            text = path.read_text(encoding="utf-8")
            for old, new in replacements.items():
                text = text.replace(old, new)
            path.write_text(text, encoding="utf-8", newline="\n")
    for path in sorted(dest.rglob("*NPIREEXE.Template*"), reverse=True):
        path.rename(path.with_name(path.name.replace("NPIREEXE.Template", ns)))
    shutil.copy(LICENSES / ("MIT.md" if public else "PROPRIETARY.md"), dest / "LICENSE.md")
    shutil.copy(Path(__file__), dest / ".github" / "npire_pkg.py")
    cmd_meta(dest)
    subprocess.run(["git", "init", "-q", "-b", "main"], cwd=dest, check=True)
    visibility = "--public" if public else "--private"
    print(f"Created {dest}\nNext: fill in package.json, README and CHANGELOG, run check, commit, then\n"
          f"  gh repo create {ORG}/{name} {visibility} --source . --push")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)
    cmd, rest = args[0], args[1:]
    if cmd == "new" and len(rest) >= 2:
        cmd_new(rest[0], rest[1], "--public" in rest[2:])
    elif cmd in ("meta", "check", "unitypackage", "notes"):
        out = Path("dist")
        if "--out" in rest:
            i = rest.index("--out")
            out = Path(rest[i + 1])
            rest = rest[:i] + rest[i + 2:]
        pkg = Path(rest[0] if rest else ".").resolve()
        {"meta": lambda: cmd_meta(pkg), "check": lambda: cmd_check(pkg),
         "unitypackage": lambda: cmd_unitypackage(pkg, out), "notes": lambda: cmd_notes(pkg)}[cmd]()
    else:
        sys.exit(__doc__)
