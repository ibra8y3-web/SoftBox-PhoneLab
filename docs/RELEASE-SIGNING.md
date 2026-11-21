# SoftBox Windows Release Signing

The update verifier accepts a package containing `softbox-update.json` and a detached RSA-PSS/SHA-256 signature. The signing CLI uses the same canonical ZIP payload and signature format as the in-app verifier.

## Publisher key

Create a dedicated RSA private/public key pair on a trusted maintainer workstation. Keep the private key outside the repository and release archives.

```powershell
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out softbox-release-private.pem
openssl pkey -in softbox-release-private.pem -pubout -out softbox-release-public.pem
```

Store the complete private PEM contents in GitHub repository Settings → Secrets and variables → Actions as `SOFTBOX_RELEASE_PRIVATE_KEY_PEM`.

Do not commit the private key. Do not upload it as an artifact. Limit Actions write access to trusted maintainers.

## Public-key trust

Publish the public key and its SHA-256 fingerprint through a channel independent of the release ZIP. In the installed app, open Update Center → Import SoftBox signing key, compare the displayed fingerprint with the independent value, and only then confirm trust. A key received only from the same update package is not an independent trust source.

## Release workflow

Pushing a version tag matching `v*` runs Windows CI, downloads the actual self-contained publish output, creates the signed package with `SoftBox.ReleaseSigner`, and publishes the ZIP to the matching GitHub Release.

The signer inventories the actual published files and rejects reparse points, unsafe paths, excessive file counts and excessive sizes. It writes the update manifest with actual file sizes and SHA-256 hashes, then signs the package with RSA-PSS/SHA-256 using the GitHub Actions secret. It refuses to publish if the secret is missing or the release tag already exists.

The signer is located in `tools/SoftBox.ReleaseSigner`. The in-app updater independently validates the official asset URL, key ID, signature, manifest, minimum supported version and every file hash before staging.

A successful unit test with a generated test key is not proof that the production publisher key is configured correctly. The first production tag must be reviewed by a maintainer, and the generated public key must be imported into the app after independent fingerprint confirmation.
