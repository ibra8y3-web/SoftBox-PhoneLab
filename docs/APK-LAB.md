# APK Lab — Local APK Inspection

## Scope

APK Lab is a read-only desktop tool for inspecting a user-selected .apk file. It runs locally in SoftBox and does not install, modify, execute, or upload the selected package.

## Implemented behavior

- Calculates the SHA-256 digest of the complete APK file.
- Opens the package as a ZIP archive and reads every non-directory entry to detect decompression/read errors and truncated content. This is not a ZIP CRC, publisher-signature, or provenance verification.
- Applies a 2 GiB maximum expanded-archive limit and an 8 MiB Android manifest limit.
- Lists up to 500 archive entry names and reports the total entry count.
- Parses AndroidManifest.xml when it is text XML or Android Binary XML (AXML).
- Extracts package name, version code/name, min/target SDK, literal application label, and declared uses-permission entries.
- Detects common v1 signature metadata files (.SF, .RSA, .DSA, .EC) and structurally detects an APK Signing Block when present.
- Compares two local APK files by package name, version name/code, full-file SHA-256, declared permissions and archive entry names.

## Security and interpretation limits

- Signature detection is not signature verification. APK Lab does not validate the cryptographic signature, signer certificate, certificate chain, publisher identity, signing-key rotation, or trust policy.
- An application label stored as a resource reference is shown as a resource identifier; the Android resource table is not decoded.
- Permissions are declarations from the manifest, not proof that an application actually uses a permission or that the package is malicious.
- SHA-256 identifies the inspected bytes. It does not establish provenance or compare the package to a trusted publisher hash.
- ZIP entry readability and manifest parsing are not CRC verification, malware analysis, or a complete security review.
- The comparison is by archive entry path, not by every entry's decompressed content. It reports when the visible 500-entry lists are incomplete.
- The tool does not decompile DEX, decode arbitrary resources, install APKs, or perform device-side package operations.
- No phone, USB transport, ADB authorization, or external service is needed for this local inspection.

## Test coverage

ApkInspectionServiceTests covers:

- Text XML manifest parsing and metadata extraction.
- Android Binary XML manifest parsing, including SDK levels and permissions.
- Exact SHA-256 calculation over the APK bytes.
- Detection of v1 signature metadata without claiming cryptographic validation.
- Valid ZIP inspection when the manifest is missing.
- Rejection of corrupt ZIP data and non-APK file extensions.

The generated test fixtures validate parser behavior; they are not claimed to be real handset or vendor firmware compatibility tests.
## CI evidence — 20 November 2026

PhoneLab CI run #48 first validated APK Lab with 40 tests passing. The later complete run #57, which includes APK Lab and Network Diagnostics source, passed 51 tests (0 failed, 0 skipped); Windows x64 publish, Android Agent build, offline tool bundling and Windows artifact upload all succeeded. Run #57: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44753337114

The added APK tests run only against generated local fixtures. No physical-device compatibility claim is made.


## APK version comparison

Select a first and a second APK to compare package identity, version metadata, SHA-256 equality, newly declared/removed permissions, and added/removed archive entry names. The tool warns when package names differ or required metadata is missing. It does not claim that files with the same package name came from the same publisher, and it does not compare the contents of every ZIP entry.
