<#
.SYNOPSIS
  Publish guard: every first-party assembly inside every packed .nupkg must carry the AssemblyVersion the release is
  published as. Fails the publish, naming each DLL that does not.

.DESCRIPTION
  1.3.2-1.3.4 shipped with most assemblies stamped 1.0.0.0: a test step between the versioned build and the
  `--no-build` pack rebuilt its reference closure without /p:Version, and the pack packed those binaries. The
  Uniqueness dialect packages (outside that closure) kept a 1.3.x.0 reference to a core that now said 1.0.0.0,
  and .NET refuses to bind a lower version than referenced: FileNotFoundException at startup for every consumer.
  This reads what was actually packed, so any future path to the same defect goes red here, before a push.

  Expected version: the published version without its prerelease/build suffix, padded to four parts — what
  /p:Version produces for AssemblyVersion: "1.3.5.0" -> 1.3.5.0, "1.3.5" -> 1.3.5.0,
  "1.0.122.154-preview" -> 1.0.122.154.

  FIRST-PARTY is what THIS repository builds — the AssemblyName of every project under src/ (or the project's file name
  when it sets none) — never a file-name pattern (rev-reach on #32: mp-rewrite.dll, the MicroPlumberd.Rewrite tool's own
  assembly, matched no MicroPlumberd*.dll and was never checked, while third-party DLLs in that same tool package must
  not be). The set is read from the project files at run time, so a project added later is covered without an edit.

  Never vacuous: an artifacts folder with no .nupkg fails, a repository whose projects yield no assembly name fails, and
  a .nupkg with no first-party DLL fails.

.EXAMPLE
  pwsh .github/scripts/check-packed-assembly-versions.ps1 -Artifacts ./artifacts -Version 1.3.5.0
#>
param(
    [Parameter(Mandatory)] [string] $Artifacts,
    [Parameter(Mandatory)] [string] $Version,
    # Where the first-party set is read from: the repository root (its src/**/*.csproj).
    [string] $RepoRoot = (Join-Path $PSScriptRoot '../..')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$core = ($Version -split '[-+]')[0]
$parts = $core.Split('.')
if ($parts.Count -lt 2 -or $parts.Count -gt 4 -or ($parts | Where-Object { $_ -notmatch '^\d+$' })) {
    Write-Error "Version '$Version' is not a version this guard can derive an AssemblyVersion from."
}
while ($parts.Count -lt 4) { $parts += '0' }
$expected = [Version]::new([int]$parts[0], [int]$parts[1], [int]$parts[2], [int]$parts[3])

$firstParty = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
# Read LITERALLY, so refused where a literal would lie (rev-reach 2f5a9a7b5): an AssemblyName built from an MSBuild
# property ($(…)) never matches a packed file name, and one set in a Directory.Build.props is not in the project file at
# all — either way that DLL would go unchecked, silently. Both are refused by name; write the name literally instead.
foreach ($props in Get-ChildItem -Path $RepoRoot -Filter Directory.Build.props -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
    [xml] $xml = Get-Content -Raw $props.FullName
    if (@($xml.Project.PropertyGroup | ForEach-Object { $_.AssemblyName } | Where-Object { $_ }).Count -gt 0) {
        Write-Error "$($props.FullName) sets an AssemblyName: this guard reads project files literally and would not see it. Set it in the project file."
    }
}
foreach ($project in Get-ChildItem -Path (Join-Path $RepoRoot 'src') -Filter *.csproj -Recurse -File) {
    [xml] $xml = Get-Content -Raw $project.FullName
    $name = @($xml.Project.PropertyGroup | ForEach-Object { $_.AssemblyName } | Where-Object { $_ }) | Select-Object -First 1
    if ($name -and $name.Contains('$(')) {
        Write-Error "$($project.FullName): AssemblyName '$name' uses an MSBuild property, which this guard cannot evaluate — its DLL would go unchecked. Write the name literally."
    }
    if (-not $name) { $name = $project.BaseName }
    [void] $firstParty.Add($name)
}
if ($firstParty.Count -eq 0) {
    Write-Error "No project under '$RepoRoot/src' yields an assembly name: there is no first-party set to check against."
}

$packages = @(Get-ChildItem -Path $Artifacts -Filter *.nupkg -File | Sort-Object Name)
if ($packages.Count -eq 0) {
    Write-Error "No .nupkg in '$Artifacts': the guard has nothing to check, which is not a pass."
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("asmver-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$bad = [Collections.Generic.List[string]]::new()
$checked = 0

try {
    foreach ($pkg in $packages) {
        $zip = [IO.Compression.ZipFile]::OpenRead($pkg.FullName)
        try {
            $entries = @($zip.Entries | Where-Object {
                $_.Name -like '*.dll' -and $firstParty.Contains([IO.Path]::GetFileNameWithoutExtension($_.Name)) })
            if ($entries.Count -eq 0) {
                $bad.Add("$($pkg.Name): contains no first-party DLL (none of this repository's $($firstParty.Count) assembly names) to check")
                continue
            }
            foreach ($entry in $entries) {
                $target = Join-Path $work ([Guid]::NewGuid().ToString('N') + '.dll')
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target)
                $actual = [Reflection.AssemblyName]::GetAssemblyName($target).Version
                $checked++
                if ($actual -ne $expected) {
                    $bad.Add("$($pkg.Name): $($entry.FullName) is AssemblyVersion $actual, expected $expected")
                } else {
                    Write-Host "ok   $($pkg.Name): $($entry.FullName) $actual"
                }
            }
        } finally { $zip.Dispose() }
    }
} finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }

if ($bad.Count -gt 0) {
    $bad | ForEach-Object { Write-Host "FAIL $_" }
    Write-Error "$($bad.Count) packed assembly version problem(s); expected every first-party DLL at $expected ($checked checked in $($packages.Count) packages). Nothing may be pushed."
}
Write-Host "All $checked first-party DLLs in $($packages.Count) packages are AssemblyVersion $expected."
# EXPLICIT, the self-test's lesson (publish-nuget run 39152113710): GitHub's pwsh shell appends `exit $LASTEXITCODE`, so a
# step's verdict must never rest on whatever native code ran last. A failure above is a terminating Write-Error ('Stop').
exit 0
