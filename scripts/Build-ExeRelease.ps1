param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\release'),
    [string]$MSBuildPath,
    [string]$InnoSetupPath,
    [switch]$PublishOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AdminConsoleFor1C.App\Package.appxmanifest') -Raw -Encoding UTF8
$version = [version]$manifest.Package.Identity.Version
if ($version.Revision -ne 0) { throw 'EXE releases use major.minor.patch versions. Set the MSIX revision component to zero.' }
$packageVersion = $version.ToString(3)
if (-not $MSBuildPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio MSBuild with WinUI tools is required.' }
    $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) { throw 'MSBuild.exe was not found.' }
$runName = '{0}-exe-x64-{1}-{2}' -f $packageVersion, (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 6))
$outputDirectory = [IO.Path]::GetFullPath((Join-Path $OutputRoot $runName))
$workerDirectory = Join-Path $outputDirectory 'worker'
$publishDirectory = Join-Path $outputDirectory 'app'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Push-Location $repositoryRoot
try {
    & dotnet publish 'src\AdminConsoleFor1C.ElevatedWorker\AdminConsoleFor1C.ElevatedWorker.csproj' `
        -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false "-p:Version=$packageVersion" -o $workerDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }

    & $MSBuildPath 'src\AdminConsoleFor1C.App\AdminConsoleFor1C.App.csproj' /restore /t:Publish /nologo /m:1 /v:minimal `
        /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
        /p:PublishProfile=win-x64-unpackaged /p:WindowsPackageType=None /p:EnableWinAppRunSupport=false `
        /p:GenerateAppxPackageOnBuild=false /p:SelfContained=true /p:WindowsAppSDKSelfContained=true `
        /p:PublishSingleFile=false /p:PublishTrimmed=false /p:DebugType=None /p:DebugSymbols=false `
        "/p:Version=$packageVersion" "/p:FileVersion=$version" "/p:PublishDir=$publishDirectory\" `
        "/p:ElevatedWorkerPublishDirectory=$workerDirectory" "/bl:$outputDirectory\build.binlog"
    if ($LASTEXITCODE -ne 0) { throw "App publish failed. See $outputDirectory\build.binlog." }
    & (Join-Path $PSScriptRoot 'Test-ExePayload.ps1') -PublishDirectory $publishDirectory
    [ordered]@{ Version = $packageVersion; FileVersion = [string]$version; PublishDirectory = $publishDirectory } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'build-info.json') -Encoding UTF8
    if (-not $PublishOnly) {
        & (Join-Path $PSScriptRoot 'Build-ExeInstaller.ps1') -PublishDirectory $publishDirectory `
            -OutputDirectory $outputDirectory -Version $packageVersion -InnoSetupPath $InnoSetupPath
        & (Join-Path $PSScriptRoot 'New-WinGetManifest.ps1') `
            -InstallerPath (Join-Path $outputDirectory "AdminConsoleFor1C-$packageVersion-win-x64-setup.exe") -Version $packageVersion
    }
    Write-Output "Release output: $outputDirectory"
} finally { Pop-Location }
