using System.Text.RegularExpressions;

namespace Hugin.Core.Entities;

/// <summary>
/// Represents a virtual host (vhost) assigned to a user account.
/// </summary>
public sealed class VirtualHost
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the account ID this vhost belongs to.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Gets or sets the virtual hostname.
    /// </summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the vhost was requested.
    /// </summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>
    /// Gets or sets when the vhost was approved (null if pending).
    /// </summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>
    /// Gets or sets the account name of who approved the vhost.
    /// </summary>
    public string? ApprovedBy { get; set; }

    /// <summary>
    /// Gets or sets whether the vhost is currently active/enabled.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets optional notes about the vhost.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets whether the vhost is approved.
    /// </summary>
    public bool IsApproved => ApprovedAt.HasValue;

    /// <summary>
    /// Gets whether the vhost is pending approval.
    /// </summary>
    public bool IsPending => !IsApproved;

    /// <summary>
    /// Creates a new virtual host.
    /// </summary>
    public VirtualHost()
    {
    }

    /// <summary>
    /// Creates a new virtual host with the specified values.
    /// </summary>
    /// <param name="id">Vhost identifier.</param>
    /// <param name="accountId">Account ID.</param>
    /// <param name="hostname">Virtual hostname.</param>
    public VirtualHost(Guid id, Guid accountId, string hostname)
    {
        Id = id;
        AccountId = accountId;
        Hostname = hostname;
        RequestedAt = DateTimeOffset.UtcNow;
        IsActive = false;
    }

    /// <summary>
    /// Approves the vhost request.
    /// </summary>
    /// <param name="approvedBy">Account name of approver.</param>
    public void Approve(string approvedBy)
    {
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovedBy = approvedBy;
    }

    /// <summary>
    /// Activates the vhost.
    /// </summary>
    public void Activate()
    {
        if (!IsApproved)
        {
            throw new InvalidOperationException("Cannot activate vhost that hasn't been approved");
        }
        IsActive = true;
    }

    /// <summary>
    /// Deactivates the vhost.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Validates a virtual hostname.
    /// </summary>
    /// <param name="hostname">Hostname to validate.</param>
    /// <param name="error">Error message if invalid.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public static bool IsValidHostname(string? hostname, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(hostname))
        {
            error = "Hostname cannot be empty";
            return false;
        }

        if (hostname.Length > 63)
        {
            error = "Hostname too long (max 63 characters)";
            return false;
        }

        // Hostname must contain only alphanumeric, dots, and hyphens
        // Must not start or end with dot or hyphen
        if (!Regex.IsMatch(hostname, @"^[a-zA-Z0-9]([a-zA-Z0-9\-\.]*[a-zA-Z0-9])?$"))
        {
            error = "Hostname contains invalid characters or format";
            return false;
        }

        // Must contain at least one dot (to look like a real hostname)
        if (!hostname.Contains('.'))
        {
            error = "Hostname must contain at least one dot (e.g., user.example)";
            return false;
        }

        // No consecutive dots
        if (hostname.Contains(".."))
        {
            error = "Hostname cannot contain consecutive dots";
            return false;
        }

        // Cannot contain @, which would be confusing in hostmasks
        if (hostname.Contains('@'))
        {
            error = "Hostname cannot contain @";
            return false;
        }

        // Cannot look like an IP address
        if (Regex.IsMatch(hostname, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$"))
        {
            error = "Hostname cannot look like an IP address";
            return false;
        }

        return true;
    }
}
