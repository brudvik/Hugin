using Hugin.Core.Entities;

namespace Hugin.Persistence;

/// <summary>
/// Entity Framework entity for VirtualHost.
/// </summary>
public sealed class VirtualHostEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Converts the entity to a domain model.
    /// </summary>
    public VirtualHost ToDomain()
    {
        return new VirtualHost(Id, AccountId, Hostname)
        {
            RequestedAt = RequestedAt,
            ApprovedAt = ApprovedAt,
            ApprovedBy = ApprovedBy,
            IsActive = IsActive,
            Notes = Notes
        };
    }
}
