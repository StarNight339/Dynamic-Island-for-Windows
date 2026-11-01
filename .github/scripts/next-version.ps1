<#
.SYNOPSIS
  Works out the next release tag in the form v0.X.Y-Z.

  X = version (0-9), Y = patch (0-9), Z = change type letter (A-Z).
  Every release bumps Y; past 9 it rolls over into X (0.4.9 -> 0.5.0). Past 0.9.9 the script fails, because the
  format has no room left and someone has to decide what comes next.

  Z comes from the pushed commit:
    - a PR merge ("Merge pull request #12 from owner/fix/thing") uses the branch prefix ("fix")
    - otherwise a conventional prefix ("feat: ...", "fix(ui): ...")
    - otherwise the first word ("Add ...", "Fix ...")
  and is mapped with the table below; anything unknown is M (other).

.EXAMPLE
  ./next-version.ps1                                    # uses the repo's tags and HEAD's commit message
  ./next-version.ps1 -Tags v0.4.0,v0.3.9 -Message "fix: crash"   # -> v0.4.1-B
#>
param(
    [string[]] $Tags,
    [string] $Message
)

$ErrorActionPreference = 'Stop'

$letters = [ordered]@{
    F = 'feat', 'feature', 'features', 'add'
    B = 'fix', 'bugfix', 'hotfix', 'bug'
    U = 'ui', 'style', 'design'
    P = 'perf', 'performance'
    R = 'refactor', 'cleanup'
    S = 'security', 'sec'
    D = 'docs', 'doc', 'readme'
    T = 'test', 'tests'
    C = 'ci', 'build', 'release'
    M = 'chore', 'misc'
}

if (-not $PSBoundParameters.ContainsKey('Tags')) { $Tags = @(git tag --list 'v*') }
if (-not $PSBoundParameters.ContainsKey('Message')) { $Message = (git log -1 --pretty=%B) -join "`n" }

# Highest existing 0.X.Y (older tags without a letter count too).
$x = 0; $y = 0; $found = $false
foreach ($tag in $Tags) {
    if ($tag -cmatch '^v0\.(\d)\.(\d)(-[A-Z])?$') {
        $tx = [int]$Matches[1]; $ty = [int]$Matches[2]
        if (-not $found -or $tx -gt $x -or ($tx -eq $x -and $ty -gt $y)) { $x = $tx; $y = $ty; $found = $true }
    }
}

$y++
if ($y -gt 9) { $x++; $y = 0 }
if ($x -gt 9) { throw "Version 0.9.9 was the last one the 0.X.Y-Z format allows; decide on the next scheme." }

# Change type word.
$firstLine = ($Message -split "`n")[0].Trim()
$word = $null
if ($firstLine -match 'Merge pull request #\d+ from [^/\s]+/(?<branch>\S+)') {
    $branch = $Matches.branch
    $word = if ($branch.Contains('/')) { $branch.Split('/')[0] } else { $branch.Split('-')[0] }
}
elseif ($firstLine -match '^(?<type>[A-Za-z]+)(\([^)]*\))?!?:') {
    $word = $Matches.type
}
elseif ($firstLine -match '^(?<first>[A-Za-z]+)') {
    $word = $Matches.first
}

$letter = 'M'
if ($word) {
    $lower = $word.ToLowerInvariant()
    foreach ($key in $letters.Keys) {
        if ($letters[$key] -contains $lower) { $letter = $key; break }
    }
}

$version = "0.$x.$y"
$tag = "v$version-$letter"
Write-Host "Next release: $tag (from '$firstLine')"

$tag
