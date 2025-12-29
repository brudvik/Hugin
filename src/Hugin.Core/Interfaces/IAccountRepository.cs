using Hugin.Core.Entities;

namespace Hugin.Core.Interfaces;

/// <summary>
/// Repository for persistent account storage.
/// </summary>
public interface IAccountRepository
{
    /// <summary>
    /// Gets an account by ID.
    /// </summary>
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an account by name.
    /// </summary>
    Task<Account?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an account by email.
    /// </summary>
    Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an account by registered nickname.
    /// </summary>
    Task<Account?> GetByNicknameAsync(string nickname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an account by certificate fingerprint.
    /// </summary>
    Task<Account?> GetByCertificateFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an account name exists.
    /// </summary>
    Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new account.
    /// </summary>
    Task<Account> CreateAsync(string name, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an account.
    /// </summary>
    Task UpdateAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an account.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all operators.
    /// </summary>
    Task<IEnumerable<Account>> GetOperatorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total account count.
    /// </summary>
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a password for an account.
    /// </summary>
    Task<bool> ValidatePasswordAsync(Guid id, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the password for an account.
    /// </summary>
    Task UpdatePasswordAsync(Guid id, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the email for an account.
    /// </summary>
    Task SetEmailAsync(Guid id, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last seen timestamp.
    /// </summary>
    Task UpdateLastSeenAsync(Guid id, CancellationToken cancellationToken = default);
}
