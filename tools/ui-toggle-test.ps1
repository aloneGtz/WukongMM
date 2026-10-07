# UI test: start WukongMM.exe, toggle every mod checkbox in the Mods page via UI Automation,
# verify through the file system that the mod moved, then toggle back.
# Usage: powershell -File tools\ui-toggle-test.ps1 -Exe <WukongMM.exe> -B1 <game>\b1 [-Shot <png>]
# NOTE: keep ASCII-only (Windows PowerShell 5.1).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$B1,
    [string]$Shot = ""
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$ErrorActionPreference = "Stop"

function Snapshot {
    $live = @()
    $live += Get-ChildItem -Recurse -File (Join-Path $B1 "Content\Paks\~mods") -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
    $live += Get-ChildItem -Directory (Join-Path $B1 "Binaries\Win64\CSharpLoader\Mods") -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
    $dis = @(Get-ChildItem -Recurse -File (Join-Path $B1 "ModManager\Disabled") -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    return @{ Live = ($live | Sort-Object) -join ","; Disabled = ($dis | Sort-Object) -join "," }
}

$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 5
$p.Refresh()
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
$boxes = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Output ("checkboxes found: {0}" -f $boxes.Count)
$failures = 0
$before = Snapshot
Write-Output ("before: live=[{0}] disabled=[{1}]" -f $before.Live, $before.Disabled)

function Box($index) {
    # re-query every time: rows may be regenerated after a toggle
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    return $all.Item($index).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
}

for ($i = 0; $i -lt $boxes.Count; $i++) {
    $state0 = (Box $i).Current.ToggleState
    (Box $i).Toggle()
    Start-Sleep -Milliseconds 1500
    $mid = Snapshot
    $state1 = (Box $i).Current.ToggleState
    if ($Shot -and $i -eq 0) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "capture-window.ps1") -Exe $Exe -Out $Shot -Keep | Out-Null
    }
    (Box $i).Toggle()
    Start-Sleep -Milliseconds 1500
    $after = Snapshot
    $state2 = (Box $i).Current.ToggleState
    $moved = ($mid.Disabled -ne $before.Disabled) -and ($mid.Live -ne $before.Live)
    $restored = ($after.Live -eq $before.Live) -and ($after.Disabled -eq $before.Disabled)
    $ok = $moved -and $restored -and ($state1 -ne $state0) -and ($state2 -eq $state0)
    if (-not $ok) { $failures++ }
    Write-Output ("[{0}] box #{1}: state {2}->{3}->{4}; disabled-store during toggle=[{5}]; restored={6}" -f ($(if ($ok) { "PASS" } else { "FAIL" })), $i, $state0, $state1, $state2, $mid.Disabled, $restored)
}

$p.CloseMainWindow() | Out-Null
Start-Sleep -Milliseconds 800
if (-not $p.HasExited) { $p.Kill() }
Write-Output ("failures: {0}" -f $failures)
exit $failures
