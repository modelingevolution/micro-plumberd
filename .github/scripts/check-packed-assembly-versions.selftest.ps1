<#
.SYNOPSIS
  Self-test for check-packed-assembly-versions.ps1 against SYNTHETIC packages — real assemblies (two throwaway class
  libraries built at 9.9.9.0 and 1.0.0.0) zipped into .nupkg files under the file names a release packs. The guard reads
  the AssemblyVersion from metadata, so the file name alone decides what is first-party. No network beyond a restore.

.EXAMPLE
  pwsh .github/scripts/check-packed-assembly-versions.selftest.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$guard = Join-Path $PSScriptRoot 'check-packed-assembly-versions.ps1'
$repo = Resolve-Path (Join-Path $PSScriptRoot '../..')
$work = Join-Path ([IO.Path]::GetTempPath()) ("asmver-selftest-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$pass = 0; $fail = 0
function Ok($m) { Write-Host "  ok   - $m"; $script:pass++ }
function Bad($m) { Write-Host "  FAIL - $m"; $script:fail++ }

function Build-Assembly([string] $version) {
    $dir = Join-Path $work "lib-$version"
    dotnet new classlib -o $dir -n Probe --force | Out-Null
    dotnet build $dir -c Release "/p:Version=$version" -o "$dir/out" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "could not build the $version probe" }
    Join-Path $dir 'out/Probe.dll'
}

function New-Package([string] $name, [hashtable] $entries) {   # entries: path-in-package -> source dll
    $dir = Join-Path $work ("pkgs-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $dir "$name.nupkg"), 'Create')
    try { foreach ($k in $entries.Keys) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entries[$k], $k) } }
    finally { $zip.Dispose() }
    $dir
}

function Run-Guard([string] $artifacts) {
    $out = & pwsh -NoProfile -File $guard -Artifacts $artifacts -Version '9.9.9.0' -RepoRoot $repo 2>&1 | Out-String
    [pscustomobject]@{ Exit = $LASTEXITCODE; Out = $out }
}

try {
    $good = Build-Assembly '9.9.9.0'
    $stale = Build-Assembly '1.0.0.0'

    # THE ROW (rev-reach on #32): the tool package's own assembly, mp-rewrite.dll, packed at 1.0.0.0 beside a correct
    # MicroPlumberd.dll. Selected by a name pattern it was never checked; selected by the repository's own assembly names
    # it fails, named.
    $r = Run-Guard (New-Package 'MicroPlumberd.Rewrite.9.9.9' @{
        'tools/net10.0/any/MicroPlumberd.dll' = $good
        'tools/net10.0/any/mp-rewrite.dll'    = $stale })
    if ($r.Exit -ne 0 -and $r.Out -match 'mp-rewrite\.dll is AssemblyVersion 1\.0\.0\.0') { Ok 'a first-party DLL outside MicroPlumberd* at 1.0.0.0: FAILS, named' }
    else { Bad "the tool's own assembly: exit $($r.Exit) $($r.Out)" }

    # Third-party DLLs in the same tool package are not ours: never checked, however they are versioned.
    $r = Run-Guard (New-Package 'MicroPlumberd.Rewrite.9.9.9' @{
        'tools/net10.0/any/MicroPlumberd.dll'   = $good
        'tools/net10.0/any/mp-rewrite.dll'      = $good
        'tools/net10.0/any/KurrentDB.Client.dll' = $stale })
    if ($r.Exit -eq 0 -and $r.Out -notmatch 'KurrentDB') { Ok 'a third-party DLL at 1.0.0.0 beside correct first-party ones: passes, unchecked' }
    else { Bad "third-party: exit $($r.Exit) $($r.Out)" }

    # Never vacuous: a package with no first-party DLL at all still fails.
    $r = Run-Guard (New-Package 'Something.9.9.9' @{ 'lib/net10.0/KurrentDB.Client.dll' = $good })
    if ($r.Exit -ne 0 -and $r.Out -match 'contains no first-party DLL') { Ok 'a package with no first-party DLL: FAILS' }
    else { Bad "vacuous: exit $($r.Exit) $($r.Out)" }

    # The population is real: the set read from the project files holds the tool's assembly and the core.
    $r = Run-Guard (New-Package 'MicroPlumberd.9.9.9' @{ 'lib/net10.0/MicroPlumberd.dll' = $good })
    if ($r.Exit -eq 0 -and $r.Out -match 'ok\s+MicroPlumberd\.9\.9\.9\.nupkg: lib/net10\.0/MicroPlumberd\.dll 9\.9\.9\.0') { Ok 'the core package at 9.9.9.0: passes, checked' }
    else { Bad "core: exit $($r.Exit) $($r.Out)" }
}
finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }

Write-Host "check-packed-assembly-versions self-test: $pass ok, $fail failed"
if ($fail -gt 0) { exit 1 }
