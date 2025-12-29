# Copilot Instructions for Hugin IRC Server

## Project Overview

Hugin is a modern, security-focused IRC server written in C# following RFC 1459/2812 and IRCv3 specifications. The project uses Clean Architecture with .NET 8 LTS.

### Architecture Layers
- **Hugin.Core** - Domain entities, value objects, enums, and interfaces
- **Hugin.Protocol** - IRC message parsing, command handlers, IRCv3 capabilities
- **Hugin.Protocol.S2S** - Server-to-server protocol (future)
- **Hugin.Security** - Authentication, encryption, TLS, rate limiting
- **Hugin.Persistence** - Database access with Entity Framework Core and PostgreSQL
- **Hugin.Network** - TCP/TLS connections using System.IO.Pipelines
- **Hugin.Server** - Application entry point, DI configuration, hosted services

## Code Standards

### Comments and Documentation
- **All code must be properly commented** - methods, classes, and complex logic should have XML documentation comments
- Use `<summary>`, `<param>`, `<returns>`, and `<remarks>` tags for public APIs
- Complex algorithms or non-obvious code should have inline comments explaining the reasoning
- Norwegian comments are acceptable for UI strings and user-facing text

```csharp
/// <summary>
/// Validates and creates a new IRC nickname.
/// </summary>
/// <param name="value">The nickname string to validate.</param>
/// <param name="nickname">The created nickname if successful.</param>
/// <param name="error">Error message if validation fails.</param>
/// <returns>True if the nickname is valid; otherwise false.</returns>
public static bool TryCreate(string? value, out Nickname? nickname, out string? error)
```

### Code Style
- Follow C# naming conventions (PascalCase for public members, camelCase for private fields with underscore prefix)
- Use meaningful variable and method names
- Keep methods focused and small (single responsibility)
- Use async/await properly - avoid `.Result` or `.Wait()` 
- Prefer `ValueTask` over `Task` for hot paths
- Use `CancellationToken` for all async operations

### Naming Conventions
```csharp
// Private fields with underscore prefix
private readonly IUserRepository _userRepository;

// Public properties in PascalCase
public string ServerName { get; }

// Parameters in camelCase - use full names for common types
public void SendMessage(string channelName, string message)
public async Task ProcessAsync(CommandContext context, CancellationToken cancellationToken)

// Constants in PascalCase
public const int MaxMessageLength = 512;

// IMPORTANT: Use 'context' not 'ctx', 'cancellationToken' not 'ct'
```

### Error Handling
- Use the Result pattern or `TryXxx` methods for expected failures
- Throw exceptions only for unexpected/exceptional situations
- Always log errors with appropriate context
- Never expose internal error details to IRC clients

## IRC Protocol Guidelines

### Message Creation
Use the appropriate factory methods for `IrcMessage`:
```csharp
// Without source (client commands)
IrcMessage.Create("PING", "server");

// With source (server responses)
IrcMessage.CreateWithSource(serverName, "PONG", token);

// With tags and source (IRCv3)
IrcMessage.CreateFull(tags, source, "PRIVMSG", target, text);
```

### Numeric Replies
Always use `IrcNumerics` helper methods:
```csharp
await ctx.ReplyAsync(IrcNumerics.Welcome(serverName, nick, welcomeMessage), ct);
await ctx.ReplyAsync(IrcNumerics.NoSuchNick(serverName, nick, target), ct);
```

### Command Handlers
- Inherit from `CommandHandlerBase`
- Override `Command`, `MinimumParameters`, and `RequiresRegistration` as needed
- Use parameter names `context` and `cancellationToken` (not `ctx` and `ct`)
- Validate all input before processing

## Security Requirements

### Critical Security Rules
1. **TLS is mandatory** - Never allow plaintext connections in production
2. **Use Argon2id** for password hashing via `PasswordHasher`
3. **Encrypt sensitive config** with `ConfigurationEncryptor` (AES-256-GCM)
4. **Validate all input** - nicknames, channel names, hostmasks
5. **Rate limit** all operations to prevent abuse
6. **Cloak hostnames** by default to protect user privacy

### Authentication
- SASL PLAIN and EXTERNAL mechanisms are supported
- Client certificates should use SHA-256 fingerprints
- Account passwords are never stored in plaintext

### Environment Variables
- `HUGIN_MASTER_KEY` - Required for configuration encryption (32 bytes, base64)
- Never log or expose encryption keys

## Testing Requirements

### Mandatory Testing Policy
**All new functionality MUST include corresponding unit tests.** This is a strict requirement:
- New classes → Tests for all public methods
- New methods → Tests covering success, failure, and edge cases
- Bug fixes → Regression test proving the fix works
- Refactoring → Verify existing tests still pass

Pull requests without adequate test coverage will not be accepted.

### Test Organization
- **Hugin.Core.Tests** - Value object and entity tests
- **Hugin.Protocol.Tests** - Message parsing and command handler tests
- **Hugin.Security.Tests** - Cryptography and authentication tests
- **Hugin.Integration.Tests** - End-to-end server tests

### Test Naming
Use PascalCase naming without underscores to comply with CA1707:
```csharp
[Fact]
public void TryCreateWithValidNicknameReturnsTrue()

[Fact]
public void TryParseMessageWithTagsParsesTagsCorrectly()
```

### Test Coverage Requirements
- All public APIs must have unit tests
- All command handlers must have tests for success and error cases
- Security-critical code requires extensive edge case testing
- Aim for minimum 80% code coverage on new code
- Use `[Theory]` with `[InlineData]` for testing multiple inputs

### Test Structure
Follow the Arrange-Act-Assert pattern:
```csharp
[Fact]
public void MethodNameWithConditionReturnsExpectedResult()
{
    // Arrange
    var input = "test";
    
    // Act
    var result = SomeMethod(input);
    
    // Assert
    result.Should().BeTrue();
}
```

## Project Documentation

### CHANGELOG.md
- **Must be updated with every feature or bugfix**
- Follow [Keep a Changelog](https://keepachangelog.com/) format
- Group changes under: Added, Changed, Deprecated, Removed, Fixed, Security
- Include date for each version
- Reference issue numbers if applicable

```markdown
## [Unreleased]

### Added
- CHATHISTORY command support for message playback (#123)

### Fixed
- Rate limiter now correctly resets after window expiry (#124)

### Security
- Updated TLS configuration to disable TLS 1.0/1.1
```

### README.md
- Keep the feature list up to date
- Document new configuration options
- Update architecture diagrams if structure changes
- Maintain accurate build/run instructions

## Dependencies

### Package Management
- Use Central Package Management (Directory.Packages.props)
- Never specify versions in individual .csproj files
- Keep packages updated, especially security-related ones

### Current Stack
| Package | Purpose |
|---------|---------|
| Npgsql.EntityFrameworkCore.PostgreSQL | Database |
| Serilog | Structured logging |
| System.IO.Pipelines | High-performance I/O |
| Konscious.Security.Cryptography.Argon2 | Password hashing |

## Configuration

### appsettings.json Structure
```json
{
  "Hugin": {
    "ServerName": "irc.example.com",
    "Network": "ExampleNet",
    "Ports": {
      "Tls": 6697
    },
    "Tls": {
      "CertificatePath": "path/to/cert.pfx"
    }
  }
}
```

### Sensitive Configuration
- Use `appsettings.Production.json` for production secrets
- Prefer environment variables for credentials
- Never commit secrets to version control

## Git Workflow

### Commit Messages
- Use conventional commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`
- Reference issues: `fix: resolve nickname collision (#42)`
- Keep commits focused and atomic

### Branch Naming
- `feature/` - New features
- `fix/` - Bug fixes
- `refactor/` - Code improvements
- `docs/` - Documentation updates

## Performance Considerations

### Hot Paths
- Use `Span<T>` and `Memory<T>` for parsing
- Prefer `ValueTask` for frequently-called async methods
- Pool objects where appropriate (e.g., `ArrayPool<byte>`)
- Avoid allocations in message processing loops

### Database
- Use async database operations
- Batch operations when possible
- Index frequently-queried columns
- Use projections to avoid loading unnecessary data

## Related Projects

- **Munin** - IRC client companion project
- Both projects share common protocol understanding and should remain compatible
