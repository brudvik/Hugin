namespace Hugin.Protocol.S2S.Commands;

/// <summary>
/// Base class for S2S command handlers.
/// </summary>
public abstract class S2SCommandHandlerBase : IS2SCommandHandler
{
    /// <inheritdoc />
    public abstract string Command { get; }

    /// <inheritdoc />
    public virtual int MinimumParameters => 0;

    /// <inheritdoc />
    public abstract ValueTask HandleAsync(S2SContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a typed service from the context's service provider.
    /// </summary>
    protected static T? GetService<T>(S2SContext context) where T : class =>
        context.ServiceProvider.GetService(typeof(T)) as T;
}
