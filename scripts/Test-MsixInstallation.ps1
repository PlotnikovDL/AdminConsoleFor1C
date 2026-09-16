param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$ResultDirectory,
    [string]$CertificatePath
)

$ErrorActionPreference = 'Stop'
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$ResultDirectory = [IO.Path]::GetFullPath($ResultDirectory)
New-Item -ItemType Directory -Path $ResultDirectory -Force | Out-Null
$reportPath = Join-Path $ResultDirectory 'installation-check.json'
$report = [ordered]@{ Passed = $false; Installed = $false; Launched = $false; BundledRuntime = $false; Worker = $false; Uninstalled = $false; Signed = [bool]$CertificatePath; SignatureVerified = $false; CertificateTrustRestored = $true; Error = $null }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installation test from an elevated Windows PowerShell to manage the isolated package and temporary certificate trust.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$testName = 'PlotnikovDL.AdminConsoleFor1C.InstallationTest'
if ($manifest.Package.Identity.Name -ne $testName) {
    throw 'Only the isolated InstallationTest package is allowed.'
}
if ($CertificatePath) {
    $CertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
    $testCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
    $signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
    if ($testCertificate.HasPrivateKey -or $testCertificate.Subject -cne $manifest.Package.Identity.Publisher -or
        $testCertificate.Subject -cne $testCertificate.Issuer -or
        $signature.SignerCertificate.Thumbprint -ne $testCertificate.Thumbprint) {
        throw 'The public test certificate must match the package publisher and signer.'
    }
} elseif ($manifest.Package.Identity.Publisher -notlike '*OID.2.25.311729368913984317654407730594956997722=1*') {
    throw 'An unsigned test requires the Windows 11 unsigned-package publisher namespace.'
}
if (Get-AppxPackage -AllUsers -Name $testName) { throw 'An InstallationTest package already exists. Remove it explicitly before running a new test.' }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
public class MsixActivationManager {}
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMsixActivationManager {
 [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.LPWStr)] string args, uint options, out uint pid);
}
public static class MsixTestWindow {
 public static uint Launch(string appId) {
  var manager = (IMsixActivationManager)new MsixActivationManager();
  try { uint processId; int hr = manager.ActivateApplication(appId, "", 0, out processId); Marshal.ThrowExceptionForHR(hr); return processId; }
  finally { Marshal.ReleaseComObject(manager); }
 }
 public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 public static IntPtr Find(uint processId) {
  IntPtr result = IntPtr.Zero;
  EnumWindows((hwnd, param) => {
   uint id; GetWindowThreadProcessId(hwnd, out id); Rect r;
   if (id == processId && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out r) && r.Right-r.Left > 400 && r.Bottom-r.Top > 200) { result = hwnd; return false; }
   return true;
  }, IntPtr.Zero);
  return result;
 }
}
'@

$installed = $null
$appProcess = $null
$window = [IntPtr]::Zero
$addedCertificatePath = $null
try {
    if ($CertificatePath) {
        $trustPath = 'Cert:\LocalMachine\TrustedPeople\' + $testCertificate.Thumbprint
        if (-not (Test-Path -LiteralPath $trustPath)) {
            # Trust only this leaf certificate, and remove only the trust added by this test.
            $addedCertificatePath = $trustPath
            $report.CertificateTrustRestored = $false
            Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
        if ($signature.Status -ne 'Valid') { throw "Package signature validation failed: $($signature.StatusMessage)" }
        $report.SignatureVerified = $true
        Add-AppxPackage -Path $PackagePath -ErrorAction Stop
    } else {
        Add-AppxPackage -Path $PackagePath -AllowUnsigned -ErrorAction Stop
    }
    $installed = Get-AppxPackage -Name $testName
    if (-not $installed) { throw 'Installed package was not found.' }
    $report.Installed = $true
    $report.InstallLocation = $installed.InstallLocation
    [uint32]$appProcessId = [MsixTestWindow]::Launch($installed.PackageFamilyName + '!App')
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 500
        $appProcess = Get-Process -Id $appProcessId -ErrorAction SilentlyContinue
        if (-not $appProcess) { throw 'Installed application exited during startup.' }
        $window = [MsixTestWindow]::Find($appProcessId)
        if ($window -ne [IntPtr]::Zero) { break }
    }
    if ($window -eq [IntPtr]::Zero) { throw 'Installed application did not show its main window.' }
    Start-Sleep -Seconds 2
    $report.Launched = $true
    $appProcess.Refresh()
    foreach ($moduleName in @('coreclr.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll')) {
        $module = $appProcess.Modules | Where-Object ModuleName -eq $moduleName | Select-Object -First 1
        if (-not $module -or -not $module.FileName.StartsWith($installed.InstallLocation + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Module was not loaded from the installed package: $moduleName"
        }
    }
    $report.BundledRuntime = $true
    $rect = New-Object MsixTestWindow+Rect
    [MsixTestWindow]::GetWindowRect($window, [ref]$rect) | Out-Null
    $bitmap = [Drawing.Bitmap]::new($rect.Right-$rect.Left, $rect.Bottom-$rect.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try { [MsixTestWindow]::PrintWindow($window, $dc, 2) | Out-Null }
    finally { $graphics.ReleaseHdc($dc) }
    try { $bitmap.Save((Join-Path $ResultDirectory 'installed-window.png'), [Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }

    # A unique nonexistent service exercises worker startup and JSON IPC without changing a service.
    $workerResult = Join-Path $ResultDirectory 'worker-result.json'
    $absentService = 'AdminConsoleFor1C-PackageTest-' + [Guid]::NewGuid().ToString('N')
    $workerPath = Join-Path $installed.InstallLocation 'ElevatedWorker\AdminConsoleFor1C.ElevatedWorker.exe'
    $workerStatusPath = Join-Path $ResultDirectory 'worker-status.json'
    $workerTestScript = Join-Path $PSScriptRoot 'Test-PackagedWorker.ps1'
    $workerTestArguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $workerTestScript +
        '" -WorkerPath "' + $workerPath + '" -ServiceName "' + $absentService +
        '" -ResultPath "' + $workerResult + '" -StatusPath "' + $workerStatusPath + '"'
    # Match the app's package context when launching its elevated child process.
    Invoke-CommandInDesktopPackage -PackageFamilyName $installed.PackageFamilyName -AppId App `
        -Command "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Args $workerTestArguments
    for ($attempt = 0; $attempt -lt 80 -and -not (Test-Path -LiteralPath $workerStatusPath); $attempt++) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path -LiteralPath $workerStatusPath)) { throw 'Packaged worker check did not complete.' }
    $workerStatus = Get-Content -LiteralPath $workerStatusPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($workerStatus.Error) { throw $workerStatus.Error }
    if ($workerStatus.ExitCode -ne 1 -or -not (Test-Path -LiteralPath $workerResult)) { throw 'Worker did not return the expected service-not-found result.' }
    $response = Get-Content -LiteralPath $workerResult -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($response.Success -or [string]::IsNullOrWhiteSpace($response.ErrorMessage)) { throw 'Invalid worker JSON result.' }
    $report.Worker = $true
}
catch { $report.Error = $_.Exception.ToString() }
finally {
    if ($appProcess -and -not $appProcess.HasExited) {
        if ($window -ne [IntPtr]::Zero) { [MsixTestWindow]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
        if (-not $appProcess.WaitForExit(5000)) { Stop-Process -Id $appProcess.Id -ErrorAction SilentlyContinue }
    }
    if ($installed) {
        try {
            Remove-AppxPackage -Package $installed.PackageFullName -AllUsers -ErrorAction Stop
            $report.Uninstalled = -not [bool](Get-AppxPackage -Name $testName)
        } catch { $report.Error = [string]$report.Error + "`nUninstall: " + $_.Exception.Message }
    }
    if ($addedCertificatePath) {
        try {
            if (Test-Path -LiteralPath $addedCertificatePath) { Remove-Item -LiteralPath $addedCertificatePath -ErrorAction Stop }
            $report.CertificateTrustRestored = -not (Test-Path -LiteralPath $addedCertificatePath)
        } catch { $report.Error = [string]$report.Error + "`nCertificate cleanup: " + $_.Exception.Message }
    }
    $report.Passed = $report.Installed -and $report.Launched -and $report.BundledRuntime -and $report.Worker -and $report.Uninstalled -and
        $report.CertificateTrustRestored -and (-not $report.Signed -or $report.SignatureVerified) -and -not $report.Error
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
if (-not $report.Passed) { throw "Installation check failed. See $reportPath" }
Write-Output "Installation, bundled runtime, worker and uninstall verified. Report: $reportPath"
