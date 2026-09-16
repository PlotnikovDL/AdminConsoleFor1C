param([string]$ToolsDirectory = (Join-Path $PSScriptRoot '..\artifacts\tools'))

$ErrorActionPreference = 'Stop'
$ToolsDirectory = [IO.Path]::GetFullPath($ToolsDirectory)
$compilerDirectory = Join-Path $ToolsDirectory 'InnoSetup-7.1.0'
$compiler = Join-Path $compilerDirectory 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { Write-Output $compiler; return }
New-Item -ItemType Directory -Path $ToolsDirectory -Force | Out-Null
$downloadPath = Join-Path $ToolsDirectory 'innosetup-7.1.0-x64.exe'
$downloadUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe'
# SHA256 published in the official GitHub release asset metadata.
$expectedHash = '0362a383ed217d4c4239b5933866dd96d3eb2102737da92f80f6057a4b40df2f'
if (-not (Test-Path -LiteralPath $downloadPath)) {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash -ine $expectedHash) {
    throw 'Inno Setup download hash mismatch.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $downloadPath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*') {
    throw 'Inno Setup publisher signature validation failed.'
}
# Install build tools for the current user without changing file associations or shortcuts.
$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /TASKS="" /DIR="' + $compilerDirectory + '"'
$installer = Start-Process -FilePath $downloadPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($installer.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) {
    throw "Inno Setup installation failed with exit code $($installer.ExitCode)."
}
Write-Output $compiler
