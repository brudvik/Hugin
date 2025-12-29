using System.Text;
using Hugin.Core.Enums;
using Hugin.Security;
using Hugin.Security.Sasl;

namespace Hugin.Protocol.Commands.Handlers;

/// <summary>
/// Handles the AUTHENTICATE command for SASL authentication.
/// </summary>
public sealed class AuthenticateHandler : CommandHandlerBase
{
    public override string Command => "AUTHENTICATE";
    public override int MinimumParameters => 1;
    public override bool RequiresRegistration => false;

    private const int MaxAuthDataLength = 400;
    private const int MaxTotalDataLength = 8192;

    public override async ValueTask HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
    {
        var nick = context.User.Nickname?.Value ?? "*";

        // Check if SASL capability is enabled
        if (!context.Capabilities.HasSasl)
        {
            await context.ReplyAsync(
                IrcNumerics.SaslFail(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Check if already authenticated
        if (context.User.Account is not null)
        {
            await context.ReplyAsync(
                IrcNumerics.SaslAlready(context.ServerName, nick),
                cancellationToken);
            return;
        }

        var param = context.Message.Parameters[0];

        // Handle abort
        if (param == "*")
        {
            context.SaslSession = null;
            await context.ReplyAsync(
                IrcNumerics.SaslAborted(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // If no session, this is mechanism selection
        if (context.SaslSession is null)
        {
            await HandleMechanismSelectionAsync(context, param, cancellationToken);
            return;
        }

        // Continue existing session
        await HandleAuthenticationDataAsync(context, param, cancellationToken);
    }

    private static async ValueTask HandleMechanismSelectionAsync(
        CommandContext context,
        string mechanismName,
        CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname?.Value ?? "*";
        var saslManager = context.GetService<SaslManager>();

        if (saslManager is null)
        {
            // No SASL manager available, create a default one
            saslManager = new SaslManager();
        }

        var mechanism = saslManager.GetMechanism(mechanismName);

        if (mechanism is null)
        {
            // Unknown mechanism
            await context.ReplyAsync(
                IrcNumerics.SaslMechanisms(context.ServerName, nick, 
                    saslManager.GetMechanismList(context.Connection.IsSecure)),
                cancellationToken);
            await context.ReplyAsync(
                IrcNumerics.SaslFail(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Check if mechanism requires TLS
        if (mechanism.RequiresTls && !context.Connection.IsSecure)
        {
            await context.ReplyAsync(
                IrcNumerics.SaslFail(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Create session
        context.SaslSession = new SaslSession(mechanismName);

        // Send empty challenge for PLAIN (expects initial response)
        await SendChallengeAsync(context, Array.Empty<byte>(), cancellationToken);
    }

    private static async ValueTask HandleAuthenticationDataAsync(
        CommandContext context,
        string data,
        CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname?.Value ?? "*";
        var session = context.SaslSession!;

        // Handle chunked data
        if (data != "+")
        {
            session.AccumulatedData.Append(data);

            // Check for continuation
            if (data.Length == MaxAuthDataLength)
            {
                // More data expected
                return;
            }
        }

        // Check total size
        if (session.AccumulatedData.Length > MaxTotalDataLength)
        {
            context.SaslSession = null;
            await context.ReplyAsync(
                IrcNumerics.SaslTooLong(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Decode the accumulated data
        byte[] clientResponse;
        try
        {
            var base64Data = session.AccumulatedData.ToString();
            if (string.IsNullOrEmpty(base64Data) || base64Data == "+")
            {
                clientResponse = Array.Empty<byte>();
            }
            else
            {
                clientResponse = Convert.FromBase64String(base64Data);
            }
        }
        catch (FormatException)
        {
            context.SaslSession = null;
            await context.ReplyAsync(
                IrcNumerics.SaslFail(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Process the response
        await ProcessSaslResponseAsync(context, clientResponse, cancellationToken);
    }

    private static async ValueTask ProcessSaslResponseAsync(
        CommandContext context,
        byte[] clientResponse,
        CancellationToken cancellationToken)
    {
        var nick = context.User.Nickname?.Value ?? "*";
        var session = context.SaslSession!;

        var saslManager = context.GetService<SaslManager>() ?? new SaslManager();
        var mechanism = saslManager.GetMechanism(session.Mechanism);

        if (mechanism is null)
        {
            context.SaslSession = null;
            await context.ReplyAsync(
                IrcNumerics.SaslFail(context.ServerName, nick),
                cancellationToken);
            return;
        }

        // Get account repository for validation
        var accountRepo = context.GetService<Core.Interfaces.IAccountRepository>();

        // Create SASL context
        var saslContext = new SaslContext(
            context.Connection.ConnectionId,
            context.Connection.CertificateFingerprint,
            async (username, password, ct) =>
            {
                if (accountRepo is null) return false;
                var account = await accountRepo.GetByNameAsync(username, ct);
                if (account is null) return false;
                return PasswordHasher.VerifyPassword(password, account.PasswordHash);
            },
            async (fingerprint, ct) =>
            {
                if (accountRepo is null) return null;
                var account = await accountRepo.GetByCertificateFingerprintAsync(fingerprint, ct);
                return account?.Name;
            }
        );

        try
        {
            var result = await mechanism.ProcessAsync(clientResponse, saslContext, cancellationToken);

            switch (result.State)
            {
                case SaslState.Continue:
                    session.AccumulatedData.Clear();
                    await SendChallengeAsync(context, result.Challenge ?? Array.Empty<byte>(), cancellationToken);
                    break;

                case SaslState.Success:
                    context.SaslSession = null;
                    session.SetSuccess(result.AccountName!);

                    // Set user as authenticated
                    context.User.SetAuthenticated(result.AccountName!);

                    // Send success numerics
                    await context.ReplyAsync(
                        IrcNumerics.LoggedIn(context.ServerName, nick, 
                            context.User.Hostmask.ToString(), result.AccountName!),
                        cancellationToken);
                    await context.ReplyAsync(
                        IrcNumerics.SaslSuccess(context.ServerName, nick),
                        cancellationToken);
                    break;

                case SaslState.Failure:
                    context.SaslSession = null;
                    session.SetFailed();
                    await context.ReplyAsync(
                        IrcNumerics.SaslFail(context.ServerName, nick),
                        cancellationToken);
                    break;
            }
        }
        catch (Exception)
        {
            context.SaslSession = null;
            await context.ReplyAsync(
                IrcNumerics.SaslFail(context.ServerName, nick),
                cancellationToken);
        }
    }

    private static async ValueTask SendChallengeAsync(
        CommandContext context,
        byte[] challenge,
        CancellationToken cancellationToken)
    {
        string encodedChallenge;
        if (challenge.Length == 0)
        {
            encodedChallenge = "+";
        }
        else
        {
            encodedChallenge = Convert.ToBase64String(challenge);
        }

        // Send AUTHENTICATE response, chunking if necessary
        for (int i = 0; i < encodedChallenge.Length; i += MaxAuthDataLength)
        {
            var chunk = encodedChallenge.Substring(i, 
                Math.Min(MaxAuthDataLength, encodedChallenge.Length - i));
            var msg = IrcMessage.Create("AUTHENTICATE", chunk);
            await context.ReplyAsync(msg, cancellationToken);
        }

        // If the last chunk was exactly 400 bytes, send a continuation marker
        if (encodedChallenge.Length > 0 && encodedChallenge.Length % MaxAuthDataLength == 0)
        {
            var msg = IrcMessage.Create("AUTHENTICATE", "+");
            await context.ReplyAsync(msg, cancellationToken);
        }
    }
}
