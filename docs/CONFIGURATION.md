# Hugin IRC Server Configuration Guide

This guide covers all configuration options for Hugin IRC Server, from basic setup to advanced tuning.

## Table of Contents

- [Quick Start](#quick-start)
- [Configuration Files](#configuration-files)
- [Environment Variables](#environment-variables)
- [Server Identity](#server-identity)
- [Network Configuration](#network-configuration)
- [Security](#security)
- [Database](#database)
- [Limits](#limits)
- [Logging](#logging)
- [MOTD (Message of the Day)](#motd-message-of-the-day)
- [Advanced Configuration](#advanced-configuration)
  - [Rate Limiting](#rate-limiting)
  - [WEBIRC Support](#webirc-support)
  - [Metrics Endpoint](#metrics-endpoint)
  - [Server Linking (S2S)](#server-linking-s2s)
  - [WebSocket Support](#websocket-support)
- [Configuration Encryption](#configuration-encryption)
- [Configuration Reference](#configuration-reference)

---

## Quick Start

1. **Generate a master key:**
   ```powershell
   # PowerShell
   $bytes = [byte[]]::new(32)
   [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
   $env:HUGIN_MASTER_KEY = [Convert]::ToHexString($bytes).ToLower()
   ```

2. **Create a minimal configuration file** (`appsettings.json`):
   ```json
   {
     "Hugin": {
       "Server": {
         "Name": "irc.example.com",
         "NetworkName": "MyNetwork"
       },
       "Security": {
         "CertificatePath": "cert.pfx",
         "CertificatePassword": "your-password"
       }
     }
   }
   ```

3. **Run the server:**
   ```bash
   dotnet run --project src/Hugin.Server
   ```

---

## Configuration Files

Hugin uses the standard .NET configuration system with the following files (in order of precedence):

| File | Purpose |
|------|---------|
| `appsettings.json` | Base configuration (committed to source control) |
| `appsettings.{Environment}.json` | Environment-specific overrides |
| `appsettings.local.json` | Local overrides (gitignored) |
| Environment variables | Runtime overrides |

### Configuration Hierarchy

All Hugin settings are under the `Hugin` section:

```json
{
  "Hugin": {
    "Server": { ... },
    "Network": { ... },
    "Security": { ... },
    "Database": { ... },
    "Limits": { ... },
    "Logging": { ... },
    "Motd": [ ... ]
  }
}
```

---

## Environment Variables

Environment variables override configuration file settings. Use the `HUGIN_` prefix followed by the configuration path with `__` as separator.

| Variable | Description | Required | Default |
|----------|-------------|----------|---------|
| `HUGIN_MASTER_KEY` | 32-byte hex-encoded encryption key | **Yes** | - |
| `HUGIN_DB_CONNECTION` | Database connection string | No | From config |
| `HUGIN__Server__Name` | Server hostname | No | `irc.hugin.local` |
| `HUGIN__Security__CertificatePath` | Path to TLS certificate | No | - |

### Example: Override via Environment

```powershell
# PowerShell
$env:HUGIN__Server__Name = "irc.production.example.com"
$env:HUGIN__Database__ConnectionString = "Host=db.prod;Database=hugin;..."
```

```bash
# Bash
export HUGIN__Server__Name="irc.production.example.com"
export HUGIN__Database__ConnectionString="Host=db.prod;Database=hugin;..."
```

---

## Server Identity

Configure how your server identifies itself to clients and other servers.

```json
{
  "Hugin": {
    "Server": {
      "Name": "irc.example.com",
      "Sid": "001",
      "Description": "Production IRC Server",
      "NetworkName": "ExampleNet",
      "AdminName": "John Doe",
      "AdminEmail": "admin@example.com"
    }
  }
}
```

| Property | Description | Default |
|----------|-------------|---------|
| `Name` | Server hostname (shown in messages) | `irc.hugin.local` |
| `Sid` | 3-character server ID for S2S linking | `001` |
| `Description` | Server description for INFO command | `Hugin IRC Server` |
| `NetworkName` | Network name (shown in welcome) | `HuginNet` |
| `AdminName` | Administrator name for ADMIN command | `Administrator` |
| `AdminEmail` | Admin contact email | `admin@hugin.local` |

---

## Network Configuration

### Client Listeners

Define ports and addresses for client connections.

```json
{
  "Hugin": {
    "Network": {
      "Listeners": [
        {
          "Address": "0.0.0.0",
          "Port": 6697,
          "Tls": true
        },
        {
          "Address": "0.0.0.0",
          "Port": 6667,
          "Tls": false
        }
      ]
    }
  }
}
```

| Property | Description | Default |
|----------|-------------|---------|
| `Address` | Bind address (`0.0.0.0` for all interfaces) | `0.0.0.0` |
| `Port` | TCP port number | `6697` |
| `Tls` | Whether to use TLS encryption | `true` |

> ⚠️ **Security Warning:** Setting `Tls: false` is not recommended for production. Plaintext connections expose passwords and messages.

### Standard IRC Ports

| Port | Protocol | Usage |
|------|----------|-------|
| 6667 | Plaintext | Legacy clients (not recommended) |
| 6697 | TLS | Standard secure port |
| 6660-6669 | Various | Alternative ports |
| 7000 | TLS | Alternative secure port |

---

## Security

### TLS Configuration

```json
{
  "Hugin": {
    "Security": {
      "CertificatePath": "/path/to/certificate.pfx",
      "CertificatePassword": "certificate-password",
      "GenerateSelfSignedCertificate": false,
      "RequireTls": true,
      "EnableSts": true,
      "StsDuration": 31536000
    }
  }
}
```

| Property | Description | Default |
|----------|-------------|---------|
| `CertificatePath` | Path to PFX/PKCS12 certificate | `null` |
| `CertificatePassword` | Certificate password (can be encrypted) | `null` |
| `GenerateSelfSignedCertificate` | Auto-generate cert if none provided | `true` |
| `RequireTls` | Reject non-TLS connections | `true` |
| `EnableSts` | Enable Strict Transport Security (STS) | `true` |
| `StsDuration` | STS policy duration in seconds | `31536000` (1 year) |

### Creating a TLS Certificate

**Option 1: Let's Encrypt (recommended for production)**
```bash
# Using certbot
certbot certonly --standalone -d irc.example.com

# Convert to PFX
openssl pkcs12 -export -out cert.pfx \
  -inkey /etc/letsencrypt/live/irc.example.com/privkey.pem \
  -in /etc/letsencrypt/live/irc.example.com/fullchain.pem
```

**Option 2: Self-signed (development only)**
```powershell
# PowerShell
$cert = New-SelfSignedCertificate -DnsName "irc.local" -CertStoreLocation "Cert:\CurrentUser\My"
Export-PfxCertificate -Cert $cert -FilePath "cert.pfx" -Password (ConvertTo-SecureString -String "password" -Force -AsPlainText)
```

### Hostname Cloaking

Protect user privacy by hiding their real hostnames.

```json
{
  "Hugin": {
    "Security": {
      "CloakSecret": "a-random-32-character-string-here",
      "CloakSuffix": "users.example.net"
    }
  }
}
```

| Property | Description | Default |
|----------|-------------|---------|
| `CloakSecret` | Secret key for cloak generation (change this!) | Random GUID |
| `CloakSuffix` | Suffix appended to cloaked hostnames | `hugin.cloak` |

**Example cloak transformation:**
- Original: `nick!user@192.168.1.100`
- Cloaked: `nick!user@a1b2c3d4.users.example.net`

---

## Database

Hugin uses PostgreSQL for persistent storage (accounts, messages, bans).

```json
{
  "Hugin": {
    "Database": {
      "ConnectionString": "Host=localhost;Port=5432;Database=hugin;Username=hugin;Password=secret",
      "RunMigrationsOnStartup": true,
      "MessageRetentionDays": 30
    }
  }
}
```

| Property | Description | Default |
|----------|-------------|---------|
| `ConnectionString` | PostgreSQL connection string | `Host=localhost;Database=hugin;...` |
| `RunMigrationsOnStartup` | Automatically apply database migrations | `true` |
| `MessageRetentionDays` | Days to keep messages (0 = forever) | `30` |

### Connection String Options

```
Host=hostname;
Port=5432;
Database=hugin;
Username=hugin;
Password=secret;
SSL Mode=Require;
Trust Server Certificate=true;
Pooling=true;
Minimum Pool Size=5;
Maximum Pool Size=100;
```

### Running Without a Database

For testing or simple deployments, Hugin can run with in-memory repositories:
- Accounts won't persist across restarts
- Message history won't be saved
- Bans are temporary

---

## Limits

Configure protocol limits to prevent abuse.

```json
{
  "Hugin": {
    "Limits": {
      "MaxNickLength": 30,
      "MaxChannelLength": 50,
      "MaxTopicLength": 390,
      "MaxKickLength": 255,
      "MaxAwayLength": 200,
      "MaxChannels": 50,
      "MaxTargets": 4,
      "PingTimeout": 180,
      "RegistrationTimeout": 60
    }
  }
}
```

| Property | Description | Default | RFC |
|----------|-------------|---------|-----|
| `MaxNickLength` | Maximum nickname length | 30 | RFC 2812: 9 |
| `MaxChannelLength` | Maximum channel name length | 50 | RFC 2812: 50 |
| `MaxTopicLength` | Maximum topic length | 390 | - |
| `MaxKickLength` | Maximum kick message length | 255 | - |
| `MaxAwayLength` | Maximum away message length | 200 | - |
| `MaxChannels` | Max channels per user | 50 | - |
| `MaxTargets` | Max targets for PRIVMSG/NOTICE | 4 | - |
| `PingTimeout` | Seconds before ping timeout | 180 | - |
| `RegistrationTimeout` | Seconds to complete registration | 60 | - |

---

## Logging

Configure log output using Serilog.

```json
{
  "Hugin": {
    "Logging": {
      "MinimumLevel": "Information",
      "FilePath": "logs/hugin-.log",
      "EnableConsole": true
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "Hugin.Protocol": "Debug"
      }
    }
  }
}
```

### Log Levels

| Level | Description |
|-------|-------------|
| `Verbose` | Everything (very noisy) |
| `Debug` | Detailed debugging info |
| `Information` | Normal operations |
| `Warning` | Unexpected but handled |
| `Error` | Failures requiring attention |
| `Fatal` | Critical failures |

### Log File Rotation

Files are automatically rotated with date suffix:
- `logs/hugin-20240101.log`
- `logs/hugin-20240102.log`

---

## MOTD (Message of the Day)

The MOTD is shown to users when they connect or use the `/MOTD` command.

```json
{
  "Hugin": {
    "Motd": [
      "╔════════════════════════════════════════╗",
      "║   Welcome to Example IRC Network!      ║",
      "╠════════════════════════════════════════╣",
      "║   Rules:                               ║",
      "║   1. Be respectful                     ║",
      "║   2. No spamming                       ║",
      "║   3. Have fun!                         ║",
      "╚════════════════════════════════════════╝"
    ]
  }
}
```

Each array element becomes one line of the MOTD.

---

## Advanced Configuration

### Rate Limiting

Prevent flooding and abuse with token bucket rate limiting.

```json
{
  "Hugin": {
    "Security": {
      "RateLimiting": {
        "ConnectionsPerSecond": 2,
        "ConnectionBurstSize": 5,
        "CommandsPerSecond": 5,
        "CommandBurstSize": 20,
        "MessagesPerSecond": 3,
        "MessageBurstSize": 10,
        "ExemptAddresses": ["127.0.0.1", "::1", "10.0.0.0/8"]
      }
    }
  }
}
```

| Property | Description | Default |
|----------|-------------|---------|
| `ConnectionsPerSecond` | New connections/second per IP | 2 |
| `ConnectionBurstSize` | Max burst of connections | 5 |
| `CommandsPerSecond` | Commands/second per connection | 5 |
| `CommandBurstSize` | Max command burst | 20 |
| `MessagesPerSecond` | Messages/second per connection | 3 |
| `MessageBurstSize` | Max message burst | 10 |
| `ExemptAddresses` | IPs exempt from limiting | `127.0.0.1`, `::1` |

**How it works:** Token bucket algorithm allows bursting while limiting sustained rate. Example: With `MessagesPerSecond: 3` and `MessageBurstSize: 10`, a user can send 10 messages instantly, then 3 per second after.

### WEBIRC Support

Allow trusted web gateways to report real client IPs.

```json
{
  "Hugin": {
    "Webirc": {
      "Enabled": true,
      "Blocks": [
        {
          "Name": "KiwiIRC",
          "Password": "secret-webirc-password",
          "AllowedHosts": ["10.0.0.5", "10.0.0.6"],
          "TrustIdent": false
        },
        {
          "Name": "TheLounge",
          "Password": "another-password",
          "AllowedHosts": ["192.168.1.0/24"],
          "TrustIdent": true
        }
      ]
    }
  }
}
```

| Property | Description |
|----------|-------------|
| `Name` | Identifier for this gateway block |
| `Password` | Shared secret (never expose!) |
| `AllowedHosts` | IPs/CIDRs allowed to use this block |
| `TrustIdent` | Trust ident from gateway |

### Metrics Endpoint

Expose Prometheus-compatible metrics for monitoring.

```json
{
  "Hugin": {
    "Metrics": {
      "Enabled": true,
      "Port": 9090,
      "Path": "/metrics",
      "AllowedIps": ["127.0.0.1", "10.0.0.0/8"]
    }
  }
}
```

| Property | Description | Default |
|----------|-------------|---------|
| `Enabled` | Enable metrics endpoint | `false` |
| `Port` | HTTP port for metrics | `9090` |
| `Path` | URL path for metrics | `/metrics` |
| `AllowedIps` | IPs allowed to scrape (empty = all) | `[]` |

**Available metrics:**
- `hugin_uptime_seconds` - Server uptime
- `hugin_connections_active` - Active connections
- `hugin_users_registered` - Registered users
- `hugin_channels_total` - Active channels
- `hugin_messages_received_total` - Messages received
- `hugin_commands_processed_total` - Commands processed
- `hugin_auth_attempts_total` - Authentication attempts
- `hugin_rate_limit_hits_total` - Rate limit triggers

### Server Linking (S2S)

Connect multiple Hugin servers into a network.

```json
{
  "Hugin": {
    "Network": {
      "ServerListeners": [
        {
          "Address": "0.0.0.0",
          "Port": 6900,
          "Tls": true
        }
      ],
      "LinkedServers": [
        {
          "Name": "hub.example.net",
          "Host": "10.0.0.10",
          "Port": 6900,
          "Password": "link-password",
          "AutoConnect": true
        }
      ]
    }
  }
}
```

| Property | Description |
|----------|-------------|
| `Name` | Remote server's name |
| `Host` | IP address or hostname |
| `Port` | S2S port |
| `Password` | Shared link password |
| `AutoConnect` | Connect automatically on startup |

### WebSocket Support

Enable browser-based IRC connections.

```json
{
  "Hugin": {
    "Network": {
      "WebSocketListeners": [
        {
          "Address": "0.0.0.0",
          "Port": 8080,
          "Path": "/webirc",
          "AllowedOrigins": ["https://webchat.example.com"]
        }
      ]
    }
  }
}
```

| Property | Description |
|----------|-------------|
| `Address` | Bind address |
| `Port` | WebSocket port |
| `Path` | WebSocket endpoint path |
| `AllowedOrigins` | CORS allowed origins (`*` for all) |

---

## Configuration Encryption

Sensitive configuration values can be encrypted with AES-256-GCM.

### Generating a Master Key

```powershell
# PowerShell - Generate and set master key
$bytes = [byte[]]::new(32)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$key = [Convert]::ToHexString($bytes).ToLower()
$env:HUGIN_MASTER_KEY = $key
Write-Host "HUGIN_MASTER_KEY=$key"
```

### Encrypting Values

Use the Hugin CLI to encrypt sensitive values:

```bash
# Encrypt a value
dotnet run --project src/Hugin.Server -- encrypt "my-secret-password"

# Output: ENC:base64-encrypted-data...
```

### Using Encrypted Values

Place encrypted values directly in configuration:

```json
{
  "Hugin": {
    "Security": {
      "CertificatePassword": "ENC:AQAAAAEAACcQ...",
      "CloakSecret": "ENC:AQAAAAEAACcQ..."
    },
    "Database": {
      "ConnectionString": "ENC:AQAAAAEAACcQ..."
    }
  }
}
```

The server automatically decrypts these values on startup using `HUGIN_MASTER_KEY`.

---

## Configuration Reference

### Complete Example

```json
{
  "Hugin": {
    "Server": {
      "Name": "irc.example.com",
      "Sid": "001",
      "Description": "Example IRC Server",
      "NetworkName": "ExampleNet",
      "AdminName": "Jane Admin",
      "AdminEmail": "admin@example.com"
    },
    "Network": {
      "Listeners": [
        { "Address": "0.0.0.0", "Port": 6697, "Tls": true }
      ],
      "ServerListeners": [],
      "LinkedServers": []
    },
    "Security": {
      "CertificatePath": "/etc/hugin/cert.pfx",
      "CertificatePassword": "ENC:...",
      "GenerateSelfSignedCertificate": false,
      "RequireTls": true,
      "EnableSts": true,
      "StsDuration": 31536000,
      "CloakSecret": "ENC:...",
      "CloakSuffix": "users.example.net",
      "RateLimiting": {
        "ConnectionsPerSecond": 2,
        "ConnectionBurstSize": 5,
        "CommandsPerSecond": 5,
        "CommandBurstSize": 20,
        "MessagesPerSecond": 3,
        "MessageBurstSize": 10,
        "ExemptAddresses": ["127.0.0.1", "::1"]
      }
    },
    "Database": {
      "ConnectionString": "Host=db.example.com;Database=hugin;Username=hugin;Password=...",
      "RunMigrationsOnStartup": true,
      "MessageRetentionDays": 90
    },
    "Limits": {
      "MaxNickLength": 30,
      "MaxChannelLength": 50,
      "MaxTopicLength": 390,
      "MaxKickLength": 255,
      "MaxAwayLength": 200,
      "MaxChannels": 50,
      "MaxTargets": 4,
      "PingTimeout": 180,
      "RegistrationTimeout": 60
    },
    "Logging": {
      "MinimumLevel": "Information",
      "FilePath": "/var/log/hugin/hugin-.log",
      "EnableConsole": false
    },
    "Motd": [
      "Welcome to Example IRC Network!",
      "",
      "For help, join #help"
    ],
    "Webirc": {
      "Enabled": true,
      "Blocks": [
        {
          "Name": "WebChat",
          "Password": "ENC:...",
          "AllowedHosts": ["10.0.0.50"]
        }
      ]
    },
    "Metrics": {
      "Enabled": true,
      "Port": 9090,
      "Path": "/metrics",
      "AllowedIps": ["10.0.0.0/8"]
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

### Configuration Validation

The server validates configuration on startup and will:
- Exit with error if `HUGIN_MASTER_KEY` is missing or invalid
- Warn if using self-signed certificates
- Warn if rate limiting is disabled
- Error if database connection fails (when using persistence)

---

## See Also

- [README.md](../README.md) - Project overview
- [RFC_COMPLIANCE.md](RFC_COMPLIANCE.md) - IRC protocol compliance
- [CONTRIBUTING.md](../CONTRIBUTING.md) - Development guidelines
