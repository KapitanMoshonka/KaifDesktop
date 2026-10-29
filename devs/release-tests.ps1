# Headless filesystem fixtures only. Never rotates the real actual/backup directories.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tools.ps1')
$buildRoot = Join-Path $PSScriptRoot '.build'
$fixture = Join-Path $buildRoot ('rotation-test-' + [Guid]::NewGuid().ToString('N'))
$null = Assert-ReleasePath $fixture $buildRoot
$log = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message }; $log.Add('PASS: ' + $Message) }
function Reject([scriptblock]$Work, [string]$Message) {
    $rejected = $false
    try { & $Work } catch { $rejected = $true }
    Check $rejected $Message
}
try {
    $null = New-Item -ItemType Directory -Path (Join-Path $fixture 'devs'), (Join-Path $fixture 'actual'), (Join-Path $fixture 'backup/version1') -Force
    Set-Content -LiteralPath (Join-Path $fixture 'actual/KaifDesktop.exe') -Value 'fixture current - not executable'
    Set-Content -LiteralPath (Join-Path $fixture 'backup/version1/KaifDesktop.exe') -Value 'fixture previous - not executable'
    $actual = Get-ReleaseFingerprint (Join-Path $fixture 'actual') $fixture
    $old = Get-ReleaseFingerprint (Join-Path $fixture 'backup/version1') $fixture
    Reject { Read-ActiveStage $fixture } 'Publishing requires an explicit stage start'
    & (Join-Path $PSScriptRoot 'release-stage.ps1') Begin -WorkspaceRoot $fixture
    Check ((Get-ReleaseFingerprint (Join-Path $fixture 'actual') $fixture) -eq $actual) 'Begin leaves actual unchanged'
    Check ((Get-ReleaseFingerprint (Join-Path $fixture 'backup/version1') $fixture) -eq $actual) 'version1 is an exact verified copy of actual'
    Check ((Get-ReleaseFingerprint (Join-Path $fixture 'backup/version2') $fixture) -eq $old) 'Previous version1 survives in version2'
    Reject { & (Join-Path $PSScriptRoot 'release-stage.ps1') Begin -WorkspaceRoot $fixture } 'Repeated Begin cannot overwrite backup in an active stage'
    $state = Read-ActiveStage $fixture
    Reject { & (Join-Path $PSScriptRoot 'release-stage.ps1') Approve -StageId 'wrong' -WorkspaceRoot $fixture } 'Approval requires the correct stage ID'
    $backupExe = Join-Path $fixture 'backup/version1/KaifDesktop.exe'
    $content = [IO.File]::ReadAllBytes($backupExe)
    Set-Content -LiteralPath $backupExe -Value 'tampered fixture'
    Reject { Read-ActiveStage $fixture } 'Tampered version1 blocks publishing/approval'
    [IO.File]::WriteAllBytes($backupExe, $content)
    foreach ($number in 1,2) {
        $package = Join-Path $fixture "devs/.build/staging-$number/package"
        New-Item -ItemType Directory -Path $package -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $package 'KaifDesktop.exe') -Value "fixture build $number - not executable"
        Publish-ReleasePackage $package $fixture
        Check ((Get-ReleaseFingerprint (Join-Path $fixture 'backup/version1') $fixture) -eq $actual) "Build $number preserves version1"
        Check ((Get-ReleaseFingerprint (Join-Path $fixture 'backup/version2') $fixture) -eq $old) "Build $number preserves unapproved version2"
    }
    & (Join-Path $PSScriptRoot 'release-stage.ps1') Approve -StageId $state.StageId -WorkspaceRoot $fixture
    Check (!(Test-Path -LiteralPath (Join-Path $fixture 'backup/version2'))) 'Explicit Approve removes only version2'
    Check ((Get-ReleaseFingerprint (Join-Path $fixture 'backup/version1') $fixture) -eq $actual) 'Approval retains version1'
    Reject { Read-ActiveStage $fixture } 'Approved stage requires new Begin before another publish'
    Reject { Assert-ReleasePath (Join-Path $fixture '../escape') $fixture } 'Path traversal outside intended target is refused'
    $junction = Join-Path $fixture 'junction'
    New-Item -ItemType Junction -Path $junction -Target (Join-Path $fixture 'actual') | Out-Null
    Reject { Assert-ReleaseTree $junction $fixture } 'Junction paths are refused'
    # Remove the verified junction itself, never recurse through its target.
    $null = Assert-ReleasePath $fixture $buildRoot
    [IO.Directory]::Delete($junction)
    $nextActual = Get-ReleaseFingerprint (Join-Path $fixture 'actual') $fixture
    & (Join-Path $PSScriptRoot 'release-stage.ps1') Begin -WorkspaceRoot $fixture
    Check ((Get-ReleaseFingerprint (Join-Path $fixture 'backup/version1') $fixture) -eq $nextActual) 'Next stage backs up the latest actual'
    $next = Read-ActiveStage $fixture
    & (Join-Path $PSScriptRoot 'release-stage.ps1') Approve -StageId $next.StageId -WorkspaceRoot $fixture
} finally {
    $reports = Join-Path $buildRoot 'verification/tests'
    New-Item -ItemType Directory -Path $reports -Force | Out-Null
    $log | Set-Content -LiteralPath (Join-Path $reports 'rotation-tests.txt') -Encoding UTF8
    if (Test-Path -LiteralPath $fixture) { Remove-ReleaseTree $fixture $buildRoot }
}
$log | ForEach-Object { Write-Host $_ }
