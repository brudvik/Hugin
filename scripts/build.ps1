<#
.SYNOPSIS
    Builds the Hugin IRC Server solution.

.DESCRIPTION
    This script builds the entire Hugin IRC Server solution with options for
    different configurations and verbosity levels.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug

.PARAMETER Clean
    Clean before building.

.PARAMETER Restore
    Force restore of NuGet packages.

.PARAMETER Verbosity
    MSBuild verbosity level (quiet, minimal, normal, detailed, diagnostic). Default: minimal

.EXAMPLE
    .\build.ps1
    Builds the solution in Debug configuration.

.EXAMPLE
    .\build.ps1 -Configuration Release -Clean
    Cleans and builds the solution in Release configuration.
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [switch]$Clean,
    
    [switch]$Restore,
    
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$SolutionFile = Join-Path $RootDir "Hugin.sln"

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                 Hugin IRC Server - Build                     ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Solution: $SolutionFile" -ForegroundColor DarkGray
Write-Host ""

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning solution..." -ForegroundColor Green
    dotnet clean $SolutionFile --configuration $Configuration --verbosity $Verbosity --nologo
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Clean failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host ""
}

# Restore if requested
if ($Restore) {
    Write-Host "Restoring NuGet packages..." -ForegroundColor Green
    dotnet restore $SolutionFile --verbosity $Verbosity
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Restore failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host ""
}

# Build
Write-Host "Building solution..." -ForegroundColor Green
$buildArgs = @(
    "build",
    $SolutionFile,
    "--configuration", $Configuration,
    "--verbosity", $Verbosity,
    "--nologo"
)

if (-not $Restore) {
    $buildArgs += "--no-restore"
}

& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Build FAILED!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Build completed successfully!" -ForegroundColor Green

# Run tests
Write-Host ""
Write-Host "Running tests..." -ForegroundColor Green
dotnet test $SolutionFile --configuration $Configuration --no-build --verbosity $Verbosity --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Tests FAILED!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "All tests passed!" -ForegroundColor Green
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                    Build Successful!                         ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
