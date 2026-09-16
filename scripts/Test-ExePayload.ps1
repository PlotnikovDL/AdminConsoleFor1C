param([Parameter(Mandatory)][string]$PublishDirectory)

$ErrorActionPreference = 'Stop'
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
foreach ($relativePath in @(
    'AdminConsoleFor1C.App.exe', 'AdminConsoleFor1C.App.dll', 'AdminConsoleFor1C.App.pri', 'Assets\AppIcon.ico',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll',
    'Microsoft.UI.Xaml.dll', 'Microsoft.WindowsAppRuntime.dll',
    'ElevatedWorker\AdminConsoleFor1C.ElevatedWorker.exe', 'ElevatedWorker\AdminConsoleFor1C.ElevatedWorker.dll',
    'ElevatedWorker\coreclr.dll', 'ElevatedWorker\hostfxr.dll', 'ElevatedWorker\hostpolicy.dll',
    'ElevatedWorker\System.Private.CoreLib.dll'
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $relativePath) -PathType Leaf)) {
        throw "Missing payload file: $relativePath"
    }
}
foreach ($relativePath in @('AdminConsoleFor1C.App.runtimeconfig.json', 'ElevatedWorker\AdminConsoleFor1C.ElevatedWorker.runtimeconfig.json')) {
    $configuration = Get-Content -LiteralPath (Join-Path $PublishDirectory $relativePath) -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($configuration.runtimeOptions.framework -or $configuration.runtimeOptions.frameworks -or -not $configuration.runtimeOptions.includedFrameworks) {
        throw "A self-contained runtime is required: $relativePath"
    }
}
$unexpected = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | Where-Object {
    $_.Extension -in @('.pfx', '.pdb', '.binlog', '.cs', '.csproj', '.ps1', '.msix', '.appx') -or
    $_.Name -in @('server-connections.json', 'auth.json', 'ragent.exe', 'ras.exe', 'rac.exe', 'AppxSignature.p7x')
})
if ($unexpected.Count) { throw "Unexpected distribution files: $($unexpected.FullName -join ', ')" }
Write-Output 'EXE payload verified: application, elevated worker and self-contained runtimes.'
