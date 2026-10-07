# UI test that really WRITES a save -- only against a SANDBOX game root (--game <fake root>, refuses a real game folder).
# Steps: record the page hints (e.g. a level outside the level table), try level 401 (must show a red error),
# change item 3201 to 50 through the add box, press Save, record the confirm dialog, press YES, record the result.
# Then try lowering money to 2,500,000,000 (must be rejected) and to 1,000,000,000 (confirm dialog, press NO).
# Usage: powershell -File tools\ui-sandbox-save-test.ps1 -Exe <WukongMM.exe> -Game <sandbox root> -Out <txt (UTF-8)>
# NOTE: keep ASCII-only (Windows PowerShell 5.1).
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
function U([int[]]$codes) { [string]::new([char[]]$codes) }
function Cond($prop, $v) { New-Object System.Windows.Automation.PropertyCondition($prop, $v) }
function ByType($el, $t) { $el.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $t)) }
function SetVal($el, $v) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }
function GetVal($el) { try { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { '' } }
function Press($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Dialog($root) { $root.FindFirst($TS::Children, (Cond $AE::ControlTypeProperty $CT::Window)) }
function DlgText($dlg) { $t = $dlg.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty '65535')); if ($t) { $t.Current.Name } else { '?' } }
Add-Type -Name W -Namespace U -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);'
function DlgClick($dlg, $id) {
    for ($i = 0; $i -lt 5; $i++) {
        foreach ($k in @($id, '1', '2')) {
            $b = $dlg.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty $k))
            if ($b) {
                # Win32 MessageBox buttons: post BM_CLICK to the HWND; the app's own WPF dialog: Invoke pattern
                if ($b.Current.NativeWindowHandle -ne 0) { [U.W]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
                else { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
                for ($w = 0; $w -lt 15 -and (Dialog $root); $w++) { Start-Sleep -Milliseconds 200 }
                return
            }
        }
        Start-Sleep -Milliseconds 300
    }
    [U.W]::PostMessage([IntPtr]$dlg.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
}
# press a button and handle up to two dialogs in a row with the given answers
function PressAndAnswer($btn, $label, $answers) {
    Press $btn
    foreach ($a in $answers) {
        Start-Sleep -Milliseconds 1500
        $dlg = Dialog $root
        if (-not $dlg) { $lines.Add($label + ": no dialog"); return }
        $lines.Add($label + " DIALOG: " + (DlgText $dlg) + "  -> answer " + $a)
        DlgClick $dlg $a
    }
}

$p = Start-Process -FilePath $Exe -ArgumentList @('--game', ('"' + $Game + '"')) -PassThru
try {
    Start-Sleep -Seconds 5
    $p.Refresh()
    $root = $AE::FromHandle($p.MainWindowHandle)
    @(ByType $root $CT::TabItem)[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2
    $grid = @(ByType $root $CT::DataGrid)[0]
    function Texts($label) {
        $g = @{}; foreach ($e in (ByType $grid $CT::Text)) { $g[$e.Current.Name] = 1 }
        $lines.Add($label)
        foreach ($e in (ByType $root $CT::Text)) { $n = $e.Current.Name; if ($n -and -not $g.ContainsKey($n) -and $n.Length -gt 12) { $lines.Add("   " + $n) } }
    }
    $edits = @(ByType $root $CT::Edit)
    $level = $edits[0]; $money = $edits[1]
    $countBox = $null
    foreach ($e in $edits) { if ((GetVal $e) -eq '1') { $countBox = $e } }
    $combos = @(ByType $root $CT::ComboBox)
    $addBox = $combos[$combos.Count - 1]
    $addBtn = $null; $saveBtn = $null
    foreach ($b in (ByType $root $CT::Button)) {
        if ($b.Current.Name -eq (U 0x6DFB, 0x52A0)) { $addBtn = $b }
        if ($b.Current.Name -eq (U 0x4FDD, 0x5B58, 0x5230, 0x5B58, 0x6863)) { $saveBtn = $b }
    }
    $lines.Add("loaded: level=[" + (GetVal $level) + "] money=[" + (GetVal $money) + "] save button enabled=" + $saveBtn.Current.IsEnabled)
    Texts "TEXTS ON LOAD:"

    SetVal $level '401'
    Start-Sleep -Milliseconds 600
    Texts "TEXTS WITH LEVEL 401:"
    SetVal $level '400'
    Start-Sleep -Milliseconds 600

    SetVal $addBox '3201'
    SetVal $countBox '50'
    Start-Sleep -Milliseconds 300
    PressAndAnswer $addBtn "ADD 3201 x50" @('6')
    $lines.Add("save button enabled=" + $saveBtn.Current.IsEnabled)
    PressAndAnswer $saveBtn "SAVE (item only)" @('6', '1')                     # YES -> sandbox written; then OK on the result box
    Start-Sleep -Seconds 2
    $lines.Add("after save: level=[" + (GetVal $level) + "] money=[" + (GetVal $money) + "]")

    SetVal $money '2500000000'
    Start-Sleep -Milliseconds 300
    PressAndAnswer $saveBtn "SAVE (money 2.5e9)" @('1')
    SetVal $money '1000000000'
    Start-Sleep -Milliseconds 300
    PressAndAnswer $saveBtn "SAVE (money 1e9)" @('7')                          # NO
}
catch { $lines.Add("ERROR: " + $_) }
finally {
    Start-Sleep -Seconds 1
    if (-not $p.HasExited) { $p.Kill() }
    [System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding($false)))
}
"done"
