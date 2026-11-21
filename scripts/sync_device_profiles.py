#!/usr/bin/env python3
"""Build SoftBox's reproducible community Android device-profile snapshot.

Sources:
- bsthen/device-models (Apache-2.0): public Google Play supported-device model-code mapping.
- LineageOS/lineage_wiki _data/devices (MIT): community-maintained codenames and published hardware specs.

The generated records are discovery evidence only. They are never OEM-certified and never create PCB maps.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import sys
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import PurePosixPath

GOOGLE_URL = "https://raw.githubusercontent.com/bsthen/device-models/main/devices.json"
LINEAGE_ARCHIVE_URL = "https://codeload.github.com/LineageOS/lineage_wiki/zip/refs/heads/main"
GOOGLE_REPO_URL = "https://github.com/bsthen/device-models"
LINEAGE_REPO_URL = "https://github.com/LineageOS/lineage_wiki"
MAX_GOOGLE_BYTES = 20 * 1024 * 1024
MAX_LINEAGE_ARCHIVE_BYTES = 100 * 1024 * 1024
MAX_YAML_ENTRY_BYTES = 512 * 1024


def download(url: str, limit: int) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "SoftBox-PhoneLab-DeviceCatalog/1.0"})
    with urllib.request.urlopen(request, timeout=90) as response:
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError(f"Source exceeds the {limit}-byte safety limit: {url}")
    if not data:
        raise ValueError(f"Source returned an empty response: {url}")
    return data


def text(value: object) -> str:
    if value is None:
        return ""
    if isinstance(value, (dict, list)):
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


def parse_lineage_profiles(archive_bytes: bytes, google: dict[str, dict[str, str]], retrieved: str):
    try:
        import yaml  # PyYAML is deliberately confined to this maintainer-side sync tool.
    except ImportError as exc:
        raise RuntimeError("PyYAML is required. Install with: python -m pip install PyYAML==6.0.3") from exc

    profiles: list[dict] = []
    archive_digest = hashlib.sha256(archive_bytes).hexdigest()
    with zipfile.ZipFile(io.BytesIO(archive_bytes)) as archive:
        entries = [
            entry for entry in archive.infolist()
            if re.search(r"/_data/devices/[^/]+\.ya?ml$", entry.filename, re.IGNORECASE)
        ]
        if not entries or len(entries) > 2_000:
            raise ValueError(f"Unexpected LineageOS device metadata entry count: {len(entries)}")

        for entry in sorted(entries, key=lambda item: item.filename.casefold()):
            if entry.file_size <= 0 or entry.file_size > MAX_YAML_ENTRY_BYTES:
                continue
            raw = archive.read(entry)
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
            google_brands = [match["brand"] for match in google_matches if match.get("brand")]
            marketing_name = google_names[0] if google_names else f"{vendor} {name}".strip()
            model_aliases = list(dict.fromkeys([name, *google_names, *models]))[:64]

            display = parsed.get("display")
            battery = parsed.get("battery")
            release = text(parsed.get("release"))
            file_name = PurePosixPath(entry.filename).name
            lineage_url = f"{LINEAGE_REPO_URL}/blob/main/_data/devices/{file_name}"
            identity_source = "LineageOS Wiki device profile"
            license_text = "MIT (LineageOS Wiki)"
            if google_matches:
                identity_source += " + Google Play supported-device identifiers"
                license_text += " + Apache-2.0 (bsthen/device-models)"

            profiles.append({
                "id": "lineageos-" + safe_id(PurePosixPath(file_name).stem),
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
                "identitySourceUrl": lineage_url,
                "identitySourceLicense": license_text,
                "identitySourceRevision": archive_digest,
                "identityVerified": False,
                "deviceType": text(parsed.get("type")),
                "releaseDate": release,
                "cpu": text(parsed.get("cpu")),
                "cpuCores": number(parsed.get("cpu_cores"), 1, 64),
                "cpuFrequency": text(parsed.get("cpu_freq")),
                "gpu": text(parsed.get("gpu")),
                "ram": text(parsed.get("ram")),
                "storage": text(parsed.get("storage")),
                "display": " ".join(value for value in [
                    nested_text(display, "size"),
                    nested_text(display, "technology")
                ] if value),
                "screenResolution": nested_text(display, "resolution"),
                "screenTechnology": nested_text(display, "technology"),
                "wifi": text(parsed.get("wifi")),
                "bluetooth": nested_text(parsed.get("bluetooth"), "spec"),
                "peripherals": as_list(parsed.get("peripherals"), 128),
                "supportedOsVersions": as_list(parsed.get("versions"), 64),
                "currentOsBranch": text(parsed.get("current_branch")),
                "sources": [
                    {
                        "id": "lineageos-wiki",
                        "name": "LineageOS Wiki device metadata",
                        "url": f"{LINEAGE_REPO_URL}/tree/main/_data/devices",
                        "license": "MIT",
                        "sha256": archive_digest,
                        "recordCount": len(entries),
                        "retrievedAtUtc": retrieved,
                    }
                ],
            })
    return profiles, archive_digest, len(entries)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="data/device-profiles/community-catalog.json")
    args = parser.parse_args()

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
        code_text = text(code)
        brand = text(record.get("brand"))
        name = text(record.get("name"))
        if code_text and brand and name and len(code_text) <= 120 and len(brand) <= 160 and len(name) <= 240:
            google[normal(code_text)] = {"brand": brand, "name": name, "code": code_text}

    lineage_bytes = download(LINEAGE_ARCHIVE_URL, MAX_LINEAGE_ARCHIVE_BYTES)
    lineage_profiles, lineage_digest, lineage_entry_count = parse_lineage_profiles(lineage_bytes, google, retrieved)

    # Avoid duplicate Google-only records for model codes already represented by a richer LineageOS profile.
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
                "name": "Google Play supported device model-code mapping",
                "url": GOOGLE_REPO_URL,
                "license": "Apache-2.0",
                "sha256": google_digest,
                "recordCount": len(google),
                "retrievedAtUtc": retrieved,
            }],
        })

    profiles = sorted(lineage_profiles + google_profiles,
                      key=lambda item: (item["manufacturer"].casefold(), item["marketingName"].casefold(), item["id"]))
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
                "sha256": google_digest,
                "recordCount": len(google),
                "retrievedAtUtc": retrieved,
            },
            {
                "id": "lineageos-wiki",
                "name": "LineageOS Wiki device metadata",
                "url": f"{LINEAGE_REPO_URL}/tree/main/_data/devices",
                "license": "MIT",
                "sha256": lineage_digest,
                "recordCount": lineage_entry_count,
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

    output = args.output
    with open(output, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(document, handle, ensure_ascii=False, indent=2, sort_keys=False)
        handle.write("\n")
    print(f"Wrote {len(profiles)} profiles from {len(google)} Google identifiers and {lineage_entry_count} LineageOS YAML entries.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"Device catalog sync failed: {error}", file=sys.stderr)
        raise
