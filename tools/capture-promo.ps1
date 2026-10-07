# Screenshots for the README / mod pages, taken against a demo game root made by "wmm demo" (no personal data):
#   <Out>\mods-<lang>.png  Mods tab with one mod selected (to show its details / conflicts)
#   <Out>\save-<lang>.png  Save editor tab
# Usage: powershell -File tools\capture-promo.ps1 -Exe <WukongMM.exe> -Game <demo root> -Lang zh|en -Out <folder> [-SelectMod <name>]
# NOTE: keep ASCII-only (Windows PowerShell 5.1). Non-ASCII mod names cannot be passed here; use the English demo or an index.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Game,
    [Parameter(Mandatory = $true)][string]$Lang,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$SelectRow = 1
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
function Cond($prop, $v) { New-Object System.Windows.Automation.PropertyCondition($prop, $v) }
function ByType($el, $t) { $el.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $t)) }
function Shot($name) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "capture-window.ps1") -Exe $Exe -Out (Join-Path $Out $name) -ProcessId $p.Id -Keep
}
New-Item -ItemType Directory -Force $Out | Out-Null
$p = Start-Process -FilePath $Exe -ArgumentList @('--game', ('"' + $Game + '"'), '--lang', $Lang) -PassThru
try {
    Start-Sleep -Seconds 5
    $p.Refresh()
    $root = $AE::FromHandle($p.MainWindowHandle)
    $grid = @(ByType $root $CT::DataGrid)[0]
    $rows = @(ByType $grid $CT::DataItem)
    if ($rows.Count -gt $SelectRow) { $rows[$SelectRow].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
    Start-Sleep -Seconds 1
    Shot ("mods-" + $Lang + ".png")
    @(ByType $root $CT::TabItem)[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2
    Shot ("save-" + $Lang + ".png")
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
}
