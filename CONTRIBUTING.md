# Contributing to Hugin IRC Server

Thank you for your interest in contributing to Hugin! This document provides guidelines and instructions for contributing.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Making Changes](#making-changes)
- [Code Standards](#code-standards)
- [Testing](#testing)
- [Submitting Changes](#submitting-changes)
- [Reporting Issues](#reporting-issues)

## Code of Conduct

By participating in this project, you agree to maintain a respectful and inclusive environment. Be kind, constructive, and professional in all interactions.

## Getting Started

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/your-username/hugin.git
   cd hugin
   ```
3. Add the upstream repository:
   ```bash
   git remote add upstream https://github.com/original-owner/hugin.git
   ```
4. Create a branch for your changes:
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Development Setup

### Prerequisites

- .NET 8.0 SDK or later
- PostgreSQL 14 or later (for integration tests)
- Visual Studio 2022, VS Code, or Rider
- Git

### Building the Project

```bash
# Restore packages
dotnet restore

# Build all projects
dotnet build

# Run tests
dotnet test
```

### Running the Server

```bash
# Set required environment variable
$env:HUGIN_MASTER_KEY = "your-base64-encoded-32-byte-key"

# Run the server
dotnet run --project src/Hugin.Server
```

## Making Changes

### Branch Naming

Use descriptive branch names with prefixes:

- `feature/` - New features (e.g., `feature/whois-command`)
- `fix/` - Bug fixes (e.g., `fix/nickname-collision`)
- `refactor/` - Code improvements (e.g., `refactor/message-parsing`)
- `docs/` - Documentation updates (e.g., `docs/api-reference`)
- `test/` - Test additions or fixes (e.g., `test/channel-handlers`)

### Commit Messages

Follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
type(scope): description

[optional body]

[optional footer]
```

**Types:**
- `feat` - New feature
- `fix` - Bug fix
- `docs` - Documentation changes
- `refactor` - Code refactoring
- `test` - Adding or updating tests
- `chore` - Maintenance tasks
- `perf` - Performance improvements
- `security` - Security-related changes

**Examples:**
```
feat(protocol): add WHOIS command handler

fix(security): prevent timing attack in password verification

docs(readme): update build instructions for .NET 8
```

## Code Standards

### General Guidelines

- Follow C# naming conventions
- Use meaningful names for variables, methods, and classes
- Keep methods focused and small (single responsibility)
- Write self-documenting code, but add comments for complex logic

### Documentation

All public APIs must have XML documentation:

```csharp
/// <summary>
/// Validates and creates a new IRC channel name.
/// </summary>
/// <param name="value">The channel name string to validate.</param>
/// <param name="channelName">The created channel name if successful.</param>
/// <param name="error">Error message if validation fails.</param>
/// <returns>True if the channel name is valid; otherwise false.</returns>
public static bool TryCreate(string? value, out ChannelName? channelName, out string? error)
```

### Async/Await

- Use `async/await` for all I/O operations
- Never use `.Result` or `.Wait()` - this can cause deadlocks
- Use `CancellationToken` for all async methods
- Prefer `ValueTask` over `Task` for hot paths

### Error Handling

- Use the Result pattern or `TryXxx` methods for expected failures
- Throw exceptions only for exceptional situations
- Always log errors with appropriate context
- Never expose internal error details to clients

### Security

- Never commit secrets, keys, or passwords
- Validate all input from untrusted sources
- Use parameterized queries for database operations
- Follow the principle of least privilege

## Testing

### Test Requirements

- All new features must include unit tests
- All bug fixes should include a regression test
- Maintain or improve code coverage
- All tests must pass before submitting a PR

### Test Naming

Use the pattern: `MethodName_Scenario_ExpectedResult`

```csharp
[Fact]
public void TryCreate_ValidNickname_ReturnsTrue()

[Fact]
public void TryParse_MessageWithTags_ParsesTagsCorrectly()

[Fact]
public void HandleAsync_UserNotOnChannel_SendsNotOnChannelError()
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/Hugin.Core.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Submitting Changes

### Before Submitting

1. Ensure all tests pass: `dotnet test`
2. Ensure the build succeeds: `dotnet build`
3. Update documentation if needed
4. Update CHANGELOG.md with your changes
5. Rebase on the latest upstream main:
   ```bash
   git fetch upstream
   git rebase upstream/main
   ```

### Pull Request Process

1. Push your branch to your fork:
   ```bash
   git push origin feature/your-feature-name
   ```

2. Create a Pull Request on GitHub

3. Fill out the PR template with:
   - Description of changes
   - Related issue numbers
   - Testing performed
   - Screenshots (if UI changes)

4. Wait for review and address any feedback

5. Once approved, your PR will be merged

### PR Requirements

- Clear description of changes
- All tests passing
- No merge conflicts
- Follows code standards
- CHANGELOG.md updated
- Documentation updated (if applicable)

## Reporting Issues

### Bug Reports

When reporting a bug, please include:

1. **Description**: Clear description of the bug
2. **Steps to Reproduce**: Detailed steps to reproduce the issue
3. **Expected Behavior**: What you expected to happen
4. **Actual Behavior**: What actually happened
5. **Environment**: OS, .NET version, PostgreSQL version
6. **Logs**: Relevant log output (sanitized of sensitive data)

### Feature Requests

When requesting a feature:

1. **Use Case**: Describe the problem you're trying to solve
2. **Proposed Solution**: Your suggested implementation
3. **Alternatives**: Other solutions you've considered
4. **IRC Specification**: Reference to relevant RFC or IRCv3 spec (if applicable)

## Architecture Overview

Understanding the project structure will help you contribute effectively:

```
src/
├── Hugin.Core           # Domain entities, interfaces, value objects
├── Hugin.Protocol       # IRC message parsing, command handlers
├── Hugin.Protocol.S2S   # Server-to-server protocol
├── Hugin.Security       # TLS, SASL, encryption, rate limiting
├── Hugin.Persistence    # Database repositories
├── Hugin.Network        # TCP/WebSocket connections
└── Hugin.Server         # Application entry point

tests/
├── Hugin.Core.Tests
├── Hugin.Protocol.Tests
├── Hugin.Security.Tests
└── Hugin.Integration.Tests
```

## Questions?

If you have questions about contributing:

1. Check existing issues and discussions
2. Open a new discussion for general questions
3. Open an issue for specific bugs or feature requests

Thank you for contributing to Hugin! 🐦
