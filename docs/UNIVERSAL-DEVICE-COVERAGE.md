# Universal Device Coverage and Execution Boundaries

## Product scope

SoftBox is a Windows phone-service workstation with adapters for multiple manufacturers and independent transport sources. It is not an Android-only device list. Windows USB PnP discovery works independently of ADB, so a phone or service interface can remain visible when Android ADB disconnects or when the handset enters a boot/service mode.

The current USB vendor-ID hint table includes Apple, Google, HTC, Samsung, Motorola, LG, Huawei, Acer, Sony, Dell, NVIDIA, ZTE, Kyocera, Pantech, Qualcomm, Toshiba, ASUS, Lenovo, Fujitsu, OPPO, Xiaomi, MediaTek and other registered manufacturers. This is not a claim that every USB VID or every phone model is covered.

**VID/PID is interface evidence, not handset identity.** A manufacturer hint does not establish the exact commercial model, chipset, mainboard revision, boot state, OEM authorization, driver compatibility or firmware compatibility. Windows PnP, ADB, Fastboot, Apple Lockdown and proprietary service endpoints remain independent evidence channels.

## Cross-vendor and OS matrix

| Manufacturer / family | What SoftBox can observe | What remains model-specific |
|---|---|---|
| Samsung | Windows PnP, ADB when authorized, Fastboot where exposed, explicit Download Mode interface and selected COM evidence | Exact model, CSC, binary/anti-rollback, Odin package and COM-to-phone ownership |
| Huawei / HONOR | Registered vendor ID and explicitly described USB recovery/download/service interfaces | HiSuite/service protocol, loader authorization, exact model/region and firmware package |
| Xiaomi / Redmi / POCO | Vendor hint, MTP/ADB when exposed, Fastboot when actually present | Bootloader unlock authorization, Mi authorization, model/region/package compatibility |
| OPPO / realme / OnePlus | OPPO vendor ID and explicit USB service descriptors where present | Proprietary Download/EDL modes, signed service tools, DA/firehose authorization and model-specific firmware |
| vivo / TECNO / Infinix and other Android OEMs | Generic USB/ADB/Fastboot evidence when Windows exposes it; known vendor hints only where registered | Brand-specific service protocol, drivers, loaders, signed firmware and account/OEM authorization |
| Qualcomm-based devices | Known USB VID/PID service clues such as 9008; QDL diagnostics/preflight | Exact OEM Firehose programmer, signature/auth, target storage geometry, board and authorized package |
| MediaTek-based devices | Preloader/BROM/DA descriptor clues, Scatter/image/DA/Auth inspection | Matching DA for the SoC/model, OEM security/auth, exact partitions and package |
| Apple iPhone / iPad | Apple USB, Lockdown when unlocked and trusted, Apple Recovery/DFU descriptor hints, separate AirPlay and supported UVC capture | Trust prompt, iOS tooling, model capabilities, AirPlay initiation, supported wired video output and capture adapter |
| Other USB devices | Present Windows PnP records and exact reported Hardware IDs | No phone or service capability is inferred from generic USB presence |

## Transport and mode rules

- **Normal Android:** Windows PnP and ADB are separate. ADB authorization is not inferred from USB presence. Screen locking, USB debugging policy and Agent permissions remain distinct.
- **Fastboot:** only the Fastboot transport may establish bootloader variables. USB visibility is not an unlock or flash authorization.
- **Samsung Download Mode:** Windows PnP remains visible after ADB vanishes. The selected PnP instance is carried into Odin; when ADB is absent, the technician must supply/confirm the exact model and COM. The COM reported inside Odin must exactly match the target COM before Start. The PnP instance suffix is never used as a serial.
- **Qualcomm EDL:** VID/PID or a QDLoader name identifies a USB interface clue only. A real restore needs an exact compatible OEM-authorized Firehose programmer and any required authentication. A generic runner cannot restore every Qualcomm phone.
- **MediaTek Preloader/BROM:** Scatter parsing and image inspection do not implement the complete BROM flash protocol. A real operation needs a compatible Download Agent, partition map and any required OEM Auth/SLA authorization.
- **Huawei/OPPO/other proprietary modes:** an explicit descriptor can be listed as an unclassified service interface. Until a verified protocol and legal, model-specific toolchain exist, the interface is read-only evidence and no Start button is offered.
- **Apple Recovery/DFU:** Windows PnP may identify the Apple USB/DFU interface. It is not equivalent to an unlocked, trusted Lockdown session and does not imply an iOS restore tool is installed or authorized.
- **AirPlay:** the Windows receiver can be separate from Android scrcpy, but the owner must initiate Screen Mirroring from the iPhone/iPad. AirPlay is view-only and does not provide touch, passcode or lock control.
- **HDMI/UVC:** works only for iPhone/iPad models supporting wired video output with the correct adapter and a compatible capture card. It is not a universal iPhone capability.

## Windows Driver Center

The OEM resource catalogue is a set of official support/download resources, not a bundle of every OEM driver installer. The local package manager adds a conservative path for selected Windows driver packages:

1. The technician selects an INF and its CAT from the same package folder.
2. SoftBox compares the INF's model rows against the selected device's actual Hardware IDs and the running Windows architecture/build.
3. SoftBox verifies the CAT through Windows trust and checks that the selected INF is a member of that catalog.
4. Only after all checks pass can the technician request elevated PnPUtil staging/installation.
5. Installed driver packages can be enumerated and exported for backup. Removal requires an exact published oemNN.inf name, an explicit confirmation and Windows UAC; SoftBox never supplies /force.

A successful pnputil command is not proof that a device interface is healthy or that Windows selected this package for every interface. Re-scan Device Manager and validate the target phone after installation. If a signer/catalog tool is unavailable, the package is blocked rather than trusted by filename.

## Plugin SDK

The public SoftBox.PluginSdk project targets .NET 10 without WPF dependencies. It exposes the Manifest v1 model, permission identifiers, validation and JSON serialization. The existing Plugin Store still handles publisher trust and the isolated runtime. SDK serialization does not sign or execute plugin code.

## Board maps and component evidence

The current bundled data includes one generic Android board-map entry and one detailed Galaxy A31 component record. The curated device-profile catalogue has 12 internal profiles; the separate community catalogue is not a verified board-view database. The current module catalogue has 198 child features across 38 sections and 93 unique action identifiers; this count is a code/catalogue count, not hardware certification.

No PCB coordinates, component references, IC part numbers, thermal images or rail values are invented in this change. A new model-specific map must include the exact commercial model and board revision, dimensions/coordinate unit, component identifiers and coordinates, source provenance/licence and review evidence. See the board-map data guide and JSON Schema for the required structure.

## Verification boundary

Automated tests cover parser behavior, authorization, COM equality, USB vendor hints, generic Huawei service descriptor handling, Apple DFU classification, driver argument validation and Plugin SDK manifest validation. CI can prove compilation/tests in the runner only. No physical testing on a Samsung, Huawei, OPPO, Xiaomi, Qualcomm, MediaTek or Apple device is claimed by this code change.
