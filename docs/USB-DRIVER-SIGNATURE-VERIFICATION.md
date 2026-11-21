# Windows USB driver signature verification

USB Doctor now opens a Windows Authenticode verifier backed by WinVerifyTrust. It verifies the selected .cat catalog signature or an embedded signature in a .sys, .dll or .exe file and reports the Windows trust status, signer subject, thumbprint and certificate validity dates.

A trusted .cat result confirms the publisher signature on that catalog file. This verifier does not independently prove that a separately selected SYS file is a member of that catalog, install a driver, modify Driver Store, or guarantee that the driver matches a particular handset. Some Windows driver SYS files are catalog-signed rather than embedded-signed; for those, choose the OEM CAT from the original driver package. The official Samsung installer retains its separate Authenticode check in the packaging workflow.
