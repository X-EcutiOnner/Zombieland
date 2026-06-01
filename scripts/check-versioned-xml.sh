#!/usr/bin/env bash
set -euo pipefail

python3 - <<'PY'
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

repo = Path(".")
load_folders = ET.parse(repo / "LoadFolders.xml").getroot()
content_roots = ("Defs", "Patches", "Languages")

root_xml = sorted(
    path.relative_to(repo).as_posix()
    for folder in content_roots
    for path in (repo / folder).rglob("*.xml")
)

errors = []
checked_versions = []

for node in list(load_folders):
    tag = node.tag
    if tag.startswith("v"):
        version = tag[1:]
    elif tag == "default":
        continue
    else:
        version = tag

    entries = [
        (child.text or "").strip()
        for child in list(node)
        if child.tag == "li"
    ]
    version_dir = repo / version

    if not version_dir.exists():
        errors.append(f"{version}: LoadFolders entry exists but {version}/ is missing")
        continue

    checked_versions.append(version)

    if "/" in entries and version in entries and entries.index("/") > entries.index(version):
        errors.append(
            f"{version}: LoadFolders should list / before {version}; RimWorld reverses the list and later entries win"
        )

    if "/" in entries:
        missing = [
            rel
            for rel in root_xml
            if not (version_dir / rel).exists()
        ]
        if missing:
            sample = ", ".join(missing[:8])
            more = "" if len(missing) <= 8 else f", ... +{len(missing) - 8} more"
            errors.append(
                f"{version}: missing version shadows for root XML: {sample}{more}"
            )

    for folder in content_roots:
        for xml_path in (version_dir / folder).rglob("*.xml"):
            try:
                ET.parse(xml_path)
            except ET.ParseError as exc:
                errors.append(f"{xml_path}: XML parse error: {exc}")

if not checked_versions:
    errors.append("No version folders checked from LoadFolders.xml")

if errors:
    print("Versioned XML layout check failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    sys.exit(1)

print(
    "Versioned XML layout OK: "
    + ", ".join(checked_versions)
    + f" shadow {len(root_xml)} root XML files"
)
PY
