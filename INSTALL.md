# 🚀 Hugin IRC Server - Installation Guide

This guide walks you through installing and setting up Hugin IRC Server from scratch. Whether you're a beginner or experienced system administrator, these step-by-step instructions will get your IRC server running quickly.

## 📋 Table of Contents

- [Prerequisites](#prerequisites)
- [Installation Steps](#installation-steps)
  - [1. Install .NET 8 SDK](#1-install-net-8-sdk)
  - [2. Install PostgreSQL](#2-install-postgresql)
  - [3. Download Hugin](#3-download-hugin)
  - [4. Configure PostgreSQL Database](#4-configure-postgresql-database)
  - [5. Run Configuration Script](#5-run-configuration-script)
  - [6. Complete Web Setup](#6-complete-web-setup)
  - [7. Connect with IRC Client](#7-connect-with-irc-client)
- [Troubleshooting](#troubleshooting)
- [Next Steps](#next-steps)

---

## Prerequisites

Before installing Hugin, ensure your system meets these requirements:

### System Requirements
- **Operating System**: Windows 10/11, Windows Server 2019+, Linux, or macOS
- **RAM**: Minimum 2 GB, recommended 4 GB or more
- **Disk Space**: At least 500 MB for application and logs
- **Network**: Open ports for IRC (6697) and Admin Panel (9443)

### Required Software
| Software | Version | Purpose |
|----------|---------|---------|
| .NET SDK | 8.0 or later | Running the server |
| PostgreSQL | 14 or later | Database backend |

---

## Installation Steps

### 1. Install .NET 8 SDK

Hugin requires the .NET 8 SDK to build and run.

#### Windows
1. Download the .NET 8 SDK from [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. Run the installer (`dotnet-sdk-8.x.x-win-x64.exe`)
3. Follow the installation wizard
4. Verify installation by opening PowerShell and running:
   ```powershell
   dotnet --version
   ```
   You should see `8.0.x` or higher

#### Linux (Ubuntu/Debian)
```bash
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
```

#### macOS
```bash
brew install dotnet@8
```

---

### 2. Install PostgreSQL

PostgreSQL is used for persistent storage of user accounts, channels, and message history.

#### Windows
1. Download PostgreSQL from [https://www.postgresql.org/download/windows/](https://www.postgresql.org/download/windows/)
2. Run the installer (`postgresql-xx.x-windows-x64.exe`)
3. During installation:
   - **Password**: Choose a strong password for the `postgres` superuser (remember this!)
   - **Port**: Use default port `5432`
   - **Locale**: Select your preferred locale
4. Ensure "PostgreSQL Server" is checked to run as a service
5. Verify PostgreSQL is running:
   ```powershell
   # Check service status
   Get-Service postgresql*
   
   # Should show "Running"
   ```

#### Linux (Ubuntu/Debian)
```bash
sudo apt update
sudo apt install postgresql postgresql-contrib
sudo systemctl start postgresql
sudo systemctl enable postgresql
```

#### macOS
```bash
brew install postgresql@14
brew services start postgresql@14
```

---

### 3. Download Hugin

Choose one of the following methods to get Hugin:

#### Option A: Clone from Git (Recommended for Development)
```bash
git clone https://github.com/brudvik/hugin.git
cd hugin
```

#### Option B: Download Release (Recommended for Production)
1. Go to [Releases](https://github.com/brudvik/hugin/releases)
2. Download the latest `hugin-irc-server-*.zip`
3. Extract to a folder like `C:\Hugin` or `/opt/hugin`

#### Option C: Download Source ZIP
1. Visit [https://github.com/brudvik/hugin](https://github.com/brudvik/hugin)
2. Click **Code** → **Download ZIP**
3. Extract the archive

---

### 4. Configure PostgreSQL Database

Create a dedicated database and user for Hugin.

#### Windows (using pgAdmin or psql)

**Option 1: Using pgAdmin GUI**
1. Open **pgAdmin 4** (installed with PostgreSQL)
2. Connect to PostgreSQL (use the password you set during installation)
3. Right-click **Databases** → **Create** → **Database**
   - Database name: `hugin`
   - Owner: `postgres`
   - Click **Save**
4. Right-click **Login/Group Roles** → **Create** → **Login/Group Role**
   - Name: `hugin`
   - **Definition** tab: Password: `hugin` (or choose your own secure password)
   - **Privileges** tab: Check "Can login?"
   - Click **Save**
5. Right-click the `hugin` database → **Properties** → **Security** tab
   - Add `hugin` user with all privileges

**Option 2: Using psql Command Line**
```powershell
# Open PowerShell and run:
psql -U postgres

# In psql, run these commands:
CREATE USER hugin WITH PASSWORD 'hugin';
CREATE DATABASE hugin OWNER hugin;
GRANT ALL PRIVILEGES ON DATABASE hugin TO hugin;
\q
```

#### Linux/macOS
```bash
sudo -u postgres psql

# In psql, run:
CREATE USER hugin WITH PASSWORD 'hugin';
CREATE DATABASE hugin OWNER hugin;
GRANT ALL PRIVILEGES ON DATABASE hugin TO hugin;
\q
```

> **🔒 Security Note**: For production environments, use a strong, randomly generated password instead of `hugin`.

---

### 5. Run Configuration Script

Hugin includes a convenient setup script that builds the server and launches it.

#### Windows

Open **PowerShell** in the Hugin directory and run:

```powershell
.\configure-server.ps1
```

**What this script does:**
1. ✅ Checks that .NET SDK is installed
2. 🔨 Builds the Hugin solution
3. 🗄️ Runs database migrations automatically
4. 🚀 Starts the server in a console window
5. 🌐 Opens the admin panel in your default browser

**Command Options:**
```powershell
# Skip building if already built
.\configure-server.ps1 -NoBuild

# Don't open browser automatically
.\configure-server.ps1 -NoBrowser

# Use custom admin panel port
.\configure-server.ps1 -AdminPort 8443

# Show help
.\configure-server.ps1 -Help
```

#### Linux/macOS

```bash
# Build the solution
dotnet build

# Run the server
dotnet run --project src/Hugin.Server
```

The server will start and display:
```
info: Hugin.Server.Program[0]
      🐦 Hugin IRC Server starting...
info: Hugin.Server.Services.HuginServerService[0]
      Running database migrations...
info: Hugin.Server.Services.HuginServerService[0]
      Server started successfully
      IRC Listener: 0.0.0.0:6697 (TLS)
      Admin Panel: https://localhost:9443/admin
```

---

### 6. Complete Web Setup

After running the configuration script, the **Setup Wizard** will open in your browser at:
```
https://localhost:9443/admin/setup
```

> **⚠️ Certificate Warning**: Since Hugin generates a self-signed certificate by default, your browser will show a security warning. Click **Advanced** → **Proceed to localhost** (safe for local development).

#### Setup Wizard Steps

**Step 1: Welcome**
- Click **Start Setup** to begin

**Step 2: Server Configuration**
- **Server Name**: `irc.example.com` (change to your domain or hostname)
- **Network Name**: `ExampleNet` (your IRC network name)
- **Description**: Brief description of your server
- **Admin Email**: Your email address
- Click **Next**

**Step 3: TLS/Security**
- For **development/testing**:
  - Keep **"Use Self-Signed Certificate"** enabled
  - Click **Generate Certificate**
- For **production**:
  - Disable self-signed option
  - **Certificate Path**: Path to your `.pfx` file
  - **Certificate Password**: Password for the certificate
  - Consider using Let's Encrypt or a commercial CA
- Click **Next**

**Step 4: Database**
- The default connection string should work if you followed step 4:
  ```
  Host=localhost;Database=hugin;Username=hugin;Password=hugin
  ```
- Click **Test Connection** to verify
- If successful, click **Next**

**Step 5: Administrator Account**
- **Username**: Your admin username (e.g., `admin`)
- **Password**: Choose a **strong password** (minimum 8 characters)
- **Confirm Password**: Re-enter password
- This account will have full server administrator privileges
- Click **Complete Setup**

**Setup Complete!**
- The setup wizard will save your configuration
- You'll be redirected to the login page
- Use the admin credentials you just created to log in

---

### 7. Connect with IRC Client

Your Hugin IRC server is now running! Time to connect with an IRC client.

#### Recommended IRC Clients
- **[Munin](https://github.com/brudvik/munin)** - Modern IRC client (Hugin's companion project)
- **HexChat** - Popular cross-platform client
- **WeeChat** - Terminal-based client (Linux/macOS)
- **mIRC** - Classic Windows IRC client
- **Textual** - macOS native client

#### Connection Settings

| Setting | Value |
|---------|-------|
| **Server** | `localhost` or `irc.example.com` |
| **Port** | `6697` |
| **TLS/SSL** | ✅ **Enabled** (required) |
| **Accept Invalid Certificate** | ✅ Enable (for self-signed cert) |
| **Username** | Your desired nickname |
| **Password** | Leave empty (for now) |

#### Example: Connecting with HexChat

1. Open HexChat
2. Add a new network:
   - Name: `Hugin Local`
   - Server: `localhost/6697`
   - Check ✅ **Use SSL for all servers**
   - Check ✅ **Accept invalid SSL certificate**
3. Click **Connect**
4. You should see the Hugin MOTD (Message of the Day)

#### First Commands to Try

Once connected, try these commands:

```irc
/join #general          # Join a channel
/msg NickServ help      # Get help from NickServ
/msg ChanServ help      # Get help from ChanServ
/whois YourNick         # Get info about yourself
```

#### Register Your Nickname

To register your nickname permanently:

```irc
/msg NickServ REGISTER your-password your-email@example.com
```

---

## Troubleshooting

### Server Won't Start

**"Database connection failed"**
- Ensure PostgreSQL service is running:
  ```powershell
  # Windows
  Get-Service postgresql*
  net start postgresql-x64-14
  
  # Linux
  sudo systemctl status postgresql
  sudo systemctl start postgresql
  ```
- Verify database credentials in `src/Hugin.Server/appsettings.Development.json`
- Check PostgreSQL logs: `C:\Program Files\PostgreSQL\14\data\log\` (Windows)

**"Database permission denied"**
```sql
-- Run in psql as postgres user:
ALTER DATABASE hugin OWNER TO hugin;
GRANT ALL ON SCHEMA public TO hugin;
```

**"Certificate error" or "TLS handshake failed"**
- Enable self-signed certificate in setup wizard
- Or provide a valid `.pfx` certificate file
- For testing, you can disable TLS requirement (not recommended):
  ```json
  "Security": {
    "RequireTls": false
  }
  ```

**"Port 6697 already in use"**
- Another IRC server or application is using the port
- Change the port in `appsettings.json`:
  ```json
  "Listeners": [
    {
      "Port": 6698,  // Different port
      "Tls": true
    }
  ]
  ```

### Can't Access Admin Panel

**"Unable to connect to https://localhost:9443"**
- Verify the server is running (check the console window)
- Check firewall isn't blocking port 9443
- Try accessing from the same machine first
- Review the server logs in `src/Hugin.Server/logs/`

**"Connection refused"**
```powershell
# Check if port is listening:
netstat -an | findstr :9443    # Windows
netstat -tuln | grep 9443      # Linux
```

**Browser certificate warning**
- This is normal for self-signed certificates
- Click **Advanced** → **Proceed to localhost (unsafe)**
- For production, use a proper SSL certificate

### IRC Client Won't Connect

**"Connection refused"**
- Ensure server is running
- Check IRC port (6697) is correct
- Verify firewall allows port 6697

**"TLS/SSL error"**
- Enable "Accept invalid SSL certificate" in client settings
- Ensure client supports TLS 1.2 or higher
- Try connecting without TLS to port 6667 (if enabled) for testing

**"Registration timeout"**
- Network issue - check server is accessible
- Ensure client is sending proper IRC protocol messages
- Review server logs for connection attempts

### Database Issues

**"Password authentication failed"**
```sql
-- Reset password in psql:
ALTER USER hugin WITH PASSWORD 'new-password';
```
Then update `appsettings.Development.json` with the new password.

**"No such database"**
```sql
-- Recreate database:
CREATE DATABASE hugin OWNER hugin;
```

**"Migration failed"**
- Delete database and recreate:
  ```sql
  DROP DATABASE hugin;
  CREATE DATABASE hugin OWNER hugin;
  ```
- The server will recreate all tables on next startup

---

## Next Steps

### Secure Your Server

1. **Change Default Passwords**
   - Admin panel password
   - Database password
   - Update `appsettings.json` with new credentials

2. **Get a Real TLS Certificate**
   - Use [Let's Encrypt](https://letsencrypt.org/) (free)
   - Or purchase from a commercial CA
   - Convert to `.pfx` format: 
     ```bash
     openssl pkcs12 -export -out certificate.pfx -inkey private.key -in certificate.crt
     ```

3. **Configure Firewall**
   ```powershell
   # Windows Firewall
   New-NetFirewallRule -DisplayName "IRC Server" -Direction Inbound -Protocol TCP -LocalPort 6697 -Action Allow
   New-NetFirewallRule -DisplayName "IRC Admin" -Direction Inbound -Protocol TCP -LocalPort 9443 -Action Allow
   ```

4. **Set Up Rate Limiting**
   - Review and adjust rate limits in [appsettings.json](src/Hugin.Server/appsettings.json)
   - See [Configuration Guide](docs/CONFIGURATION.md)

### Customize Your Server

- **Edit MOTD**: Customize the welcome message in `appsettings.json` → `Motd`
- **Configure Services**: Enable/disable NickServ, ChanServ, etc.
- **Add Operators**: Use `/oper username password` after creating operator accounts
- **Set Up Channels**: Register channels with `/msg ChanServ REGISTER #channel`

### Enable Advanced Features

- **Server Linking**: Connect multiple servers - see [S2S Protocol Guide](docs/S2S_PROTOCOL.md)
- **Lua Scripting**: Write custom scripts in `scripts/` folder
- **JSON Triggers**: Create automated responses in `triggers/` folder
- **Plugins**: Develop C# plugins for advanced functionality

### Production Deployment

1. **Use PostgreSQL in Production Mode**
   - Create strong database password
   - Enable SSL for database connections
   - Set up regular backups

2. **Run as a Service**
   ```powershell
   # Windows Service
   sc create Hugin binPath="C:\Hugin\Hugin.Server.exe"
   sc start Hugin
   ```

3. **Reverse Proxy** (Optional)
   - Use Nginx or IIS for TLS termination
   - Proxy requests to Hugin admin panel

4. **Monitoring**
   - Enable metrics endpoint in `appsettings.json`
   - Use Prometheus or similar for monitoring
   - Set up log aggregation (Serilog sinks)

### Resources

- 📖 **[Configuration Guide](docs/CONFIGURATION.md)** - Comprehensive configuration reference
- 🎛️ **[Admin Panel Guide](docs/ADMIN_PANEL.md)** - Web interface documentation
- 🔗 **[Server Linking](docs/S2S_PROTOCOL.md)** - Connect multiple Hugin servers
- 📜 **[RFC Compliance](docs/RFC_COMPLIANCE.md)** - Supported IRC standards

### Get Help

- 💬 **IRC Channel**: Join `#hugin` on your server
- 🐛 **Report Issues**: [GitHub Issues](https://github.com/brudvik/hugin/issues)
- 📚 **Documentation**: [GitHub Wiki](https://github.com/brudvik/hugin/wiki)
- 💡 **Discussions**: [GitHub Discussions](https://github.com/brudvik/hugin/discussions)

---

## Quick Reference Card

### Default Ports
| Service | Port | Protocol |
|---------|------|----------|
| IRC (TLS) | 6697 | IRC/TLS |
| IRC (Plain) | 6667 | IRC |
| Admin Panel | 9443 | HTTPS |

### Default Credentials
| Service | Username | Password |
|---------|----------|----------|
| Admin Panel | `admin` | *Set during setup* |
| Database | `hugin` | `hugin` |
| PostgreSQL Admin | `postgres` | *Set during PostgreSQL installation* |

### Important Files
| File | Purpose |
|------|---------|
| `appsettings.json` | Main configuration |
| `appsettings.Development.json` | Development overrides |
| `appsettings.Production.json` | Production overrides |
| `configure-server.ps1` | Setup script (Windows) |
| `server.pfx` | TLS certificate |

### Essential Commands
```powershell
# Build
dotnet build

# Run (development)
dotnet run --project src/Hugin.Server

# Run tests
dotnet test

# Publish (production)
dotnet publish -c Release -r win-x64 --self-contained
```

---

**🎉 Congratulations!** You now have a fully functional Hugin IRC server running. Welcome to the world of IRC!
