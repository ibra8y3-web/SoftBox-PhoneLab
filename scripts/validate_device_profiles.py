#!/usr/bin/env python3
"""Validate a generated SoftBox community device catalog without network access."""
import json
import re
import sys
from pathlib import Path
from urllib.parse import urlparse

path = Path(sys.argv[1] if len(sys.argv) > 1 else "data/device-profiles/community-catalog.json")
document = json.loads(path.read_text(encoding="utf-8"))
errors = []
if document.get("schemaVersion") != 1:
    errors.append("schemaVersion must be 1")
profiles = document.get("profiles")
sources = document.get("sources")
if not isinstance(profiles, list) or not 1 <= len(profiles) <= 100_000:
    errors.append("profiles must be a non-empty array with at most 100,000 records")
if not isinstance(sources, list) or not 1 <= len(sources) <= 20:
    errors.append("sources must be a non-empty array")
id_pattern = re.compile(r"^[a-z0-9][a-z0-9._-]{0,159}$")
sha_pattern = re.compile(r"^[a-f0-9]{64}$")
def https(value):
    parsed = urlparse(value or "")
    return parsed.scheme == "https" and bool(parsed.netloc)

source_ids = set()
for source in sources if isinstance(sources, list) else []:
    if not isinstance(source, dict):
        errors.append("source entry must be an object")
        continue
    source_id = source.get("id", "")
    if not id_pattern.fullmatch(source_id) or source_id in source_ids:
        errors.append(f"invalid/duplicate source id: {source_id}")
    source_ids.add(source_id)
    if (not https(source.get("url")) or not source.get("license") or
            not sha_pattern.fullmatch(source.get("sha256", "")) or not source.get("revision")):
        errors.append(f"source provenance is incomplete: {source_id}")
    if not isinstance(source.get("recordCount"), int) or source["recordCount"] < 1:
        errors.append(f"invalid source recordCount: {source_id}")

profile_ids = set()
model_owner = {}
for profile in profiles if isinstance(profiles, list) else []:
    if not isinstance(profile, dict):
        errors.append("profile entry must be an object")
        continue
    pid = profile.get("id", "")
    if not id_pattern.fullmatch(pid) or pid in profile_ids:
        errors.append(f"invalid/duplicate profile id: {pid}")
    profile_ids.add(pid)
    for field in ("manufacturer", "marketingName", "identitySource", "identitySourceLicense"):
        if not isinstance(profile.get(field), str) or not profile[field].strip():
            errors.append(f"{pid}: missing {field}")
    if not https(profile.get("identitySourceUrl")):
        errors.append(f"{pid}: identitySourceUrl must use HTTPS")
    if profile.get("identityVerified") is not False:
        errors.append(f"{pid}: community profile must not claim identityVerified=true")
    if profile.get("boardMapId"):
        errors.append(f"{pid}: community identity profile must not assign BoardMapId")
    if not profile.get("modelNumbers") and not profile.get("deviceCodes"):
        errors.append(f"{pid}: missing model number and device codename")
    for model in profile.get("modelNumbers", []):
        key = str(model).casefold()
        owner = model_owner.setdefault(key, pid)
        if owner != pid:
            print(f"WARNING duplicate model identifier {model}: {owner}, {pid}")
    for key in ("modelNumbers", "deviceCodes", "productNames", "modelAliases", "socAliases",
                "boardIds", "peripherals", "supportedOsVersions", "sources"):
        if not isinstance(profile.get(key, []), list):
            errors.append(f"{pid}: {key} must be an array")
if errors:
    print("Device profile catalog validation FAILED:")
    for error in errors[:100]:
        print(" -", error)
    raise SystemExit(1)
print(f"Device profile catalog validation passed: {len(profiles)} profiles, {len(sources)} sources.")
