# Only Invoke/Value patterns on a freshly revalidated owned product control.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$stage='initialize'
try {
    $record=[Console]::In.ReadToEnd() | ConvertFrom-Json
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
    $path=@($record.selector.path)
    if($path.Count -lt 2 -or $path.Count -gt 42 -or $path[0] -lt 0 -or $path[0] -ge $windows.Count) {throw 'InvalidPath'}
    $element=$windows.Item([int]$path[0])
    if($element.Current.IsOffscreen -or $element.Current.ProcessId -ne $targetPid) {throw 'InvisibleWindow'}
    $walker=[System.Windows.Automation.TreeWalker]::RawViewWalker
    $insideWeb=$false
    $stage='resolve-control'
    for($i=1;$i -lt $path.Count;$i++) {
        if($path[$i] -lt 0) {throw 'InvalidPath'}
        $element=$walker.GetFirstChild($element)
        for($n=0;$n -lt $path[$i] -and $null -ne $element;$n++) {$element=$walker.GetNextSibling($element)}
        if($null -eq $element) {throw 'MissingControl'}
        $insideWeb=$insideWeb -or $element.Current.ControlType.ProgrammaticName -eq 'ControlType.Document'
    }
    $current=$element.Current
    $role=$current.ControlType.ProgrammaticName
    $stage='validate-control'
    if(!$insideWeb -or !$current.IsEnabled -or $current.IsOffscreen -or $current.IsPassword -or $role -ne $record.selector.role -or
        $current.Name -ne $record.selector.name -or $current.AutomationId -ne $record.selector.identifier) {throw 'ChangedControl'}
    if($role -notin @('ControlType.Button','ControlType.Hyperlink','ControlType.MenuItem','ControlType.CheckBox',
                     'ControlType.RadioButton','ControlType.Edit','ControlType.ComboBox')) {throw 'UnexpectedRole'}
    $after=Get-Process -Id $targetPid -ErrorAction Stop
    if($after.StartTime.ToUniversalTime().ToString('o') -ne $record.started -or
        ![string]::Equals($after.Path,$expected,[StringComparison]::OrdinalIgnoreCase)) {throw 'ProcessChanged'}
    $stage='perform-action'
    if($element.Current.IsOffscreen) {throw 'InvisibleControlBeforeAction'}
    if($record.operation -eq 'press') {
        $pattern=$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
    } elseif($record.operation -eq 'set' -and $role -eq 'ControlType.Edit') {
        $pattern=[System.Windows.Automation.ValuePattern]$element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        if($pattern.Current.IsReadOnly) {throw 'ReadOnlyControl'}
        $pattern.SetValue([string]$record.value)
    } else {throw 'UnsupportedAction'}
    @{performed=$true;operation=$record.operation} | ConvertTo-Json -Compress
} catch {
    @{performed=$false;errorStage=$stage} | ConvertTo-Json -Compress
}
