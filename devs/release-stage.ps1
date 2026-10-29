# Begin once at the start of an authorized change; rebuilds do not rotate backups.
# Status prints the stage ID. Only explicit user approval permits:
#   ./devs/release-stage.ps1 Approve -StageId '<ID from Status>'
# Approve removes temporary version2 and retains version1. Never call it automatically.
param(
    [Parameter(Position=0)][ValidateSet('Status','Begin','Approve')][string]$Action = 'Status',
    [string]$StageId,
    # Override only for isolated fixtures under devs; production uses the parent workspace.
    [string]$WorkspaceRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tools.ps1')
$workspace = [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\', '/')
$production = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\', '/')
if ($workspace -ne $production) { $null = Assert-ReleasePath $workspace $PSScriptRoot }
$devs = Join-Path $workspace 'devs'
$actual = Join-Path $workspace 'actual'
$backup = Join-Path $workspace 'backup'
$v1 = Join-Path $backup 'version1'
$v2 = Join-Path $backup 'version2'
$statePath = Join-Path $devs '.release-stage.json'
foreach ($path in @($devs, $actual, $backup)) { Assert-ReleaseTree $path $workspace }
if ($Action -eq 'Status') {
    if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw } else { Write-Host 'No stage has been started.' }
    return
}
if (!(Test-Path -LiteralPath $devs -PathType Container)) { throw 'Missing devs directory.' }
$lock = Open-ReleaseLock $devs
try {
    Assert-ReleaseStopped $actual
    Assert-ReleaseStopped $v1
    Assert-ReleaseStopped $v2
    if ($Action -eq 'Approve') {
        $state = Read-ActiveStage $workspace
        if (!$StageId -or $StageId -ne $state.StageId) { throw 'Approval requires -StageId with the exact active stage ID.' }
        if (!(Test-Path -LiteralPath (Join-Path $actual 'KaifDesktop.exe'))) { throw 'actual/KaifDesktop.exe is missing.' }
        if ($state.Version2Fingerprint) {
            if ((Get-ReleaseFingerprint $v2 $workspace) -ne $state.Version2Fingerprint) { throw 'version2 changed; deletion refused.' }
            Remove-ReleaseTree $v2 $backup
        } elseif (Test-Path -LiteralPath $v2) { throw 'Unexpected version2; deletion refused.' }
        $state.Status = 'Approved'
        $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding UTF8
        Write-Host "Approved $StageId. version1 retained; version2 removed."
        return
    }
    if (Test-Path -LiteralPath $statePath) {
        $old = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($old.Status -ne 'Approved') { throw 'A stage is already active. Rebuild without Begin; version1 must not be overwritten.' }
    }
    if (Test-Path -LiteralPath $v2) { throw 'version2 already exists. Resolve/approve the prior stage first.' }
    if (!(Test-Path -LiteralPath (Join-Path $actual 'KaifDesktop.exe'))) { throw 'actual/KaifDesktop.exe is missing.' }
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $fingerprint = Get-ReleaseFingerprint $actual $workspace
    $previous = if (Test-Path -LiteralPath $v1) { Get-ReleaseFingerprint $v1 $workspace } else { $null }
    $candidate = Join-Path $backup ('.version1-' + [Guid]::NewGuid().ToString('N'))
    $null = Assert-ReleasePath $candidate $backup
    $movedOld = $false
    $installed = $false
    try {
        if ($previous) { Move-Item -LiteralPath $v1 -Destination $v2; $movedOld = $true }
        Copy-Item -LiteralPath $actual -Destination $candidate -Recurse -Force
        if ((Get-ReleaseFingerprint $candidate $workspace) -ne $fingerprint -or (Get-ReleaseFingerprint $actual $workspace) -ne $fingerprint) { throw 'Actual changed or backup copy failed verification.' }
        Move-Item -LiteralPath $candidate -Destination $v1
        $installed = $true
        $state = [ordered]@{ StageId=[Guid]::NewGuid().ToString('N'); Status='Active'; StartedUtc=[DateTime]::UtcNow.ToString('o'); Version1Fingerprint=$fingerprint; Version2Fingerprint=$previous }
        $state | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
        Write-Host "Stage started: $($state.StageId). actual untouched; version1 verified. Keep version2 until explicit Approve."
    } catch {
        if ($installed) { Remove-ReleaseTree $v1 $backup }
        if (Test-Path -LiteralPath $candidate) { Remove-ReleaseTree $candidate $backup }
        if ($movedOld) { Move-Item -LiteralPath $v2 -Destination $v1 }
        throw
    }
} finally { $lock.Dispose() }
