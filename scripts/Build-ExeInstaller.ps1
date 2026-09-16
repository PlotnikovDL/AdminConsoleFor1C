param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$InnoSetupPath,
    [switch]$InstallationTest
)

$ErrorActionPreference = 'Stop'
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $PSScriptRoot 'Test-ExePayload.ps1') -PublishDirectory $PublishDirectory
if (-not $InnoSetupPath) { $InnoSetupPath = & (Join-Path $PSScriptRoot 'Get-InnoSetup.ps1') }
if (-not (Test-Path -LiteralPath $InnoSetupPath -PathType Leaf)) { throw 'Inno Setup compiler was not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& (Join-Path $PSScriptRoot 'Copy-DistributionLicenses.ps1') -PublishDirectory $PublishDirectory
$compilerArguments = @('/Qp', "/DPublishDirectory=$PublishDirectory", "/DReleaseDirectory=$OutputDirectory", "/DReleaseVersion=$Version")
$fileName = "AdminConsoleFor1C-$Version-win-x64-setup"
if ($InstallationTest) {
    $fileName = "AdminConsoleFor1C-InstallationTest-$Version-win-x64-setup"
    $compilerArguments += @('/DInstallerId=PlotnikovDL.AdminConsoleFor1C.InstallationTest',
        '/DInstallFolderName=AdminConsoleFor1C.InstallationTest', '/DDisplayName=AdminConsoleFor1C Installation Test',
        "/DInstallerFileName=$fileName")
}
$installerPath = Join-Path $OutputDirectory ($fileName + '.exe')
if (Test-Path -LiteralPath $installerPath) { throw "Output already exists: $installerPath" }
$compilerArguments += (Join-Path $PSScriptRoot '..\packaging\AdminConsoleFor1C.iss')
& $InnoSetupPath @compilerArguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) { throw "Installer compilation failed: $LASTEXITCODE" }
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $fileName.exe" | Set-Content -LiteralPath (Join-Path $OutputDirectory ($fileName + '.sha256')) -Encoding ASCII
Write-Output "EXE installer: $installerPath"
