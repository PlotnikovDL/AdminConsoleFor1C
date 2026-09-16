param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory, ParameterSetName = 'New')][switch]$CreateCertificate,
    [Parameter(Mandatory, ParameterSetName = 'Existing')][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$packageFile = Get-Item -LiteralPath $PackagePath
& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PackagePath $packageFile.FullName
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packageFile.FullName)
try {
    if ($archive.GetEntry('AppxSignature.p7x')) { throw 'The input package is already signed.' }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$publisher = [string]$manifest.Package.Identity.Publisher
if ($publisher -like '*OID.2.25.311729368913984317654407730594956997722*') {
    throw 'Use New-InstallTestPackage.ps1 -ForSigning to create a signable installation test package.'
}

$assetsPath = Join-Path $PSScriptRoot '..\src\AdminConsoleFor1C.App\obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$sdk = $assets.libraries.PSObject.Properties | Where-Object Name -like 'Microsoft.Windows.SDK.BuildTools/*' | Select-Object -First 1
$signTool = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $sdkDirectory = Join-Path $folder $sdk.Value.path
    if (Test-Path -LiteralPath $sdkDirectory) {
        $signTool = Get-ChildItem -Path (Join-Path $sdkDirectory 'bin\*\x64\signtool.exe') -File | Select-Object -First 1
        if ($signTool) { break }
    }
}
if (-not $signTool) { throw 'SignTool.exe was not found in the restored Windows SDK build tools.' }

$friendlyName = 'AdminConsoleFor1C local test signing'
if ($CreateCertificate) {
    # Keep the private key non-exportable in the user's certificate store, outside the repository.
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher `
        -FriendlyName $friendlyName -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyUsage DigitalSignature `
        -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddDays(90) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
} else {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
}
if ($certificate.FriendlyName -cne $friendlyName -or $certificate.Subject -cne $publisher -or
    $certificate.Issuer -cne $certificate.Subject -or -not $certificate.HasPrivateKey -or
    $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
    throw 'Expected an unexpired local test signing certificate matching the package publisher.'
}

$outputDirectory = Join-Path $packageFile.DirectoryName ('test-signed-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $outputDirectory | Out-Null
$signedPackage = Join-Path $outputDirectory $packageFile.Name
Copy-Item -LiteralPath $packageFile.FullName -Destination $signedPackage
& $signTool.FullName sign /fd SHA256 /s My /sha1 $certificate.Thumbprint $signedPackage
if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE." }

# Verify the embedded cryptographic signature without installing trust on the machine.
# Windows installation verification additionally checks the complete MSIX payload.
Add-Type -AssemblyName System.Security
$archive = [IO.Compression.ZipFile]::OpenRead($signedPackage)
try {
    $stream = $archive.GetEntry('AppxSignature.p7x').Open()
    $buffer = [IO.MemoryStream]::new()
    try { $stream.CopyTo($buffer); $signatureBytes = $buffer.ToArray() }
    finally { $stream.Dispose(); $buffer.Dispose() }
} finally { $archive.Dispose() }
if ([Text.Encoding]::ASCII.GetString($signatureBytes, 0, 4) -cne 'PKCX') { throw 'Invalid MSIX signature header.' }
$cms = [Security.Cryptography.Pkcs.SignedCms]::new()
$cms.Decode([byte[]]$signatureBytes[4..($signatureBytes.Length - 1)])
$cms.CheckSignature($true)
if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $certificate.Thumbprint) {
    throw 'The MSIX signer does not match the requested certificate.'
}

$certificatePath = Join-Path $outputDirectory 'AdminConsoleFor1C.TestOnly.cer'
Export-Certificate -Cert $certificate -FilePath $certificatePath -Type CERT | Out-Null
$hash = (Get-FileHash -LiteralPath $signedPackage -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($packageFile.Name)" | Set-Content -LiteralPath (Join-Path $outputDirectory 'SHA256SUMS.txt') -Encoding ASCII
[ordered]@{
    TestOnly = $true
    PackagePath = $signedPackage
    Sha256 = $hash
    CertificatePath = $certificatePath
    CertificateThumbprint = $certificate.Thumbprint
    Publisher = $publisher
    CertificateExpires = $certificate.NotAfter.ToUniversalTime().ToString('o')
    PrivateKeyExported = $false
    EmbeddedSignatureVerified = $true
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'signing-info.json') -Encoding UTF8
Write-Output "Test-signed MSIX: $signedPackage"
Write-Output "Public certificate: $certificatePath"
Write-Output 'For testing only. Installation requires explicitly trusting the public certificate. Do not submit this package to WinGet.'
