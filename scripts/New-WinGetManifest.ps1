param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$ReleaseRepository = 'PlotnikovDL/AdminConsoleFor1C',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$installerFile = Get-Item -LiteralPath $InstallerPath
if ($installerFile.Name -cne "AdminConsoleFor1C-$Version-win-x64-setup.exe") { throw 'Expected the public EXE installer with its versioned filename.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $installerFile.DirectoryName "winget\manifests\p\PlotnikovDL\AdminConsoleFor1C\$Version" }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$repositoryUrl = "https://github.com/$ReleaseRepository"
$installerUrl = "$repositoryUrl/releases/download/v$Version/$($installerFile.Name)"
$hash = (Get-FileHash -LiteralPath $installerFile.FullName -Algorithm SHA256).Hash
foreach ($template in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\packaging\winget') -Filter '*.yaml.template' -File) {
    $text = Get-Content -LiteralPath $template.FullName -Raw -Encoding UTF8
    $text = $text.Replace('@VERSION@', $Version).Replace('@INSTALLER_URL@', $installerUrl).Replace('@SHA256@', $hash).Replace('@REPOSITORY_URL@', $repositoryUrl)
    $targetPath = Join-Path $OutputDirectory ($template.Name -replace '\.template$', '')
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($targetPath), $text, [Text.UTF8Encoding]::new($false))
}
Write-Output "WinGet manifests: $OutputDirectory"
Write-Output 'Publish the matching installer at InstallerUrl before submitting these manifests to winget-pkgs.'
