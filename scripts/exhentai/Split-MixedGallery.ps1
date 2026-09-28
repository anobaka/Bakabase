#Requires -Version 5.1
<#
.SYNOPSIS
Preview or split a mixed ExHentai download directory by a marker in image page titles.

.DESCRIPTION
Only direct child image files whose base names end in _<digits>_p<page> are selected.
The digits are a local page-title marker, NOT an ExHentai gallery ID. No Bakabase
gallery ownership markers are created. The default is a read-only preview.

.EXAMPLE
.\Split-MixedGallery.ps1 -SourceDirectory 'D:\Downloads\[Misc] Example'

.EXAMPLE
.\Split-MixedGallery.ps1 -SourceDirectory 'D:\Downloads\[Misc] Example' -Apply

.EXAMPLE
.\Split-MixedGallery.ps1 -SourceDirectory 'D:\Downloads\[Misc] Example' -Apply -WhatIf
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $SourceDirectory,

    [switch] $Apply,

    # Must capture numeric groups named Marker and Page. Match the end of BaseName.
    [ValidateNotNullOrEmpty()]
    [string] $MarkerPattern = '_(?<Marker>\d{8,12})_p(?<Page>\d+)$'
)

$ErrorActionPreference = 'Stop'
$allowedExtensions = @('.jpg', '.jpeg', '.png', '.webp', '.gif', '.bmp', '.tif', '.tiff', '.avif')

function Get-PageMarker {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo] $File,
        [Parameter(Mandatory = $true)][regex] $Pattern
    )

    if ($allowedExtensions -notcontains $File.Extension.ToLowerInvariant()) { return $null }
    if (($File.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $null }

    $match = $Pattern.Match($File.BaseName)
    if (-not $match.Success) { return $null }

    $marker = $match.Groups['Marker'].Value
    if ($marker -notmatch '^\d{1,15}$') { return $null }
    [int] $page = 0
    if (-not [int]::TryParse($match.Groups['Page'].Value, [ref] $page)) { return $null }

    return [pscustomobject]@{ Marker = $marker; Page = $page }
}

try {
    $markerRegex = [regex]::new($MarkerPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
} catch {
    throw "Invalid MarkerPattern: $($_.Exception.Message)"
}
if ($markerRegex.GetGroupNames() -notcontains 'Marker' -or
    $markerRegex.GetGroupNames() -notcontains 'Page') {
    throw 'MarkerPattern must contain named groups Marker and Page.'
}

$source = Get-Item -LiteralPath $SourceDirectory -ErrorAction Stop
if ($source -isnot [System.IO.DirectoryInfo]) { throw 'SourceDirectory must be a directory.' }
if ($null -eq $source.Parent) { throw 'A filesystem root cannot be split into sibling directories.' }
if (($source.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'SourceDirectory must not be a symbolic link or junction.'
}
if ([System.IO.File]::Exists((Join-Path $source.FullName '.bakabase-exhentai-gallery.json'))) {
    throw 'SourceDirectory has a Bakabase gallery ownership marker; refusing to split an owned gallery.'
}

$plan = [System.Collections.Generic.List[object]]::new()
$unmatched = [System.Collections.Generic.List[object]]::new()
$conflicts = [System.Collections.Generic.List[string]]::new()

foreach ($file in (Get-ChildItem -LiteralPath $source.FullName -File -Force | Sort-Object Name)) {
    $info = Get-PageMarker -File $file -Pattern $markerRegex
    if ($null -eq $info) {
        $unmatched.Add($file.FullName)
        continue
    }

    # Keep this visibly distinct from the downloader's [g<gallery-id>] suffix.
    $destinationDirectory = Join-Path $source.Parent.FullName ("{0} [local-{1}]" -f $source.Name, $info.Marker)
    $plan.Add([pscustomobject]@{
        Marker = $info.Marker
        Page = $info.Page
        Source = $file.FullName
        DestinationDirectory = $destinationDirectory
        Destination = Join-Path $destinationDirectory $file.Name
    })
}

$groups = @($plan | Group-Object Marker | Sort-Object Name)
foreach ($group in $groups) {
    $marker = $group.Name
    $destinationDirectory = $group.Group[0].DestinationDirectory

    foreach ($duplicate in @($group.Group | Group-Object Page | Where-Object Count -gt 1)) {
        $conflicts.Add("Marker ${marker} has multiple source files for page p$($duplicate.Name).")
    }

    if ([System.IO.File]::Exists($destinationDirectory)) {
        $conflicts.Add("Destination is a file: $destinationDirectory")
        continue
    }
    if (-not [System.IO.Directory]::Exists($destinationDirectory)) { continue }

    $destinationInfo = Get-Item -LiteralPath $destinationDirectory
    if (($destinationInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        $conflicts.Add("Destination is a symbolic link or junction: $destinationDirectory")
        continue
    }

    if (@(Get-ChildItem -LiteralPath $destinationDirectory -Force).Count -gt 0) {
        $conflicts.Add("Destination is not empty and cannot be verified as this batch: $destinationDirectory")
    }
}

$mode = if ($Apply -and -not $WhatIfPreference) { 'APPLY' } else { 'DRY RUN (read-only)' }
Write-Host ("Mode: {0}; source: {1}" -f $mode, $source.FullName)
Write-Host ("Matched images: {0}; local marker groups: {1}; unmatched files: {2}; conflicts: {3}" -f `
    $plan.Count, $groups.Count, $unmatched.Count, $conflicts.Count)
foreach ($group in $groups) {
    Write-Host ("  {0}: {1} images -> {2}" -f $group.Name, $group.Count, $group.Group[0].DestinationDirectory)
}
foreach ($conflict in $conflicts) { Write-Warning $conflict }

if ($Apply -and -not $WhatIfPreference -and $conflicts.Count -gt 0) {
    throw 'No files were moved. Resolve every conflict or use the read-only preview to review them.'
}

$execute = $false
if ($Apply -and $conflicts.Count -eq 0 -and $plan.Count -gt 0) {
    $execute = $PSCmdlet.ShouldProcess($source.FullName,
        ("Move {0} images into {1} sibling directories" -f $plan.Count, $groups.Count))
}

if ($execute) {
    # Create destinations before moving any file. A later I/O failure may still leave a partial
    # move; nonempty destinations require manual review before the script can be rerun.
    foreach ($group in $groups) {
        $directory = $group.Group[0].DestinationDirectory
        [void] [System.IO.Directory]::CreateDirectory($directory)
        $current = Get-Item -LiteralPath $directory
        if (($current.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Destination became a symbolic link or junction: $directory"
        }
        if (@(Get-ChildItem -LiteralPath $directory -Force).Count -gt 0) {
            throw "Destination became nonempty: $directory"
        }
    }

    foreach ($item in $plan) {
        if (-not [System.IO.File]::Exists($item.Source)) {
            throw "Source disappeared after preview: $($item.Source)"
        }
        if ([System.IO.File]::Exists($item.Destination) -or
            [System.IO.Directory]::Exists($item.Destination)) {
            throw "Destination appeared after preview: $($item.Destination)"
        }
        [System.IO.File]::Move($item.Source, $item.Destination)
        [pscustomobject]@{
            Action = 'Moved'; Marker = $item.Marker; Page = $item.Page
            Source = $item.Source; Destination = $item.Destination
        }
    }
} else {
    $action = if ($conflicts.Count -gt 0) { 'Blocked' } else { 'WouldMove' }
    foreach ($item in $plan) {
        [pscustomobject]@{
            Action = $action; Marker = $item.Marker; Page = $item.Page
            Source = $item.Source; Destination = $item.Destination
        }
    }
}

foreach ($file in $unmatched) {
    [pscustomobject]@{
        Action = 'LeftUnchanged'; Marker = ''; Page = ''
        Source = $file; Destination = ''
    }
}
