using System.Reflection;

namespace Hugin.Protocol.Commands;

/// <summary>
/// Registry for command handlers.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    /// Creates a new command registry.
    /// </summary>
    public CommandRegistry()
    {
    }

    /// <summary>
    /// Creates a new command registry with dependency injection support.
    /// </summary>
    /// <param name="serviceProvider">The service provider for resolving handler dependencies.</param>
    public CommandRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

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
            try
            {
                ICommandHandler? handler = null;

                // Try to create using DI first if we have a service provider
                if (_serviceProvider != null)
                {
                    handler = TryCreateWithServiceProvider(type);
                }

                // Fall back to parameterless constructor
                if (handler == null)
                {
                    handler = TryCreateParameterless(type);
                }

                if (handler != null)
                {
                    Register(handler);
                }
            }
            catch
            {
                // Skip handlers that can't be instantiated
                // They may require dependencies not available at this time
            }
        }
    }

    private ICommandHandler? TryCreateWithServiceProvider(Type type)
    {
        if (_serviceProvider == null) return null;

        try
        {
            // Get the first public constructor
            var constructor = type.GetConstructors().FirstOrDefault();
            if (constructor == null) return null;

            var parameters = constructor.GetParameters();
            var args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                var service = _serviceProvider.GetService(paramType);
                
                // If required parameter can't be resolved, fail
                if (service == null && !parameters[i].HasDefaultValue)
                {
                    return null;
                }
                
                args[i] = service ?? parameters[i].DefaultValue;
            }

            return constructor.Invoke(args) as ICommandHandler;
        }
        catch
        {
            return null;
        }
    }

    private static ICommandHandler? TryCreateParameterless(Type type)
    {
        try
        {
            // Check if type has a parameterless constructor
            var constructor = type.GetConstructor(Type.EmptyTypes);
            if (constructor != null)
            {
                return Activator.CreateInstance(type) as ICommandHandler;
            }

            // Check if the type has a constructor with all optional parameters
            var constructors = type.GetConstructors();
            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                if (parameters.All(p => p.HasDefaultValue))
                {
                    var args = parameters.Select(p => p.DefaultValue).ToArray();
                    return ctor.Invoke(args) as ICommandHandler;
                }
            }
        }
        catch
        {
            // Ignore and return null
        }
        return null;
    }

    /// <summary>
    /// Registers all built-in handlers.
    /// </summary>
    public void RegisterBuiltInHandlers()
    {
        RegisterFromAssembly(typeof(CommandRegistry).Assembly);
    }
}
