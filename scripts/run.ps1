# Hugin IRC Server - Run Script
# Copyright (c) 2024 Hugin Contributors
# Licensed under the MIT License
#
# This script starts a pre-configured Hugin IRC Server instance and opens
# the admin panel in your default browser.

<#
.SYNOPSIS
    Starts the Hugin IRC Server.

.DESCRIPTION
    This script starts an already configured Hugin IRC Server.
    If the server is not configured, it will prompt you to run configure-server.ps1 first.
    The admin panel will automatically open in your default browser.

.PARAMETER Environment
    The environment to run in (Development, Staging, Production). Default: Development

.PARAMETER NoBuild
    Skip building before running.

.PARAMETER NoBrowser
    Do not open the browser automatically.

.PARAMETER AdminPort
    Admin panel port (default: 9443)

.EXAMPLE
    .\run.ps1
    Builds and runs the server in Development mode with admin panel.

.EXAMPLE
    .\run.ps1 -NoBuild -NoBrowser
    Runs the server without rebuilding or opening browser.

.EXAMPLE
    .\run.ps1 -Environment Production
    Runs the server in Production mode.
#>

param(
    [ValidateSet("Development", "Staging", "Production")]
    [string]$Environment = "Development",
    
    [switch]$NoBuild,
    [switch]$NoBrowser,
    [int]$AdminPort = 9443
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ServerProject = Join-Path $RootDir "src\Hugin.Server\Hugin.Server.csproj"
$ServerDir = Join-Path $RootDir "src\Hugin.Server"
$AppSettingsPath = Join-Path $ServerDir "appsettings.json"

# Colors for output
$Host.UI.RawUI.WindowTitle = "Hugin IRC Server"

function Write-Header {
    Write-Host ""
    Write-Host "  Hugin IRC Server" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([string]$Message)
    Write-Host "  -> " -NoNewline -ForegroundColor Green
    Write-Host $Message
}

function Write-Info {
    param([string]$Message)
    Write-Host "     " -NoNewline
    Write-Host $Message -ForegroundColor DarkGray
}

function Write-Error-Custom {
    param([string]$Message)
    Write-Host "  [X] " -NoNewline -ForegroundColor Red
    Write-Host $Message -ForegroundColor Red
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] " -NoNewline -ForegroundColor Green
    Write-Host $Message -ForegroundColor Green
}

function Test-ServerConfigured {
    # Check if appsettings.json exists
    if (-not (Test-Path $AppSettingsPath)) {
        return $false
    }
    
    # Try to read and validate configuration
    try {
        $config = Get-Content $AppSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        
        # Check if essential configuration exists
        if (-not $config.Hugin) {
            return $false
        }
        
        if (-not $config.Hugin.Server) {
            return $false
        }
        
        if (-not $config.Hugin.Database) {
            return $false
        }
        
        # Check if database connection string is set and not empty
        $connString = $config.Hugin.Database.ConnectionString
        if ([string]::IsNullOrWhiteSpace($connString)) {
            return $false
        }
        
        # Consider it configured if it has a real connection string
        return $true
    }
    catch {
        return $false
    }
}

Write-Header

# Check if server is configured
Write-Step "Checking server configuration..."
if (-not (Test-ServerConfigured)) {
    Write-Host ""
    Write-Host "  +--------------------------------------------------------------+" -ForegroundColor Yellow
    Write-Host "  |  SERVER NOT CONFIGURED                                       |" -ForegroundColor Yellow
    Write-Host "  +--------------------------------------------------------------+" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  The server must be configured before it can run." -ForegroundColor DarkGray
    Write-Host "  Run the configuration script first:" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "     .\configure-server.ps1" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  This will:" -ForegroundColor DarkGray
    Write-Host "    - Build the solution" -ForegroundColor DarkGray
    Write-Host "    - Start the server" -ForegroundColor DarkGray
    Write-Host "    - Open the admin panel for setup" -ForegroundColor DarkGray
    Write-Host ""
    exit 1
}
Write-Info "Configuration found"

# Set environment
$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:DOTNET_ENVIRONMENT = $Environment
Write-Info "Environment: $Environment"

# Build if needed
if (-not $NoBuild) {
    Write-Step "Building solution..."
    $buildOutput = dotnet build $ServerProject --configuration Debug --nologo --verbosity quiet 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Build failed!"
        Write-Host $buildOutput -ForegroundColor Red
        exit 1
    }
    Write-Success "Build completed"
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

# Start server and capture output for error detection
$logFile = Join-Path $env:TEMP "hugin-startup.log"
$errorLogFile = Join-Path $env:TEMP "hugin-startup-error.log"

$serverProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $ServerProject, "--no-build" `
    -WindowStyle Hidden `
    -RedirectStandardOutput $logFile `
    -RedirectStandardError $errorLogFile `
    -PassThru

Write-Info "Server started (PID: $($serverProcess.Id))"

# Wait for server to start by checking the log file
Write-Step "Waiting for server to start..."
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
        if ($logContent -match "Server started successfully|Now listening on|Application started") {
            $serverStatus = "ready"
            break
        }
        if ($logContent -match "FTL\]|FATAL|Host terminated unexpectedly|Unhandled exception") {
            $serverStatus = "crashed"
            break
        }
    }
    
    Start-Sleep -Seconds 1
    $elapsed++
    Write-Host "." -NoNewline -ForegroundColor DarkGray
}
Write-Host ""

# Check if server crashed
if ($serverStatus -eq "crashed" -or $serverProcess.HasExited) {
    Write-Error-Custom "Server crashed during startup!"
    Write-Host ""
    
    # Read error logs
    $errorContent = ""
    if (Test-Path $errorLogFile) {
        $errorContent = Get-Content $errorLogFile -Raw -ErrorAction SilentlyContinue
    }
    if (-not $errorContent -and (Test-Path $logFile)) {
        $errorContent = Get-Content $logFile -Raw -ErrorAction SilentlyContinue
    }
    
    # Check for common errors
    if ($errorContent -match "password authentication failed|28P01") {
        Write-Host "  DATABASE CONNECTION FAILED" -ForegroundColor White
        Write-Host ""
        Write-Host "  PostgreSQL authentication failed. Please check:" -ForegroundColor Yellow
        Write-Host "    - Database user and password in appsettings.json" -ForegroundColor DarkGray
        Write-Host "    - PostgreSQL is running" -ForegroundColor DarkGray
        Write-Host ""
    }
    elseif ($errorContent -match "permission denied for schema|42501") {
        Write-Host "  DATABASE PERMISSION DENIED" -ForegroundColor White
        Write-Host ""
        Write-Host "  The database user lacks permissions." -ForegroundColor Yellow
        Write-Host "  Grant permissions with: GRANT ALL ON SCHEMA public TO hugin;" -ForegroundColor DarkGray
        Write-Host ""
    }
    elseif ($errorContent -match "connection refused|could not connect|No such host") {
        Write-Host "  DATABASE SERVER NOT RUNNING" -ForegroundColor White
        Write-Host ""
        Write-Host "  Cannot connect to PostgreSQL. Please ensure:" -ForegroundColor Yellow
        Write-Host "    - PostgreSQL is installed and running" -ForegroundColor DarkGray
        Write-Host "    - Connection string in appsettings.json is correct" -ForegroundColor DarkGray
        Write-Host ""
    }
    elseif ($errorContent -match "Address already in use|port.*in use") {
        Write-Host "  PORT ALREADY IN USE" -ForegroundColor White
        Write-Host ""
        Write-Host "  Another process is using the required port." -ForegroundColor Yellow
        Write-Host "  Check if another Hugin instance is running." -ForegroundColor DarkGray
        Write-Host ""
    }
    else {
        Write-Host "  Error details:" -ForegroundColor Yellow
        if ($errorContent) {
            # Show last 15 lines of error
            $lines = $errorContent -split "`n" | Select-Object -Last 15
            foreach ($line in $lines) {
                if ($line.Trim()) {
                    Write-Host "    $line" -ForegroundColor DarkGray
                }
            }
        }
        else {
            Write-Host "    No error details captured. Check Windows Event Log." -ForegroundColor DarkGray
        }
        Write-Host ""
    }
    
    # Cleanup temp files
    Remove-Item $logFile -ErrorAction SilentlyContinue
    Remove-Item $errorLogFile -ErrorAction SilentlyContinue
    
    exit 1
}

if ($serverStatus -eq "timeout") {
    Write-Host "  Server is taking longer than expected to start..." -ForegroundColor Yellow
    Write-Host "  Continuing anyway. Check the server window for status." -ForegroundColor DarkGray
}

# Server started successfully - restart it in visible window
Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# Clean up temp files
Remove-Item $logFile -ErrorAction SilentlyContinue
Remove-Item $errorLogFile -ErrorAction SilentlyContinue

# Now start in visible window so user can see output
$serverProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--project", $ServerProject, "--no-build" `
    -WindowStyle Normal `
    -PassThru

Write-Info "Server running in console window (PID: $($serverProcess.Id))"

Write-Success "Server is running!"

# Open admin panel in browser
$adminUrl = "https://localhost:$AdminPort"

if (-not $NoBrowser) {
    Write-Step "Opening admin panel..."
    Start-Sleep -Seconds 1
    Start-Process "$adminUrl/admin"
    Write-Info "Admin panel opened in browser"
}

# Display summary
Write-Host ""
Write-Host "  ============================================================" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Hugin IRC Server is running!" -ForegroundColor Green
Write-Host ""
Write-Host "  Admin Panel:    " -NoNewline -ForegroundColor Yellow
Write-Host "$adminUrl/admin" -ForegroundColor Cyan
Write-Host ""
Write-Host "  IRC Port:       " -NoNewline -ForegroundColor Yellow
Write-Host "6697 (TLS)" -ForegroundColor Cyan
Write-Host "  Server Name:    " -NoNewline -ForegroundColor Yellow
Write-Host "irc.hugin.local" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Press Ctrl+C in the server window to stop." -ForegroundColor DarkGray
Write-Host ""
Write-Host "  ============================================================" -ForegroundColor DarkGray
Write-Host ""
