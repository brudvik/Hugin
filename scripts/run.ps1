<#
.SYNOPSIS
    Starts the Hugin IRC Server.

.DESCRIPTION
    This script builds and runs the Hugin IRC Server with the specified configuration.
    By default, it runs in Development environment.

.PARAMETER Environment
    The environment to run in (Development, Staging, Production). Default: Development

.PARAMETER NoBuild
    Skip building before running.

.EXAMPLE
    .\run.ps1
    Builds and runs the server in Development mode.

.EXAMPLE
    .\run.ps1 -Environment Production -NoBuild
    Runs the server in Production mode without rebuilding.
#>

param(
    [ValidateSet("Development", "Staging", "Production")]
    [string]$Environment = "Development",
    
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ServerProject = Join-Path $RootDir "src\Hugin.Server\Hugin.Server.csproj"

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                    Hugin IRC Server                          ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Set environment
$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:DOTNET_ENVIRONMENT = $Environment

Write-Host "Environment: $Environment" -ForegroundColor Yellow

if (-not $NoBuild) {
    Write-Host ""
    Write-Host "Building..." -ForegroundColor Green
    dotnet build $ServerProject --configuration Debug --nologo
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed!" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Starting Hugin IRC Server..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop the server." -ForegroundColor DarkGray
Write-Host ""

# Run the server
dotnet run --project $ServerProject --configuration Debug --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Host "Server exited with error code: $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}
