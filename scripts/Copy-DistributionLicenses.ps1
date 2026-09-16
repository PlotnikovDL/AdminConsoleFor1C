param([Parameter(Mandatory)][string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$licenseDirectory = Join-Path $PublishDirectory 'Licenses'
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $PublishDirectory 'LICENSE.txt')
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'packaging\licenses') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $licenseDirectory
}
$seen = @{}
$notices = [Collections.Generic.List[string]]::new()
$notices.Add('Third-party components used by AdminConsoleFor1C')
$notices.Add('The application MIT license does not replace the licenses of its dependencies.')
$notices.Add('Original license and notice files are included in this directory where provided by each package.')
foreach ($project in @('AdminConsoleFor1C.App', 'AdminConsoleFor1C.ElevatedWorker')) {
    $assetsPath = Join-Path $repositoryRoot "src\$project\obj\project.assets.json"
    $assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in $assets.libraries.PSObject.Properties) {
        if ($entry.Value.type -ne 'package' -or $entry.Name -like 'Microsoft.Windows.SDK.BuildTools*' -or $seen.ContainsKey($entry.Name)) { continue }
        $seen[$entry.Name] = $true
        $packageDirectory = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder $entry.Value.path
            if (Test-Path -LiteralPath $candidate) { $packageDirectory = $candidate; break }
        }
        if (-not $packageDirectory) { throw "Restored package was not found: $($entry.Name)" }
        $nuspecFile = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File | Select-Object -First 1
        [xml]$nuspec = Get-Content -LiteralPath $nuspecFile.FullName -Raw -Encoding UTF8
        $metadata = $nuspec.package.metadata
        $notices.Add('')
        $notices.Add($entry.Name)
        $notices.Add('Authors: ' + [string]$metadata.authors)
        $licenseText = if ($metadata.license -is [Xml.XmlElement]) { $metadata.license.InnerText } else { [string]$metadata.license }
        if ($licenseText) { $notices.Add('License: ' + $licenseText) }
        if ($metadata.licenseUrl) { $notices.Add('License URL: ' + [string]$metadata.licenseUrl) }
        if ($metadata.repository.url) { $notices.Add('Source: ' + [string]$metadata.repository.url) }
        $prefix = $entry.Name.Replace('/', '-')
        foreach ($relativePath in @($entry.Value.files | Where-Object { $_ -match '(?i)(^|/)(licen[cs]e[^/]*|notice[^/]*|third-party-notices[^/]*)$' })) {
            $sourcePath = Join-Path $packageDirectory $relativePath
            $targetName = $prefix + '-' + $relativePath.Replace('/', '-')
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $licenseDirectory $targetName)
        }
    }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $PublishDirectory 'AdminConsoleFor1C.App.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$runtimeVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$runtimePackagePath = $null
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path $folder "microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
    if (Test-Path -LiteralPath $candidate) { $runtimePackagePath = $candidate; break }
}
if (-not $runtimePackagePath) { throw 'The bundled .NET runtime license package was not found.' }
foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    Copy-Item -LiteralPath (Join-Path $runtimePackagePath $name) -Destination (Join-Path $licenseDirectory ("dotnet-$runtimeVersion-$name"))
}
$notices.Add('')
$notices.Add("Microsoft.NETCore.App $runtimeVersion; license and third-party notices: dotnet-$runtimeVersion-*.TXT")
$notices.Add('FluentIcons: https://github.com/davidxuang/FluentIcons/tree/906efc959e3b76a2b933214ac7ba38c0979fdc7e (MIT)')
$notices.Add('Fluent UI System Icons: https://github.com/microsoft/fluentui-system-icons/tree/1.1.328 (MIT)')
$notices | Set-Content -LiteralPath (Join-Path $licenseDirectory 'THIRD-PARTY-NOTICES.txt') -Encoding UTF8
Write-Output "Distribution licenses copied: $licenseDirectory"
