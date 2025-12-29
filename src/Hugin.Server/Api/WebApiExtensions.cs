// Hugin IRC Server - Web API Configuration
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using System.Text;
using Hugin.Server.Api.Auth;
using Hugin.Server.Api.Controllers;
using Hugin.Server.Api.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Hugin.Server.Api;

/// <summary>
/// Extension methods for configuring the Web API.
/// </summary>
public static class WebApiExtensions
{
    /// <summary>
    /// Adds Web API services to the service collection.
    /// </summary>
    public static IServiceCollection AddHuginWebApi(this IServiceCollection services, IConfiguration configuration)
    {
        // JWT configuration
        var jwtConfig = new JwtConfiguration();
        configuration.GetSection("Hugin:Admin:Jwt").Bind(jwtConfig);

        // Generate secret key if not configured
        if (string.IsNullOrEmpty(jwtConfig.SecretKey))
        {
            jwtConfig.SecretKey = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        }

        services.AddSingleton(jwtConfig);
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IAdminUserService, AdminUserService>();
        services.AddSingleton<ISetupService, SetupService>();
        services.AddSingleton<IServerStatusService, ServerStatusService>();

        // Add SignalR for real-time communication
        services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });

        // Register SignalR services
        services.AddSingleton<SignalRSerilogSink>();
        services.AddSingleton<IAdminHubService, AdminHubService>();
        services.AddHostedService<StatsBackgroundService>();

        // Add controllers
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });

        // Add authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtConfig.Issuer,
                    ValidAudience = jwtConfig.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.SecretKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Support token in query string for WebSocket
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        // Add Swagger/OpenAPI
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Hugin IRC Server Admin API",
                Version = "v1",
                Description = "REST API for managing the Hugin IRC Server",
                Contact = new OpenApiContact
                {
                    Name = "Hugin Project",
                    Url = new Uri("https://github.com/brudvik/hugin")
                },
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            // Add JWT authentication to Swagger
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Include XML comments
            var xmlFile = $"{typeof(WebApiExtensions).Assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        // Add CORS with SignalR support
        services.AddCors(options =>
        {
            options.AddPolicy("AdminPanel", builder =>
            {
                builder
                    .SetIsOriginAllowed(_ => true) // For development - restrict in production
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials(); // Required for SignalR
            });
        });

        return services;
    }

    /// <summary>
    /// Configures the Web API middleware pipeline.
    /// </summary>
    public static WebApplication UseHuginWebApi(this WebApplication app)
    {
        // Enable CORS with SignalR support
        app.UseCors("AdminPanel");

        // Enable Swagger in development
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Hugin Admin API v1");
                options.RoutePrefix = "api/docs";
            });
        }

        // Serve static files (Angular app)
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        // Map SignalR hub
        app.MapHub<AdminHub>("/api/hubs/admin");

        // SPA fallback for client-side routing
        app.MapFallbackToFile("index.html");

        return app;
    }
}
