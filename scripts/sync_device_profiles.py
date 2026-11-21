#!/usr/bin/env python3
"""Build SoftBox's reproducible community Android device-profile snapshot.

Sources:
- bsthen/device-models (Apache-2.0): Google Play supported-device model-code mapping.
- LineageOS/lineage_wiki _data/devices (MIT): device codenames and published specifications.

Only device YAML files are downloaded from a pinned upstream commit. The full wiki archive
contains unrelated images/assets and is deliberately never downloaded.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
from pathlib import PurePosixPath

GOOGLE_URL = "https://raw.githubusercontent.com/bsthen/device-models/main/devices.json"
GOOGLE_REPO_URL = "https://github.com/bsthen/device-models"
LINEAGE_REPO_URL = "https://github.com/LineageOS/lineage_wiki"
LINEAGE_API = "https://api.github.com/repos/LineageOS/lineage_wiki"
MAX_GOOGLE_BYTES = 20 * 1024 * 1024
MAX_LINEAGE_TREE_BYTES = 8 * 1024 * 1024
MAX_YAML_ENTRY_BYTES = 512 * 1024
MAX_LINEAGE_ENTRIES = 2_000


def download(url: str, limit: int, attempts: int = 3) -> bytes:
    request = urllib.request.Request(url, headers={
        "User-Agent": "SoftBox-PhoneLab-DeviceCatalog/1.0",
        "Accept": "application/vnd.github+json",
    })
    last_error = None
    for attempt in range(attempts):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read(limit + 1)
            if len(data) > limit:
                raise ValueError(f"Source exceeds the {limit}-byte safety limit: {url}")
            if not data:
                raise ValueError(f"Source returned an empty response: {url}")
            return data
        except (urllib.error.URLError, TimeoutError) as error:
            last_error = error
            if attempt + 1 == attempts:
                break
    raise RuntimeError(f"Failed to download pinned source after {attempts} attempts: {url}: {last_error}")


def text(value: object) -> str:
    if value is None or isinstance(value, (dict, list)):
        return ""
    return str(value).strip()


def as_list(value: object, limit: int = 64) -> list[str]:
    raw = value if isinstance(value, list) else ([value] if value is not None else [])
    result = []
    for item in raw:
        candidate = text(item)
        if candidate and len(candidate) <= 160 and candidate not in result:
            result.append(candidate)
        if len(result) >= limit:
            break
    return result


def normal(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.casefold())


def safe_id(value: str) -> str:
    output = re.sub(r"[^a-z0-9._-]+", "-", value.casefold()).strip("-._")
    return output[:150] or "unknown-device"


def number(value: object, low: int, high: int) -> int | None:
    try:
        result = int(str(value).strip())
        return result if low <= result <= high else None
    except (TypeError, ValueError):
        return None


def nested_text(value: object, key: str) -> str:
    return text(value.get(key)) if isinstance(value, dict) else ""


def fetch_lineage_files(workers: int) -> tuple[str, list[tuple[str, bytes]], str]:
    commit = json.loads(download(f"{LINEAGE_API}/commits/main", 2 * 1024 * 1024))
    commit_sha = text(commit.get("sha"))
    tree_sha = text(commit.get("commit", {}).get("tree", {}).get("sha"))
    if not re.fullmatch(r"[0-9a-f]{40}", commit_sha) or not re.fullmatch(r"[0-9a-f]{40}", tree_sha):
        raise ValueError("LineageOS API did not return a valid pinned commit/tree SHA.")

    tree_payload = json.loads(download(f"{LINEAGE_API}/git/trees/{tree_sha}?recursive=1", MAX_LINEAGE_TREE_BYTES))
    if tree_payload.get("truncated"):
        raise ValueError("LineageOS tree response is truncated; refusing an incomplete device snapshot.")
    candidates = [
        entry for entry in tree_payload.get("tree", [])
        if entry.get("type") == "blob"
        and entry.get("path", "").startswith("_data/devices/")
        and PurePosixPath(entry.get("path", "")).suffix.lower() in {".yml", ".yaml"}
    ]
    candidates = [entry for entry in candidates if 0 < int(entry.get("size", 0)) <= MAX_YAML_ENTRY_BYTES]
    if not candidates or len(candidates) > MAX_LINEAGE_ENTRIES:
        raise ValueError(f"Unexpected LineageOS device YAML entry count: {len(candidates)}")

    def get_one(entry: dict) -> tuple[str, bytes]:
        path = entry["path"]
        escaped = urllib.parse.quote(path, safe="/")
        raw_url = f"https://raw.githubusercontent.com/LineageOS/lineage_wiki/{commit_sha}/{escaped}"
        content = download(raw_url, MAX_YAML_ENTRY_BYTES)
        if len(content) != int(entry["size"]):
            raise ValueError(f"LineageOS YAML size changed within pinned commit: {path}")
        return path, content

    files: list[tuple[str, bytes]] = []
    errors = []
    with ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {executor.submit(get_one, entry): entry["path"] for entry in candidates}
        for future in as_completed(futures):
            try:
                files.append(future.result())
            except Exception as error:
                errors.append(f"{futures[future]}: {error}")
    if errors:
        raise RuntimeError(f"Failed to fetch {len(errors)} of {len(candidates)} pinned LineageOS YAML files: " + " | ".join(errors[:5]))
    files.sort(key=lambda item: item[0].casefold())
    return commit_sha, files, tree_sha


def parse_lineage_profiles(files: list[tuple[str, bytes]], google: dict[str, dict[str, str]],
                           retrieved: str, commit_sha: str):
    try:
        import yaml
    except ImportError as exc:
        raise RuntimeError("PyYAML is required. Install with: python -m pip install PyYAML==6.0.3") from exc

    profiles: list[dict] = []
    digest = hashlib.sha256()
    for path, raw in files:
        digest.update(path.encode("utf-8"))
        digest.update(b"\0")
        digest.update(raw)
        parsed = yaml.safe_load(raw.decode("utf-8"))
        if not isinstance(parsed, dict):
            continue

        codename = text(parsed.get("codename"))
        vendor = text(parsed.get("vendor"))
        name = text(parsed.get("name"))
        if not codename or not vendor or not name or len(codename) > 100 or len(vendor) > 160:
            continue

        models = as_list(parsed.get("models"), 64)
        google_matches = [google.get(normal(model)) for model in models]
        google_matches = [match for match in google_matches if match]
        google_names = [match["name"] for match in google_matches if match.get("name")]
        marketing_name = google_names[0] if google_names else f"{vendor} {name}".strip()
        model_aliases = list(dict.fromkeys([name, *google_names, *models]))[:64]

        display = parsed.get("display")
        battery = parsed.get("battery")
        filename = PurePosixPath(path).name
        identity_source = "LineageOS Wiki device profile"
        license_text = "MIT (LineageOS Wiki)"
        if google_matches:
            identity_source += " + Google Play supported-device identifiers"
            license_text += " + Apache-2.0 (bsthen/device-models)"

        profiles.append({
            "id": "lineageos-" + safe_id(PurePosixPath(filename).stem),
            "manufacturer": vendor,
            "marketingName": marketing_name,
            "modelNumbers": models,
            "deviceCodes": [codename],
            "productNames": as_list(parsed.get("tree"), 16),
            "modelAliases": model_aliases,
            "boardIds": [],
            "socAliases": [text(parsed.get("soc"))] if text(parsed.get("soc")) else [],
            "hardwareFamily": text(parsed.get("soc")),
            "designBatteryCapacityMah": number(nested_text(battery, "capacity"), 100, 20_000),
            "boardMapId": "",
            "identitySource": identity_source,
            "identitySourceUrl": f"{LINEAGE_REPO_URL}/blob/{commit_sha}/{path}",
            "identitySourceLicense": license_text,
            "identitySourceRevision": commit_sha,
            "identityVerified": False,
            "deviceType": text(parsed.get("type")),
            "releaseDate": text(parsed.get("release")),
            "cpu": text(parsed.get("cpu")),
            "cpuCores": number(parsed.get("cpu_cores"), 1, 64),
            "cpuFrequency": text(parsed.get("cpu_freq")),
            "gpu": text(parsed.get("gpu")),
            "ram": text(parsed.get("ram")),
            "storage": text(parsed.get("storage")),
            "display": " ".join(value for value in [
                nested_text(display, "size"), nested_text(display, "technology")
            ] if value),
            "screenResolution": nested_text(display, "resolution"),
            "screenTechnology": nested_text(display, "technology"),
            "wifi": text(parsed.get("wifi")),
            "bluetooth": nested_text(parsed.get("bluetooth"), "spec"),
            "peripherals": as_list(parsed.get("peripherals"), 128),
            "supportedOsVersions": as_list(parsed.get("versions"), 64),
            "currentOsBranch": text(parsed.get("current_branch")),
            "sources": [{
                "id": "lineageos-wiki",
                "name": "LineageOS Wiki device metadata",
                "url": f"{LINEAGE_REPO_URL}/tree/{commit_sha}/_data/devices",
                "license": "MIT",
                "revision": commit_sha,
                "sha256": digest.hexdigest() if False else "",
                "recordCount": len(files),
                "retrievedAtUtc": retrieved,
            }],
        })
    return profiles, digest.hexdigest(), len(files)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="data/device-profiles/community-catalog.json")
    parser.add_argument("--workers", type=int, default=12)
    args = parser.parse_args()
    if args.workers < 1 or args.workers > 24:
        raise ValueError("--workers must be between 1 and 24.")

    retrieved = datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    google_bytes = download(GOOGLE_URL, MAX_GOOGLE_BYTES)
    google_digest = hashlib.sha256(google_bytes).hexdigest()
    google_json = json.loads(google_bytes)
    if not isinstance(google_json, dict) or not 1_000 <= len(google_json) <= 250_000:
        raise ValueError("Google Play supported-device mapping has an unexpected shape or record count.")

    google: dict[str, dict[str, str]] = {}
    for code, record in google_json.items():
        if not isinstance(record, dict):
            continue
        code_text, brand, marketing_name = text(code), text(record.get("brand")), text(record.get("name"))
        if code_text and brand and marketing_name and len(code_text) <= 120 and len(brand) <= 160 and len(marketing_name) <= 240:
            google[normal(code_text)] = {"brand": brand, "name": marketing_name, "code": code_text}

    lineage_commit, lineage_files, lineage_tree = fetch_lineage_files(args.workers)
    lineage_profiles, lineage_digest, lineage_count = parse_lineage_profiles(
        lineage_files, google, retrieved, lineage_commit)

    lineage_codes = {normal(code) for profile in lineage_profiles for code in profile["modelNumbers"]}
    google_profiles = []
    for code, record in google.items():
        if code in lineage_codes:
            continue
        item = record["code"]
        google_profiles.append({
            "id": "google-play-" + safe_id(item),
            "manufacturer": record["brand"],
            "marketingName": record["name"],
            "modelNumbers": [item],
            "deviceCodes": [],
            "productNames": [],
            "modelAliases": [record["name"]],
            "boardIds": [],
            "socAliases": [],
            "hardwareFamily": "",
            "designBatteryCapacityMah": None,
            "boardMapId": "",
            "identitySource": "Google Play supported-device identifiers via bsthen/device-models",
            "identitySourceUrl": GOOGLE_REPO_URL,
            "identitySourceLicense": "Apache-2.0",
            "identitySourceRevision": google_digest,
            "identityVerified": False,
            "deviceType": "",
            "releaseDate": "",
            "cpu": "",
            "cpuCores": None,
            "cpuFrequency": "",
            "gpu": "",
            "ram": "",
            "storage": "",
            "display": "",
            "screenResolution": "",
            "screenTechnology": "",
            "wifi": "",
            "bluetooth": "",
            "peripherals": [],
            "supportedOsVersions": [],
            "currentOsBranch": "",
            "sources": [{
                "id": "google-play-supported-devices",
                "name": "Google Play supported-device model-code mapping via bsthen/device-models",
                "url": GOOGLE_REPO_URL,
                "license": "Apache-2.0",
                "revision": "sha256:" + google_digest,
                "sha256": google_digest,
                "recordCount": len(google),
                "retrievedAtUtc": retrieved,
            }],
        })

    # Attach the completed source digest after all LineageOS files have been hashed.
    for profile in lineage_profiles:
        for source in profile["sources"]:
            source["sha256"] = lineage_digest
            source["revision"] = lineage_commit

    profiles = lineage_profiles + google_profiles
    # The document-level source manifest carries full hashes/licenses; individual rows keep
    # per-profile source URL/revision fields only. Omit empty optional values and pretty-print
    # nothing in this machine-generated 40k+ record snapshot to keep downloads and RAM bounded.
    for profile in profiles:
        profile.pop("sources", None)
    profiles = [
        {key: value for key, value in profile.items()
         if value is not None and value != "" and value != [] or key == "identityVerified"}
        for profile in profiles
    ]
    profiles.sort(key=lambda item: (item["manufacturer"].casefold(), item["marketingName"].casefold(), item["id"]))
    if not profiles:
        raise ValueError("The combined device catalog is empty.")

    document = {
        "schemaVersion": 1,
        "catalogVersion": datetime.now(timezone.utc).strftime("%Y.%m.%d") + "." + google_digest[:8] + "." + lineage_digest[:8],
        "source": "Google Play supported-device identifiers + LineageOS Wiki community device profiles; neither source is an OEM service specification.",
        "updatedAtUtc": retrieved,
        "generatedAtUtc": retrieved,
        "sources": [
            {
                "id": "google-play-supported-devices",
                "name": "Google Play supported device model-code mapping via bsthen/device-models",
                "url": GOOGLE_REPO_URL,
                "license": "Apache-2.0",
                "revision": "sha256:" + google_digest,
                "sha256": google_digest,
                "recordCount": len(google),
                "retrievedAtUtc": retrieved,
            },
            {
                "id": "lineageos-wiki",
                "name": "LineageOS Wiki device metadata",
                "url": f"{LINEAGE_REPO_URL}/tree/{lineage_commit}/_data/devices",
                "license": "MIT",
                "revision": lineage_commit,
                "sha256": lineage_digest,
                "recordCount": lineage_count,
                "retrievedAtUtc": retrieved,
            },
        ],
        "profiles": profiles,
        "notes": [
            "Community records are unverified identity hints; they must not grant flashing, unlocking or OEM service capabilities.",
            "Board IDs and BoardMapId are intentionally empty; no PCB coordinates or schematics are derived from identity data.",
            "Published specifications are not live diagnostic measurements and may vary by region or hardware revision.",
            "Duplicate model identifiers across community profiles must remain ambiguous until independently reviewed.",
        ],
    }

    with open(args.output, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(document, handle, ensure_ascii=False, separators=(",", ":"))
        handle.write("\n")
    print(f"Wrote {len(profiles)} device profiles from {len(google)} Google identifiers and {lineage_count} pinned LineageOS YAML files.")
    print(f"Upstream LineageOS revision: {lineage_commit}; metadata digest: {lineage_digest}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Device catalog sync failed: {error}", file=sys.stderr)
        raise
