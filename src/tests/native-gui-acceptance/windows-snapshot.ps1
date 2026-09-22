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
$script:nodeCount = 0
$script:truncated = $false
function Short-Text($value) {
    $text = [string]$value
    if ($text.Length -gt 300) { return $text.Substring(0,300) }
    return $text
}
function Read-Node($element, [int[]]$path, [int]$depth, [bool]$insideWeb, $nodes) {
    if ($script:nodeCount -ge $inputRecord.maxNodes -or $depth -gt $inputRecord.maxDepth -or $watch.ElapsedMilliseconds -gt 24000) {
        $script:truncated = $true
        return
    }
    $script:nodeCount++
    $current = $element.Current
    $role = $current.ControlType.ProgrammaticName
    $insideWeb = $insideWeb -or ($role -eq 'ControlType.Document')
    $password = $current.IsPassword
    $actions = @()
    if (!$password) {
        $actions = @($element.GetSupportedPatterns() | ForEach-Object { Short-Text $_.ProgrammaticName })
    }
    $name = if ($password) { '' } else { Short-Text $current.Name }
    $nodes.Add(@{path=@($path); role=$role; name=$name; text='';
        identifier=(Short-Text $current.AutomationId); enabled=$current.IsEnabled;
        insideWebContent=$insideWeb; password=$password; actions=@($actions)})
    $child = $walker.GetFirstChild($element)
    $index = 0
    while ($null -ne $child) {
        if ($script:nodeCount -ge $inputRecord.maxNodes) { $script:truncated = $true; break }
        Read-Node $child ($path + $index) ($depth+1) $insideWeb $nodes
        $child = $walker.GetNextSibling($child)
        $index++
    }
}
$outputWindows = [Collections.Generic.List[object]]::new()
if ($windows.Count -gt 8) { $script:truncated = $true }
for ($i=0; $i -lt [Math]::Min($windows.Count,8); $i++) {
    $window = $windows.Item($i)
    if ($window.Current.ProcessId -ne $targetPid) { throw 'WindowProcessMismatch' }
    $nodes = [Collections.Generic.List[object]]::new()
    $stage = 'read-tree'
    Read-Node $window @($i) 0 $false $nodes
    $outputWindows.Add(@{index=$i; name=(Short-Text $window.Current.Name);
        visible=(!$window.Current.IsOffscreen); nodes=@($nodes.ToArray())})
}
$stage = 'verify-process'
$after = Get-Process -Id $targetPid -ErrorAction Stop
if ($after.StartTime.ToUniversalTime().ToString('o') -ne $started -or
    ![string]::Equals($after.Path, $expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'ProcessChangedDuringProbe' }
@{backend='windows-uia'; readOnly=$true; enabled=$true; truncated=$script:truncated;
  process=@{pid=$targetPid; executable=$after.Path; started=$started};
  windows=@($outputWindows.ToArray()); elapsedMs=$watch.ElapsedMilliseconds} | ConvertTo-Json -Depth 60 -Compress
} catch {
    # No exception text, commands, field values or raw streams in evidence.
    @{backend='windows-uia'; readOnly=$true; enabled=$false; windows=@(); errorStage=$stage} | ConvertTo-Json -Compress
}
