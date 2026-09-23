# Shared bounded UIA RawView reader. The caller validates the exact owned
# process/window before use; this function never performs an action or reads a value.
function Get-OwnedTree($windows, $walker, $inputRecord, $watch) {
    if($inputRecord.maxNodes -ne 1000 -or $inputRecord.maxDepth -ne 40 -or
        $inputRecord.readBudgetMs -le 0 -or $inputRecord.readBudgetMs -gt 24000) {throw 'InvalidTreeBudget'}
    $state=@{count=0;truncated=$false;elements=@{}}
    function Short-Text($value) {
        $text=[string]$value
        if($text.Length -gt 300) {return $text.Substring(0,300)}
        return $text
    }
    function Read-Node($element,[int[]]$path,[int]$depth,[bool]$insideWeb,[bool]$editableAncestor,$nodes) {
        if($state.count -ge $inputRecord.maxNodes -or $depth -gt $inputRecord.maxDepth -or
            $watch.ElapsedMilliseconds -ge $inputRecord.readBudgetMs) {$state.truncated=$true;return}
        $state.count++
        $current=$element.Current
        $role=$current.ControlType.ProgrammaticName
        $insideWeb=$insideWeb -or $role -eq 'ControlType.Document'
        $password=$current.IsPassword
        $private=$password -or $editableAncestor
        $actions=@();$name='';$identifier='';$settable=$null
        if(!$private) {
            $actions=@($element.GetSupportedPatterns() | ForEach-Object {Short-Text $_.ProgrammaticName})
            $name=Short-Text $current.Name
            $identifier=Short-Text $current.AutomationId
            if($role -eq 'ControlType.Edit' -and $actions -contains 'ValuePatternIdentifiers.Pattern') {
                $pattern=$element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
                $readOnly=$pattern.Current.IsReadOnly # Never access Current.Value.
                if($readOnly -isnot [bool]) {throw 'UnknownValueSettable'}
                $settable=!$readOnly
            }
        }
        $nodes.Add(@{path=@($path);role=$role;name=$name;text='';identifier=$identifier;
            runtimeId=@($element.GetRuntimeId());enabled=(!$private -and $current.IsEnabled);
            visible=(!$private -and !$current.IsOffscreen);insideWebContent=$insideWeb;
            editableAncestor=$editableAncestor;password=$password;actions=@($actions);valueSettable=$settable;
            scrollToVisible=($actions -contains 'ScrollItemPatternIdentifiers.Pattern')})
        $state.elements[($path -join '/')]= $element
        $child=$walker.GetFirstChild($element);$index=0
        while($null -ne $child) {
            if($state.count -ge $inputRecord.maxNodes -or $watch.ElapsedMilliseconds -ge $inputRecord.readBudgetMs) {
                $state.truncated=$true;break
            }
            Read-Node $child ($path+$index) ($depth+1) $insideWeb ($private -or $role -in @('ControlType.Edit','ControlType.ComboBox')) $nodes
            $child=$walker.GetNextSibling($child);$index++
        }
    }
    $outputWindows=[Collections.Generic.List[object]]::new()
    if($windows.Count -gt 8) {$state.truncated=$true}
    for($i=0;$i -lt [Math]::Min($windows.Count,8);$i++) {
        $window=$windows.Item($i)
        if($window.Current.ProcessId -ne $inputRecord.pid) {throw 'WindowProcessMismatch'}
        $nodes=[Collections.Generic.List[object]]::new()
        Read-Node $window @($i) 0 $false $false $nodes
        $outputWindows.Add(@{index=$i;name=(Short-Text $window.Current.Name);visible=(!$window.Current.IsOffscreen);
            nodes=@($nodes.ToArray())})
    }
    return @{windows=@($outputWindows.ToArray());truncated=$state.truncated;elements=$state.elements}
}
