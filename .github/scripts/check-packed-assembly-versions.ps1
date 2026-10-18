<#
.SYNOPSIS
  Publish guard: every first-party assembly (MicroPlumberd*.dll) inside every packed .nupkg must carry the
  AssemblyVersion the release is published as. Fails the publish, naming each DLL that does not.

.DESCRIPTION
  1.3.2-1.3.4 shipped with most assemblies stamped 1.0.0.0: a test step between the versioned build and the
  `--no-build` pack rebuilt its reference closure without /p:Version, and the pack packed those binaries. The
  Uniqueness dialect packages (outside that closure) kept a 1.3.x.0 reference to a core that now said 1.0.0.0,
  and .NET refuses to bind a lower version than referenced: FileNotFoundException at startup for every consumer.
  This reads what was actually packed, so any future path to the same defect goes red here, before a push.

  Expected version: the published version without its prerelease/build suffix, padded to four parts — what
  /p:Version produces for AssemblyVersion: "1.3.5.0" -> 1.3.5.0, "1.3.5" -> 1.3.5.0,
  "1.0.122.154-preview" -> 1.0.122.154.

  Never vacuous: an artifacts folder with no .nupkg fails, and a .nupkg with no first-party DLL fails.

.EXAMPLE
  pwsh .github/scripts/check-packed-assembly-versions.ps1 -Artifacts ./artifacts -Version 1.3.5.0
#>
param(
    [Parameter(Mandatory)] [string] $Artifacts,
    [Parameter(Mandatory)] [string] $Version
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
            $entries = @($zip.Entries | Where-Object { $_.FullName -match '(^|/)MicroPlumberd[^/]*\.dll$' })
            if ($entries.Count -eq 0) {
                $bad.Add("$($pkg.Name): contains no first-party MicroPlumberd*.dll to check")
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
