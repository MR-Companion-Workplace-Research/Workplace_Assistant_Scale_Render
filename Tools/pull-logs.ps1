<#
.SYNOPSIS
  Pull ConversationLogger output off the Quest 3, verify it landed intact, and
  (optionally) delete it from the headset.

.DESCRIPTION
  The study app writes JSONL + console logs to
      /sdcard/Android/data/<package>/files/ConversationLogs/<participant>/
  This script copies that tree to a local folder, byte-size-verifies every file,
  and only then offers to wipe the device copy. Nothing is deleted unless you
  pass -Delete, and a file is never deleted unless its local copy verified.

  adb is found on PATH, else via ANDROID_HOME/ANDROID_SDK_ROOT, else from the
  copy Unity bundles with the Android build support module.

.EXAMPLE
  .\Tools\pull-logs.ps1
      Pull everything to .\StudyLogs, leave the headset untouched.

.EXAMPLE
  .\Tools\pull-logs.ps1 -Participant P07 -Delete
      Pull only P07's folder, verify, then delete P07's logs from the headset
      (asks for confirmation first).

.EXAMPLE
  .\Tools\pull-logs.ps1 -Delete -Force
      Pull everything, verify, delete without prompting. Use in a hurry between
      participants.
#>
[CmdletBinding()]
param(
    # Local destination root. A dated subfolder is NOT created — files keep their
    # participant/date structure, so repeated pulls merge cleanly.
    [string]$Dest = (Join-Path $PSScriptRoot '..\StudyLogs'),

    # Pull only this participant's subfolder (matches the folder name written by
    # ConversationLogger, e.g. "P07"). Omit for everything.
    [string]$Participant,

    # Android package name. Override if you changed the bundle identifier.
    [string]$Package = 'com.DefaultCompany.MRWorkplaceAssistant',

    # Delete the device copy after every file verifies.
    [switch]$Delete,

    # Skip the "are you sure" prompt before deleting.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- find adb ---
function Find-Adb {
    $onPath = Get-Command adb -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    foreach ($root in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)) {
        if ($root) {
            $p = Join-Path $root 'platform-tools\adb.exe'
            if (Test-Path $p) { return $p }
        }
    }

    # Unity ships adb with the Android build support module. Newest Unity first.
    $hubRoots = @(
        'C:\Program Files\Unity\Hub\Editor',
        'C:\Program Files\Unity\Editor'
    ) | Where-Object { Test-Path $_ }

    foreach ($hub in $hubRoots) {
        $hit = Get-ChildItem $hub -Directory -ErrorAction SilentlyContinue |
               Sort-Object Name -Descending |
               ForEach-Object {
                   Join-Path $_.FullName 'Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
               } |
               Where-Object { Test-Path $_ } |
               Select-Object -First 1
        if ($hit) { return $hit }
    }

    throw "adb.exe not found. Install Android platform-tools, or set ANDROID_HOME, or install Unity's Android Build Support module."
}

$adb = Find-Adb
Write-Host "adb: $adb" -ForegroundColor DarkGray

# ------------------------------------------------------------ device check ---
$devices = & $adb devices | Select-Object -Skip 1 |
           Where-Object { $_ -match '\S' -and $_ -notmatch 'offline|unauthorized' }
if (-not $devices) {
    & $adb devices
    throw "No authorized device. Plug the Quest in over USB, put it on, and accept the 'Allow USB debugging' prompt."
}
if ($devices.Count -gt 1) {
    throw "More than one device attached. Disconnect the extras (or set ANDROID_SERIAL)."
}
Write-Host "device: $(($devices[0] -split '\s+')[0])" -ForegroundColor DarkGray

# --------------------------------------------------------------- remote dir ---
$remoteRoot = "/sdcard/Android/data/$Package/files/ConversationLogs"
$remote = if ($Participant) { "$remoteRoot/$Participant" } else { $remoteRoot }

# Toybox find + stat: one shell round-trip returning "<bytes>|<path>" per file.
$listing = & $adb shell "if [ -d '$remote' ]; then for f in `$(find '$remote' -type f); do stat -c '%s|%n' `"`$f`"; done; else echo __MISSING__; fi"

if ($listing -match '__MISSING__') {
    throw "Nothing at $remote on the headset. Wrong package name, wrong participant id, or the app hasn't run yet."
}

$remoteFiles = @()
foreach ($line in $listing) {
    $line = $line.Trim()
    if (-not $line) { continue }
    $parts = $line -split '\|', 2
    if ($parts.Count -eq 2) {
        $remoteFiles += [pscustomobject]@{ Size = [int64]$parts[0]; Path = $parts[1] }
    }
}

if ($remoteFiles.Count -eq 0) { Write-Host "No log files on the headset — nothing to do."; exit 0 }
Write-Host "found $($remoteFiles.Count) file(s) on device" -ForegroundColor Cyan

# --------------------------------------------------------------------- pull ---
New-Item -ItemType Directory -Force -Path $Dest | Out-Null
$Dest = (Resolve-Path $Dest).Path

# Pull the tree. adb creates <Dest>\<leaf-of-remote>\... so the participant
# structure is preserved either way.
& $adb pull "$remote" "$Dest"
if ($LASTEXITCODE -ne 0) { throw "adb pull failed (exit $LASTEXITCODE). Nothing was deleted." }

# ------------------------------------------------------------------- verify ---
# Map each remote path to where adb just put it, then compare byte sizes.
$remoteLeaf = ($remote -split '/')[-1]
$localRoot  = Join-Path $Dest $remoteLeaf

$verified = @()
$failed   = @()
foreach ($f in $remoteFiles) {
    $rel   = $f.Path.Substring($remote.Length).TrimStart('/') -replace '/', '\'
    $local = Join-Path $localRoot $rel

    if ((Test-Path $local) -and ((Get-Item $local).Length -eq $f.Size)) {
        $verified += $f
    } else {
        $failed += $f
        $got = if (Test-Path $local) { (Get-Item $local).Length } else { 'missing' }
        Write-Warning "MISMATCH $($f.Path): device=$($f.Size) local=$got"
    }
}

Write-Host "verified $($verified.Count)/$($remoteFiles.Count) file(s) into $localRoot" -ForegroundColor Green

# ------------------------------------------------------------------- delete ---
if (-not $Delete) {
    if ($failed.Count -gt 0) { Write-Warning "$($failed.Count) file(s) did not verify — re-run before trusting this pull." }
    Write-Host "Headset copy left in place. Re-run with -Delete to clear it." -ForegroundColor DarkGray
    exit ($(if ($failed.Count -gt 0) { 1 } else { 0 }))
}

if ($failed.Count -gt 0) {
    throw "$($failed.Count) file(s) failed verification — refusing to delete anything. Fix the pull first."
}

if (-not $Force) {
    Write-Host ""
    Write-Host "About to DELETE these $($verified.Count) file(s) from the headset:" -ForegroundColor Yellow
    $verified | ForEach-Object { Write-Host "  $($_.Path)" }
    $answer = Read-Host "Type 'yes' to delete"
    if ($answer -ne 'yes') { Write-Host "Aborted — headset untouched."; exit 0 }
}

foreach ($f in $verified) {
    & $adb shell "rm -f '$($f.Path)'"
}
# Clear out the now-empty participant folders (rmdir only removes empty dirs, so
# this can never take anything that still has files in it).
& $adb shell "find '$remote' -type d -empty -delete" 2>$null | Out-Null

Write-Host "Deleted $($verified.Count) file(s) from the headset." -ForegroundColor Green
Write-Host "Local copy: $localRoot" -ForegroundColor Green
