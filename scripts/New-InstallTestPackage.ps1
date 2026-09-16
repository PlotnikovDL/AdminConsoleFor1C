param(
    [Parameter(Mandatory)][string]$PackagePath,
    [switch]$ForSigning
)

$ErrorActionPreference = 'Stop'
$packageFile = Get-Item -LiteralPath $PackagePath
& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PackagePath $packageFile.FullName
$assetsPath = Join-Path $PSScriptRoot '..\src\AdminConsoleFor1C.App\obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$sdk = $assets.libraries.PSObject.Properties | Where-Object Name -like 'Microsoft.Windows.SDK.BuildTools/*' | Select-Object -First 1
$makeAppx = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $sdkDirectory = Join-Path $folder $sdk.Value.path
    if (Test-Path -LiteralPath $sdkDirectory) {
        $makeAppx = Get-ChildItem -Path (Join-Path $sdkDirectory 'bin\*\x64\makeappx.exe') -File | Select-Object -First 1
        if ($makeAppx) { break }
    }
}
if (-not $makeAppx) { throw 'MakeAppx.exe was not found in the restored Windows SDK build tools.' }

$testDirectory = Join-Path $packageFile.DirectoryName ('installation-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$layout = Join-Path $testDirectory 'layout'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
& $makeAppx.FullName unpack /p $packageFile.FullName /d $layout /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'MSIX unpack failed.' }

$manifestPath = Join-Path $layout 'AppxManifest.xml'
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
$manifest.Package.Identity.Name = 'PlotnikovDL.AdminConsoleFor1C.InstallationTest'
# Microsoft's Windows 11 unsigned-package namespace: installable only with explicit -AllowUnsigned.
# https://learn.microsoft.com/windows/msix/package/unsigned-package
if (-not $ForSigning) {
    $manifest.Package.Identity.Publisher = 'CN=PlotnikovDL, OID.2.25.311729368913984317654407730594956997722=1'
}
$manifest.Package.Properties.DisplayName += ' (MSIX test)'
$manifest.Save($manifestPath)
foreach ($metadataFile in @('AppxBlockMap.xml', 'AppxSignature.p7x', '[Content_Types].xml')) {
    $metadataPath = Join-Path $layout $metadataFile
    if (Test-Path -LiteralPath $metadataPath) { Remove-Item -LiteralPath $metadataPath }
}
$testPackage = Join-Path $testDirectory 'AdminConsoleFor1C.InstallationTest.msix'
& $makeAppx.FullName pack /d $layout /p $testPackage /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Test package creation failed.' }
& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PackagePath $testPackage
Write-Output "Windows 11 installation test package: $testPackage"
Write-Output 'This separate package is for installation testing only; do not distribute it as a release.'
