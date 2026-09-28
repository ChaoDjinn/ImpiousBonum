<#
.SYNOPSIS
  Builds the Impious Bonum installer (Velopack) into artifacts/releases.

.DESCRIPTION
  Publishes the dashboard and the sensor host (framework-dependent, win-x64), then packs them with Velopack.
  The installer is per-user (no admin) and installs the .NET 10 Desktop Runtime first if it's missing.
  With -GitHubToken, the previous release is downloaded first (so a small delta update is produced) and the
  new release is uploaded to GitHub Releases, where installed copies pick it up automatically.

.EXAMPLE
  pwsh build/publish.ps1 -Version 0.2.0
.EXAMPLE
  pwsh build/publish.ps1 -Version 0.2.0 -GitHubToken $env:GITHUB_TOKEN
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Output = 'artifacts',
    [string] $GitHubToken,
    [string] $RepoUrl = 'https://github.com/ChaoDjinn/ImpiousBonum',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $root
try {
    dotnet tool restore

    if (-not $SkipTests) {
        dotnet test ImpiousBonum.slnx -c Release
    }

    $publish = Join-Path $Output 'publish'
    $releases = Join-Path $Output 'releases'
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

    dotnet publish src/ImpiousBonum.App -c Release -r win-x64 --self-contained false -p:Version=$Version -o $publish
    # The sensor host runs as its own process (and, once installed, from Program Files), so it's published on its own.
    dotnet publish src/ImpiousBonum.Sensors -c Release -r win-x64 --self-contained false -p:Version=$Version -o (Join-Path $publish 'sensors')

    if (-not $GitHubToken -and (Test-Path $releases)) {
        # Local builds start clean; vpk won't pack a version that's already in the folder.
        Remove-Item -Recurse -Force $releases
    }

    if ($GitHubToken) {
        # The previous full release lets vpk build a delta package, so updates download only what changed.
        # There's nothing to download before the first release, so a failure here isn't fatal.
        try {
            dotnet vpk download github --repoUrl $RepoUrl --token $GitHubToken -o $releases
        }
        catch {
            Write-Warning "No previous release downloaded ($($_.Exception.Message)); packing without a delta."
        }
    }

    dotnet vpk pack `
        --packId ImpiousBonum `
        --packVersion $Version `
        --packDir $publish `
        --mainExe ImpiousBonum.exe `
        --packTitle 'Impious Bonum' `
        --packAuthors 'ChaoDjinn' `
        --icon assets/ImpiousBonum.ico `
        --framework net10-x64-desktop `
        --outputDir $releases

    if ($GitHubToken) {
        dotnet vpk upload github --repoUrl $RepoUrl --token $GitHubToken -o $releases `
            --publish --releaseName "Impious Bonum $Version" --tag "v$Version"
    }

    Write-Host ''
    Write-Host "Installer: $(Resolve-Path (Join-Path $releases 'ImpiousBonum-win-Setup.exe'))"
}
finally {
    Pop-Location
}
