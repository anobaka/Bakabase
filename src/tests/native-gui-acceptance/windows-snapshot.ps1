# Read-only UIA. Called only after probe.py's disposable hosted-runner guard.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$stage = 'initialize'
try {
$inputRecord = [Console]::In.ReadToEnd() | ConvertFrom-Json
$watch = [Diagnostics.Stopwatch]::StartNew()
$targetPid = [int]$inputRecord.pid
$stage = 'resolve-process'
$target = Get-Process -Id $targetPid -ErrorAction Stop
$expected = [IO.Path]::GetFullPath([string]$inputRecord.executable)
if (![string]::Equals($target.Path, $expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'ExecutableMismatch' }
$started = $target.StartTime.ToUniversalTime().ToString('o')
$stage = 'load-uia'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$condition = [System.Windows.Automation.PropertyCondition]::new(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $targetPid)
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$stage = 'enumerate-windows'
$windows = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
$walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
. (Join-Path $PSScriptRoot 'windows-tree.ps1')
$stage = 'read-tree'
$tree = Get-OwnedTree $windows $walker $inputRecord $watch
$stage = 'verify-process'
$after = Get-Process -Id $targetPid -ErrorAction Stop
if ($after.StartTime.ToUniversalTime().ToString('o') -ne $started -or
    ![string]::Equals($after.Path, $expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'ProcessChangedDuringProbe' }
@{backend='windows-uia'; readOnly=$true; enabled=$true; truncated=$tree.truncated;
  process=@{pid=$targetPid; executable=$after.Path; started=$started};
  windows=@($tree.windows); elapsedMs=$watch.ElapsedMilliseconds} | ConvertTo-Json -Depth 60 -Compress
} catch {
    # No exception text, commands, field values or raw streams in evidence.
    @{backend='windows-uia'; readOnly=$true; enabled=$false; windows=@(); errorStage=$stage} | ConvertTo-Json -Compress
}
