# Hugin Server-to-Server (S2S) Protocol Specification

This document describes the server-to-server protocol used by Hugin IRC servers to communicate and form IRC networks.

## Table of Contents

- [Overview](#overview)
- [Connection Flow](#connection-flow)
- [Message Format](#message-format)
- [Server Identification](#server-identification)
- [Burst Sequence](#burst-sequence)
- [Commands](#commands)
  - [Server Commands](#server-commands)
  - [User Commands](#user-commands)
  - [Channel Commands](#channel-commands)
  - [Message Commands](#message-commands)
  - [ENCAP Sub-commands](#encap-sub-commands)
- [Network Services](#network-services)
- [Netsplit Handling](#netsplit-handling)
- [Security Considerations](#security-considerations)

---

## Overview

The Hugin S2S protocol is based on the TS6 (Timestamp 6) protocol, which is widely used by IRCd implementations like charybdis, ircd-ratbox, and others. Key features:

- **UID-based user identification**: Each user has a unique 9-character UID
- **SID-based server identification**: Each server has a unique 3-character SID
- **Timestamp-based conflict resolution**: Older entities take precedence
- **ENCAP for extensibility**: Custom commands can be encapsulated for network-wide propagation

---

## Connection Flow

### Incoming Connection (Server Accepting Link)

```
Client connects → 
  Server waits for PASS →
  Server waits for CAPAB →
  Server waits for SERVER →
  Server validates credentials →
  Server sends own PASS/CAPAB/SERVER →
  Burst exchange begins
```

### Outgoing Connection (Server Initiating Link)

```
Server connects →
  Server sends PASS →
  Server sends CAPAB →
  Server sends SERVER →
  Server waits for remote PASS/CAPAB/SERVER →
  Burst exchange begins
```

### Example Handshake

```irc
# Outgoing server sends:
PASS linkpassword TS 6 :001
CAPAB :QS EX CHW IE KLN GLN KNOCK UNKLN CLUSTER ENCAP SAVE SAVETS_100 EUID TB MLOCK
SERVER irc1.example.com 1 :Hugin IRC Server

# Incoming server responds:
PASS linkpassword TS 6 :002
CAPAB :QS EX CHW IE KLN GLN KNOCK UNKLN CLUSTER ENCAP SAVE SAVETS_100 EUID TB MLOCK
SERVER irc2.example.com 1 :Hugin IRC Server

# After successful handshake, both servers exchange burst data
```

---

## Message Format

S2S messages follow the IRC message format with UID/SID sources:

```
[@tags] [:source] COMMAND [params...] [:trailing]
```

### Components

| Component | Description |
|-----------|-------------|
| `tags` | Optional IRCv3 message tags (prefixed with `@`) |
| `source` | SID (3 chars) for server, UID (9 chars) for user |
| `COMMAND` | The command name |
| `params` | Space-separated parameters |
| `trailing` | Final parameter (prefixed with `:`) |

### Examples

```irc
# Server introducing itself (no source for initial handshake)
SERVER irc2.example.com 1 002 :Second server

# Server introducing a user (source is SID)
:001 UID nick 1 1234567890 user host 001AAAAAB 0 +i vhost :Real Name

# User sending a message (source is UID)
:001AAAAAB PRIVMSG #channel :Hello world

# Server broadcasting a mode change
:001 TMODE 1234567890 #channel +o 001AAAAAB
```

---

## Server Identification

### SID Format

Server IDs (SIDs) are exactly 3 characters:
- First character: digit 0-9
- Remaining characters: digits 0-9 or uppercase letters A-Z

Examples: `001`, `00A`, `1AB`, `42X`

### UID Format

User IDs (UIDs) are exactly 9 characters:
- First 3 characters: the server's SID
- Remaining 6 characters: unique user identifier on that server

Examples: `001AAAAAB`, `002XYZABC`

### ServerId Structure

```csharp
public sealed record ServerId(string Sid, string Name)
{
    // Sid: 3-character server identifier
    // Name: Server hostname (e.g., "irc.example.com")
}
```

---

## Burst Sequence

When two servers link, they exchange a "burst" of all current state. The sequence is:

1. **Server introductions**: Introduce all known servers
2. **User introductions**: Introduce all users (UID command)
3. **Channel state**: Send SJOIN for all channels with users and modes
4. **Bans/AKILLs**: Propagate network-wide bans

### Server Introduction

```irc
# Direct link introducing itself
SERVER irc2.example.com 1 002 :Second IRC server

# Introducing a server learned from another (hopcount > 1)
:002 SERVER irc3.example.com 2 003 :Third IRC server
```

### User Introduction (UID)

```irc
:001 UID nick hopcount timestamp user host uid servicestamp modes vhost :realname
```

| Parameter | Description |
|-----------|-------------|
| `nick` | User's nickname |
| `hopcount` | Distance from originating server |
| `timestamp` | Unix timestamp of nick registration |
| `user` | Username/ident |
| `host` | Real hostname |
| `uid` | 9-character UID |
| `servicestamp` | Service stamp (usually 0) |
| `modes` | User modes (e.g., `+i`) |
| `vhost` | Virtual/cloaked hostname |
| `realname` | Real name/gecos |

Example:
```irc
:001 UID Alice 1 1703862000 alice client.example.com 001AAAAAB 0 +i users.hugin.net :Alice Smith
```

### Channel Burst (SJOIN)

```irc
:001 SJOIN timestamp #channel modes [mode_params] :[@+]uid [@+]uid ...
```

| Parameter | Description |
|-----------|-------------|
| `timestamp` | Channel creation timestamp |
| `#channel` | Channel name |
| `modes` | Channel modes |
| `mode_params` | Mode parameters (key, limit, etc.) |
| `uid list` | Space-separated UIDs with optional status prefixes |

Status prefixes:
- `@` = Channel operator (+o)
- `+` = Voice (+v)
- `%` = Halfop (+h)

Example:
```irc
:001 SJOIN 1703860000 #hugin +nt :@001AAAAAB +001AAAAAC 001AAAAAD
```

This shows:
- Channel `#hugin` created at timestamp `1703860000`
- Modes: `+nt` (no external messages, topic lock)
- `001AAAAAB` is an operator (@)
- `001AAAAAC` has voice (+)
- `001AAAAAD` is a regular member

---

## Commands

### Server Commands

#### SERVER
Introduces a server to the network.

```irc
SERVER name hopcount sid :description
:source SERVER name hopcount sid :description
```

#### SQUIT
Disconnects a server from the network.

```irc
:source SQUIT servername :reason
```

#### PING / PONG
Keepalive between servers.

```irc
PING source :target
:source PONG target :source
```

#### ERROR
Terminates a connection with an error message.

```irc
ERROR :message
```

### User Commands

#### UID
Introduces a user (see Burst Sequence above).

#### QUIT
User disconnects from the network.

```irc
:uid QUIT :reason
```

#### NICK
User changes nickname.

```irc
:uid NICK newnick timestamp
```

#### KILL
Forcibly disconnects a user.

```irc
:source KILL target_uid :reason
```

### Channel Commands

#### SJOIN
Synchronized join (see Burst Sequence above).

#### PART
User leaves a channel.

```irc
:uid PART #channel :reason
```

#### KICK
User is kicked from a channel.

```irc
:source KICK #channel target_uid :reason
```

#### TMODE
Timestamped mode change.

```irc
:source TMODE timestamp #channel modes [params]
```

Example:
```irc
:001AAAAAB TMODE 1703860000 #hugin +o 001AAAAAC
```

#### TOPIC
Channel topic change.

```irc
:source TOPIC #channel setter timestamp :topic text
```

### Message Commands

#### PRIVMSG
Private message to user or channel.

```irc
:uid PRIVMSG target :message
```

#### NOTICE
Notice to user or channel.

```irc
:uid NOTICE target :message
```

### ENCAP Sub-commands

ENCAP (encapsulated) commands provide extensibility:

```irc
:source ENCAP target command [params...]
```

Target can be:
- `*` = Broadcast to all servers
- `SID` = Send to specific server

#### AKILL
Network-wide autokill.

```irc
:sid ENCAP * AKILL user@host duration setter :reason
```

#### UNAKILL
Remove an autokill.

```irc
:sid ENCAP * UNAKILL user@host
```

#### LOGIN
Set user account after authentication.

```irc
:sid ENCAP * LOGIN uid accountname
```

#### LOGOUT
Clear user account.

```irc
:sid ENCAP * LOGOUT uid
```

#### CERTFP
Propagate certificate fingerprint.

```irc
:sid ENCAP * CERTFP uid fingerprint
```

#### SASL
Relay SASL authentication messages.

```irc
:sid ENCAP target_sid SASL source_uid target_uid mode :data
```

#### KLINE / UNKLINE
K-line propagation.

```irc
:sid ENCAP * KLINE duration user host :reason
:sid ENCAP * UNKLINE user host
```

---

## Network Services

Hugin includes built-in network services that communicate via the S2S protocol:

### NickServ
- UID: `{SID}AAAAAN`
- Purpose: Nickname registration and authentication
- Commands: REGISTER, IDENTIFY, SET, DROP, GHOST, INFO

### ChanServ
- UID: `{SID}AAAAAC`
- Purpose: Channel registration and management
- Commands: REGISTER, OP, DEOP, VOICE, KICK, BAN, SET, DROP

### OperServ
- UID: `{SID}AAAAAO`
- Purpose: Network administration
- Commands: AKILL, JUPE, STATS, GLOBAL, KILL, RESTART, DIE

Services are introduced during burst like regular users:

```irc
:001 UID NickServ 1 1703860000 NickServ services.hugin.net 001AAAAAN 0 +oS services.hugin.net :Nickname Registration Service
```

---

## Netsplit Handling

When a server link breaks (netsplit), Hugin performs the following:

### Detection
1. Connection error or timeout triggers disconnect
2. SQUIT is propagated to remaining servers
3. All servers learned via the split server are also removed

### Propagation
```irc
:001 SQUIT irc2.example.com :Connection reset by peer
```

### Cascade Removal
Servers learned through the split server are automatically removed:

```
Network before split:
irc1 -- irc2 -- irc3
             \- irc4

After irc2 splits from irc1:
irc1 sees: irc2, irc3, irc4 all removed
```

### User Notification
Local users in channels with split users see:

```irc
:nick!user@host QUIT :irc1.example.com irc2.example.com
```

### Automatic Reconnection

The `NetsplitHandler` manages automatic reconnection:

1. **Exponential backoff**: Starts at 15 seconds, increases up to 5 minutes
2. **Configurable attempts**: Default unlimited, can be limited
3. **Event notifications**: Raises events for netsplit detection and healing

Configuration:
```json
{
  "Hugin": {
    "S2S": {
      "Reconnect": {
        "EnableAutoReconnect": true,
        "InitialDelaySeconds": 15,
        "MaxDelaySeconds": 300,
        "BackoffMultiplier": 2.0,
        "MaxReconnectAttempts": 0
      }
    }
  }
}
```

---

## Security Considerations

### Link Authentication
- Links must use strong, unique passwords
- Passwords should be configured using encrypted configuration
- TLS is strongly recommended for all S2S connections

### Certificate Validation
- Production links should validate certificates
- Self-signed certificates require explicit trust configuration

### Ban Propagation
- AKILL/GLINE propagation requires operator privileges
- Validate source before applying bans locally

### Rate Limiting
- S2S connections have separate rate limits from clients
- Excessive commands may trigger throttling

### JUPE Protection
- JUPE prevents servers from linking
- Use to block compromised or misconfigured servers

---

## See Also

- [CONFIGURATION.md](CONFIGURATION.md) - Server configuration
- [RFC_COMPLIANCE.md](RFC_COMPLIANCE.md) - IRC protocol compliance
- [TS6 Protocol Documentation](https://github.com/charybdis-ircd/charybdis/blob/master/doc/technical/ts6-protocol.txt) - Reference TS6 specification
