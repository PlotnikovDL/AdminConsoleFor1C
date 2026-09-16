param(
    [Parameter(Mandatory)][string]$WorkerPath,
    [Parameter(Mandatory)][string]$ServiceName,
    [Parameter(Mandatory)][string]$ResultPath,
    [Parameter(Mandatory)][string]$StatusPath
)
$ErrorActionPreference = 'Stop'
$status = [ordered]@{ ExitCode = $null; Error = $null }
try {
    if ($ServiceName -notmatch '^AdminConsoleFor1C-PackageTest-[0-9a-f]{32}$') { throw 'Expected a unique package-test service name.' }
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) { throw 'The test service must not exist.' }
    $worker = Start-Process -FilePath $WorkerPath -Verb RunAs -WindowStyle Hidden -PassThru `
        -ArgumentList @('service', 'start', '--name', $ServiceName, '--result', ('"' + $ResultPath + '"'))
    if (-not $worker.WaitForExit(30000)) { Stop-Process -Id $worker.Id; throw 'Worker timed out.' }
    $status.ExitCode = $worker.ExitCode
}
catch { $status.Error = $_.Exception.ToString() }
finally {
    $temporaryStatus = $StatusPath + '.tmp'
    $status | ConvertTo-Json | Set-Content -LiteralPath $temporaryStatus -Encoding UTF8
    Move-Item -LiteralPath $temporaryStatus -Destination $StatusPath -Force
}
