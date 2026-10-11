<#
.SYNOPSIS
  A version's release notes, from its section of CHANGELOG.md, and optionally puts them on its GitHub release.

.DESCRIPTION
  Prints the notes for -Version: the text under its "## <version>" heading, with links into the repository made
  absolute (they're shown on github.com/.../releases, not next to the file) and a download line at the end. Throws if
  the changelog has no section for it, so a release can't go out without notes.

  With -Apply, sets them as the description of the release tagged v<Version> (GH_TOKEN must allow editing releases).
  -Version all applies every version in the changelog that has a release, for putting notes on older releases.

.EXAMPLE
  pwsh build/release-notes.ps1 -Version 0.9.0
.EXAMPLE
  pwsh build/release-notes.ps1 -Version all -Apply
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $Apply,
    [string] $Changelog = (Join-Path $PSScriptRoot '../CHANGELOG.md'),
    [string] $Repo = 'ChaoDjinn/ImpiousBonum'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

# Every "## x.y.z" section, in the order they're written.
$sections = [ordered]@{}
$current = $null
foreach ($line in Get-Content $Changelog) {
    if ($line -match '^## (\d+\.\d+\.\d+)\s*$') {
        $current = $Matches[1]
        $sections[$current] = [System.Collections.Generic.List[string]]::new()
    }
    elseif ($line -match '^## ') {
        $current = $null
    }
    elseif ($current) {
        $sections[$current].Add($line)
    }
}

function Get-Notes([string] $v) {
    if (-not $sections.Contains($v)) {
        throw "CHANGELOG.md has no '## $v' section. Add what's new in $v before releasing it."
    }
    $text = ($sections[$v] -join "`n").Trim()
    if (-not $text) {
        throw "CHANGELOG.md's '## $v' section is empty."
    }
    # Relative links point into the repository; on the releases page they'd go nowhere.
    $text = [regex]::Replace($text, '\]\((?!https?://|#)([^)]+)\)', "](https://github.com/$Repo/blob/main/`$1)")
    $download = "https://github.com/$Repo/releases/download/v$v/ImpiousBonum-win-Setup.exe"
    return "$text`n`n---`n`n**Install:** [ImpiousBonum-win-Setup.exe]($download) (Windows 10 or 11). Installed copies update themselves. See the [user guide](https://github.com/$Repo/blob/main/docs/guide.md) for how to use everything."
}

$versions = if ($Version -eq 'all') { @($sections.Keys) } else { @($Version) }
foreach ($v in $versions) {
    $notes = Get-Notes $v
    if (-not $Apply) {
        Write-Output $notes
        continue
    }
    gh release view "v$v" --repo $Repo --json tagName *> $null
    if ($LASTEXITCODE -ne 0) {
        if ($Version -eq 'all') {
            Write-Host "v$v has no release; skipped."
            $global:LASTEXITCODE = 0
            continue
        }
        throw "There's no release tagged v$v."
    }
    $file = New-TemporaryFile
    try {
        Set-Content -Path $file -Value $notes -Encoding utf8NoBOM
        gh release edit "v$v" --repo $Repo --notes-file $file
        Write-Host "Release notes set for v$v."
    }
    finally {
        Remove-Item $file
    }
}
