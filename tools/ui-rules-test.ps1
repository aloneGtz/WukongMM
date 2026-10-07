# UI test for the save editor rules added in session 3: pending (not yet saved) rows, same-name items,
# renaming an item keeps the add-item box. Runs the app in test mode against a SANDBOX game root (--game),
# so settings / item names are not written. The Save button is never pressed.
# Usage: powershell -File tools\ui-rules-test.ps1 -Exe <WukongMM.exe> -Game <sandbox root> -Out <txt (UTF-8)>
# NOTE: keep ASCII-only (Windows PowerShell 5.1).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Game,
    [Parameter(Mandatory = $true)][string]$Out
)
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
# Win32 MessageBox buttons: post BM_CLICK (6 = Yes, 7 = No, 1/2 = OK); an OK-only box is closed with WM_CLOSE if no button is found
function DlgClick($dlg, $id) {
    for ($i = 0; $i -lt 5; $i++) {
        foreach ($k in @($id, '1', '2')) {
            $b = $dlg.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty $k))
            if ($b) {
                # Win32 MessageBox buttons: post BM_CLICK to the HWND; the app's own WPF dialog: Invoke pattern
                if ($b.Current.NativeWindowHandle -ne 0) { [U.W]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
                else { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
                for ($w = 0; $w -lt 15 -and (Dialog $root); $w++) { Start-Sleep -Milliseconds 200 }  # wait until it is gone
                return
            }
        }
        Start-Sleep -Milliseconds 300
    }
    [U.W]::PostMessage([IntPtr]$dlg.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
}

$p = Start-Process -FilePath $Exe -ArgumentList @('--game', ('"' + $Game + '"')) -PassThru
try {
    Start-Sleep -Seconds 5
    $p.Refresh()
    $root = $AE::FromHandle($p.MainWindowHandle)
    (ByType $root $CT::TabItem).Item(1).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2

    $grid = @(ByType $root $CT::DataGrid)[0]
    # visible texts outside the item grid (preview line, status bar, hints)
    function TextNames {
        $g = @{}; foreach ($e in (ByType $grid $CT::Text)) { $g[$e.Current.Name] = 1 }
        $s = @{}; foreach ($e in (ByType $root $CT::Text)) { $n = $e.Current.Name; if ($n -and -not $g.ContainsKey($n)) { $s[$n] = 1 } }
        return $s
    }
    $base = TextNames
    function Snap($label) {
        Start-Sleep -Milliseconds 800
        $now = TextNames
        $lines.Add($label)
        foreach ($n in @($now.Keys | Where-Object { -not $base.ContainsKey($_) })) { $lines.Add("   " + $n) }
    }

    $combos = @(ByType $root $CT::ComboBox)
    $addBox = $combos[$combos.Count - 1]                                          # add-item box is the last ComboBox
    $edits = ByType $root $CT::Edit
    $countBox = $null; $filterBox = $null
    $filterTip = (U 0x6309) + ' ID / ' + (U 0x540D, 0x79F0) + ' / ' + (U 0x7C7B, 0x522B, 0x7B5B, 0x9009)
    foreach ($e in $edits) {
        if ((GetVal $e) -eq '1') { $countBox = $e }                              # add-item count box (default 1)
        if ($e.Current.HelpText -eq $filterTip) { $filterBox = $e }              # item filter box (tooltip)
    }
    $addBtn = $null
    foreach ($b in (ByType $root $CT::Button)) { if ($b.Current.Name -eq (U 0x6DFB, 0x52A0)) { $addBtn = $b } }
    if (-not ($countBox -and $filterBox -and $addBtn)) { throw "controls not found" }

    function Preview($txt) { SetVal $addBox $txt; Snap ("PREVIEW [" + $txt + "]") }
    function AddItem($txt, $count, $answer) {
        SetVal $addBox $txt
        SetVal $countBox $count
        Start-Sleep -Milliseconds 300
        Press $addBtn
        Start-Sleep -Milliseconds 1500
        $dlg = Dialog $root
        if ($dlg) {
            $lines.Add("ADD [" + $txt + " x" + $count + "] DIALOG: " + (DlgText $dlg) + "  -> answer " + $answer)
            DlgClick $dlg $answer
            Start-Sleep -Seconds 1
        } else { $lines.Add("ADD [" + $txt + " x" + $count + "] no dialog") }
        Snap "   after:"
    }
    # first candidate whose preview says it is not in the bag yet ("will add", or a warning without "already in bag")
    $willAdd = U 0x5C06, 0x6DFB, 0x52A0
    $inBag = U 0x80CC, 0x5305, 0x91CC, 0x5DF2, 0x6709
    function FirstNotInBag($cands) {
        foreach ($c in $cands) {
            SetVal $addBox $c
            Start-Sleep -Milliseconds 600
            foreach ($n in (TextNames).Keys) {
                if ($n.StartsWith($willAdd) -or ($n.StartsWith([string][char]0x26A0) -and -not $n.Contains($inBag))) { return $c }
            }
        }
        return $null
    }
    # candidates: every material (ItemType 3) / seed (ItemType 19) in the sandbox's exported item table
    $table = Get-Content (Join-Path $Game 'b1\ModManager\GameData\items.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $mat = FirstNotInBag @($table.items | Where-Object { $_.itemType -eq 3 } | ForEach-Object { [string]$_.id })
    $seed = FirstNotInBag @($table.items | Where-Object { $_.itemType -eq 19 } | ForEach-Object { [string]$_.id })
    if (-not ($mat -and $seed)) { throw "no material / seed missing from the bag" }
    $lines.Add("material not in bag: " + $mat + " ; seed not in bag: " + $seed)

    # 1) pending row: add, preview again, count 0 is rejected (with the "set the grid count to 0" hint), change the count
    AddItem $mat '1' '6'
    Preview $mat
    AddItem $mat '0' '2'
    AddItem $mat '3' '6'
    # 2) seed: warning on add, and again when changing the pending row's count (answer NO the second time)
    AddItem $seed '1' '6'
    Preview $seed
    AddItem $seed '2' '7'
    # 3) same-name items: elixir 2211 and recipe 1111 must mention each other
    Preview '2211'
    Preview '1111'
    # 4) select 3202 in the add box by its display text, rename row 3202 in the grid, the add box must follow
    SetVal $filterBox '3202'
    Start-Sleep -Milliseconds 800
    $gp = $grid.GetCurrentPattern([System.Windows.Automation.GridPattern]::Pattern)
    $nameCell = $gp.GetItem(0, 1)
    $origName = GetVal $nameCell
    $disp = $origName + (U 0xFF08) + '3202' + (U 0xFF0C) + (GetVal $gp.GetItem(0, 2)) + (U 0xFF09)
    SetVal $addBox $disp
    Start-Sleep -Milliseconds 800
    Snap ("SELECTED [" + $disp + "] box=[" + (GetVal $addBox) + "]")
    SetVal $nameCell 'TESTNAME'
    Start-Sleep -Milliseconds 800
    Snap ("RENAMED -> TESTNAME: box=[" + (GetVal $addBox) + "] cell=[" + (GetVal $nameCell) + "]")
    SetVal $nameCell ''
    Start-Sleep -Milliseconds 800
    Snap ("RENAMED -> empty: box=[" + (GetVal $addBox) + "] cell=[" + (GetVal $nameCell) + "] (expect game name " + $origName + ")")
    # 5) partial text with no selection survives a rename too
    SetVal $addBox '3202'
    SetVal $nameCell 'TESTNAME2'
    Start-Sleep -Milliseconds 800
    Snap ("RENAMED with typed id: box=[" + (GetVal $addBox) + "]")
    SetVal $nameCell ''
}
catch { $lines.Add("ERROR: " + $_) }
finally {
    Start-Sleep -Seconds 1
    if (-not $p.HasExited) { $p.Kill() }
    [System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding($false)))
}
"done"
