namespace Hugin.Core.Enums;

/// <summary>
/// Represents the registration state of a client connection.
/// </summary>
public enum RegistrationState
{
    /// <summary>Client has just connected, no registration started.</summary>
    None = 0,

    /// <summary>Client is negotiating capabilities.</summary>
    CapNegotiating = 1,

    /// <summary>Client has sent PASS command.</summary>
    PassReceived = 2,

    /// <summary>Client has sent NICK command.</summary>
    NickReceived = 3,

    /// <summary>Client has sent USER command.</summary>
    UserReceived = 4,

    /// <summary>Client has sent both NICK and USER commands.</summary>
    NickAndUserReceived = 5,

    /// <summary>Client is authenticating via SASL.</summary>
    SaslAuthenticating = 6,

    /// <summary>Client has completed registration.</summary>
    Registered = 7
}
