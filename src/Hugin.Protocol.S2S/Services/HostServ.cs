using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Hugin.Protocol.S2S.Services;

/// <summary>
/// HostServ - Virtual host (vhost) management service.
/// Allows users to request custom hostnames.
/// </summary>
public sealed class HostServ : INetworkService
{
    private readonly Func<IVirtualHostRepository> _vhostRepositoryFactory;
    private readonly Func<IAccountRepository> _accountRepositoryFactory;
    private readonly ServerId _localServerId;
    private readonly ILogger<HostServ> _logger;

    /// <inheritdoc />
    public string Nickname => "HostServ";

    /// <inheritdoc />
    public string Ident => "HostServ";

    /// <inheritdoc />
    public string Host { get; }

    /// <inheritdoc />
    public string Realname => "Virtual Host Service";

    /// <inheritdoc />
    public string Uid { get; }

    /// <summary>
    /// Creates a new HostServ instance.
    /// </summary>
    public HostServ(
        Func<IVirtualHostRepository> vhostRepositoryFactory,
        Func<IAccountRepository> accountRepositoryFactory,
        ServerId localServerId,
        string servicesHost,
        ILogger<HostServ> logger)
    {
        _vhostRepositoryFactory = vhostRepositoryFactory;
        _accountRepositoryFactory = accountRepositoryFactory;
        _localServerId = localServerId;
        Host = servicesHost;
        // UID format: SID + "AAAAAA" (services get predictable UIDs)
        Uid = $"{localServerId.Sid}AAAAAH";
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask HandleMessageAsync(ServiceMessageContext context, CancellationToken cancellationToken = default)
    {
        switch (context.Command)
        {
            case "HELP":
                await ShowHelpAsync(context, context.Arguments.Length > 0 ? context.Arguments[0] : null, cancellationToken);
                break;

            case "REQUEST":
                await HandleRequestAsync(context, cancellationToken);
                break;

            case "ACTIVATE":
            case "ON":
                await HandleActivateAsync(context, cancellationToken);
                break;

            case "DEACTIVATE":
            case "OFF":
                await HandleDeactivateAsync(context, cancellationToken);
                break;

            case "DELETE":
            case "DEL":
                await HandleDeleteAsync(context, cancellationToken);
                break;

            case "LIST":
                await HandleListAsync(context, cancellationToken);
                break;

            case "APPROVE":
                await HandleApproveAsync(context, cancellationToken);
                break;

            case "REJECT":
                await HandleRejectAsync(context, cancellationToken);
                break;

            case "WAITING":
            case "PENDING":
                await HandleWaitingAsync(context, cancellationToken);
                break;

            default:
                await context.ReplyAsync(this, $"Unknown command: {context.Command}. Type /msg HostServ HELP for help.", cancellationToken);
                break;
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GetHelp(string? command = null)
    {
        if (string.IsNullOrEmpty(command))
        {
            return new[]
            {
                "*** HostServ Help ***",
                " ",
                "HostServ allows users to request custom virtual hosts",
                "to replace their real hostname for privacy.",
                " ",
                "Available commands:",
                "  REQUEST   - Request a new vhost",
                "  ACTIVATE  - Activate your approved vhost",
                "  OFF       - Deactivate your vhost",
                "  DELETE    - Delete a vhost",
                "  LIST      - List your vhosts",
                " ",
                "IRC Operator commands:",
                "  APPROVE   - Approve a pending vhost request",
                "  REJECT    - Reject a pending vhost request",
                "  WAITING   - List pending vhost requests",
                " ",
                "For help on a specific command, type:",
                "  /msg HostServ HELP <command>"
            };
        }

        return command.ToUpperInvariant() switch
        {
            "REQUEST" => new[]
            {
                "*** Help for REQUEST ***",
                "Syntax: REQUEST <hostname>",
                " ",
                "Requests a custom virtual host. The hostname must:",
                "  - Be 1-63 characters long",
                "  - Contain only letters, numbers, dots, and hyphens",
                "  - Contain at least one dot",
                "  - Not look like an IP address",
                " ",
                "Example: REQUEST user.example.net",
                " ",
                "Your request will be reviewed by network staff."
            },
            "ACTIVATE" or "ON" => new[]
            {
                "*** Help for ACTIVATE ***",
                "Syntax: ACTIVATE",
                " ",
                "Activates your approved virtual host.",
                "You must have an approved vhost to use this command."
            },
            "DEACTIVATE" or "OFF" => new[]
            {
                "*** Help for OFF ***",
                "Syntax: OFF",
                " ",
                "Deactivates your virtual host and shows your real hostname."
            },
            "DELETE" or "DEL" => new[]
            {
                "*** Help for DELETE ***",
                "Syntax: DELETE <hostname>",
                " ",
                "Deletes one of your virtual hosts.",
                "You must specify the exact hostname to delete."
            },
            "LIST" => new[]
            {
                "*** Help for LIST ***",
                "Syntax: LIST",
                " ",
                "Lists all your virtual hosts, showing their status",
                "(pending, approved, active)."
            },
            "APPROVE" => new[]
            {
                "*** Help for APPROVE ***",
                "Syntax: APPROVE <hostname>",
                " ",
                "Approves a pending vhost request.",
                "This command requires IRC operator privileges."
            },
            "REJECT" => new[]
            {
                "*** Help for REJECT ***",
                "Syntax: REJECT <hostname> [reason]",
                " ",
                "Rejects and deletes a pending vhost request.",
                "This command requires IRC operator privileges."
            },
            "WAITING" or "PENDING" => new[]
            {
                "*** Help for WAITING ***",
                "Syntax: WAITING",
                " ",
                "Lists all pending vhost requests waiting for approval.",
                "This command requires IRC operator privileges."
            },
            _ => new[] { $"No help available for {command}." }
        };
    }

    private async ValueTask ShowHelpAsync(ServiceMessageContext context, string? command, CancellationToken cancellationToken)
    {
        foreach (var line in GetHelp(command))
        {
            await context.ReplyAsync(this, line, cancellationToken);
        }
    }

    private async ValueTask HandleRequestAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to request a vhost.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: REQUEST <hostname>", cancellationToken);
            return;
        }

        var hostname = context.Arguments[0].ToLowerInvariant();

        // Validate hostname
        if (!VirtualHost.IsValidHostname(hostname, out var error))
        {
            await context.ReplyAsync(this, $"Invalid hostname: {error}", cancellationToken);
            return;
        }

        // Get user's account
        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        // Check if hostname is already in use
        var vhostRepo = _vhostRepositoryFactory();
        if (await vhostRepo.IsHostnameInUseAsync(hostname, cancellationToken))
        {
            await context.ReplyAsync(this, $"The hostname \x02{hostname}\x02 is already in use.", cancellationToken);
            return;
        }

        // Create the vhost request
        await vhostRepo.CreateAsync(account.Id, hostname, cancellationToken);

        await context.ReplyAsync(this, $"Your vhost request for \x02{hostname}\x02 has been submitted.", cancellationToken);
        await context.ReplyAsync(this, "Network staff will review your request. You will be notified when it is approved.", cancellationToken);

        _logger.LogInformation("HostServ: {User} requested vhost {Hostname}", context.SourceAccount, hostname);
    }

    private async ValueTask HandleActivateAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ to activate a vhost.", cancellationToken);
            return;
        }

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        // Find an approved but inactive vhost
        var vhostRepo = _vhostRepositoryFactory();
        var vhosts = (await vhostRepo.GetByAccountAsync(account.Id, cancellationToken)).ToList();
        var approvedVhost = vhosts.FirstOrDefault(v => v.IsApproved && !v.IsActive);

        if (approvedVhost is null)
        {
            await context.ReplyAsync(this, "You don't have any approved vhosts to activate.", cancellationToken);
            await context.ReplyAsync(this, "Use REQUEST to request a new vhost.", cancellationToken);
            return;
        }

        // Deactivate any currently active vhosts
        await vhostRepo.DeactivateAllForAccountAsync(account.Id, cancellationToken);

        // Activate this vhost
        approvedVhost.Activate();
        await vhostRepo.UpdateAsync(approvedVhost, cancellationToken);

        await context.ReplyAsync(this, $"Your vhost \x02{approvedVhost.Hostname}\x02 is now active.", cancellationToken);
        
        // Note: Actual hostname change would be sent via IRC protocol here
        _logger.LogInformation("HostServ: {User} activated vhost {Hostname}", context.SourceAccount, approvedVhost.Hostname);
    }

    private async ValueTask HandleDeactivateAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ.", cancellationToken);
            return;
        }

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var vhostRepo = _vhostRepositoryFactory();
        var count = await vhostRepo.DeactivateAllForAccountAsync(account.Id, cancellationToken);

        if (count > 0)
        {
            await context.ReplyAsync(this, "Your vhost has been deactivated.", cancellationToken);
            // Note: Actual hostname change would be sent via IRC protocol here
            _logger.LogInformation("HostServ: {User} deactivated vhost", context.SourceAccount);
        }
        else
        {
            await context.ReplyAsync(this, "You don't have an active vhost.", cancellationToken);
        }
    }

    private async ValueTask HandleDeleteAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: DELETE <hostname>", cancellationToken);
            return;
        }

        var hostname = context.Arguments[0].ToLowerInvariant();

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        // Find the vhost
        var vhostRepo = _vhostRepositoryFactory();
        var vhosts = (await vhostRepo.GetByAccountAsync(account.Id, cancellationToken)).ToList();
        var vhost = vhosts.FirstOrDefault(v => v.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));

        if (vhost is null)
        {
            await context.ReplyAsync(this, $"You don't have a vhost with hostname \x02{hostname}\x02.", cancellationToken);
            return;
        }

        await vhostRepo.DeleteAsync(vhost.Id, cancellationToken);

        await context.ReplyAsync(this, $"Vhost \x02{hostname}\x02 has been deleted.", cancellationToken);
        _logger.LogInformation("HostServ: {User} deleted vhost {Hostname}", context.SourceAccount, hostname);
    }

    private async ValueTask HandleListAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Must be identified
        if (context.SourceAccount is null)
        {
            await context.ReplyAsync(this, "You must be identified with NickServ.", cancellationToken);
            return;
        }

        var accountRepo = _accountRepositoryFactory();
        var account = await accountRepo.GetByNameAsync(context.SourceAccount, cancellationToken);
        if (account is null)
        {
            await context.ReplyAsync(this, "Unable to find your account.", cancellationToken);
            return;
        }

        var vhostRepo = _vhostRepositoryFactory();
        var vhosts = (await vhostRepo.GetByAccountAsync(account.Id, cancellationToken)).ToList();

        if (vhosts.Count == 0)
        {
            await context.ReplyAsync(this, "You don't have any vhosts.", cancellationToken);
            await context.ReplyAsync(this, "Use REQUEST to request a new vhost.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, "Your virtual hosts:", cancellationToken);

        foreach (var vhost in vhosts)
        {
            var status = vhost.IsActive ? "[ACTIVE]" : vhost.IsApproved ? "[APPROVED]" : "[PENDING]";
            await context.ReplyAsync(this, $"  {status} \x02{vhost.Hostname}\x02", cancellationToken);
        }
    }

    private async ValueTask HandleApproveAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Requires operator privileges
        if (!context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be an IRC operator.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: APPROVE <hostname>", cancellationToken);
            return;
        }

        var hostname = context.Arguments[0].ToLowerInvariant();

        // Find the pending vhost
        var vhostRepo = _vhostRepositoryFactory();
        var pending = (await vhostRepo.GetPendingAsync(cancellationToken)).ToList();
        var vhost = pending.FirstOrDefault(v => v.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));

        if (vhost is null)
        {
            await context.ReplyAsync(this, $"No pending vhost request found for \x02{hostname}\x02.", cancellationToken);
            return;
        }

        // Approve the vhost
        vhost.Approve(context.SourceAccount ?? context.SourceNick);
        await vhostRepo.UpdateAsync(vhost, cancellationToken);

        await context.ReplyAsync(this, $"Vhost \x02{hostname}\x02 has been approved.", cancellationToken);
        
        // Note: Would send notification to the requesting user via IRC protocol
        _logger.LogInformation("HostServ: {Oper} approved vhost {Hostname}", context.SourceNick, hostname);
    }

    private async ValueTask HandleRejectAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Requires operator privileges
        if (!context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be an IRC operator.", cancellationToken);
            return;
        }

        if (context.Arguments.Length < 1)
        {
            await context.ReplyAsync(this, "Syntax: REJECT <hostname> [reason]", cancellationToken);
            return;
        }

        var hostname = context.Arguments[0].ToLowerInvariant();
        var reason = context.Arguments.Length > 1 ? string.Join(" ", context.Arguments.Skip(1)) : "Rejected by staff";

        // Find the pending vhost
        var vhostRepo = _vhostRepositoryFactory();
        var pending = (await vhostRepo.GetPendingAsync(cancellationToken)).ToList();
        var vhost = pending.FirstOrDefault(v => v.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));

        if (vhost is null)
        {
            await context.ReplyAsync(this, $"No pending vhost request found for \x02{hostname}\x02.", cancellationToken);
            return;
        }

        // Delete the vhost request
        await vhostRepo.DeleteAsync(vhost.Id, cancellationToken);

        await context.ReplyAsync(this, $"Vhost \x02{hostname}\x02 has been rejected.", cancellationToken);
        
        // Note: Would send notification to the requesting user via IRC protocol
        _logger.LogInformation("HostServ: {Oper} rejected vhost {Hostname}: {Reason}", context.SourceNick, hostname, reason);
    }

    private async ValueTask HandleWaitingAsync(ServiceMessageContext context, CancellationToken cancellationToken)
    {
        // Requires operator privileges
        if (!context.IsOperator)
        {
            await context.ReplyAsync(this, "Access denied. You must be an IRC operator.", cancellationToken);
            return;
        }

        var vhostRepo = _vhostRepositoryFactory();
        var pending = (await vhostRepo.GetPendingAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            await context.ReplyAsync(this, "No pending vhost requests.", cancellationToken);
            return;
        }

        await context.ReplyAsync(this, $"Pending vhost requests ({pending.Count}):", cancellationToken);

        var accountRepo = _accountRepositoryFactory();
        foreach (var vhost in pending)
        {
            var account = await accountRepo.GetByIdAsync(vhost.AccountId, cancellationToken);
            var accountName = account?.Name ?? "unknown";
            var timeAgo = FormatTimeAgo(vhost.RequestedAt);
            
            await context.ReplyAsync(this, $"  \x02{vhost.Hostname}\x02 - requested by {accountName} ({timeAgo})", cancellationToken);
        }

        await context.ReplyAsync(this, "Use APPROVE <hostname> or REJECT <hostname> [reason]", cancellationToken);
    }

    private static string FormatTimeAgo(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.UtcNow - time;

        if (elapsed.TotalMinutes < 1)
            return "just now";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7)
            return $"{(int)elapsed.TotalDays}d ago";
        
        return time.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
