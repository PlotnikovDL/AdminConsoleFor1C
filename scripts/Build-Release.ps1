param(
    [ValidateSet('Exe', 'Msix')][string]$Format = 'Exe',
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\release'),
    [string]$MSBuildPath,
    [string]$InnoSetupPath
)

$ErrorActionPreference = 'Stop'
if ($Format -eq 'Exe') {
    & (Join-Path $PSScriptRoot 'Build-ExeRelease.ps1') -OutputRoot $OutputRoot -MSBuildPath $MSBuildPath -InnoSetupPath $InnoSetupPath
    return
}
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $repositoryRoot 'src\AdminConsoleFor1C.App\AdminConsoleFor1C.App.csproj'
$workerProject = Join-Path $repositoryRoot 'src\AdminConsoleFor1C.ElevatedWorker\AdminConsoleFor1C.ElevatedWorker.csproj'
[xml]$manifest = Get-Content (Join-Path $repositoryRoot 'src\AdminConsoleFor1C.App\Package.appxmanifest') -Raw -Encoding UTF8
$version = $manifest.Package.Identity.Version

if (-not $MSBuildPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio with WinUI/MSIX build tools, or specify -MSBuildPath.' }
    $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) { throw 'MSBuild.exe was not found.' }

$runName = '{0}-x64-{1}-{2}' -f $version, (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 6))
$outputDirectory = [IO.Path]::GetFullPath((Join-Path $OutputRoot $runName))
$workerDirectory = Join-Path $outputDirectory 'worker'
$packageDirectory = Join-Path $outputDirectory 'packages'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    & dotnet publish $workerProject -c Release -r win-x64 --self-contained true `
        -p:Platform=x64 -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false `
        -o $workerDirectory
    if ($LASTEXITCODE -ne 0) { throw "Worker publish failed with exit code $LASTEXITCODE." }

    & $MSBuildPath $appProject /restore /nologo /m:1 /v:minimal `
        /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
        /p:SelfContained=true /p:WindowsAppSDKSelfContained=true /p:PublishTrimmed=false `
        /p:DebugType=None /p:DebugSymbols=false /p:EnableWinAppRunSupport=false `
        /p:GenerateAppxPackageOnBuild=true /p:UapAppxPackageBuildMode=SideloadOnly `
        /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false /p:AppxSymbolPackageEnabled=false `
        /p:AppxAutoIncrementPackageRevision=false /p:GenerateAppInstallerFile=false `
        "/p:ElevatedWorkerPublishDirectory=$workerDirectory" "/p:AppxPackageDir=$packageDirectory\" `
        "/bl:$outputDirectory\build.binlog"
    if ($LASTEXITCODE -ne 0) { throw "MSIX build failed with exit code $LASTEXITCODE. See $outputDirectory\build.binlog." }

    $packages = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -File | Where-Object {
        $_.Extension -in @('.msix', '.appx') -and $_.Name -like 'AdminConsoleFor1C*'
    })
    if ($packages.Count -ne 1) { throw "Expected one application package, found $($packages.Count)." }
    $packagePath = Join-Path $outputDirectory "AdminConsoleFor1C_${version}_x64.msix"
    Copy-Item -LiteralPath $packages[0].FullName -Destination $packagePath
    & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -PackagePath $packagePath

    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($packagePath))" | Set-Content (Join-Path $outputDirectory 'SHA256SUMS.txt') -Encoding ASCII
    Write-Output "Unsigned MSIX: $packagePath"
    Write-Output 'Sign this package before installing it. Test certificates are not production signing.'
}
finally { Pop-Location }
