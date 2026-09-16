param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][string]$ResultDirectory,
    [switch]$CheckWorker
)

$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$applicationDirectory = Split-Path $ExecutablePath -Parent
New-Item -ItemType Directory -Path $ResultDirectory -Force | Out-Null
$ResultDirectory = (Resolve-Path -LiteralPath $ResultDirectory).Path
$report = [ordered]@{ Passed = $false; Launched = $false; Unpackaged = $false; BundledRuntime = $false; Worker = $null; Error = $null }
Add-Type -AssemblyName System.Drawing
if (-not ('ExeRuntimeWindow' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class ExeRuntimeWindow {
 public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder name);
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
}
$applicationProcess = $null
try {
    $applicationProcess = Start-Process -FilePath $ExecutablePath -WorkingDirectory $applicationDirectory -PassThru
    $window = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 500
        $applicationProcess.Refresh()
        if ($applicationProcess.HasExited) { throw "Application exited during startup: $($applicationProcess.ExitCode)" }
        $window = [ExeRuntimeWindow]::Find($applicationProcess.Id)
        if ($window -ne [IntPtr]::Zero) { break }
    }
    if ($window -eq [IntPtr]::Zero) { throw 'The application did not show its main window.' }
    Start-Sleep -Seconds 2
    $report.Launched = $true
    [uint32]$nameLength = 0
    $packageResult = [ExeRuntimeWindow]::GetPackageFullName($applicationProcess.Handle, [ref]$nameLength, $null)
    if ($packageResult -ne 15700) { throw "Expected an unpackaged process, GetPackageFullName returned $packageResult." }
    $report.Unpackaged = $true
    foreach ($moduleName in @('coreclr.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll')) {
        $module = $applicationProcess.Modules | Where-Object ModuleName -eq $moduleName | Select-Object -First 1
        if (-not $module -or -not $module.FileName.StartsWith($applicationDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Runtime module was not loaded from the app directory: $moduleName"
        }
    }
    $report.BundledRuntime = $true
    $oldDpiContext = [ExeRuntimeWindow]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
    try {
        $rectangle = New-Object ExeRuntimeWindow+Rect
        [ExeRuntimeWindow]::GetWindowRect($window, [ref]$rectangle) | Out-Null
        $bitmap = [Drawing.Bitmap]::new($rectangle.Right - $rectangle.Left, $rectangle.Bottom - $rectangle.Top)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $dc = $graphics.GetHdc()
            try {
                if (-not [ExeRuntimeWindow]::PrintWindow($window, $dc, 2)) { throw 'Window capture failed.' }
            } finally { $graphics.ReleaseHdc($dc) }
            $bitmap.Save((Join-Path $ResultDirectory 'application-window.png'), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    } finally { [ExeRuntimeWindow]::SetThreadDpiAwarenessContext($oldDpiContext) | Out-Null }
    if ($CheckWorker) {
        $workerResult = Join-Path $ResultDirectory 'worker-result.json'
        $workerStatus = Join-Path $ResultDirectory 'worker-status.json'
        & (Join-Path $PSScriptRoot 'Test-PackagedWorker.ps1') `
            -WorkerPath (Join-Path $applicationDirectory 'ElevatedWorker\AdminConsoleFor1C.ElevatedWorker.exe') `
            -ServiceName ('AdminConsoleFor1C-PackageTest-' + [Guid]::NewGuid().ToString('N')) `
            -ResultPath $workerResult -StatusPath $workerStatus
        $status = Get-Content -LiteralPath $workerStatus -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($status.Error -or $status.ExitCode -ne 1) { throw "Unexpected worker result: $($status | ConvertTo-Json -Compress)" }
        $response = Get-Content -LiteralPath $workerResult -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($response.Success -or -not $response.ErrorMessage) { throw 'Expected the service-not-found result.' }
        $report.Worker = $true
    }
    $report.Passed = $true
} catch { $report.Error = $_.Exception.ToString() }
finally {
    if ($applicationProcess -and -not $applicationProcess.HasExited) {
        $applicationProcess.CloseMainWindow() | Out-Null
        if (-not $applicationProcess.WaitForExit(5000)) { Stop-Process -Id $applicationProcess.Id }
    }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ResultDirectory 'runtime-check.json') -Encoding UTF8
}
if (-not $report.Passed) { throw $report.Error }
Write-Output "Unpackaged application and bundled runtime verified: $ResultDirectory"
