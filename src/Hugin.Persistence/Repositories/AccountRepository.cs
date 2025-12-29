// CA1304/CA1311/CA1862: ToLower/ToUpper in EF Core LINQ queries are translated to SQL LOWER()/UPPER()
// which are culture-invariant. Suppressing these warnings is intentional.
#pragma warning disable CA1304, CA1311, CA1862

using Hugin.Core.Entities;
using Hugin.Core.Interfaces;
using Hugin.Security;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence.Repositories;

/// <summary>
/// PostgreSQL implementation of account repository.
/// </summary>
public sealed class AccountRepository : IAccountRepository
{
    private readonly HuginDbContext _context;

    /// <summary>
    /// Creates a new account repository.
    /// </summary>
    /// <param name="context">The database context.</param>
    public AccountRepository(HuginDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts
            .Include(a => a.Nicknames)
            .Include(a => a.Fingerprints)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<Account?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts
            .Include(a => a.Nicknames)
            .Include(a => a.Fingerprints)
            .FirstOrDefaultAsync(a => a.Name.ToLower() == name.ToLower(), cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts
            .Include(a => a.Nicknames)
            .Include(a => a.Fingerprints)
            .FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == email.ToLower(), cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<Account?> GetByNicknameAsync(string nickname, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts
            .Include(a => a.Nicknames)
            .Include(a => a.Fingerprints)
            .FirstOrDefaultAsync(a => a.Nicknames.Any(n => n.Nickname.ToLower() == nickname.ToLower()), cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<Account?> GetByCertificateFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts
            .Include(a => a.Nicknames)
            .Include(a => a.Fingerprints)
            .FirstOrDefaultAsync(a => a.Fingerprints.Any(f => f.Fingerprint.ToUpper() == fingerprint.ToUpper()), cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Accounts.AnyAsync(a => a.Name.ToLower() == name.ToLower(), cancellationToken);
    }

    public async Task<Account> CreateAsync(string name, string passwordHash, CancellationToken cancellationToken = default)
    {
        var entity = new AccountEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            PasswordHash = passwordHash,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Accounts.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return entity.ToDomain();
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts
            .Include(a => a.Nicknames)
            .Include(a => a.Fingerprints)
            .FirstOrDefaultAsync(a => a.Id == account.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Account {account.Id} not found");
        }

        // Update basic properties
        entity.PasswordHash = account.PasswordHash;
        entity.Email = account.Email;
        entity.LastSeenAt = account.LastSeenAt;
        entity.IsVerified = account.IsVerified;
        entity.IsSuspended = account.IsSuspended;
        entity.SuspensionReason = account.SuspensionReason;
        entity.IsOperator = account.IsOperator;
        entity.OperatorPrivileges = (int)account.OperatorPrivileges;

        // Sync nicknames
        var existingNicks = entity.Nicknames.Select(n => n.Nickname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredNicks = account.RegisteredNicknames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove old
        foreach (var nick in entity.Nicknames.Where(n => !desiredNicks.Contains(n.Nickname)).ToList())
        {
            entity.Nicknames.Remove(nick);
        }

        // Add new
        foreach (var nick in desiredNicks.Where(n => !existingNicks.Contains(n)))
        {
            entity.Nicknames.Add(new RegisteredNicknameEntity
            {
                Id = Guid.NewGuid(),
                AccountId = entity.Id,
                Nickname = nick,
                RegisteredAt = DateTimeOffset.UtcNow
            });
        }

        // Sync fingerprints
        var existingFps = entity.Fingerprints.Select(f => f.Fingerprint).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredFps = account.CertificateFingerprints.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove old
        foreach (var fp in entity.Fingerprints.Where(f => !desiredFps.Contains(f.Fingerprint)).ToList())
        {
            entity.Fingerprints.Remove(fp);
        }

        // Add new
        foreach (var fp in desiredFps.Where(f => !existingFps.Contains(f)))
        {
            entity.Fingerprints.Add(new CertificateFingerprintEntity
            {
                Id = Guid.NewGuid(),
                AccountId = entity.Id,
                Fingerprint = fp,
                AddedAt = DateTimeOffset.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts.FindAsync(new object[] { id }, cancellationToken);
        if (entity is not null)
        {
            _context.Accounts.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<Account>> GetOperatorsAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Accounts
            .Include(a => a.Nicknames)
            .Include(a => a.Fingerprints)
            .Where(a => a.IsOperator)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain());
    }

    public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Accounts.CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ValidatePasswordAsync(Guid id, string password, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Accounts.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return false;
        }

        // Use Argon2id password verification (static methods)
        return PasswordHasher.VerifyPassword(password, entity.PasswordHash);
    }

    /// <inheritdoc />
    public async Task UpdatePasswordAsync(Guid id, string newPassword, CancellationToken cancellationToken = default)
    {
        var newHash = PasswordHasher.HashPassword(newPassword);

        await _context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PasswordHash, newHash), cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetEmailAsync(Guid id, string email, CancellationToken cancellationToken = default)
    {
        await _context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Email, email), cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateLastSeenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _context.Accounts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastSeenAt, DateTimeOffset.UtcNow), cancellationToken);
    }
}
