using Hugin.Core.Interfaces;
using Hugin.Core.Metrics;
using Hugin.Core.ValueObjects;
using Hugin.Network;
using Hugin.Network.S2S;
using Hugin.Persistence;
using Hugin.Persistence.Repositories;
using Hugin.Protocol.Commands;
using Hugin.Protocol.S2S;
using Hugin.Security;
using Hugin.Server.Api;
using Hugin.Server.Configuration;
using Hugin.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;

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

            // Load configuration early to check admin panel settings
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .AddEnvironmentVariables("HUGIN_");
            
            var earlyConfig = configBuilder.Build();
            var huginConfig = earlyConfig.GetSection("Hugin").Get<HuginConfiguration>() ?? new();

            if (huginConfig.Admin.Enabled)
            {
                // Run with Web API support
                await RunWithWebApiAsync(args, huginConfig);
            }
            else
            {
                // Run as background service only
                await RunAsServiceAsync(args);
            }

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
    /// Runs the server with Web API support for the admin panel.
    /// </summary>
    private static async Task RunWithWebApiAsync(string[] args, HuginConfiguration huginConfig)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        // Configure Kestrel for admin panel
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(huginConfig.Admin.Port, listenOptions =>
            {
                var tlsConfig = ConfigureTls(huginConfig);
                if (tlsConfig.Certificate != null)
                {
                    listenOptions.UseHttps(tlsConfig.Certificate);
                }
                else
                {
                    // Generate self-signed for admin panel
                    var cert = TlsConfiguration.GenerateSelfSignedCertificate(huginConfig.Server.Name);
                    listenOptions.UseHttps(cert);
                }
            });
        });

        // Add Serilog
        builder.Host.UseSerilog((context, services, configuration) =>
        {
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
        });

        // Add Web API services
        builder.Services.AddHuginWebApi(builder.Configuration);

        // Add all IRC server services
        ConfigureIrcServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        // Run migrations
        await RunMigrationsAsync(app.Services, huginConfig);

        // Configure Web API middleware
        app.UseHuginWebApi();

        Log.Information("Admin panel listening on https://localhost:{Port}", huginConfig.Admin.Port);

        await app.RunAsync();
    }

    /// <summary>
    /// Runs the server as a background service without Web API.
    /// </summary>
    private static async Task RunAsServiceAsync(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        using (var scope = host.Services.CreateScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
            ValidateSecurityConfiguration(config);

            if (config.Database.RunMigrationsOnStartup)
            {
                var db = scope.ServiceProvider.GetRequiredService<HuginDbContext>();
                await db.Database.MigrateAsync();
                Log.Information("Database migrations applied");
            }
        }

        await host.RunAsync();
    }

    /// <summary>
    /// Runs database migrations.
    /// </summary>
    private static async Task RunMigrationsAsync(IServiceProvider services, HuginConfiguration config)
    {
        ValidateSecurityConfiguration(config);

        if (config.Database.RunMigrationsOnStartup)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HuginDbContext>();
            await db.Database.MigrateAsync();
            Log.Information("Database migrations applied");
        }
    }

    /// <summary>
    /// Configures IRC server services.
    /// </summary>
    private static void ConfigureIrcServices(IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.Configure<HuginConfiguration>(configuration.GetSection("Hugin"));
        var config = configuration.GetSection("Hugin").Get<HuginConfiguration>() ?? new();

        // Database
        services.AddDbContext<HuginDbContext>(options =>
        {
            options.UseNpgsql(config.Database.ConnectionString);
        });

        // Repositories - In-memory for connected entities
        services.AddSingleton<IUserRepository, InMemoryUserRepository>();
        services.AddSingleton<IChannelRepository, InMemoryChannelRepository>();

        // Metrics
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<IrcMetrics>();

        // Repositories - Persistent
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IServerLinkRepository, ServerLinkRepository>();
        services.AddScoped<IRegisteredChannelRepository, RegisteredChannelRepository>();
        services.AddScoped<IMemoRepository, MemoRepository>();

        // Ban repository - persisted with in-memory cache for fast lookups
        services.AddSingleton<IServerBanRepository, PersistedServerBanRepository>();

        // Operator configuration
        services.AddSingleton<IOperatorConfigService, OperatorConfigService>();

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
        services.AddSingleton<S2SHandshakeManager>(sp =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
            var serverId = sp.GetRequiredService<ServerId>();
            var connectionManager = sp.GetRequiredService<S2SConnectionManager>();
            var logger = sp.GetRequiredService<ILogger<S2SHandshakeManager>>();
            
            return new S2SHandshakeManager(
                serverId,
                cfg.Server.Description,
                (connectionId, message, ct) => connectionManager.SendToConnectionAsync(connectionId, message.ToString(), ct),
                logger);
        });
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
        services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.SjoinHandler>();
        services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SPrivmsgHandler>();
        services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2SNoticeHandler>();
        services.AddSingleton<IS2SCommandHandler, Hugin.Protocol.S2S.Commands.S2STopicHandler>();

        // Burst Manager
        services.AddSingleton<IBurstManager, BurstManager>();

        // Network Services (ChanServ, NickServ, etc.)
        services.AddSingleton<Hugin.Protocol.S2S.Services.ChanServ>(sp =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
            var serverId = sp.GetRequiredService<ServerId>();
            var channelRepo = sp.GetRequiredService<IChannelRepository>();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Hugin.Protocol.S2S.Services.ChanServ>>();
            
            // Create factory functions that create scoped repositories
            Func<IAccountRepository> accountRepoFactory = () =>
            {
                var scope = scopeFactory.CreateScope();
                return scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            };
            Func<IRegisteredChannelRepository> registeredChannelRepoFactory = () =>
            {
                var scope = scopeFactory.CreateScope();
                return scope.ServiceProvider.GetRequiredService<IRegisteredChannelRepository>();
            };
            
            return new Hugin.Protocol.S2S.Services.ChanServ(
                channelRepo,
                accountRepoFactory,
                registeredChannelRepoFactory,
                serverId,
                cfg.Server.Name,
                logger);
        });
        services.AddSingleton<Hugin.Protocol.S2S.Services.INetworkService>(sp => sp.GetRequiredService<Hugin.Protocol.S2S.Services.ChanServ>());

        // MemoServ - offline messaging
        services.AddSingleton<Hugin.Protocol.S2S.Services.MemoServ>(sp =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HuginConfiguration>>().Value;
            var serverId = sp.GetRequiredService<ServerId>();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Hugin.Protocol.S2S.Services.MemoServ>>();
            
            Func<IMemoRepository> memoRepoFactory = () =>
            {
                var scope = scopeFactory.CreateScope();
                return scope.ServiceProvider.GetRequiredService<IMemoRepository>();
            };
            Func<IAccountRepository> accountRepoFactory = () =>
            {
                var scope = scopeFactory.CreateScope();
                return scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            };
            
            return new Hugin.Protocol.S2S.Services.MemoServ(
                memoRepoFactory,
                accountRepoFactory,
                serverId,
                cfg.Server.Name,
                logger);
        });
        services.AddSingleton<Hugin.Protocol.S2S.Services.INetworkService>(sp => sp.GetRequiredService<Hugin.Protocol.S2S.Services.MemoServ>());

        services.AddSingleton<Hugin.Protocol.S2S.Services.IServicesManager, Hugin.Protocol.S2S.Services.ServicesManager>();

        // Protocol
        services.AddSingleton<CommandRegistry>(sp => new CommandRegistry(sp));

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
    }

    /// <summary>
    /// Creates and configures the host builder for the IRC server (without Web API).
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
                ConfigureIrcServices(services, context.Configuration);
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
        var tlsConfig = new TlsConfiguration
        {
            AllowTls12Fallback = config.Security.AllowTls12Fallback
        };

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
