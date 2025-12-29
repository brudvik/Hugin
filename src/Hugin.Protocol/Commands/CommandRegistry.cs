using System.Reflection;

namespace Hugin.Protocol.Commands;

/// <summary>
/// Registry for command handlers.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a command handler.
    /// </summary>
    public void Register(ICommandHandler handler)
    {
        _handlers[handler.Command] = handler;
    }

    /// <summary>
    /// Gets a handler for a command.
    /// </summary>
    public ICommandHandler? GetHandler(string command)
    {
        return _handlers.GetValueOrDefault(command.ToUpperInvariant());
    }

    /// <summary>
    /// Gets all registered commands.
    /// </summary>
    public IEnumerable<string> GetCommands() => _handlers.Keys;

    /// <summary>
    /// Registers all handlers in an assembly.
    /// </summary>
    public void RegisterFromAssembly(Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => typeof(ICommandHandler).IsAssignableFrom(t));

        foreach (var type in handlerTypes)
        {
            if (Activator.CreateInstance(type) is ICommandHandler handler)
            {
                Register(handler);
            }
        }
    }

    /// <summary>
    /// Registers all built-in handlers.
    /// </summary>
    public void RegisterBuiltInHandlers()
    {
        RegisterFromAssembly(typeof(CommandRegistry).Assembly);
    }
}
