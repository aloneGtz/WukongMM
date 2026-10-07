# UI test for the save editor: type several IDs / names into the "add item" ComboBox and record the preview
# line shown under it (ok / warning / blocked). Read-only: nothing is added or saved.
# Usage: powershell -File tools\ui-additem-test.ps1 -Exe <WukongMM.exe> -Out <txt (UTF-8)>
# NOTE: keep ASCII-only (Windows PowerShell 5.1).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Out
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$p = Start-Process -FilePath $Exe -PassThru
try {
    Start-Sleep -Seconds 5
    $p.Refresh()
    $root = $AE::FromHandle($p.MainWindowHandle)
    function ByType($t) { $root.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $t))) }
    (ByType $CT::TabItem).Item(1).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Seconds 2

    function TextNames { $s = @{}; foreach ($e in (ByType $CT::Text)) { $n = $e.Current.Name; if ($n) { $s[$n] = 1 } }; return $s }
    $base = TextNames
    $combos = ByType $CT::ComboBox
    $vp = $combos.Item($combos.Count - 1).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)  # add-item box is the last ComboBox

    # 3201 material, 4001 quest item, 2001 wine, 10101 equipment, 6001 seed, 1006 spark item, 99999 unknown, 'abc',
    # the name of 3201, and a name shared by recipe 1111 and elixir 2211 (must list both candidates)
    $gancao = [string]::new([char[]](0x7518, 0x8349))
    $baoming = [string]::new([char[]](0x4FDD, 0x547D, 0x4E39))
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($txt in @('3201', '4001', '2001', '10101', '6001', '1006', '99999', 'abc', $gancao, $baoming)) {
        $vp.SetValue($txt)
        Start-Sleep -Milliseconds 700
        $now = TextNames
        $lines.Add("INPUT [" + $txt + "]")
        foreach ($n in @($now.Keys | Where-Object { -not $base.ContainsKey($_) })) { $lines.Add("   " + $n) }
    }
    [System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding($false)))
}
finally {
    $p.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 1
    if (-not $p.HasExited) { $p.Kill() }
}
"done"
