#Requires -Version 5.1
<#
  Windows local upgrade test. Verifies %LocalAppData%\Bakabase survives an upgrade
  simulated as a Velopack-style atomic swap of current/.

  Usage:  .\run-windows.ps1 [-Scenario B|C|D]
#>

[CmdletBinding()]
param(
  [ValidateSet("B","C","D")]
  [string] $Scenario = "B"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot  = Resolve-Path (Join-Path $ScriptDir "..\..\..")
$WorkBase  = Join-Path $ScriptDir "work"
New-Item -ItemType Directory -Force -Path $WorkBase | Out-Null

$RunId   = "{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID
$RunDir  = Join-Path $WorkBase "windows-$Scenario-$RunId"
New-Item -ItemType Directory -Force -Path $RunDir | Out-Null

# Use a per-run scratch %LocalAppData% so we don't pollute the developer's machine.
$ScratchLocalAppData = Join-Path $RunDir "LocalAppData"
New-Item -ItemType Directory -Force -Path $ScratchLocalAppData | Out-Null

$InstallRoot = Join-Path $RunDir "install"
New-Item -ItemType Directory -Force -Path (Join-Path $InstallRoot "current") | Out-Null

$VersionA = "99.0.0-test-{0}" -f (Get-Date -Format "HHmmss")
$VersionB = "99.0.1-test-{0}" -f (Get-Date -Format "HHmmss")
$PublishA = Join-Path $RunDir "publish-A"
$PublishB = Join-Path $RunDir "publish-B"

function Publish-App {
  param([string]$Version, [string]$Out)
  Write-Host "==> Publishing version=$Version → $Out" -ForegroundColor Cyan
  if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
  New-Item -ItemType Directory -Force -Path $Out | Out-Null
  $assemblyVersion = ($Version -split "-")[0]
  & dotnet publish (Join-Path $RepoRoot "src\apps\Bakabase\Bakabase.csproj") `
    -p:RuntimeMode=WINFORMS `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    --self-contained -r win-x64 `
    -o $Out
  if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
  $webDir = Join-Path $Out "web"
  New-Item -ItemType Directory -Force -Path $webDir | Out-Null
  Set-Content (Join-Path $webDir "index.html") "<!doctype html><title>upgrade-test</title>"
}

function Seed-Fixture {
  param([string]$AppData)
  New-Item -ItemType Directory -Force -Path (Join-Path $AppData "data\covers") | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $AppData "configs") | Out-Null
  Set-Content -Path (Join-Path $AppData "data\covers\cover-a.bin") -Value "fixture-A"
  $bytes = New-Object byte[] (4 * 1024)
  [System.IO.File]::WriteAllBytes((Join-Path $AppData "data\covers\cover-b.bin"), $bytes)
  Set-Content -Path (Join-Path $AppData "configs\test.json") -Value '{"version":"test","key":"value"}'
  Set-Content -Path (Join-Path $AppData "sentinel.txt") -Value "sentinel"
  $rand = New-Object byte[] (8 * 1024)
  (New-Object System.Random).NextBytes($rand)
  [System.IO.File]::WriteAllBytes((Join-Path $AppData "bakabase_insideworld.db"), $rand)
}

function Capture-Manifest {
  param([string]$Dir, [string]$OutFile)
  if (-not (Test-Path $Dir)) { Set-Content $OutFile ""; return }
  $entries = Get-ChildItem -Path $Dir -Recurse -File | Sort-Object FullName
  $lines = foreach ($f in $entries) {
    $rel  = (Resolve-Path -Relative -Path $f.FullName).TrimStart(".\")
    $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
    "{0}`t{1}`t{2}" -f $hash, $rel, $f.Length
  }
  Set-Content -Path $OutFile -Value $lines
}

function Simulate-VelopackSwap {
  param([string]$CurrentDir, [string]$NewPublish)
  Write-Host "==> Simulating Velopack swap: $CurrentDir <= $NewPublish" -ForegroundColor Cyan
  $tmp = "$CurrentDir.tmp"
  if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
  Copy-Item -Recurse -Path $NewPublish -Destination $tmp
  Remove-Item -Recurse -Force $CurrentDir
  Rename-Item -Path $tmp -NewName (Split-Path -Leaf $CurrentDir)
}

# ── Build both versions ─────────────────────────────────────────────────────
Publish-App -Version $VersionA -Out $PublishA
Publish-App -Version $VersionB -Out $PublishB

Copy-Item -Recurse -Path (Join-Path $PublishA "*") -Destination (Join-Path $InstallRoot "current")

# ── Resolve AppData path per scenario ───────────────────────────────────────
switch ($Scenario) {
  "B" {
    $AppData = Join-Path $ScratchLocalAppData "Bakabase"
  }
  "C" {
    $AppData = Join-Path $RunDir "custom-data\Bakabase"
    New-Item -ItemType Directory -Force -Path (Join-Path $ScratchLocalAppData "Bakabase") | Out-Null
    $appJson = @{
      App = @{ DataPath = $AppData; Version = $VersionA; Language = "en-US" }
    } | ConvertTo-Json -Depth 4
    Set-Content -Path (Join-Path $ScratchLocalAppData "Bakabase\app.json") -Value $appJson
  }
  "D" {
    $AppData = Join-Path $RunDir "env-mounted-data"
    $env:BAKABASE_DATA_DIR = $AppData
  }
}

New-Item -ItemType Directory -Force -Path $AppData | Out-Null
Seed-Fixture -AppData $AppData

$Before = Join-Path $RunDir "manifest.before"
$After  = Join-Path $RunDir "manifest.after"
Capture-Manifest -Dir $AppData -OutFile $Before
Write-Host ("Seeded {0} files in {1}" -f (Get-Content $Before).Count, $AppData)

Simulate-VelopackSwap -CurrentDir (Join-Path $InstallRoot "current") -NewPublish $PublishB

Capture-Manifest -Dir $AppData -OutFile $After

if ((Get-FileHash $Before).Hash -ne (Get-FileHash $After).Hash) {
  Write-Host "FAIL: AppData diverged across upgrade." -ForegroundColor Red
  Compare-Object (Get-Content $Before) (Get-Content $After) | Format-Table
  exit 1
}

Write-Host ""
Write-Host "=== Windows upgrade test PASS [scenario=$Scenario] ===" -ForegroundColor Green
Write-Host "AppData kept at: $AppData"
Write-Host "Install root:    $InstallRoot"
