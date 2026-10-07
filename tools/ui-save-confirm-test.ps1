# UI test for the save editor: set level 95, add item 3202 x1000 (exceeds carry max 999), press Save,
# record both confirm dialogs, then press NO on the save dialog -- the save file is never written.
# Runs in test mode against a SANDBOX (tools\make-sandbox.ps1) since saving is enabled again; refuses a real game folder.
# Usage: powershell -File tools\ui-save-confirm-test.ps1 -Exe <WukongMM.exe> -Game <sandbox root> -Out <txt (UTF-8)>
# NOTE: keep ASCII-only (Windows PowerShell 5.1). WPF button Invoke is asynchronous, so modal MessageBoxes do not block.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Game,
    [Parameter(Mandatory = $true)][string]$Out
)
if (Test-Path (Join-Path $Game 'b1\Content')) { throw "$Game looks like a real game folder (b1\Content exists); use a sandbox" }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$lines = New-Object System.Collections.Generic.List[string]
function ByType($el, $t) { $el.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $t))) }
function SetVal($el, $v) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }
function GetVal($el) { try { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { '' } }
function Press($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Dialog($root) { $root.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window))) }
function DlgItem($dlg, $id) { $dlg.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id))) }
Add-Type -Name W -Namespace U -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);'
# Buttons keep the Win32 MessageBox ids (6 = Yes, 7 = No). Win32 buttons: post BM_CLICK to the HWND; the app's WPF dialog: Invoke.
function DlgClick($dlg, $id) {
    $b = DlgItem $dlg $id
    if ($null -eq $b) { throw "dialog button $id not found" }
    if ($b.Current.NativeWindowHandle -ne 0) { [U.W]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
    else { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}

$p = Start-Process -FilePath $Exe -ArgumentList @('--game', ('"' + $Game + '"'), '--lang', 'zh') -PassThru
try {
    Start-Sleep -Seconds 5
    $p.Refresh()
    $root = $AE::FromHandle($p.MainWindowHandle)
    (ByType $root $CT::TabItem).Item(1).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2

    $edits = ByType $root $CT::Edit
    SetVal $edits.Item(0) '95'                                                   # level is the first TextBox
    Start-Sleep -Milliseconds 300
    $countBox = $null
    foreach ($e in $edits) { if ((GetVal $e) -eq '1') { $countBox = $e } }      # add-item count box (default 1)
    $combos = ByType $root $CT::ComboBox
    SetVal $combos.Item($combos.Count - 1) '3202'                                # add-item box is the last ComboBox
    SetVal $countBox '1000'
    Start-Sleep -Milliseconds 300

    $addName = [string]::new([char[]](0x6DFB, 0x52A0))                           # button text "add"
    $saveName = [string]::new([char[]](0x4FDD, 0x5B58, 0x5230, 0x5B58, 0x6863)) # button text "save to archive"
    $addBtn = $null; $saveBtn = $null
    foreach ($b in (ByType $root $CT::Button)) {
        if ($b.Current.Name -eq $addName) { $addBtn = $b }
        if ($b.Current.Name -eq $saveName) { $saveBtn = $b }
    }

    Press $addBtn
    Start-Sleep -Seconds 2
    $dlg = Dialog $root
    if ($dlg) {
        $lines.Add("ADD-DIALOG: " + (DlgItem $dlg '65535').Current.Name)
        DlgClick $dlg '6'                                                        # YES: only edits the in-memory table
        Start-Sleep -Seconds 1
    } else { $lines.Add("ADD: no dialog") }

    Press $saveBtn
    Start-Sleep -Seconds 2
    $dlg = Dialog $root
    if ($dlg) {
        $lines.Add("SAVE-DIALOG: " + (DlgItem $dlg '65535').Current.Name)
        DlgClick $dlg '7'                                                        # NO: never write the save
        $lines.Add("pressed NO")
    } else { $lines.Add("SAVE: no dialog") }
}
catch { $lines.Add("ERROR: " + $_) }
finally {
    Start-Sleep -Seconds 1
    if (-not $p.HasExited) { $p.Kill() }
    [System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding($false)))
}
"done"
