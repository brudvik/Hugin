using Hugin.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hugin.Persistence;

/// <summary>
/// Entity Framework Core database context for Hugin.
/// </summary>
public sealed class HuginDbContext : DbContext
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<RegisteredNicknameEntity> RegisteredNicknames => Set<RegisteredNicknameEntity>();
    public DbSet<CertificateFingerprintEntity> CertificateFingerprints => Set<CertificateFingerprintEntity>();
    public DbSet<StoredMessageEntity> Messages => Set<StoredMessageEntity>();
    public DbSet<ChannelRegistrationEntity> RegisteredChannels => Set<ChannelRegistrationEntity>();
    public DbSet<ServerLinkEntity> ServerLinks => Set<ServerLinkEntity>();

    public HuginDbContext(DbContextOptions<HuginDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Account
        modelBuilder.Entity<AccountEntity>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
            entity.Property(e => e.IsVerified).HasColumnName("is_verified");
            entity.Property(e => e.IsSuspended).HasColumnName("is_suspended");
            entity.Property(e => e.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(500);
            entity.Property(e => e.IsOperator).HasColumnName("is_operator");
            entity.Property(e => e.OperatorPrivileges).HasColumnName("operator_privileges");

            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();

            entity.HasMany(e => e.Nicknames)
                .WithOne(n => n.Account)
                .HasForeignKey(n => n.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Fingerprints)
                .WithOne(f => f.Account)
                .HasForeignKey(f => f.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Registered Nicknames
        modelBuilder.Entity<RegisteredNicknameEntity>(entity =>
        {
            entity.ToTable("registered_nicknames");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.Nickname).HasColumnName("nickname").HasMaxLength(30).IsRequired();
            entity.Property(e => e.RegisteredAt).HasColumnName("registered_at");

            entity.HasIndex(e => e.Nickname).IsUnique();
        });

        // Certificate Fingerprints
        modelBuilder.Entity<CertificateFingerprintEntity>(entity =>
        {
            entity.ToTable("certificate_fingerprints");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.Fingerprint).HasColumnName("fingerprint").HasMaxLength(128).IsRequired();
            entity.Property(e => e.AddedAt).HasColumnName("added_at");

            entity.HasIndex(e => e.Fingerprint).IsUnique();
        });

        // Stored Messages
        modelBuilder.Entity<StoredMessageEntity>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MessageId).HasColumnName("message_id").HasMaxLength(64).IsRequired();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp");
            entity.Property(e => e.SenderHostmask).HasColumnName("sender_hostmask").HasMaxLength(512).IsRequired();
            entity.Property(e => e.SenderAccount).HasColumnName("sender_account").HasMaxLength(50);
            entity.Property(e => e.Target).HasColumnName("target").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.Tags).HasColumnName("tags");

            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => new { e.Target, e.Timestamp });
            entity.HasIndex(e => e.Timestamp);
        });

        // Registered Channels
        modelBuilder.Entity<ChannelRegistrationEntity>(entity =>
        {
            entity.ToTable("registered_channels");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.FounderId).HasColumnName("founder_id");
            entity.Property(e => e.Topic).HasColumnName("topic").HasMaxLength(512);
            entity.Property(e => e.Modes).HasColumnName("modes").HasMaxLength(50);
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(50);
            entity.Property(e => e.RegisteredAt).HasColumnName("registered_at");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");

            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Server Links
        modelBuilder.Entity<ServerLinkEntity>(entity =>
        {
            entity.ToTable("server_links");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Sid).HasColumnName("sid").HasMaxLength(3).IsRequired();
            entity.Property(e => e.Host).HasColumnName("host").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Port).HasColumnName("port");
            entity.Property(e => e.SendPassword).HasColumnName("send_password").HasMaxLength(255).IsRequired();
            entity.Property(e => e.ReceivePassword).HasColumnName("receive_password").HasMaxLength(255).IsRequired();
            entity.Property(e => e.AutoConnect).HasColumnName("auto_connect");
            entity.Property(e => e.UseTls).HasColumnName("use_tls");
            entity.Property(e => e.CertificateFingerprint).HasColumnName("certificate_fingerprint").HasMaxLength(128);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.LastConnectedAt).HasColumnName("last_connected_at");
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
            entity.Property(e => e.LinkClass).HasColumnName("link_class");

            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.Sid).IsUnique();
        });
    }
}

// Entity classes

/// <summary>
/// Database entity representing a registered user account.
/// </summary>
public sealed class AccountEntity
{
    /// <summary>Gets or sets the unique account identifier.</summary>
    public Guid Id { get; set; }
    
    /// <summary>Gets or sets the account name (username).</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the Argon2id password hash.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the optional email address.</summary>
    public string? Email { get; set; }
    
    /// <summary>Gets or sets when the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    
    /// <summary>Gets or sets when the account was last seen online.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }
    
    /// <summary>Gets or sets whether the account email is verified.</summary>
    public bool IsVerified { get; set; }
    
    /// <summary>Gets or sets whether the account is suspended.</summary>
    public bool IsSuspended { get; set; }
    
    /// <summary>Gets or sets the reason for suspension, if suspended.</summary>
    public string? SuspensionReason { get; set; }
    
    /// <summary>Gets or sets whether the account has IRC operator privileges.</summary>
    public bool IsOperator { get; set; }
    
    /// <summary>Gets or sets the operator privilege flags.</summary>
    public int OperatorPrivileges { get; set; }

    /// <summary>Gets or sets the registered nicknames for this account.</summary>
    public ICollection<RegisteredNicknameEntity> Nicknames { get; set; } = new List<RegisteredNicknameEntity>();
    
    /// <summary>Gets or sets the certificate fingerprints for SASL EXTERNAL.</summary>
    public ICollection<CertificateFingerprintEntity> Fingerprints { get; set; } = new List<CertificateFingerprintEntity>();

    /// <summary>
    /// Converts this entity to a domain Account object.
    /// </summary>
    /// <returns>A domain Account instance.</returns>
    public Account ToDomain()
    {
        var account = new Account(Id, Name, PasswordHash);
        if (Email is not null) account.SetEmail(Email);
        if (IsVerified) account.Verify();
        if (IsSuspended && SuspensionReason is not null) account.Suspend(SuspensionReason);
        if (IsOperator) account.GrantOperator((OperatorPrivileges)OperatorPrivileges);
        foreach (var nick in Nicknames) account.RegisterNickname(nick.Nickname);
        foreach (var fp in Fingerprints) account.AddCertificateFingerprint(fp.Fingerprint);
        return account;
    }

    /// <summary>
    /// Creates an AccountEntity from a domain Account object.
    /// </summary>
    /// <param name="account">The domain account.</param>
    /// <returns>A database entity representation.</returns>
    public static AccountEntity FromDomain(Account account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        PasswordHash = account.PasswordHash,
        Email = account.Email,
        CreatedAt = account.CreatedAt,
        LastSeenAt = account.LastSeenAt,
        IsVerified = account.IsVerified,
        IsSuspended = account.IsSuspended,
        SuspensionReason = account.SuspensionReason,
        IsOperator = account.IsOperator,
        OperatorPrivileges = (int)account.OperatorPrivileges
    };
}

/// <summary>
/// Database entity for a nickname registered to an account.
/// </summary>
public sealed class RegisteredNicknameEntity
{
    /// <summary>Gets or sets the unique identifier.</summary>
    public Guid Id { get; set; }
    
    /// <summary>Gets or sets the owning account ID.</summary>
    public Guid AccountId { get; set; }
    
    /// <summary>Gets or sets the registered nickname.</summary>
    public string Nickname { get; set; } = string.Empty;
    
    /// <summary>Gets or sets when the nickname was registered.</summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Gets or sets the navigation property to the owning account.</summary>
    public AccountEntity Account { get; set; } = null!;
}

/// <summary>
/// Database entity for a TLS client certificate fingerprint.
/// </summary>
public sealed class CertificateFingerprintEntity
{
    /// <summary>Gets or sets the unique identifier.</summary>
    public Guid Id { get; set; }
    
    /// <summary>Gets or sets the owning account ID.</summary>
    public Guid AccountId { get; set; }
    
    /// <summary>Gets or sets the SHA-256 certificate fingerprint.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    
    /// <summary>Gets or sets when the fingerprint was added.</summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Gets or sets the navigation property to the owning account.</summary>
    public AccountEntity Account { get; set; } = null!;
}

/// <summary>
/// Database entity for stored chat messages (for chathistory).
/// </summary>
public sealed class StoredMessageEntity
{
    /// <summary>Gets or sets the database primary key.</summary>
    public long Id { get; set; }
    
    /// <summary>Gets or sets the unique IRCv3 message ID.</summary>
    public string MessageId { get; set; } = string.Empty;
    
    /// <summary>Gets or sets when the message was sent.</summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>Gets or sets the sender's hostmask.</summary>
    public string SenderHostmask { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the sender's account name, if logged in.</summary>
    public string? SenderAccount { get; set; }
    
    /// <summary>Gets or sets the message target (channel or nickname).</summary>
    public string Target { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the message type (PRIVMSG, NOTICE, etc.).</summary>
    public MessageType Type { get; set; }
    
    /// <summary>Gets or sets the message content.</summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the serialized IRCv3 message tags.</summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Converts this entity to a domain StoredMessage object.
    /// </summary>
    /// <returns>A domain StoredMessage instance.</returns>
    public StoredMessage ToDomain() => new(
        MessageId, Timestamp, SenderHostmask, SenderAccount, Target, Type, Content, Tags);

    /// <summary>
    /// Creates a StoredMessageEntity from a domain StoredMessage object.
    /// </summary>
    /// <param name="msg">The domain message.</param>
    /// <returns>A database entity representation.</returns>
    public static StoredMessageEntity FromDomain(StoredMessage msg) => new()
    {
        MessageId = msg.MessageId,
        Timestamp = msg.Timestamp,
        SenderHostmask = msg.SenderHostmask,
        SenderAccount = msg.SenderAccount,
        Target = msg.Target,
        Type = msg.Type,
        Content = msg.Content,
        Tags = msg.Tags
    };
}

/// <summary>
/// Database entity for a registered (persistent) channel.
/// </summary>
public sealed class ChannelRegistrationEntity
{
    /// <summary>Gets or sets the unique identifier.</summary>
    public Guid Id { get; set; }
    
    /// <summary>Gets or sets the channel name.</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the account ID of the channel founder.</summary>
    public Guid FounderId { get; set; }
    
    /// <summary>Gets or sets the channel topic.</summary>
    public string? Topic { get; set; }
    
    /// <summary>Gets or sets the default channel modes.</summary>
    public string? Modes { get; set; }
    
    /// <summary>Gets or sets the channel key (password).</summary>
    public string? Key { get; set; }
    
    /// <summary>Gets or sets when the channel was registered.</summary>
    public DateTimeOffset RegisteredAt { get; set; }
    
    /// <summary>Gets or sets when the channel was last used.</summary>
    public DateTimeOffset LastUsedAt { get; set; }
}
