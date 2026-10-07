# Start WukongMM.exe, select a tab by index via UI Automation, save a screenshot, close.
# Usage: powershell -File tools\capture-tab.ps1 -Exe <WukongMM.exe> -Tab <0|1|2> -Out <png> [-Game <sandbox root>] [-Lang zh|en]
#   -Game: start in test mode (WukongMM.exe --game <root>), e.g. against a sandbox copy of the game folder.
#   -Lang: interface language for this run only (WukongMM.exe --lang en); the user's setting is not changed.
# The started process is captured by its id, so a manager the user already has open is left alone.
# NOTE: keep ASCII-only (Windows PowerShell 5.1).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][int]$Tab,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Game = '',
    [string]$Lang = ''
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$argList = @()
if ($Game) { $argList += @('--game', ('"' + $Game + '"')) }
if ($Lang) { $argList += @('--lang', $Lang) }
if ($argList.Count -gt 0) { $p = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru }
else { $p = Start-Process -FilePath $Exe -PassThru }
Start-Sleep -Seconds 5
$p.Refresh()
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
$tabs = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
$tabs[$Tab].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 2
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "capture-window.ps1") -Exe $Exe -Out $Out -ProcessId $p.Id
