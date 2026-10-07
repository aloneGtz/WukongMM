# UI test (read-only, test mode against a sandbox): language toggle at runtime + item category filter + editable scope.
# Steps: start in Chinese on the save page; list the category filter entries; pick each category and count the grid rows;
# type a hidden item (3304, Bodhisattva's Left Hand: exchange-only) into the add box and record the preview (must be rejected);
# press the Language button; record tab headers, the category entries, the first grid row and the preview in English;
# press the Language button again (back to Chinese). Nothing is saved.
# Usage: powershell -File tools\ui-lang-filter-test.ps1 -Exe <WukongMM.exe> -Game <sandbox root> -Out <txt (UTF-8)>
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
function Cond($prop, $v) { New-Object System.Windows.Automation.PropertyCondition($prop, $v) }
function ByType($el, $t) { $el.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $t)) }
function ByName($el, $n) { $el.FindFirst($TS::Descendants, (Cond $AE::NameProperty $n)) }
function SetVal($el, $v) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }
function Press($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Rows() { @(ByType $grid $CT::DataItem).Count }
function ComboItems($combo) {
    $ec = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand(); Start-Sleep -Milliseconds 400
    $items = @($combo.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::ListItem)))
    $ec.Collapse(); Start-Sleep -Milliseconds 200
    return $items
}
function SelectComboIndex($combo, $i) {
    $items = ComboItems $combo
    $ec = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand(); Start-Sleep -Milliseconds 300
    $items = @($combo.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::ListItem)))
    $items[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $ec.Collapse(); Start-Sleep -Milliseconds 500
}
function Preview() {
    foreach ($e in (ByType $root $CT::Text)) { $n = $e.Current.Name; if ($n -and ($n.StartsWith([string][char]0x2717) -or $n.StartsWith([string][char]0x26A0))) { return $n } }
    return '(no preview)'
}

$p = Start-Process -FilePath $Exe -ArgumentList @('--game', ('"' + $Game + '"'), '--lang', 'zh') -PassThru
try {
    Start-Sleep -Seconds 5
    $p.Refresh()
    $root = $AE::FromHandle($p.MainWindowHandle)
    @(ByType $root $CT::TabItem)[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2
    $grid = @(ByType $root $CT::DataGrid)[0]
    $cat = ByName $root 'Category'
    $combos = @(ByType $root $CT::ComboBox)
    $addBox = $combos[$combos.Count - 1]

    $entries = @(ComboItems $cat)
    $lines.Add("ZH categories: " + (($entries | ForEach-Object { $_.Current.Name }) -join ' | '))
    for ($i = 0; $i -lt $entries.Count; $i++) {
        SelectComboIndex $cat $i
        $lines.Add("  category #" + $i + " -> rows " + (Rows))
    }
    SelectComboIndex $cat 3
    $lines.Add("ZH selected #3, rows " + (Rows))

    SetVal $addBox '3304'
    Start-Sleep -Milliseconds 600
    $lines.Add("ZH add 3304 preview: " + (Preview))

    Press (ByName $root 'Language')
    Start-Sleep -Seconds 2
    $lines.Add("EN tabs: " + ((@(ByType $root $CT::TabItem) | ForEach-Object { $_.Current.Name }) -join ' | '))
    $lines.Add("EN categories: " + ((@(ComboItems $cat) | ForEach-Object { $_.Current.Name }) -join ' | '))
    $lines.Add("EN rows after switch (filter kept?) " + (Rows))
    $first = @(ByType $grid $CT::DataItem)[0]
    if ($first) { $lines.Add("EN first row: " + ((@(ByType $first $CT::Text) | ForEach-Object { $_.Current.Name }) -join ' | ')) }
    $lines.Add("EN add box text: " + $addBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)
    $lines.Add("EN preview: " + (Preview))
    $lines.Add("EN language button now: " + (ByName $root 'Language').Current.Name)

    Press (ByName $root 'Language')
    Start-Sleep -Seconds 2
    $lines.Add("back to ZH tabs: " + ((@(ByType $root $CT::TabItem) | ForEach-Object { $_.Current.Name }) -join ' | '))
}
catch { $lines.Add("ERROR: " + $_) }
finally {
    Start-Sleep -Seconds 1
    if (-not $p.HasExited) { $p.Kill() }
    [System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding($false)))
}
"done"
