param([Parameter(Mandatory)][string]$PackagePath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
$entries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $package.Entries) { $entries.Add($entry.FullName, $entry) }
function Read-PackageText([string]$Name) {
    if (-not $entries.ContainsKey($Name)) { throw "Missing package entry: $Name" }
    $entry = $entries[$Name]
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
try {
    foreach ($name in @(
        'AppxManifest.xml', 'resources.pri', 'AdminConsoleFor1C.App.exe',
        'AdminConsoleFor1C.App.dll', 'AdminConsoleFor1C.App.runtimeconfig.json',
        'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
        'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Assets/AppIcon.ico',
        'ElevatedWorker/AdminConsoleFor1C.ElevatedWorker.exe',
        'ElevatedWorker/AdminConsoleFor1C.ElevatedWorker.dll',
        'ElevatedWorker/AdminConsoleFor1C.ElevatedWorker.runtimeconfig.json',
        'ElevatedWorker/coreclr.dll', 'ElevatedWorker/hostfxr.dll', 'ElevatedWorker/hostpolicy.dll',
        'ElevatedWorker/System.Private.CoreLib.dll'
    )) {
        if (-not $entries.ContainsKey($name)) { throw "Missing package entry: $name" }
    }

    [xml]$manifest = Read-PackageText 'AppxManifest.xml'
    if ($manifest.Package.Identity.ProcessorArchitecture -ne 'x64') { throw 'Expected x64 package.' }
    $frameworkDependencies = @($manifest.Package.Dependencies.ChildNodes | Where-Object LocalName -eq 'PackageDependency')
    if ($frameworkDependencies.Count -gt 0) { throw "Unexpected external package dependencies: $($frameworkDependencies.Name -join ', ')" }
    foreach ($configPath in @('AdminConsoleFor1C.App.runtimeconfig.json', 'ElevatedWorker/AdminConsoleFor1C.ElevatedWorker.runtimeconfig.json')) {
        $config = (Read-PackageText $configPath) | ConvertFrom-Json
        if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks) { throw "Framework-dependent runtime: $configPath" }
        if (-not $config.runtimeOptions.includedFrameworks) { throw "Bundled runtime not declared: $configPath" }
    }
    $unexpectedFiles = @($package.Entries | Where-Object {
        $_.FullName -match '(?i)(\.(pfx|pdb|binlog|cs|csproj|ps1)$|(^|/)(server-connections\.json|auth\.json|\.git/|ragent\.exe|ras\.exe|rac\.exe))'
    })
    if ($unexpectedFiles.Count) { throw "Unexpected release files: $($unexpectedFiles.FullName -join ', ')" }
    Write-Output "Package structure verified: $($manifest.Package.Identity.Name) $($manifest.Package.Identity.Version), x64, $($package.Entries.Count) entries."
}
finally { $package.Dispose() }
