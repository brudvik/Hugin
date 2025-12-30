<#
.SYNOPSIS
    Creates a release build of the Hugin IRC Server.

.DESCRIPTION
    This script creates a release build with auto-incremented version,
    packages it as a ZIP file, and manages old releases.

.PARAMETER Major
    Increment the major version number (e.g., 1.0.0 -> 2.0.0).

.PARAMETER Minor
    Increment the minor version number (e.g., 1.0.0 -> 1.1.0).

.PARAMETER Patch
    Increment the patch version number (default) (e.g., 1.0.0 -> 1.0.1).

.PARAMETER ReuseVersion
    Use the current version without incrementing. Useful for rebuilds.

.PARAMETER Version
    Set a specific version number (e.g., "2.0.0"). Overrides auto-increment.

.PARAMETER SkipTests
    Skip running tests before creating the release.

.PARAMETER KeepDays
    Number of days to keep old releases. Default: 60

.PARAMETER MinKeep
    Minimum number of releases to always keep regardless of age. Default: 1

.EXAMPLE
    .\release.ps1
    Creates a release with incremented patch version.

.EXAMPLE
    .\release.ps1 -Minor
    Creates a release with incremented minor version.

.EXAMPLE
    .\release.ps1 -ReuseVersion
    Rebuilds the release with the current version (no increment).

.EXAMPLE
    .\release.ps1 -Version "2.0.0"
    Creates a release with the specified version.

.EXAMPLE
    .\release.ps1 -Major -SkipTests
    Creates a major version release, skipping tests.
#>

param(
    [switch]$Major,
    [switch]$Minor,
    [switch]$Patch,
    [switch]$ReuseVersion,
    [string]$Version,
    [switch]$SkipTests,
    [int]$KeepDays = 60,
    [int]$MinKeep = 1
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$SolutionFile = Join-Path $RootDir "Hugin.sln"
$ServerProject = Join-Path $RootDir "src\Hugin.Server\Hugin.Server.csproj"
$PropsFile = Join-Path $RootDir "Directory.Build.props"
$ReleasesDir = Join-Path $RootDir "releases"
$PublishDir = Join-Path $RootDir "publish"

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║               Hugin IRC Server - Release                     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Ensure releases directory exists
if (-not (Test-Path $ReleasesDir)) {
    New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null
    Write-Host "Created releases directory: $ReleasesDir" -ForegroundColor DarkGray
}

# Read current version from Directory.Build.props
[xml]$propsXml = Get-Content $PropsFile
$currentVersion = $propsXml.Project.PropertyGroup.Version
Write-Host "Current version: $currentVersion" -ForegroundColor Yellow

# Determine the new version
if ($Version) {
    # User specified an explicit version
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "Invalid version format: $Version. Expected format: X.Y.Z (e.g., 2.0.0)" -ForegroundColor Red
        exit 1
    }
    $newVersion = $Version
    Write-Host "Using specified version: $newVersion" -ForegroundColor Green
} elseif ($ReuseVersion) {
    # Reuse current version (rebuild without increment)
    $newVersion = $currentVersion
    Write-Host "Reusing current version: $newVersion" -ForegroundColor Green
} else {
    # Parse and increment version
    $versionParts = $currentVersion -split '\.'
    $majorVer = [int]$versionParts[0]
    $minorVer = [int]$versionParts[1]
    $patchVer = [int]$versionParts[2]

    # Increment version based on parameter
    if ($Major) {
        $majorVer++
        $minorVer = 0
        $patchVer = 0
        Write-Host "Incrementing major version..." -ForegroundColor DarkGray
    } elseif ($Minor) {
        $minorVer++
        $patchVer = 0
        Write-Host "Incrementing minor version..." -ForegroundColor DarkGray
    } else {
        # Default to patch increment
        $patchVer++
        Write-Host "Incrementing patch version..." -ForegroundColor DarkGray
    }

    $newVersion = "$majorVer.$minorVer.$patchVer"
    Write-Host "New version: $newVersion" -ForegroundColor Green
}
Write-Host ""

# Update version in Directory.Build.props (only if version changed)
$propsContent = Get-Content $PropsFile -Raw
if ($currentVersion -ne $newVersion) {
    $propsContent = $propsContent -replace "<Version>$currentVersion</Version>", "<Version>$newVersion</Version>"
    Set-Content $PropsFile -Value $propsContent -NoNewline
    Write-Host "Updated Directory.Build.props with version $newVersion" -ForegroundColor DarkGray
    $versionChanged = $true
} else {
    Write-Host "Version unchanged, rebuilding $currentVersion" -ForegroundColor DarkGray
    $versionChanged = $false
}

# Run tests (unless skipped)
if (-not $SkipTests) {
    Write-Host "Running tests..." -ForegroundColor Green
    dotnet test $SolutionFile --configuration Release --verbosity minimal --nologo
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Tests FAILED! Reverting version change..." -ForegroundColor Red
        if ($versionChanged) {
            $propsContent = $propsContent -replace "<Version>$newVersion</Version>", "<Version>$currentVersion</Version>"
            Set-Content $PropsFile -Value $propsContent -NoNewline
        }
        exit 1
    }
    Write-Host "All tests passed!" -ForegroundColor Green
    Write-Host ""
}

# Clean publish directory
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}

# Publish the server
Write-Host "Publishing release build..." -ForegroundColor Green
$publishArgs = @(
    "publish",
    $ServerProject,
    "--configuration", "Release",
    "--output", $PublishDir,
    "--self-contained", "false",
    "--nologo",
    "/p:PublishSingleFile=false",
    "/p:DebugType=none",
    "/p:DebugSymbols=false"
)

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Publish FAILED!" -ForegroundColor Red
    exit 1
}

Write-Host "Published to: $PublishDir" -ForegroundColor DarkGray
Write-Host ""

# Create ZIP file
$dateStr = Get-Date -Format "yyyy-MM-dd"
$zipFileName = "hugin-irc-server-$dateStr-v$newVersion.zip"
$zipFilePath = Join-Path $ReleasesDir $zipFileName

Write-Host "Creating release archive..." -ForegroundColor Green

# Remove existing ZIP if it exists (same version on same day)
if (Test-Path $zipFilePath) {
    Remove-Item $zipFilePath -Force
}

# Create ZIP
Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipFilePath -CompressionLevel Optimal

if (-not (Test-Path $zipFilePath)) {
    Write-Host "Failed to create ZIP file!" -ForegroundColor Red
    exit 1
}

$zipSize = (Get-Item $zipFilePath).Length / 1MB
Write-Host "Created: $zipFileName ($([math]::Round($zipSize, 2)) MB)" -ForegroundColor Green
Write-Host ""

# Clean up publish directory
Remove-Item -Path $PublishDir -Recurse -Force

# Cleanup old releases (keep files newer than KeepDays, but always keep at least MinKeep)
Write-Host "Cleaning up old releases..." -ForegroundColor Green
$releaseFiles = Get-ChildItem -Path $ReleasesDir -Filter "hugin-irc-server-*.zip" | Sort-Object LastWriteTime -Descending

if ($releaseFiles.Count -gt $MinKeep) {
    $cutoffDate = (Get-Date).AddDays(-$KeepDays)
    $filesToDelete = $releaseFiles | Select-Object -Skip $MinKeep | Where-Object { $_.LastWriteTime -lt $cutoffDate }
    
    foreach ($file in $filesToDelete) {
        Write-Host "  Deleting old release: $($file.Name)" -ForegroundColor DarkGray
        Remove-Item $file.FullName -Force
    }
    
    if ($filesToDelete.Count -eq 0) {
        Write-Host "  No old releases to clean up." -ForegroundColor DarkGray
    } else {
        Write-Host "  Deleted $($filesToDelete.Count) old release(s)." -ForegroundColor DarkGray
    }
} else {
    Write-Host "  Only $($releaseFiles.Count) release(s) exist, keeping all (MinKeep=$MinKeep)." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                   Release Successful!                        ║" -ForegroundColor Green
Write-Host "╠══════════════════════════════════════════════════════════════╣" -ForegroundColor Green
Write-Host "║  Version: $newVersion                                            ║" -ForegroundColor Green
Write-Host "║  File: $zipFileName  ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green

# Update CHANGELOG.md with new version section if not already present
$changelogPath = Join-Path $RootDir "CHANGELOG.md"
if (Test-Path $changelogPath) {
    $changelogContent = Get-Content $changelogPath -Raw
    $todayFormatted = Get-Date -Format "yyyy-MM-dd"
    $newVersionHeader = "## [$newVersion] - $todayFormatted"
    
    if ($changelogContent -notmatch [regex]::Escape("## [$newVersion]")) {
        # Add new version section after [Unreleased]
        $changelogContent = $changelogContent -replace "(\[Unreleased\]\s*\r?\n)", "`$1`n$newVersionHeader`n`n"
        Set-Content $changelogPath -Value $changelogContent -NoNewline
        Write-Host ""
        Write-Host "Added version $newVersion section to CHANGELOG.md" -ForegroundColor DarkGray
    }
}
