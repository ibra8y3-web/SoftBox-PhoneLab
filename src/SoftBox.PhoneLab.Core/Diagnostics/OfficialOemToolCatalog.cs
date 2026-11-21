using System;
using System.Collections.Generic;
using System.Linq;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public enum OemServiceVendor
{
    Samsung,
    Xiaomi,
    GooglePixel,
    MotorolaLenovo,
    Qualcomm,
    MediaTek,
    OtherAndroid
}

public sealed record OfficialOemToolResource(
    string Id,
    OemServiceVendor Vendor,
    string DisplayName,
    string Purpose,
    string OfficialUrl,
    string Availability,
    string SafetyNote);

/// <summary>
/// Curated links to first-party vendor or upstream-tool sources only.
/// A listed resource is an official download/support entry point, not a claim that it supports every handset.
/// </summary>
public static class OfficialOemToolCatalog
{
    private static readonly OfficialOemToolResource[] Resources =
    [
        new("samsung-usb-driver", OemServiceVendor.Samsung,
            "Samsung Android USB Driver",
            "Windows USB/ADB driver from Samsung Developer",
            "https://developer.samsung.com/android-usb-driver",
            "Public official download",
            "Driver installation does not provide Odin, Download Mode automation or model-specific flashing authorization."),
        new("samsung-odin-user-supplied", OemServiceVendor.Samsung,
            "Samsung Odin hand-off",
            "Use an Odin executable obtained through an authorized Samsung service channel",
            "https://developer.samsung.com/",
            "No public official Odin automation/CLI reference located",
            "SoftBox validates packages and target model, then opens the user-supplied GUI. It does not inject undocumented Odin commands or auto-press Start."),
        new("xiaomi-mi-unlock", OemServiceVendor.Xiaomi,
            "Xiaomi Mi Unlock",
            "Official account-based bootloader unlock application and eligibility workflow",
            "https://www.miui.com/unlock/download_en.html",
            "Official Xiaomi unlock page",
            "Unlock requires the owner's eligible Mi Account and Xiaomi authorization. Waiting periods and eligibility remain enforced by Xiaomi; SoftBox does not bypass them."),
        new("google-android-flash-tool", OemServiceVendor.GooglePixel,
            "Android Flash Tool",
            "Google's web-based flashing tool for supported Pixel and development devices",
            "https://flash.android.com/",
            "Official Google tool",
            "Device support, bootloader-unlock requirement and data-wipe warnings are controlled by Google and the selected build."),
        new("google-factory-images", OemServiceVendor.GooglePixel,
            "Google Pixel Factory Images",
            "Official factory image and release instructions",
            "https://developers.google.com/android/images",
            "Official Android Developers documentation",
            "Use the exact device codename and release instructions; factory-image flashing can erase user data."),
        new("motorola-rescue", OemServiceVendor.MotorolaLenovo,
            "Motorola / Lenovo Software Fix (Rescue and Smart Assistant)",
            "Official Windows software repair and reinstall utility",
            "https://en-us.support.motorola.com/app/answers/detail/a_id/187566",
            "Official Motorola support download page",
            "Motorola describes it for software problems, not Google Account removal or hardware repair."),
        new("qualcomm-software-center", OemServiceVendor.Qualcomm,
            "Qualcomm Software Center",
            "Qualcomm software, SDK and chipset tool catalog",
            "https://softwarecenter.qualcomm.com/",
            "Official Qualcomm portal; account/catalog access may be required",
            "Only use programmers and packages authorized for the exact SoC, model, board and secure-boot policy."),
        new("qualcomm-qdl-source", OemServiceVendor.Qualcomm,
            "Qualcomm QDL (source)",
            "Qualcomm's public EDL/QDL source project and documented CLI",
            "https://github.com/qualcomm/qdl",
            "Official upstream source repository; Windows binary must be built/provided separately",
            "QDL requires a device-authorized Firehose programmer. A USB ID or chip name alone does not establish firmware compatibility."),
        new("mediatek-genio-tools", OemServiceVendor.MediaTek,
            "MediaTek Genio Developer Center",
            "MediaTek SP Flash Tool and developer packages for supported Genio evaluation platforms",
            "https://genio.mediatek.com/software-development#tools",
            "Official MediaTek developer portal; NDA/account access may be required",
            "This resource is for Genio IoT development platforms. It is not a generic phone DA or authorization package."),
        new("mediatek-genio-docs", OemServiceVendor.MediaTek,
            "MediaTek Genio Android documentation",
            "Platform-specific Android BSP and supported hardware documentation",
            "https://mediatek.gitlab.io/genio/doc/android/",
            "Official MediaTek Genio documentation",
            "Do not apply Genio packages to handset chipsets. Handset BROM/Preloader recovery needs the OEM's exact signed DA and authorization."),
        new("android-oem-usb-docs", OemServiceVendor.OtherAndroid,
            "Android OEM USB driver documentation",
            "Android Developers guidance for Windows USB driver setup by device manufacturer",
            "https://developer.android.com/studio/run/win-usb",
            "Official Android Developers documentation",
            "This page is for USB driver setup, not a firmware recovery package or device-specific service protocol.")
    ];

    public static IReadOnlyList<OfficialOemToolResource> All { get; } = Resources;

    public static IReadOnlyList<OfficialOemToolResource> ForVendor(OemServiceVendor vendor)
        => Resources.Where(resource => resource.Vendor == vendor).ToArray();
}
