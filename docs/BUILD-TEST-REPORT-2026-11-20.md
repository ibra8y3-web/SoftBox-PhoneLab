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