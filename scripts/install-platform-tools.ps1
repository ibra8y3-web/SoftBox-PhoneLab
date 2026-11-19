$ErrorActionPreference = "Stop"

$destination = Join-Path $env:LOCALAPPDATA "SoftBox-PhoneLab\platform-tools"
$zipPath = Join-Path $env:TEMP "softbox-platform-tools.zip"
$url = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip"

Write-Host "Downloading official Android SDK Platform-Tools from Google..."
Invoke-WebRequest -Uri $url -OutFile $zipPath
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Expand-Archive -Path $zipPath -DestinationPath (Split-Path $destination -Parent) -Force

$installed = Join-Path (Split-Path $destination -Parent) "platform-tools"
if (-not (Test-Path (Join-Path $installed "adb.exe"))) {
    throw "adb.exe was not found in the downloaded archive."
}

[Environment]::SetEnvironmentVariable("SOFTBOX_ADB", (Join-Path $installed "adb.exe"), "User")
$env:SOFTBOX_ADB = Join-Path $installed "adb.exe"
Remove-Item $zipPath -Force
Write-Host "Installed Platform-Tools. Restart SoftBox PhoneLab so it can locate adb.exe."
