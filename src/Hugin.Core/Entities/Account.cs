namespace Hugin.Core.Entities;

/// <summary>
/// Represents a registered user account for persistent authentication.
/// </summary>
public sealed class Account
{
    /// <summary>
    /// Gets the unique account ID.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the account name (used for SASL authentication).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the password hash (Argon2id).
    /// </summary>
    public string PasswordHash { get; private set; }

    /// <summary>
    /// Gets the account email (optional).
    /// </summary>
    public string? Email { get; private set; }

    /// <summary>
    /// Gets when the account was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets when the account was last seen online.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; private set; }

    /// <summary>
    /// Gets the registered nicknames for this account.
    /// </summary>
    public IReadOnlyList<string> RegisteredNicknames => _registeredNicknames;
    private readonly List<string> _registeredNicknames = new();

    /// <summary>
    /// Gets the certificate fingerprints for EXTERNAL auth.
    /// </summary>
    public IReadOnlyList<string> CertificateFingerprints => _certificateFingerprints;
    private readonly List<string> _certificateFingerprints = new();

    /// <summary>
    /// Gets whether the account is verified.
    /// </summary>
    public bool IsVerified { get; private set; }

    /// <summary>
    /// Gets whether the account is suspended.
    /// </summary>
    public bool IsSuspended { get; private set; }

    /// <summary>
    /// Gets the suspension reason (if suspended).
    /// </summary>
    public string? SuspensionReason { get; private set; }

    /// <summary>
    /// Gets whether the account has IRC operator privileges.
    /// </summary>
    public bool IsOperator { get; private set; }

    /// <summary>
    /// Gets the operator privilege flags.
    /// </summary>
    public OperatorPrivileges OperatorPrivileges { get; private set; }

    /// <summary>
    /// Creates a new account.
    /// </summary>
    /// <param name="id">The unique account ID.</param>
    /// <param name="name">The account name.</param>
    /// <param name="passwordHash">The Argon2id password hash.</param>
    public Account(Guid id, string name, string passwordHash)
    {
        Id = id;
        Name = name;
        PasswordHash = passwordHash;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Updates the password hash.
    /// </summary>
    public void SetPassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
    }

    /// <summary>
    /// Sets the email address.
    /// </summary>
    public void SetEmail(string email)
    {
        Email = email;
    }

    /// <summary>
    /// Updates the last seen timestamp.
    /// </summary>
    public void UpdateLastSeen()
    {
        LastSeenAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Registers a nickname to this account.
    /// </summary>
    public void RegisterNickname(string nickname)
    {
        if (!_registeredNicknames.Contains(nickname, StringComparer.OrdinalIgnoreCase))
        {
            _registeredNicknames.Add(nickname);
        }
    }

    /// <summary>
    /// Unregisters a nickname from this account.
    /// </summary>
    public void UnregisterNickname(string nickname)
    {
        _registeredNicknames.RemoveAll(n => n.Equals(nickname, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a certificate fingerprint for EXTERNAL auth.
    /// </summary>
    public void AddCertificateFingerprint(string fingerprint)
    {
        if (!_certificateFingerprints.Contains(fingerprint, StringComparer.OrdinalIgnoreCase))
        {
            _certificateFingerprints.Add(fingerprint.ToUpperInvariant());
        }
    }

    /// <summary>
    /// Removes a certificate fingerprint.
    /// </summary>
    public void RemoveCertificateFingerprint(string fingerprint)
    {
        _certificateFingerprints.RemoveAll(f => f.Equals(fingerprint, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Marks the account as verified.
    /// </summary>
    public void Verify()
    {
        IsVerified = true;
    }

    /// <summary>
    /// Suspends the account.
    /// </summary>
    public void Suspend(string reason)
    {
        IsSuspended = true;
        SuspensionReason = reason;
    }

    /// <summary>
    /// Unsuspends the account.
    /// </summary>
    public void Unsuspend()
    {
        IsSuspended = false;
        SuspensionReason = null;
    }

    /// <summary>
    /// Grants operator privileges.
    /// </summary>
    public void GrantOperator(OperatorPrivileges privileges)
    {
        IsOperator = true;
        OperatorPrivileges = privileges;
    }

    /// <summary>
    /// Revokes operator privileges.
    /// </summary>
    public void RevokeOperator()
    {
        IsOperator = false;
        OperatorPrivileges = OperatorPrivileges.None;
    }
}

/// <summary>
/// IRC operator privileges.
/// </summary>
[Flags]
public enum OperatorPrivileges
{
    None = 0,

    /// <summary>Can set global K-lines.</summary>
    Kline = 1 << 0,

    /// <summary>Can set D-lines (IP bans).</summary>
    Dline = 1 << 1,

    /// <summary>Can use KILL command.</summary>
    Kill = 1 << 2,

    /// <summary>Can use remote KILL.</summary>
    RemoteKill = 1 << 3,

    /// <summary>Can use REHASH command.</summary>
    Rehash = 1 << 4,

    /// <summary>Can use DIE/RESTART commands.</summary>
    Die = 1 << 5,

    /// <summary>Can connect/disconnect servers.</summary>
    Connect = 1 << 6,

    /// <summary>Can see real hostnames.</summary>
    SeeRealHost = 1 << 7,

    /// <summary>Can send global notices.</summary>
    GlobalNotice = 1 << 8,

    /// <summary>Local operator (limited privileges).</summary>
    LocalOper = Kline | Kill | SeeRealHost,

    /// <summary>Global operator (full privileges).</summary>
    GlobalOper = Kline | Dline | Kill | RemoteKill | Rehash | Connect | SeeRealHost | GlobalNotice,

    /// <summary>Server administrator (all privileges).</summary>
    Admin = GlobalOper | Die
}
