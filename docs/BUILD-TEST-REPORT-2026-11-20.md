# Build & Test Report — 20 November 2026

## Scope

This report records the Windows and Android CI verification of the APK Lab implementation on the `feature/apk-lab-real-apk-inspector` branch. It does not claim physical-handset verification.

## Verified workflow run

- Workflow: PhoneLab CI, run #48.
- Result: Success.
- Commit tested: `a370def76ea3dd091c679c3b219cc4b18e888361`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44752020306
- Pull request: https://github.com/ibra8y3-web/SoftBox-PhoneLab/pull/1

## Results

| Check | Result | Evidence |
|---|---|---|
| .NET dependency restore | Passed | Windows job Restore step completed successfully |
| .NET unit tests | Passed | 40 passed, 0 failed, 0 skipped |
| Windows x64 publish | Passed | `dotnet publish` completed for self-contained `win-x64` |
| Android Agent build | Passed | Gradle `:app:assembleDebug` and artifact upload completed |
| Offline tool bundling | Passed | Platform-Tools, scrcpy, TRACE Boardviewer and Samsung driver validation completed |
| Windows package artifact | Passed | Artifact `SoftBox-PhoneLab-Windows-x64` uploaded successfully |

## Corrections made from CI evidence

The first pull-request run exposed two compile errors in the new parser: an AXML chunk offset type mismatch (`CS0266`) and an XML attribute dictionary nullability warning (`CS8619`). Both were corrected in the source before run #48. Run #48 then completed all tests, publish and packaging steps successfully.

## Test scope and limitations

- The five added APK tests use generated local ZIP/APK fixtures to cover plain XML, binary Android XML, metadata extraction, SHA-256, v1 signature-file detection, missing Manifest, invalid ZIP and extension validation.
- These unit tests do not prove compatibility with every APK producer, device, firmware package or Android release.
- Signature-file detection and APK Signing Block detection are structural observations only. Cryptographic signature and certificate-chain verification is not implemented.
- No APK was installed or executed; no physical phone or USB hardware was used for this test run.
- Successful publish is not a release certification or a production code-signing claim.

## Windows artifact

The Windows x64 package from this run was uploaded as a GitHub Actions artifact (227,151,756 bytes):

https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44752020306/artifacts/14636351322

GitHub Actions artifacts are temporary workflow outputs; this is not a permanent GitHub Release.

## Follow-up verification — Network Diagnostics (run #57)

- Workflow: PhoneLab CI, run #57; result: Success.
- Tested source commit: `0b6f7bc10490327cbd3a3ee1804f872841dea501`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44753337114
- Restore: passed.
- Unit tests: 51 passed, 0 failed, 0 skipped.
- Windows x64 self-contained publish: passed.
- Android Agent build and artifact upload: passed.
- Offline USB, scrcpy and TRACE Boardviewer bundle validation: passed.
- Windows x64 artifact: 227,161,431 bytes; artifact ID `14637025549`.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44753337114/artifacts/14637025549

The APK Lab code was already validated by run #48 (40 tests). Run #57 includes both APK Lab and Network Diagnostics code, including single-label local DNS host validation. Network Diagnostics tests validate input handling; they do not make an external DNS or live ICMP request in the test suite.

No physical Android handset or USB device was attached during these CI runs.
## Follow-up verification — APK version comparison (run #65)

- Workflow: PhoneLab CI, run #65; result: Success.
- Tested PR merge commit: run #65 completed against the updated pull-request source and catalog.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44754536968
- Restore: passed.
- Unit tests: 53 passed, 0 failed, 0 skipped.
- Windows self-contained x64 publish: passed.
- Android Agent build and artifact upload: passed.
- Offline USB/scrcpy/TRACE Boardviewer tool bundling: passed.
- Windows x64 artifact: 227,167,411 bytes; artifact ID `14636852748`.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44754536968/artifacts/14636852748

The two added APK comparison tests cover version metadata, same/different package handling, permission changes, archive entry-name changes and the stated path-only comparison boundary. The code does not claim a per-entry content hash comparison or cryptographic signer verification.

## Follow-up verification — canonical tool routes, vendor USB filters and warning cleanup (run #257)

- Workflow: PhoneLab CI, run #257; result: **Success**.
- Tested source commit: `55ae843048a988b5883af0cb8953a4eab741f322`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44779995644
- Restore: passed.
- Unit tests: **150 passed, 0 failed, 0 skipped**.
- Windows self-contained x64 publish: passed.
- Android Agent Gradle build and artifact upload: passed.
- Offline USB, scrcpy and TRACE Boardviewer bundling: passed.
- Windows x64 artifact: **227,398,184 bytes**, artifact ID `14652201449`.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44779995644/artifacts/14652201449
- Source-code compiler and xUnit analyzer warnings (CS*/xUnit*) were not present in this run's build log.

The catalog test now enforces one unique executable ActionId per canonical tool entry, with every action registered in the supported-action set. Android file browsing, app inspection, read-only commands and batch inspection open their specific tabs in the same authorized-device workspace. Qualcomm and MediaTek open Windows PnP inventory views filtered to the common USB identity hints 05C6:9008 and 0E8D:0003 respectively. These USB identities do not implement or verify EDL/BROM service protocols.

CI uses generated unit-test fixtures and hosted build runners. It does not constitute physical handset, USB chipset, firmware, EDL/BROM or board-level verification.


## Final documentation-sync verification — CI run #258

- Workflow: PhoneLab CI, run #258; result: **Success** on the documentation-updated branch head.
- Tested commit: `526c776e6bb28e7a4f7e7e141e02bac3f0fc879b`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44780672062
- Restore: passed.
- Unit tests: **150 passed, 0 failed, 0 skipped**.
- Windows self-contained x64 publish: passed.
- Android Agent Gradle build/upload: passed.
- Offline tool bundling: passed.
- Windows x64 artifact: **227,398,186 bytes**, artifact ID `14652362018`.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44780672062/artifacts/14652362018
- No CS* compiler or xUnit analyzer warnings were present in the build log.

This CI run includes the catalog/reorganization/build report updates. The tested application source remains the same as run #257; the additional commit updates documentation only. Neither run exercised physical handsets or vendor low-level servicing protocols.


## Follow-up verification — local accounts, RBAC and session security (CI run #264)

- Workflow: PhoneLab CI, run #264; result: **Success**.
- Tested source commit: `2b13abedff8689d614d6a07721888d3e10206839`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44784653573
- Restore: passed.
- Unit tests: **165 passed, 0 failed, 0 skipped**.
- Windows self-contained x64 publish: passed.
- Android Agent Gradle build/upload: passed.
- Offline USB, scrcpy and TRACE Boardviewer bundling: passed.
- Windows x64 artifact: **227,450,369 bytes**, artifact ID `14653526981`.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44784653573/artifacts/14653526981
- No CS* compiler or xUnit analyzer warnings were present in the build log.

The added account tests cover first-admin bootstrap, PBKDF2 verifier storage (not the password), login, admin/technician/read-only account creation, non-admin denial, repeated-failure lockout, last-admin protection, password reset/change, disabled-account rejection, local audit redaction and corrupted-store fail-closed behavior. Authorization tests validate all 53 catalog ActionIds and role restrictions.

This run is hosted Windows CI with temporary account-store fixtures. It does not validate Windows ACL/DPAPI deployment policy, domain accounts, multi-workstation synchronization or physical Android hardware.

## Final follow-up verification — read-only permission hardening and per-tool audit (CI run #273)

- Workflow: PhoneLab CI, run #273; result: **Success** on the latest PR #2 source head `4cb3578711e45d87e525a86162b99100c7f93644`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44786097393
- Restore: passed.
- Unit tests: **166 passed, 0 failed, 0 skipped**.
- Windows self-contained x64 publish: passed.
- Android Agent build/upload: passed.
- Offline USB, scrcpy and TRACE Boardviewer bundling: passed.
- Windows artifact: **227,451,055 bytes**, artifact ID `14653329474`.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44786097393/artifacts/14653329474
- No CS* compiler or xUnit analyzer warnings were present in the build log.

The final follow-up adds user-attributed module-action invocation to the bounded audit log and blocks tool launch if that audit write fails. Read-only roles can run allow-listed read-only command/workflow/batch inspection but cannot invoke interactive screen control; automatic screen mirroring and Agent installation are suppressed for read-only sessions. Tests cover these role boundaries. This is still a local application role system, not Windows account isolation, ACL/DPAPI enforcement or domain identity.


## Follow-up verification — Offline Readiness Center (CI run #279)

- Workflow: PhoneLab CI, run #279; result: **Success** on source commit `dd8b725a97b3a2f5da439391aea4cc34d7c12abc`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44788760474
- Restore: passed.
- Unit tests: **171 passed, 0 failed, 0 skipped**.
- Windows self-contained x64 publish: passed.
- Android Agent Gradle build/upload: passed.
- Offline USB, scrcpy and TRACE Boardviewer bundling: passed.
- Windows x64 artifact: **227,459,925 bytes**, artifact ID `14654323613`.
- Artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44788760474/artifacts/14654323613
- No CS* compiler or xUnit analyzer warnings were present in the build log.

The five Offline Readiness tests use isolated local fixture files. They validate actual filesystem inventory and hashing; they do not test Internet reachability, install tools, validate a publisher's signature or prove that a device driver works on physical hardware.


## Additive development verification — Wireless ADB and Android Network Diagnostics (CI run #297)

- Workflow: PhoneLab CI, run #297; result: **Success** on source/documentation commit `c373d008b2e78e39e12edfbe386f3d6d6a8e2305`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44797805010
- Restore: passed.
- Unit tests: **198 passed, 0 failed, 0 skipped**.
- Windows self-contained x64 publish: passed.
- Android Agent Gradle build/upload: passed.
- Offline Android Platform-Tools, scrcpy and TRACE Boardviewer bundling: passed.
- Windows x64 package: **227,456,372 bytes**, artifact ID `14660375282`.
- Windows package artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44797805010/artifacts/14660375282
- Android Agent APK: **2,343,509 bytes**, artifact ID `14658529701`.
- Android Agent artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44797805010/artifacts/14658529701
- No C# compiler (CS*) or xUnit analyzer warnings were observed in the Windows job log. GitHub Actions did emit a Node.js 20 deprecation notice for its hosted actions; this is an Actions-runtime notice, not an application compiler/test warning.

The added tests cover ADB endpoint formatting and validation, pairing-code validation, real command ordering through the production command-runner interface, pair/connect error handling, cancellation, role restrictions and Android interface/route reads. The fake command runner exists only inside the test project. No physical Android handset was connected; wireless pairing and Android network reads remain unverified on real hardware.


## Follow-up verification — Confirmed third-party Android app removal (CI run #300)

- Workflow: PhoneLab CI, run #300; result: **Success** on source commit `0338c2562796f7fc7f5a1c9b9cbf7f750f4b4191`.
- Run: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44799798040
- Restore: passed.
- Unit tests: **205 passed, 0 failed, 0 skipped**.
- Windows self-contained x64 publish: passed.
- Android Agent Gradle build/upload: passed.
- Offline Android Platform-Tools, scrcpy and TRACE Boardviewer bundling: passed.
- Windows x64 package: **227,455,566 bytes**, artifact ID `14661101336`.
- Windows package artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44799798040/artifacts/14661101336
- Android Agent APK: **2,343,509 bytes**, artifact ID `14660926631`.
- Android Agent artifact: https://github.com/ibra8y3-web/SoftBox-PhoneLab/actions/runs/44799798040/artifacts/14660926631
- No C# compiler (CS*) or xUnit analyzer warnings were observed in the Windows job log. GitHub Actions emitted its Node.js 20 deprecation notice for hosted Actions; this is separate from application build/test output.

The new app-removal tests validate package-list re-checking, exact serial targeting, refusal of a package not listed as third-party, invalid package/serial rejection and strict ADB success-output handling. Tests use a fake command runner only inside the isolated test project. No physical handset was attached, so real-device removal behavior is not hardware Verified.
