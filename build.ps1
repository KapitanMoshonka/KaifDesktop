param([string]$OutputDirectory = 'dist\Alpha', [string]$SettingsSourceDirectory = 'dist\Alpha')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$output = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
if (!(Test-Path -LiteralPath $msbuild)) { throw 'Installed VS MSBuild not found.' }
# Read the manifest once; package calm behavior and the seven enabled interactions.
$source = Join-Path $root 'Frames'
$manifest = Get-Content -LiteralPath (Join-Path $source 'animations.json') -Raw | ConvertFrom-Json
$selected = [ordered]@{}
foreach ($name in @('idle','walk-left','walk-right','lie-down','sleep','wake-up','chase-tail','stretch-yawn','sit-down','ear-scratch-enter','ear-scratch-loop','ear-scratch-exit','stand-up','running-left','running-right','grab-left','grab-right','pull-left','pull-right','release-left','release-right','watch-idle','petting','dragged')) {
    $animation = $manifest.animations.$name
    if (!$animation -or $animation.frames.Count -eq 0 -or $animation.frames.Count -ne $animation.durations_ms.Count) { throw "Invalid animation: $name" }
    # Runtime owns scenarios; do not retain next links to actions outside this package.
    $copy = [ordered]@{frames=@($animation.frames); durations_ms=@($animation.durations_ms); loop=[bool]$animation.loop}
    if ($animation.revision) { $copy.revision = $animation.revision }
    if ($animation.mouth_anchors_px) { $copy.mouth_anchors_px = $animation.mouth_anchors_px }
    if ($animation.frame_events) { $copy.frame_events = $animation.frame_events }
    $selected[$name] = $copy
}
& $msbuild (Join-Path $root 'KaifDesktop.sln') /t:Build /p:Configuration=Release /p:Platform=x64 "/p:OutputPath=$output\" /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed: $LASTEXITCODE" }
$destination = Join-Path $output 'Frames'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
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
$snapshot = [ordered]@{schema_version=2; name=$manifest.name; frame_width=$manifest.frame_width; frame_height=$manifest.frame_height; format=$manifest.format; anchors=$manifest.anchors; animations=$selected; watch_look_directions=$manifest.watch_look_directions; total_frame_count=(@($selected.Values | ForEach-Object { $_.frames }).Count + 8); runtime_note='Sequences are controlled explicitly by application; next metadata is not followed automatically.'}
$snapshot | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'animations.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $root 'meme-scales.json') -Destination (Join-Path $output 'meme-scales.json')
Copy-Item -LiteralPath (Join-Path $root 'ALPHA-README.md') -Destination (Join-Path $output 'README.md')
$priorSettings = Join-Path (Join-Path $root $SettingsSourceDirectory) 'data\settings.json'
$outputSettings = Join-Path $output 'data\settings.json'
if ($output -ne [System.IO.Path]::GetFullPath((Join-Path $root 'dist\Alpha')) -and (Test-Path -LiteralPath $priorSettings) -and !(Test-Path -LiteralPath $outputSettings)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $outputSettings) | Out-Null
    Copy-Item -LiteralPath $priorSettings -Destination $outputSettings
}
Write-Host "Ready: $output\KaifDesktop.exe"
