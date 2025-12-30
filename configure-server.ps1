# Hugin IRC Server - Configuration Script
# Copyright (c) 2024 Hugin Contributors
# Licensed under the MIT License
#
# This script builds and starts the Hugin IRC Server and opens the admin panel
# in your default web browser for initial configuration.

param(
    [switch]$NoBuild,
    [switch]$NoBrowser,
    [int]$AdminPort = 9443,
    [switch]$Help
)

# Colors for output
$Host.UI.RawUI.WindowTitle = "Hugin IRC Server Configuration"

function Write-Header {
    Write-Host ""
    Write-Host "  🐦 " -NoNewline -ForegroundColor Yellow
    Write-Host "Hugin IRC Server" -ForegroundColor Cyan
    Write-Host "     Configuration Script" -ForegroundColor DarkGray
    Write-Host ""
}

function Write-Step {
    param([string]$Message)
    Write-Host "  → " -NoNewline -ForegroundColor Green
    Write-Host $Message
}

function Write-Info {
    param([string]$Message)
    Write-Host "    " -NoNewline
    Write-Host $Message -ForegroundColor DarkGray
}

function Write-Error-Custom {
    param([string]$Message)
    Write-Host "  ✗ " -NoNewline -ForegroundColor Red
    Write-Host $Message -ForegroundColor Red
}

function Write-Success {
    param([string]$Message)
    Write-Host "  ✓ " -NoNewline -ForegroundColor Green
    Write-Host $Message -ForegroundColor Green
}

function Show-Help {
    Write-Header
    Write-Host "  Usage: .\configure-server.ps1 [options]" -ForegroundColor White
    Write-Host ""
    Write-Host "  Options:" -ForegroundColor Yellow
    Write-Host "    -NoBuild      Skip building the solution"
    Write-Host "    -NoBrowser    Do not open the browser automatically"
    Write-Host "    -AdminPort    Admin panel port (default: 9443)"
    Write-Host "    -Help         Show this help message"
    Write-Host ""
    Write-Host "  Examples:" -ForegroundColor Yellow
    Write-Host "    .\configure-server.ps1"
    Write-Host "    .\configure-server.ps1 -NoBuild"
    Write-Host "    .\configure-server.ps1 -AdminPort 8443"
    Write-Host ""
    exit 0
}

function Test-DotNetInstalled {
    try {
        $version = dotnet --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            return $true
        }
    } catch { }
    return $false
}

function Wait-ForServer {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 30,
        [System.Diagnostics.Process]$Process = $null
    )
    
    $elapsed = 0
    $interval = 1
    
    while ($elapsed -lt $TimeoutSeconds) {
        # Check if process crashed
        if ($Process -and $Process.HasExited) {
            return "crashed"
        }
        
        try {
            # Ignore SSL certificate errors for development
            [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
            
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 401) {
                return "ready"
            }
        } catch {
            # Server not ready yet, continue waiting
        }
        
        Start-Sleep -Seconds $interval
        $elapsed += $interval
        Write-Host "." -NoNewline -ForegroundColor DarkGray
    }
    
    return "timeout"
}

# Main script
if ($Help) {
    Show-Help
}

Write-Header

# Check .NET SDK
Write-Step "Checking .NET SDK..."
if (-not (Test-DotNetInstalled)) {
    Write-Error-Custom ".NET SDK is not installed or not in PATH"
    Write-Info "Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download"
    exit 1
}
$dotnetVersion = dotnet --version
Write-Info ".NET SDK $dotnetVersion found"

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Build solution
if (-not $NoBuild) {
    Write-Step "Building solution..."
    $buildResult = dotnet build --configuration Debug 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Build failed!"
        Write-Host $buildResult -ForegroundColor Red
        exit 1
    }
    Write-Success "Build completed successfully"
} else {
    Write-Info "Skipping build (using -NoBuild)"
}

# Check if server is already running
$existingProcess = Get-Process -Name "Hugin.Server" -ErrorAction SilentlyContinue
if ($existingProcess) {
    Write-Step "Stopping existing Hugin server..."
    Stop-Process -Name "Hugin.Server" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# Start the server
Write-Step "Starting Hugin IRC Server..."
$serverPath = Join-Path $scriptDir "src\Hugin.Server"

# Start server and capture output for error detection
$logFile = Join-Path $env:TEMP "hugin-startup.log"
$serverProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $serverPath, "--no-build" `
    -WindowStyle Hidden `
    -RedirectStandardOutput $logFile `
    -RedirectStandardError "$logFile.err" `
    -PassThru

Write-Info "Server process started (PID: $($serverProcess.Id))"

Write-Step "Waiting for server to start..."
Write-Host "    " -NoNewline

# Wait for server to start by checking the log file for success message
$serverStatus = "timeout"
$elapsed = 0
$maxWait = 30

while ($elapsed -lt $maxWait) {
    # Check if process crashed
    if ($serverProcess.HasExited) {
        $serverStatus = "crashed"
        break
    }
    
    # Check log for success message
    if (Test-Path $logFile) {
        $logContent = Get-Content $logFile -Raw -ErrorAction SilentlyContinue
        if ($logContent -match "Server started successfully") {
            $serverStatus = "ready"
            break
        }
        if ($logContent -match "FTL\]|FATAL|Host terminated unexpectedly") {
            $serverStatus = "crashed"
            break
        }
    }
    
    Start-Sleep -Seconds 1
    $elapsed++
    Write-Host "." -NoNewline -ForegroundColor DarkGray
}

Write-Host ""

# Check if process exited (crashed)
if ($serverStatus -eq "crashed" -or $serverProcess.HasExited) {
    Write-Error-Custom "Server crashed during startup!"
    Write-Host ""
    
    # Read error log
    $errorContent = ""
    if (Test-Path "$logFile.err") {
        $errorContent = Get-Content "$logFile.err" -Raw -ErrorAction SilentlyContinue
    }
    if (-not $errorContent -and (Test-Path $logFile)) {
        $errorContent = Get-Content $logFile -Raw -ErrorAction SilentlyContinue
    }
    
    # Check for common errors and provide helpful messages
    # Order matters - check more specific patterns first
    if ($errorContent -match "password authentication failed|28P01") {
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Red
        Write-Host "  ║  " -NoNewline -ForegroundColor Red
        Write-Host "DATABASE CONNECTION FAILED" -NoNewline -ForegroundColor White
        Write-Host "                                  ║" -ForegroundColor Red
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Red
        Write-Host ""
        Write-Host "  PostgreSQL authentication failed. Please set up the database:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  1. Make sure PostgreSQL is installed and running" -ForegroundColor DarkGray
        Write-Host "  2. Create the database and user:" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "     psql -U postgres" -ForegroundColor Cyan
        Write-Host "     CREATE USER hugin WITH PASSWORD 'hugin';" -ForegroundColor Cyan
        Write-Host "     CREATE DATABASE hugin_dev OWNER hugin;" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  Or update the connection string in:" -ForegroundColor DarkGray
        Write-Host "     src\Hugin.Server\appsettings.Development.json" -ForegroundColor Cyan
        Write-Host ""
    }
    elseif ($errorContent -match "permission denied for schema|42501") {
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Red
        Write-Host "  ║  " -NoNewline -ForegroundColor Red
        Write-Host "DATABASE PERMISSION DENIED" -NoNewline -ForegroundColor White
        Write-Host "                                  ║" -ForegroundColor Red
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Red
        Write-Host ""
        Write-Host "  The 'hugin' user lacks permissions to create tables." -ForegroundColor Yellow
        Write-Host "  Run these commands in PostgreSQL (pgAdmin or psql):" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "     GRANT ALL ON SCHEMA public TO hugin;" -ForegroundColor Cyan
        Write-Host "     GRANT ALL PRIVILEGES ON DATABASE hugin_dev TO hugin;" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  Or make hugin the database owner:" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "     ALTER DATABASE hugin_dev OWNER TO hugin;" -ForegroundColor Cyan
        Write-Host ""
    }
    elseif ($errorContent -match "connection refused|could not connect|No such host") {
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Red
        Write-Host "  ║  " -NoNewline -ForegroundColor Red
        Write-Host "DATABASE SERVER NOT RUNNING" -NoNewline -ForegroundColor White
        Write-Host "                                 ║" -ForegroundColor Red
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Cannot connect to PostgreSQL. Please ensure:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  1. PostgreSQL is installed" -ForegroundColor DarkGray
        Write-Host "  2. The PostgreSQL service is running:" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "     net start postgresql-x64-14" -ForegroundColor Cyan
        Write-Host "     # or check Services (services.msc)" -ForegroundColor DarkGray
        Write-Host ""
    }
    elseif ($errorContent -match "(?<!permission denied.*)certificate|(?<!permission denied.*)ssl error|tls handshake") {
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Red
        Write-Host "  ║  " -NoNewline -ForegroundColor Red
        Write-Host "TLS/CERTIFICATE ERROR" -NoNewline -ForegroundColor White
        Write-Host "                                       ║" -ForegroundColor Red
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Red
        Write-Host ""
        Write-Host "  There was an issue with TLS certificates." -ForegroundColor Yellow
        Write-Host "  Check src\Hugin.Server\appsettings.Development.json" -ForegroundColor DarkGray
        Write-Host ""
    }
    elseif ($errorContent -match "MissingMethodException|No parameterless constructor") {
        Write-Host "  ╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Red
        Write-Host "  ║  " -NoNewline -ForegroundColor Red
        Write-Host "CODE ERROR - MISSING CONSTRUCTOR" -NoNewline -ForegroundColor White
        Write-Host "                        ║" -ForegroundColor Red
        Write-Host "  ╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Red
        Write-Host ""
        Write-Host "  A command handler is missing a required constructor." -ForegroundColor Yellow
        Write-Host "  This is a code issue. Please rebuild with:" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "     dotnet build" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "  If the issue persists, check the error details below." -ForegroundColor DarkGray
        Write-Host ""
        # Show the specific error
        if ($errorContent -match "type '([^']+)'") {
            Write-Host "  Affected type: $($Matches[1])" -ForegroundColor Yellow
        }
        Write-Host ""
    }
    else {
        Write-Host "  Error details:" -ForegroundColor Yellow
        if ($errorContent) {
            # Show last 10 lines of error
            $lines = $errorContent -split "`n" | Select-Object -Last 10
            foreach ($line in $lines) {
                Write-Host "    $line" -ForegroundColor DarkGray
            }
        }
        else {
            Write-Host "    No error details captured." -ForegroundColor DarkGray
        }
        Write-Host ""
    }
    
    # Cleanup temp files
    Remove-Item $logFile -ErrorAction SilentlyContinue
    Remove-Item "$logFile.err" -ErrorAction SilentlyContinue
    
    exit 1
}

if ($serverStatus -eq "timeout") {
    Write-Error-Custom "Server failed to start within 30 seconds"
    Write-Info "The server process is running but no startup confirmation was logged."
    Write-Info "Check the log file for more details."
    
    # Show last few lines of log
    if (Test-Path $logFile) {
        Write-Host ""
        Write-Host "  Log output:" -ForegroundColor Yellow
        Get-Content $logFile -Tail 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    }
    
    # Cleanup
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $logFile -ErrorAction SilentlyContinue
    Remove-Item "$logFile.err" -ErrorAction SilentlyContinue
    
    exit 1
}

# Server started successfully - restart it in visible window for user interaction
Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
Remove-Item $logFile -ErrorAction SilentlyContinue
Remove-Item "$logFile.err" -ErrorAction SilentlyContinue

# Now start in visible window
$serverProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $serverPath, "--no-build" `
    -WindowStyle Normal `
    -PassThru

Write-Info "Server running in console window (PID: $($serverProcess.Id))"

Write-Success "Server is running!"

# Open admin panel in browser
$adminUrl = "https://localhost:$AdminPort"
$setupUrl = "$adminUrl/admin/setup"

if (-not $NoBrowser) {
    Write-Step "Opening admin panel in browser..."
    Start-Sleep -Seconds 2  # Give server time to fully start
    Start-Process $setupUrl
}

# Display summary
Write-Host ""
Write-Host "  ═══════════════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  " -NoNewline
Write-Host "Hugin IRC Server is running!" -ForegroundColor Green
Write-Host ""
Write-Host "  Admin Panel:    " -NoNewline -ForegroundColor Yellow
Write-Host "$adminUrl/admin" -ForegroundColor Cyan
Write-Host "  Setup Wizard:   " -NoNewline -ForegroundColor Yellow
Write-Host "$setupUrl" -ForegroundColor Cyan
Write-Host ""
Write-Host "  IRC Port:       " -NoNewline -ForegroundColor Yellow
Write-Host "6697 (TLS)" -ForegroundColor Cyan
Write-Host "  Server Name:    " -NoNewline -ForegroundColor Yellow
Write-Host "irc.hugin.local" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Default Login:" -ForegroundColor Yellow
Write-Host "    Username:     " -NoNewline -ForegroundColor DarkGray
Write-Host "admin" -ForegroundColor White
Write-Host "    Password:     " -NoNewline -ForegroundColor DarkGray
Write-Host "admin" -ForegroundColor White
Write-Host ""
Write-Host "  ⚠️  " -NoNewline -ForegroundColor Yellow
Write-Host "Change the default password after first login!" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Press " -NoNewline -ForegroundColor DarkGray
Write-Host "Ctrl+C" -NoNewline -ForegroundColor White
Write-Host " in the server window to stop the server." -ForegroundColor DarkGray
Write-Host ""
Write-Host "  ═══════════════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host ""
