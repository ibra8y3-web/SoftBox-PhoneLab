# SoftBox owner support approvals

SoftBox PhoneLab supports offline, manually issued owner approvals for two separate operations:

- `PasswordRecovery` — recover one named local SoftBox account.
- `ResetLocalAccountDatabase` — delete the local account database only. It is not the same operation as password recovery.

## Owner applications

The owner uses the private repository `ibra8y3-web/SoftBox-Support-Key-Manager`, which builds two separate applications:

- Windows x64 WPF manager. Its existing ECDSA P-256 key remains protected by Windows DPAPI CurrentUser.
- Android Support Key Manager APK. It creates a separate ECDSA P-256 key in Android Keystore and accepts a request through Android Share intents or a JSON file picker.

The two owner keys are independent. Do not replace the pinned Windows public key with the Android public key.

## Manual WhatsApp process

1. SoftBox creates a support-request JSON bound to its persistent installation ID, request ID, random nonce, exact requested action and (for password recovery) exact username. The request also includes an informational workstation label; this label is not an authorization factor.
2. The user manually sends the request through WhatsApp.
3. The owner shares the received text/file into SoftBox Support Key Manager, checks the requested operation and installation details, and chooses a maximum 1/6/24-hour approval lifetime.
4. The owner manager signs the exact protocol-v1 payload locally.
5. The owner manually shares the approval JSON back through WhatsApp. The owner chooses the customer's conversation and presses Send.

WhatsApp is not accessed through a bot or a remote API. The support manager does not read WhatsApp conversations and does not automatically send messages. There is no central support database or signing server.

## Protocol v1 verification

The signed canonical payload contains, in order:

1. Version
2. Grant ID
3. Request ID
4. Installation ID
5. Nonce
6. Exact action
7. Subject username (empty for account-database reset)
8. UTC issue timestamp
9. UTC expiry timestamp

The signature is ECDSA P-256 with SHA-256 and the IEEE-P1363 fixed-width 64-byte `r || s` encoding. The public client verifies the approval against the trusted owner public-key set and checks pending request, installation ID, nonce, action, username, expiry and the one-time grant/request replay ledger.

The replay ledger and installation identity are stored outside `users.json`, so resetting local account data does not reset replay protection. Consumed approvals cannot be used again.

## Trusted owner key provisioning

The existing Windows key is already pinned. The Android key was generated inside Android Keystore on the owner's actual phone and its public key is now pinned alongside the Windows key:

1. Install and launch the Android Support Key Manager APK on the owner's phone.
2. The APK generates its P-256 key pair inside Android Keystore. The private key is non-exportable and is never sent to GitHub or WhatsApp.
3. Use the app's **Export public key** action to save `SoftBox-Android-Support-PublicKey.pem`; compare the SHA-256 fingerprint shown by the app.
4. The exported Android PEM is pinned in `AndroidPinnedPublicKeyPem`; the existing Windows `PinnedPublicKeyPem` remains unchanged.
5. Run the public repository's Windows tests and publish a new SoftBox customer build. Verify the exported fingerprint against `TrustedPublicKeyFingerprints` before using Android-issued grants.

Pinned Android public-key SHA-256: `E2CC4F47D2642E3AA91B16899D046581D9397362154EB9BFA032F9BEA8B2CFD1`.

Do not commit the Android private key. Do not fill the placeholder using a random generated CI key. Android approvals are accepted only by client builds that contain this exact phone-generated public key.

If the Android app is uninstalled or the phone is reset and its Keystore key is lost, a new key must be provisioned and pinned in a subsequent client release. A device label is for operator review only; identity binding is performed by the installation ID and nonce.
