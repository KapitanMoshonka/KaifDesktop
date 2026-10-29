# Shared local release safeguards. No downloads, process termination or UI launches.
Set-StrictMode -Version Latest

function Assert-ReleasePath([string]$Path, [string]$Parent) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $base = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    if (!$full.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Path must stay inside ${base}: $full" }
    # Reject junctions/symlinks in the entire ancestor chain before any move/delete/copy.
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $cursor" }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}
function Assert-ReleaseTree([string]$Path, [string]$Parent) {
    $null = Assert-ReleasePath $Path $Parent
    if (Test-Path -LiteralPath $Path) {
        foreach ($item in Get-ChildItem -LiteralPath $Path -Force -Recurse) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused: $($item.FullName)" }
        }
    }
}
function Assert-ReleaseStopped([string]$Directory) {
    $prefix = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='KaifDesktop.exe'" -ErrorAction Stop)) {
        if (!$process.ExecutablePath) { throw 'Cannot verify the path of a running KaifDesktop.exe. Close it before updating releases.' }
        if ($process.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Running EXE refused: $($process.ExecutablePath)" }
    }
}
function Get-ReleaseFingerprint([string]$Directory, [string]$Parent) {
    Assert-ReleaseTree $Directory $Parent
    if (!(Test-Path -LiteralPath $Directory -PathType Container)) { throw "Missing release directory: $Directory" }
    $prefix = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $lines = @(Get-ChildItem -LiteralPath $Directory -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($prefix.Length) + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-', '') }
    finally { $sha.Dispose() }
}
function Remove-ReleaseTree([string]$Path, [string]$Parent) {
    Assert-ReleaseTree $Path $Parent
    Assert-ReleaseStopped $Path
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}
function Open-ReleaseLock([string]$Devs) {
    return [IO.File]::Open((Join-Path $Devs '.release.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
}
function Read-ActiveStage([string]$Workspace) {
    $path = Join-Path $Workspace 'devs/.release-stage.json'
    if (!(Test-Path -LiteralPath $path)) { throw 'Run devs/release-stage.ps1 Begin before publishing to actual.' }
    $state = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($state.Status -ne 'Active') { throw 'No active release stage. Run release-stage.ps1 Begin.' }
    $version1 = Join-Path $Workspace 'backup/version1'
    if ((Get-ReleaseFingerprint $version1 $Workspace) -ne $state.Version1Fingerprint) { throw 'backup/version1 changed since stage start. Publishing/approval refused.' }
    return $state
}
function Publish-ReleasePackage([string]$Package, [string]$Workspace) {
    $build = Join-Path $Workspace 'devs/.build'
    $actual = Join-Path $Workspace 'actual'
    Assert-ReleaseTree $Package $build
    Assert-ReleaseTree $actual $Workspace
    Assert-ReleaseStopped $actual
    $null = Read-ActiveStage $Workspace
    if (!(Test-Path -LiteralPath (Join-Path $Package 'KaifDesktop.exe'))) { throw 'Package executable missing.' }
    $prior = Join-Path (Split-Path $Package -Parent) 'prior-actual'
    if (Test-Path -LiteralPath $prior) { throw 'Unexpected rollback directory.' }
    $null = Assert-ReleasePath $prior $build
    Move-Item -LiteralPath $actual -Destination $prior
    try { Move-Item -LiteralPath $Package -Destination $actual }
    catch { Move-Item -LiteralPath $prior -Destination $actual; throw }
    Remove-ReleaseTree $prior $build
}
