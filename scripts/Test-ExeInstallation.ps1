param(
    [Parameter(Mandatory)][string]$PreviousInstaller,
    [Parameter(Mandatory)][string]$CurrentInstaller,
    [Parameter(Mandatory)][string]$ResultDirectory
)

$ErrorActionPreference = 'Stop'
$PreviousInstaller = (Resolve-Path -LiteralPath $PreviousInstaller).Path
$CurrentInstaller = (Resolve-Path -LiteralPath $CurrentInstaller).Path
foreach ($file in @($PreviousInstaller, $CurrentInstaller)) {
    if ([IO.Path]::GetFileName($file) -notmatch '^AdminConsoleFor1C-InstallationTest-\d+\.\d+\.\d+-win-x64-setup\.exe$') {
        throw 'Only isolated InstallationTest installers are allowed.'
    }
}
$testDirectory = Join-Path $env:ProgramFiles 'AdminConsoleFor1C.InstallationTest'
$registryPath = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PlotnikovDL.AdminConsoleFor1C.InstallationTest_is1'
if ((Test-Path -LiteralPath $testDirectory) -or (Test-Path -LiteralPath $registryPath)) {
    throw 'The isolated installation test already exists. Remove it explicitly before testing.'
}
New-Item -ItemType Directory -Path $ResultDirectory -Force | Out-Null
$ResultDirectory = (Resolve-Path -LiteralPath $ResultDirectory).Path
$settingsPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'AdminConsoleFor1C\server-connections.json'
$settingsHash = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
$report = [ordered]@{ Passed = $false; Installed = $false; Runtime = $false; Shortcut = $false; Upgraded = $false; RunningAppClosed = $false; DowngradeBlocked = $false; Uninstalled = $false; SettingsPreserved = $false; Error = $null }
$runningApp = $null

function Invoke-TestSetup([string]$FilePath, [string]$LogName, [bool]$AllowFailure = $false) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LANG=russian /LOG="' + (Join-Path $ResultDirectory $LogName) + '"'
    if ($FilePath -ne (Join-Path $testDirectory 'unins000.exe')) { $arguments += ' /DIR="' + $testDirectory + '"' }
    $process = Start-Process -FilePath $FilePath -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit(180000)) { throw "Installer timed out: $FilePath" }
    if (-not $AllowFailure -and $process.ExitCode -ne 0) { throw "Installer failed with exit code $($process.ExitCode). See $LogName" }
    return $process.ExitCode
}

try {
    Invoke-TestSetup $PreviousInstaller 'install.log' | Out-Null
    $installed = Get-ItemProperty -LiteralPath $registryPath
    $previousVersion = $installed.DisplayVersion
    if ($installed.InstallLocation.TrimEnd('\') -ine $testDirectory) { throw 'Unexpected installation directory.' }
    $report.Installed = $true
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'AdminConsoleFor1C Installation Test.lnk'
    if (-not (Test-Path -LiteralPath $shortcutPath)) { throw 'The Start menu shortcut is missing.' }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ($shortcut.TargetPath -ine (Join-Path $testDirectory 'AdminConsoleFor1C.App.exe')) { throw 'Invalid shortcut target.' }
    $report.Shortcut = $true
    & (Join-Path $PSScriptRoot 'Test-AppRuntime.ps1') -ExecutablePath (Join-Path $testDirectory 'AdminConsoleFor1C.App.exe') `
        -ResultDirectory (Join-Path $ResultDirectory 'before-upgrade') -CheckWorker
    $report.Runtime = $true

    # Exercise the standard Windows Restart Manager during a silent upgrade.
    $runningApp = Start-Process -FilePath (Join-Path $testDirectory 'AdminConsoleFor1C.App.exe') -PassThru
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 500
        if ($runningApp.HasExited) { throw 'Application exited before the upgrade test.' }
        if ([ExeRuntimeWindow]::Find($runningApp.Id) -ne [IntPtr]::Zero) { break }
    }
    Invoke-TestSetup $CurrentInstaller 'upgrade.log' | Out-Null
    $upgraded = Get-ItemProperty -LiteralPath $registryPath
    $report.Upgraded = ([version]$upgraded.DisplayVersion -gt [version]$previousVersion) -and
        ($upgraded.InstallLocation.TrimEnd('\') -ieq $testDirectory)
    $report.RunningAppClosed = $runningApp.WaitForExit(5000)
    if (-not $report.Upgraded -or -not $report.RunningAppClosed) { throw 'Upgrade or running application shutdown verification failed.' }
    & (Join-Path $PSScriptRoot 'Test-AppRuntime.ps1') -ExecutablePath (Join-Path $testDirectory 'AdminConsoleFor1C.App.exe') `
        -ResultDirectory (Join-Path $ResultDirectory 'after-upgrade')
    $downgradeExit = Invoke-TestSetup $PreviousInstaller 'downgrade.log' $true
    $report.DowngradeBlocked = $downgradeExit -ne 0 -and (Get-ItemProperty -LiteralPath $registryPath).DisplayVersion -eq $upgraded.DisplayVersion
    if (-not $report.DowngradeBlocked) { throw 'A downgrade was not blocked.' }
} catch { $report.Error = $_.Exception.ToString() }
finally {
    if ($runningApp -and -not $runningApp.HasExited) {
        $runningApp.CloseMainWindow() | Out-Null
        if (-not $runningApp.WaitForExit(5000)) { Stop-Process -Id $runningApp.Id }
    }
    $uninstallerPath = Join-Path $testDirectory 'unins000.exe'
    if (Test-Path -LiteralPath $uninstallerPath) {
        try {
            Invoke-TestSetup $uninstallerPath 'uninstall.log' | Out-Null
            # Inno's initial uninstaller process may exit before its temporary cleanup process.
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                $report.Uninstalled = -not (Test-Path -LiteralPath $testDirectory) -and -not (Test-Path -LiteralPath $registryPath) -and
                    -not (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'AdminConsoleFor1C Installation Test.lnk'))
                if ($report.Uninstalled) { break }
                Start-Sleep -Milliseconds 250
            }
        } catch { $report.Error = [string]$report.Error + "`nUninstall: " + $_.Exception.ToString() }
    }
    $currentSettingsHash = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
    $report.SettingsPreserved = $settingsHash -eq $currentSettingsHash
    $report.Passed = $report.Installed -and $report.Runtime -and $report.Shortcut -and $report.Upgraded -and
        $report.RunningAppClosed -and $report.DowngradeBlocked -and $report.Uninstalled -and $report.SettingsPreserved -and -not $report.Error
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultDirectory 'installation-check.json') -Encoding UTF8
}
if (-not $report.Passed) { throw "Installer verification failed. See $ResultDirectory\installation-check.json" }
Write-Output "Install, upgrade, downgrade protection, runtime and uninstall verified: $ResultDirectory"
