using Hugin.Core.Interfaces;
using Hugin.Core.ValueObjects;
using Hugin.Network;
using Hugin.Network.S2S;
using Hugin.Persistence;
using Hugin.Persistence.Repositories;
using Hugin.Protocol.Commands;
using Hugin.Protocol.S2S;
using Hugin.Security;
using Hugin.Server.Configuration;
using Hugin.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Globalization;

namespace Hugin.Server;

/// <summary>
/// Entry point for the Hugin IRC Server application.
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point for the application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code (0 for success, 1 for failure).</returns>
    public static async Task<int> Main(string[] args)
    {
        // Early logging setup
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting Hugin IRC Server");

            var host = CreateHostBuilder(args).Build();

            // Run migrations if enabled
            using (var scope = host.Services.CreateScope())
            {
                var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
                
                // Security validation: warn about default secrets
                ValidateSecurityConfiguration(config);
                
                if (config.Database.RunMigrationsOnStartup)
                {
                    var db = scope.ServiceProvider.GetRequiredService<HuginDbContext>();
                    await db.Database.MigrateAsync();
                    Log.Information("Database migrations applied");
                }
            }

            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Creates and configures the host builder for the IRC server.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>A configured host builder.</returns>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "HuginIRC";
            })
            .UseSerilog((context, services, configuration) =>
            {
                var huginConfig = context.Configuration.GetSection("Hugin").Get<HuginConfiguration>() ?? new();

                configuration
                    .MinimumLevel.Is(Enum.Parse<LogEventLevel>(huginConfig.Logging.MinimumLevel, true))
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("MachineName", Environment.MachineName);

                if (huginConfig.Logging.EnableConsole)
                {
                    configuration.WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                        formatProvider: CultureInfo.InvariantCulture);
                }

                if (!string.IsNullOrEmpty(huginConfig.Logging.FilePath))
                {
                    configuration.WriteTo.File(
                        huginConfig.Logging.FilePath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        formatProvider: CultureInfo.InvariantCulture);
                }
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables("HUGIN_");
                config.AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<HuginConfiguration>(context.Configuration.GetSection("Hugin"));
                var config = context.Configuration.GetSection("Hugin").Get<HuginConfiguration>() ?? new();

                // Database
                services.AddDbContext<HuginDbContext>(options =>
                {
                    options.UseNpgsql(config.Database.ConnectionString);
                });

                // Repositories - In-memory for connected entities
                services.AddSingleton<IUserRepository, InMemoryUserRepository>();
                services.AddSingleton<IChannelRepository, InMemoryChannelRepository>();

                // Repositories - Persistent
                services.AddScoped<IAccountRepository, AccountRepository>();
                services.AddScoped<IMessageRepository, MessageRepository>();
                services.AddScoped<IServerLinkRepository, ServerLinkRepository>();

                // Network
                services.AddSingleton<ConnectionManager>();
                services.AddSingleton<MessageBroker>();
                services.AddSingleton<IMessageBroker>(sp => sp.GetRequiredService<MessageBroker>());
                services.AddSingleton<IConnectionManager>(sp => sp.GetRequiredService<ConnectionManager>());

                // S2S Network
                services.AddSingleton<S2SConnectionManager>();
                services.AddSingleton<IS2SConnectionManager>(sp => sp.GetRequiredService<S2SConnectionManager>());
                services.AddSingleton(sp =>
                {
                    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
                    return ServerId.Create(cfg.Server.Sid, cfg.Server.Name);
                });
                services.AddSingleton<ServerLinkManager>();
                services.AddSingleton<IServerLinkManager>(sp => sp.GetRequiredService<ServerLinkManager>());
                services.AddSingleton<S2SHandshakeManager>();
                services.AddSingleton<IS2SHandshakeManager>(sp => sp.GetRequiredService<S2SHandshakeManager>());

                // S2S Command Handlers
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.ServerHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.SquitHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SPingHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SPongHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.ErrorHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.UidHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SQuitHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SKillHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SNickHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.SjoinHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SPartHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SKickHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SModeHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2STopicHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SPrivmsgHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SNoticeHandler>();
                services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.EncapHandler>();

                // S2S Connector and Dispatcher
                services.AddSingleton<S2SConnector>();
                services.AddSingleton<S2SMessageDispatcher>();

                // IRC Services (NickServ, ChanServ, etc.)
                services.AddSingleton<Hugin.Protocol.S2S.Services.NickServ>(sp =>
                {
                    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
                    var serverId = sp.GetRequiredService<ServerId>();
                    var accountRepo = sp.GetRequiredService<IAccountRepository>();
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Hugin.Protocol.S2S.Services.NickServ>>();
                    return new Hugin.Protocol.S2S.Services.NickServ(
                        accountRepo,
                        serverId,
                        cfg.Server.Name,
                        logger);
                });
                services.AddSingleton<Hugin.Protocol.S2S.Services.INetworkService>(sp => sp.GetRequiredService<Hugin.Protocol.S2S.Services.NickServ>());

                services.AddSingleton<Hugin.Protocol.S2S.Services.ChanServ>(sp =>
                {
                    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
                    var serverId = sp.GetRequiredService<ServerId>();
                    var channelRepo = sp.GetRequiredService<IChannelRepository>();
                    var accountRepo = sp.GetRequiredService<IAccountRepository>();
                    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Hugin.Protocol.S2S.Services.ChanServ>>();
                    return new Hugin.Protocol.S2S.Services.ChanServ(
                        channelRepo,
                        accountRepo,
                        serverId,
                        cfg.Server.Name,
                        logger);
                });
                services.AddSingleton<Hugin.Protocol.S2S.Services.INetworkService>(sp => sp.GetRequiredService<Hugin.Protocol.S2S.Services.ChanServ>());

                services.AddSingleton<Hugin.Protocol.S2S.Services.IServicesManager, Hugin.Protocol.S2S.Services.ServicesManager>();

                // Protocol
                services.AddSingleton<CommandRegistry>();

                // Security
                services.AddSingleton(new RateLimiter(config.Security.RateLimiting));
                services.AddSingleton(new HostCloaker(config.Security.CloakSecret, config.Security.CloakSuffix));
                services.AddSingleton<PasswordHasher>();
                services.AddSingleton(sp => ConfigureTls(config));

                // SASL
                services.AddSingleton<Hugin.Security.Sasl.SaslManager>();

                // Main service
                services.AddHostedService<IrcServerService>();

                // S2S Service (only if S2S listeners or linked servers are configured)
                if (config.Network.ServerListeners.Count > 0 || config.Network.LinkedServers.Count > 0)
                {
                    services.AddHostedService<S2SService>();
                }
            });

    /// <summary>
    /// Validates security-sensitive configuration and logs warnings for default/insecure values.
    /// </summary>
    /// <param name="config">The Hugin configuration to validate.</param>
    private static void ValidateSecurityConfiguration(HuginConfiguration config)
    {
        // Check for default CloakSecret
        if (config.Security.CloakSecret == "change-this-to-a-random-secret")
        {
            Log.Warning("SECURITY: Using default CloakSecret. " +
                "Change this to a cryptographically random value in production!");
        }

        // Check for default database password
        if (!string.IsNullOrEmpty(config.Database.ConnectionString))
        {
            if (config.Database.ConnectionString.Contains("Password=hugin", StringComparison.OrdinalIgnoreCase) ||
                config.Database.ConnectionString.Contains("Password=postgres", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("SECURITY: Database connection string contains a default password. " +
                    "Change this to a strong, unique password in production!");
            }
        }

        // Check for self-signed certificate in non-development
        if (config.Security.GenerateSelfSignedCertificate)
        {
            Log.Warning("SECURITY: Using self-signed certificate. " +
                "Use a proper TLS certificate from a trusted CA in production!");
        }
    }

    private static TlsConfiguration ConfigureTls(HuginConfiguration config)
    {
        var tlsConfig = new TlsConfiguration();

        if (!string.IsNullOrEmpty(config.Security.CertificatePath))
        {
            string? password = null;
            if (!string.IsNullOrEmpty(config.Security.CertificatePassword))
            {
                var masterKey = ConfigurationEncryptor.GetMasterKeyFromEnvironment();
                if (masterKey is not null && ConfigurationEncryptor.IsEncrypted(config.Security.CertificatePassword))
                {
                    password = ConfigurationEncryptor.Decrypt(config.Security.CertificatePassword, masterKey);
                }
                else
                {
                    password = config.Security.CertificatePassword;
                }
            }

            tlsConfig.Certificate = TlsConfiguration.LoadCertificate(config.Security.CertificatePath, password);
        }
        else if (config.Security.GenerateSelfSignedCertificate)
        {
            Log.Warning("No certificate configured. Generating self-signed certificate for development.");
            tlsConfig.Certificate = TlsConfiguration.GenerateSelfSignedCertificate(config.Server.Name);
        }

        return tlsConfig;
    }
}
