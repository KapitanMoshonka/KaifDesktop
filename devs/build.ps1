# From the workspace root:
#   ./devs/release-stage.ps1 Begin       # explicit, once before changing a release
#   ./devs/build.ps1                    # stages, self-tests, then publishes to actual
#   ./devs/build.ps1 -OutputDirectory '.build/check/package'  # new temporary output
# Relative parameters resolve against devs/. Existing actual settings are preserved.
param([string]$OutputDirectory = '..\actual', [string]$SettingsSourceDirectory = '..\actual')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-tools.ps1')
$root = $PSScriptRoot
$workspace = Split-Path $root -Parent
$actual = [IO.Path]::GetFullPath((Join-Path $workspace 'actual'))
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
$output = $output.TrimEnd('\', '/')
$publishing = $output -eq $actual
$buildRoot = Join-Path $root '.build'
Assert-ReleaseTree $buildRoot $root
if ($publishing) { Assert-ReleaseTree $actual $workspace; Assert-ReleaseStopped $actual }
else {
    $null = Assert-ReleasePath $output $buildRoot
    if (Test-Path -LiteralPath $output) { throw 'Custom output must be a NEW directory under devs/.build/.' }
}
$settingsSource = if ([IO.Path]::IsPathRooted($SettingsSourceDirectory)) { [IO.Path]::GetFullPath($SettingsSourceDirectory) } else { [IO.Path]::GetFullPath((Join-Path $root $SettingsSourceDirectory)) }
$null = Assert-ReleasePath $settingsSource $workspace
Assert-ReleaseTree $settingsSource $workspace
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (Test-Path -LiteralPath $vswhere) {
    $found = @(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')
    if ($LASTEXITCODE -eq 0) { $msbuild = $found | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1 }
}
if (!$msbuild) { $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue; if ($command) { $msbuild = $command.Source } }
if (!$msbuild) { throw 'Installed VS MSBuild not found. No downloads/installations were attempted.' }
$lock = Open-ReleaseLock $root
try {
    if ($publishing) { $null = Read-ActiveStage $workspace }
    $stage = Join-Path $buildRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
    $package = Join-Path $stage 'package'
    $null = Assert-ReleasePath $stage $buildRoot
    $tests = Join-Path $package 'tests'
    New-Item -ItemType Directory -Path $tests -Force | Out-Null
    $source = Join-Path $root 'Frames'
    Assert-ReleaseTree $source $root
    $manifest = Get-Content -LiteralPath (Join-Path $source 'animations.json') -Raw | ConvertFrom-Json
    $selected = [ordered]@{}
    foreach ($name in @('idle','walk-left','walk-right','lie-down','sleep','wake-up','chase-tail','stretch-yawn','sit-down','ear-scratch-enter','ear-scratch-loop','ear-scratch-exit','stand-up','running-left','running-right','grab-left','grab-right','pull-left','pull-right','release-left','release-right','watch-idle','petting','dragged')) {
        $animation = $manifest.animations.$name
        if (!$animation -or $animation.frames.Count -eq 0 -or $animation.frames.Count -ne $animation.durations_ms.Count) { throw "Invalid animation: $name" }
        $copy = [ordered]@{frames=@($animation.frames); durations_ms=@($animation.durations_ms); loop=[bool]$animation.loop}
        foreach ($property in @('revision','mouth_anchors_px','frame_events')) {
            if ($animation.PSObject.Properties.Name -contains $property) { $copy[$property] = $animation.$property }
        }
        $selected[$name] = $copy
    }
    & $msbuild (Join-Path $root 'KaifDesktop.sln') /t:Rebuild /p:Configuration=Release /p:Platform=x64 "/p:OutputPath=$package\" /nologo /verbosity:minimal 2>&1 | Tee-Object -FilePath (Join-Path $tests 'build.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed: $LASTEXITCODE. Staging retained: $stage" }
    $destination = Join-Path $package 'Frames'
    foreach ($name in $selected.Keys) {
        foreach ($relative in $selected[$name].frames) {
            if ($relative -notmatch ('^' + [regex]::Escape($name) + '/[a-zA-Z0-9_.-]+\.png$')) { throw "Unsafe frame path: $relative" }
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $target
        }
    }
    if (@($manifest.watch_look_directions).Count -ne 8) { throw 'Expected 8 seated look directions.' }
    foreach ($direction in $manifest.watch_look_directions) {
        $relative = $direction.file
        if ($relative -notmatch '^watch-look/[0-9]+\.png$') { throw "Unsafe watch-look path: $relative" }
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $target
    }
    $snapshot = [ordered]@{schema_version=2; name=$manifest.name; frame_width=$manifest.frame_width; frame_height=$manifest.frame_height; format=$manifest.format; anchors=$manifest.anchors; animations=$selected; watch_look_directions=$manifest.watch_look_directions; total_frame_count=(@($selected.Values | ForEach-Object { $_.frames }).Count + 8); runtime_note='Scenarios are controlled explicitly; next metadata is not executed.'}
    $snapshot | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'animations.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $root 'meme-scales.json') -Destination $package
    $memeFiles = @(Get-ChildItem -LiteralPath (Join-Path $source 'memes') -File)
    if ($memeFiles.Count -eq 0) { throw 'No source memes found.' }
    New-Item -ItemType Directory -Path (Join-Path $package 'memes') | Out-Null
    foreach ($file in $memeFiles) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $package 'memes') }
    New-Item -ItemType Directory -Path (Join-Path $package 'data') | Out-Null
    $priorSettings = Join-Path $settingsSource 'data/settings.json'
    $settings = if (Test-Path -LiteralPath $priorSettings) { Get-Content -LiteralPath $priorSettings -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    if ($null -eq $settings) { throw 'Settings source is null.' }
    $memePath = if (@($settings.PSObject.Properties | ForEach-Object { $_.Name }) -contains 'MemeSourceDirectory') { [string]$settings.MemeSourceDirectory } else { '' }
    if ([string]::IsNullOrWhiteSpace($memePath) -or $memePath.Trim().Replace('/', '\').TrimEnd('\') -ieq 'C:\Users\SirxM\KaifDesktop\Frames\memes') {
        $settings | Add-Member -NotePropertyName MemeSourceDirectory -NotePropertyValue 'memes' -Force
    }
    if (!(@($settings.PSObject.Properties | ForEach-Object { $_.Name }) -contains 'CursorCaptureEnabled')) {
        $settings | Add-Member -NotePropertyName CursorCaptureEnabled -NotePropertyValue $true
    }
    $settings | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $package 'data/settings.json') -Encoding UTF8
    $description = Join-Path $package 'Decription.md'
    @"
# KaifDesktop — этап беты: «Крутись» и захват мышки
This is one beta stage, not the completed full beta release.
Windows x64 / .NET Framework 4.8. Run KaifDesktop.exe; keep the entire folder together.
Frames/, memes/, meme-scales.json and data/settings.json are local to the executable.
MemeSourceDirectory defaults to memes; explicitly configured custom paths are preserved.
Normal settings/logs are in data/. Diagnostic reports, settings and logs are in tests/.
Tray/control window: pause, hide/show, return, seven actions, queue skip and exit.
The control window starts minimized. Calm click plays petting; drag is forbidden throughout
cursor/meme interactions. Escape releases the cursor. Close the control window to exit.
Spin action uses the user-approved v5 animation: 10 frames, 1150 ms per cycle;
TailCycles still sets the number of cycles (default 3). Its button is named "Крутись".
The checkbox "Включить захват мышки" defaults to enabled and is saved immediately.
Turning it off removes all waiting Cursor actions, disables manual cursor capture and
safely releases an active capture. Watch, petting and dragging the pet remain available.
Headless check: KaifDesktop.exe --self-test. No real cursor movement or UI.
UI, real mouse, monitors/DPI and visual acceptance need separate verification.
Known unresolved issue: reported minimized meme windows. This build does not fix it.
"@ | Set-Content -LiteralPath $description -Encoding UTF8
    $testProcess = Start-Process -FilePath (Join-Path $package 'KaifDesktop.exe') -ArgumentList '--self-test' -WorkingDirectory ([IO.Path]::GetTempPath()) -WindowStyle Hidden -PassThru
    if (!$testProcess.WaitForExit(60000)) { throw "Self-test timeout; PID $($testProcess.Id) not terminated. Staging retained: $stage" }
    if ($testProcess.ExitCode -ne 0) { throw "Self-test failed: $($testProcess.ExitCode). Staging retained: $stage" }
    $selfReport = Join-Path $tests 'self-test.txt'
    if (!(Test-Path -LiteralPath $selfReport) -or !(Get-Content -LiteralPath $selfReport -Raw).StartsWith('PASS:')) { throw 'Self-test PASS report missing.' }
    @"
# Build verification
UTC: $([DateTime]::UtcNow.ToString('o'))
MSBuild: $msbuild
Release / net48 / C# 7.3 / x64 compilation and headless self-test passed.
Self-test working directory: TEMP. See tests/build.log and tests/self-test.txt.
Packaged $($snapshot.total_frame_count) sprite frames and $($memeFiles.Count) meme files.
This build command runs no UI/cursor tests; separately executed UI checks are recorded in tests/.
Historical PASS is not new-machine UI validation.
Implemented: approved spin v5 and the persistent cursor-capture checkbox.
Other beta animation work and the minimized-meme fix are not implemented here.
"@ | Add-Content -LiteralPath $description -Encoding UTF8
    if ($publishing) { Publish-ReleasePackage $package $workspace }
    else { New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null; Move-Item -LiteralPath $package -Destination $output }
    Remove-ReleaseTree $stage $buildRoot
    Write-Host "Ready: $output\KaifDesktop.exe"
} finally { $lock.Dispose() }
