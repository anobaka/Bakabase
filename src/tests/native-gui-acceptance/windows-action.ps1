# Only observed patterns on a complete, freshly revalidated owned product tree.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$stage='initialize'
try {
    $record=[Console]::In.ReadToEnd() | ConvertFrom-Json
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $op=[string]$record.operation
    if($op -notin @('press','toggle','set','scroll') -or $record.selector.name -isnot [string] -or
        !$record.selector.name -or $record.selector.name.Length -gt 300) {throw 'InvalidOperation'}
    if($op -eq 'set' -and ($record.value -isnot [string] -or $record.value.Length -gt 4096)) {throw 'InvalidInput'}
    $targetPid=[int]$record.pid
    $stage='resolve-process'
    $process=Get-Process -Id $targetPid -ErrorAction Stop
    $expected=[IO.Path]::GetFullPath([string]$record.executable)
    if(![string]::Equals($process.Path,$expected,[StringComparison]::OrdinalIgnoreCase) -or
        $process.StartTime.ToUniversalTime().ToString('o') -ne $record.started) {throw 'ProcessChanged'}
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$targetPid)
    $windows=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)
    $walker=[System.Windows.Automation.TreeWalker]::RawViewWalker
    . (Join-Path $PSScriptRoot 'windows-tree.ps1')
    $stage='read-tree'
    $tree=Get-OwnedTree $windows $walker @{pid=$targetPid;maxNodes=1000;maxDepth=40;readBudgetMs=12000} $watch
    if($tree.truncated) {throw 'IncompleteTree'}
    $matches=@{}
    foreach($window in $tree.windows) {
        if(!$window.visible) {continue}
        foreach($node in $window.nodes) {
            if(!$node.insideWebContent -or $node.password -or $node.editableAncestor -or
                $node.role -ne $record.selector.role -or $node.name -ne $record.selector.name -or
                ($op -ne 'scroll' -and (!$node.visible -or !$node.enabled))) {continue}
            $key=($node.runtimeId -join ',')
            if(!$key -or $node.runtimeId.Count -gt 64) {throw 'InvalidRuntimeId'}
            if($matches.ContainsKey($key)) {
                foreach($field in @('role','name','identifier','password','editableAncestor','enabled','visible','valueSettable','scrollToVisible')) {
                    if($matches[$key][$field] -ne $node[$field]) {throw 'InconsistentRuntimeId'}
                }
            } else {$matches[$key]=$node}
        }
    }
    if($matches.Count -ne 1) {throw 'AmbiguousControl'}
    $observed=@($matches.Values)[0]
    $path=@($record.selector.path)
    if($path.Count -lt 2 -or $path.Count -gt 42 -or $path[0] -lt 0 -or $path[0] -ge $windows.Count -or
        @($path | Where-Object {($_ -isnot [int] -and $_ -isnot [long]) -or $_ -lt 0 -or $_ -ge 1000}).Count) {throw 'InvalidPath'}
    $element=$windows.Item([int]$path[0])
    if($element.Current.IsOffscreen -or $element.Current.ProcessId -ne $targetPid) {throw 'InvisibleWindow'}
    $ownedWindow=$element
    $insideWeb=$false
    $stage='resolve-control'
    for($i=1;$i -lt $path.Count;$i++) {
        if($watch.ElapsedMilliseconds -ge 12000) {throw 'ActionDeadline'}
        if($element.Current.IsPassword -or $element.Current.ControlType.ProgrammaticName -in @('ControlType.Edit','ControlType.ComboBox')) {throw 'EditableDescendant'}
        if($path[$i] -lt 0) {throw 'InvalidPath'}
        $element=$walker.GetFirstChild($element)
        for($n=0;$n -lt $path[$i] -and $null -ne $element;$n++) {
            if($watch.ElapsedMilliseconds -ge 12000) {throw 'ActionDeadline'}
            $element=$walker.GetNextSibling($element)
        }
        if($null -eq $element) {throw 'MissingControl'}
        $insideWeb=$insideWeb -or $element.Current.ControlType.ProgrammaticName -eq 'ControlType.Document'
    }
    $current=$element.Current
    $role=$current.ControlType.ProgrammaticName
    $stage='validate-control'
    [int[]]$expectedRuntimeId=@($record.selector.runtimeId)
    $held=$tree.elements[($path -join '/')]
    if($null -eq $held -or ![System.Windows.Automation.Automation]::Compare($expectedRuntimeId,[int[]]$observed.runtimeId) -or
        ![System.Windows.Automation.Automation]::Compare($expectedRuntimeId,[int[]]($held.GetRuntimeId()))) {throw 'ChangedObservedControl'}
    if($expectedRuntimeId.Count -lt 1 -or $expectedRuntimeId.Count -gt 64 -or
        ![System.Windows.Automation.Automation]::Compare($expectedRuntimeId,[int[]]($element.GetRuntimeId()))) {throw 'ChangedRuntimeId'}
    if(!$insideWeb -or $current.IsPassword -or ($op -ne 'scroll' -and (!$current.IsEnabled -or $current.IsOffscreen))) {throw 'PrivateOrInvisibleControl'}
    if($role -ne $record.selector.role -or
        $current.Name -ne $record.selector.name -or $current.AutomationId -ne $record.selector.identifier) {throw 'ChangedControl'}
    $roles=@('ControlType.Button','ControlType.Hyperlink','ControlType.MenuItem','ControlType.CheckBox',
             'ControlType.RadioButton','ControlType.Edit','ControlType.ComboBox')
    if($op -eq 'scroll') {$roles+=@('ControlType.Group','ControlType.Pane','ControlType.Text','ControlType.ListItem')}
    if($role -notin $roles) {throw 'UnexpectedRole'}
    $after=Get-Process -Id $targetPid -ErrorAction Stop
    if($after.StartTime.ToUniversalTime().ToString('o') -ne $record.started -or
        ![string]::Equals($after.Path,$expected,[StringComparison]::OrdinalIgnoreCase)) {throw 'ProcessChanged'}
    $stage='perform-action'
    if($element.Current.IsOffscreen) {if($op -ne 'scroll') {throw 'InvisibleControlBeforeAction'}}
    if(![System.Windows.Automation.Automation]::Compare($expectedRuntimeId,[int[]]($element.GetRuntimeId()))) {throw 'ChangedRuntimeIdBeforeAction'}
    if($watch.ElapsedMilliseconds -ge 12000 -or $element.Current.IsPassword -or
        ($op -ne 'scroll' -and !$element.Current.IsEnabled) -or
        $ownedWindow.Current.IsOffscreen -or $ownedWindow.Current.ProcessId -ne $targetPid) {throw 'ActionBoundaryChanged'}
    if($record.operation -eq 'press') {
        $pattern=$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
    } elseif($record.operation -eq 'toggle' -and $record.selector.operation -eq 'toggle' -and
             $role -in @('ControlType.Button','ControlType.CheckBox')) {
        # aria-pressed buttons in WebView2 expose TogglePattern, not Invoke.
        $pattern=$element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        ([System.Windows.Automation.TogglePattern]$pattern).Toggle()
    } elseif($record.operation -eq 'set' -and $role -eq 'ControlType.Edit') {
        if($observed.valueSettable -ne $true) {throw 'ValuePatternNotObserved'}
        $pattern=$element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $readOnly=$pattern.Current.IsReadOnly
        if($readOnly -isnot [bool] -or $readOnly) {throw 'ReadOnlyControl'}
        $pattern.SetValue([string]$record.value)
    } elseif($record.operation -eq 'scroll') {
        if($observed.scrollToVisible -ne $true) {throw 'ScrollPatternNotObserved'}
        $pattern=$element.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern)
        $pattern.ScrollIntoView()
    } else {throw 'UnsupportedAction'}
    @{performed=$true;operation=$record.operation} | ConvertTo-Json -Compress
} catch {
    @{performed=$false;errorStage=$stage} | ConvertTo-Json -Compress
}
