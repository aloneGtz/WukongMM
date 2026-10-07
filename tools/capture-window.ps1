# Start (or attach to) a process and save its main window as PNG. Used during development to check the WPF UI.
# Usage: powershell -File tools\capture-window.ps1 -Exe <path> -Out <png> [-WaitSeconds 4] [-Keep] [-ProcessId N] [-GameArg <root>]
#   -ProcessId: capture this process (e.g. one started by capture-tab.ps1). Without it a NEW process is always started,
#               so a manager the user has open is never captured or closed by this script.
#   -GameArg:   start the app in test mode against a sandbox game root (WukongMM.exe --game <root>).
# NOTE: keep this file ASCII-only (Windows PowerShell 5.1 reads BOM-less scripts as ANSI).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$WaitSeconds = 4,
    [switch]$Keep,
    [int]$ProcessId = 0,
    [string]$GameArg = ''
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[Win]::SetProcessDPIAware() | Out-Null
if ($ProcessId -ne 0) { $p = Get-Process -Id $ProcessId }
elseif ($GameArg) { $p = Start-Process -FilePath $Exe -ArgumentList @('--game', ('"' + $GameArg + '"')) -PassThru; Start-Sleep -Seconds $WaitSeconds }
else { $p = Start-Process -FilePath $Exe -PassThru; Start-Sleep -Seconds $WaitSeconds }
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Error "main window not found"; exit 1 }
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
$r = New-Object Win+RECT
[Win]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$hh = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Win]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output ("saved {0} {1}x{2}" -f $Out, $w, $hh)
if (-not $Keep) {
    $p.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 500
    if (-not $p.HasExited) { $p.Kill() }
}
