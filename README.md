# 🐦 Hugin IRC Server

[![Build Status](https://img.shields.io/github/actions/workflow/status/brudvik/hugin/build.yml?branch=main)](https://github.com/brudvik/hugin/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

A modern, security-focused IRC server written in C# that follows RFC and IRCv3 specifications.

*Named after Huginn, Odin’s raven of thought — ever-present, ever-listening, gathering every word spoken across the realms.*

## ✨ Features

- **Full RFC Compliance**: RFC 1459, RFC 2812, Modern IRC
- **IRCv3 Support**: CAP negotiation, SASL, message-tags, batch, chathistory, and more
- **Security First**: TLS 1.2/1.3, SASL authentication, encrypted configuration, Argon2id password hashing
- **Scalable**: Designed for thousands of concurrent users
- **Server Linking**: S2S protocol for multi-server networks
- **Persistent Storage**: PostgreSQL backend for user accounts and channel history
- **Extensible**: Lua scripting, JSON triggers, and C# plugins for custom functionality

## 🎛️ Admin Panel

Hugin includes a modern web-based admin panel for easy server management:

- **Setup Wizard**: Guided 5-step initial configuration
- **Dashboard**: Real-time server statistics and status
- **User Management**: View, message, and disconnect users
- **Channel Management**: Create, edit, and delete channels
- **Operator Management**: Configure IRCops with granular permissions
- **Ban Management**: K-lines, G-lines, Z-lines with expiration
- **Configuration Editor**: Server settings, rate limits, IRCv3 capabilities

👉 **[Admin Panel Documentation](docs/ADMIN_PANEL.md)** — detailed setup and usage guide.

## 📚 Related Projects

- **[Munin](https://github.com/brudvik/munin)**: IRC client companion project

## 📋 Requirements

- .NET 8.0 or later
- PostgreSQL 14 or later
- Valid TLS certificate for production use

## 🚀 Quick Start

```bash
# Clone the repository
git clone https://github.com/yourusername/hugin.git
cd hugin

# Build the solution
dotnet build

# Run the server (development mode)
dotnet run --project src/Hugin.Server
```

### Windows: Configuration Script

Use the included PowerShell script for easy first-time setup:

```powershell
# Run the configuration script (builds, starts server, opens admin panel)
.\configure-server.ps1

# Skip build if already built
.\configure-server.ps1 -NoBuild

# Use custom admin port
.\configure-server.ps1 -AdminPort 8443
```

## ⚙️ Configuration

Hugin uses the standard .NET configuration system with JSON files and environment variable overrides.

👉 **[Complete Configuration Guide](docs/CONFIGURATION.md)** — detailed documentation for all settings.

### Quick Start Configuration

```json
{
  "Hugin": {
    "Server": {
      "Name": "irc.example.com",
      "NetworkName": "ExampleNet"
    },
    "Security": {
      "CertificatePath": "cert.pfx",
      "CertificatePassword": "your-password"
    }
  }
}
```

### Key Environment Variables

| Variable | Description | Required |
|----------|-------------|----------|
| `HUGIN_MASTER_KEY` | Master encryption key (32 bytes hex) | Yes |
| `HUGIN_DB_CONNECTION` | Database connection string override | No |

See the [Configuration Guide](docs/CONFIGURATION.md) for rate limiting, WEBIRC, metrics, and advanced options.

## 🏗️ Architecture

```
src/
├── Hugin.Core           # Domain entities, interfaces, value objects
├── Hugin.Protocol       # IRC message parsing, command handlers, numerics
├── Hugin.Protocol.S2S   # Server-to-server linking protocol
├── Hugin.Security       # TLS, SASL, encryption, rate limiting
├── Hugin.Persistence    # Database repositories, Entity Framework Core
├── Hugin.Network        # TCP/WebSocket listeners, connection management
└── Hugin.Server         # Console application / Windows Service host

tests/
├── Hugin.Core.Tests
├── Hugin.Protocol.Tests
├── Hugin.Security.Tests
└── Hugin.Integration.Tests
```

## 📡 IRCv3 Extensions Supported

| Extension | Status | Description |
|-----------|--------|-------------|
| CAP 302 | ✅ | Capability negotiation |
| SASL 3.2 | ✅ | PLAIN, EXTERNAL, SCRAM-SHA-256 |
| STS | ✅ | Strict Transport Security |
| message-tags | ✅ | Arbitrary key-value metadata |
| message-ids | ✅ | Unique message identifiers |
| server-time | ✅ | Server-side timestamps |
| batch | ✅ | Grouped messages |
| chathistory | ✅ | Message playback |
| echo-message | ✅ | Echo sent messages back |
| labeled-response | ✅ | Request-response correlation |
| account-notify | ✅ | Account login notifications |
| extended-join | ✅ | Extended JOIN information |
| away-notify | ✅ | Away status notifications |
| multi-prefix | ✅ | Multiple channel prefixes |
| userhost-in-names | ✅ | Full hostmask in NAMES |
| WHOX | ✅ | Extended WHO queries |
| WebSocket | ✅ | Browser-based IRC connections |

## 🤖 Network Services

Hugin includes built-in network services for enhanced functionality:

| Service | Status | Description |
|---------|--------|-------------|
| **NickServ** | ✅ | Nickname registration and authentication |
| **ChanServ** | ✅ | Channel registration and management |
| **MemoServ** | ✅ | Offline messaging between registered users |
| **OperServ** | ✅ | Network administration and operator tools |
| **BotServ** | ✅ | Bot hosting for channels with customizable greetings |
| **HostServ** | ✅ | Virtual host requests, approval, and activation |

### MemoServ Commands
- `SEND <nick> <message>` - Send offline message to registered user
- `LIST` - View all your memos with preview
- `READ <number>` - Read a specific memo
- `DELETE <number>` - Delete a memo
- `CLEAR` - Delete all memos

### OperServ Commands (IRC Operators only)
- `AKILL` - Network-wide autokills with expiration support
- `JUPE` - Block server names from linking
- `STATS` - Display network statistics
- `RESTART/DIE` - Server control commands
- `MODE` - Force mode changes on channels or users
- `KICK` - Force a kick from a channel
- `KILL` - Disconnect a user from the network
- `RAW` - Send raw S2S commands (dangerous)
- `GLOBAL` - Send a global notice to all users

### BotServ Commands
- `BOTLIST` - List available bots for assignment
- `ASSIGN <#channel> <bot>` - Assign bot to your channel
- `UNASSIGN <#channel> <bot>` - Remove bot from channel
- `SAY <#channel> <bot> <msg>` - Make bot say something
- `ACT <#channel> <bot> <action>` - Make bot perform action
- `INFO <#channel>` - View assigned bots and settings
- `SET <#channel> <bot> GREET ON/OFF` - Toggle auto-greet
- `SET <#channel> <bot> GREETMSG <msg>` - Set greet message

### HostServ Commands
- `REQUEST <hostname>` - Request a virtual host (validated for length/dots/no IP)
- `ACTIVATE` / `OFF` - Enable or disable your approved vhost
- `DELETE <hostname>` - Delete one of your vhosts
- `LIST` - List your vhosts with status (pending/approved/active)
- `APPROVE <hostname>` - Approve a pending vhost (IRC operators only)
- `REJECT <hostname> [reason]` - Reject and delete a pending vhost (IRC operators only)
- `WAITING` - Show all pending vhost requests (IRC operators only)

## 🔌 Extensibility

Hugin supports three ways to extend functionality:

| System | Format | Use Case |
|--------|--------|----------|
| **Lua Scripts** | `.lua` files | Event handlers, custom commands, timers |
| **JSON Triggers** | `.json` files | Automated responses, moderation rules |
| **C# Plugins** | DLL assemblies | Full API access, complex integrations |

### Lua Scripts
```lua
-- scripts/welcome.lua
function on_join(event)
    irc:SendNotice(event.nick, "Welcome to " .. event.channel .. "!")
    return true
end
```

### JSON Triggers
```json
{
  "id": "hello-cmd",
  "events": ["Message"],
  "conditions": [{ "type": "Command", "pattern": "hello" }],
  "actions": [{ "type": "Reply", "message": "Hello, {nick}!" }]
}
```

### C# Plugins
Create a `plugin.json` manifest and implement `IPlugin` for full server API access.

See [docs/EXTENSIBILITY.md](docs/EXTENSIBILITY.md) for complete documentation.

## 🔒 Security

- All connections must use TLS (plaintext is disabled by default)
- Passwords are hashed using Argon2id with secure defaults
- Configuration files are encrypted with AES-256-GCM
- Rate limiting prevents flooding and brute force attacks
- Support for client certificate authentication (SASL EXTERNAL)
- Hostname cloaking protects user privacy

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

**Current Status**: 553 unit tests passing

## 📄 License

MIT License - see [LICENSE](LICENSE) for details.

## 🤝 Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) before submitting PRs.

See [CHANGELOG.md](CHANGELOG.md) for version history and recent changes.

## 🙏 Acknowledgments

- The IRC community and [ircdocs.horse](https://modern.ircdocs.horse/) for excellent documentation
- [IRCv3 Working Group](https://ircv3.net/) for modern IRC specifications
- The .NET community for excellent tooling and libraries
