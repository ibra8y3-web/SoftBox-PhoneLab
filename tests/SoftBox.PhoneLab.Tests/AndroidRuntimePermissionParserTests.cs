using SoftBox.PhoneLab.Core.Android;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidRuntimePermissionParserTests
{
    [Fact]
    public void ReturnsOnlyPermissionsRequestedByTheAppAndListedInRuntimeSection()
    {
        const string output = """
            Packages:
              Package [com.example.customerapp]
                requested permissions:
                  android.permission.CAMERA
                  android.permission.RECORD_AUDIO
                  android.permission.ACCESS_FINE_LOCATION
                install permissions:
                  android.permission.INTERNET: granted=true
                runtime permissions:
                  android.permission.CAMERA: granted=false, flags=[ USER_SET|USER_SENSITIVE_WHEN_GRANTED ]
                  android.permission.RECORD_AUDIO: granted=true, flags=[ USER_SET ]
                  android.permission.READ_CONTACTS: granted=true, flags=[ USER_SET ]
            """;

        var permissions = AndroidRuntimePermissionParser.Parse(output);

        Assert.Equal(2, permissions.Count);
        Assert.Contains(permissions, item =>
            item.Name == "android.permission.CAMERA" &&
            item.IsGranted == false &&
            item.Flags.Contains("USER_SET", StringComparison.Ordinal));
        Assert.Contains(permissions, item =>
            item.Name == "android.permission.RECORD_AUDIO" &&
            item.IsGranted == true);
    }

    [Fact]
    public void DuplicateConflictingRuntimeStatesAreMarkedAndNotResolved()
    {
        const string output = """
            requested permissions:
              android.permission.CAMERA
            runtime permissions:
              android.permission.CAMERA: granted=true, flags=[ USER_SET ]
              android.permission.CAMERA: granted=false, flags=[ USER_SET ]
            """;

        var result = Assert.Single(AndroidRuntimePermissionParser.Parse(output));

        Assert.Null(result.IsGranted);
        Assert.True(result.IsConflicting);
    }

    [Fact]
    public void InstallAndPrivilegedPermissionsAreNeverReturnedAsRuntimeOperations()
    {
        const string output = """
            requested permissions:
              android.permission.CAMERA
              android.permission.INTERNET
            install permissions:
              android.permission.INTERNET: granted=true, flags=[ ]
            runtime permissions:
              android.permission.CAMERA: granted=true, flags=[ USER_SET ]
            """;

        var result = Assert.Single(AndroidRuntimePermissionParser.Parse(output));

        Assert.Equal("android.permission.CAMERA", result.Name);
    }

    [Fact]
    public void InvalidPermissionNamesAndMalformedRowsAreIgnored()
    {
        const string output = """
            requested permissions:
              android.permission.CAMERA;pm
              --dangerous
              android.permission.RECORD_AUDIO
            runtime permissions:
              android.permission.CAMERA;pm: granted=true
              android.permission.RECORD_AUDIO: granted=true
            """;

        var result = Assert.Single(AndroidRuntimePermissionParser.Parse(output));

        Assert.Equal("android.permission.RECORD_AUDIO", result.Name);
    }

    [Fact]
    public void EmptyOrUnrelatedDumpProducesNoPermissions()
    {
        Assert.Empty(AndroidRuntimePermissionParser.Parse("Package not found\nNo permission state available."));
        Assert.False(AndroidRuntimePermissionParser.IsSafePermissionName("--revoke"));
        Assert.True(AndroidRuntimePermissionParser.IsSafePermissionName("android.permission.CAMERA"));
    }
}
