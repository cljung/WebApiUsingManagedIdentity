using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Graph.Models.ExternalConnectors;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Tokens.Experimental;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using static System.Net.WebRequestMethods;

namespace WebApiUsingManagedIdentity; 
public class Program {
    public static ConfigurationManager _configuration;
    public static ILogger<Program> _logger;
    public static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);

        _configuration = builder.Configuration;

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi( builder.Configuration.GetSection("Entra"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        string tenantId = builder.Configuration[$"Entra:TenantId"]?.ToString() ?? string.Empty;
        string clientId = builder.Configuration[$"Entra:ClientId"]?.ToString() ?? string.Empty;
        string audience = builder.Configuration[$"Entra:Audience"]?.ToString() ?? string.Empty;

        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => {
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidIssuers = new[] { $"https://sts.windows.net/{tenantId}/", $"https://sts.windows.net/{tenantId}/v2.0/" };
            options.TokenValidationParameters.ValidateAudience = true;
            options.TokenValidationParameters.ValidAudiences = new[] { audience, clientId };
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.ValidateLifetime = true;
            // this not to overwrite AddMicrosoftIdentityWebApi & EnableTokenAcquisitionToCallDownstreamApi above
            if (options.Events == null) {
                options.Events = new JwtBearerEvents();
            }
            var existingOnAuthFailed = options.Events.OnAuthenticationFailed;
            options.Events.OnAuthenticationFailed = async context => {
                if (existingOnAuthFailed != null) {
                    await existingOnAuthFailed(context);
                }
                System.Diagnostics.Debug.WriteLine($"Token validation failed: {context.Exception.Message}");
            };
        });

        builder.Services.AddTransient<IGraphClientService, GraphClientService>();

        string[] requiredPermissions = new string[] { "access_as_user", "Api.Read" };
        builder.Services.AddAuthorization(options => {
            options.AddPolicy("ScopeOrRolePolicy.Read", policy => {
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
                policy.RequireAssertion(context => { return AuthorizationPolicyHandler.EvaluatePermission(context, requiredPermissions); });
            });
        });

        builder.Services.AddControllers();

        var app = builder.Build();

        _logger = app.Services.GetRequiredService<ILogger<Program>>();

        bool graphUsesManagedIdentity = false; 
        try {
            graphUsesManagedIdentity = builder.Configuration.GetValue("Entra:Graph:AuthMode", "ClientCreds").StartsWith("ManagedIdentity");
            if ( !graphUsesManagedIdentity ) {
                GetClientSecretFromKeyVault();
            }
        } catch (Exception ex) {
            _logger.LogError( "Error getting secret from KeyVault: " + ex.Message );
        }

        if (graphUsesManagedIdentity) {
            _logger.LogInformation("Startup info:\tUsing Managed Identity"); 
        } else {
            string clientSecret = builder.Configuration[$"Entra:ClientSecret"]?.ToString() ?? string.Empty;

            _logger.LogInformation("Startup info:\n\tTenantID:\t{TenantId}\n\tclientId:\t{ClientId}\n\tAudience:\t{Audience}\n\tSecret:\t\t{Secret}...",
                tenantId, clientId, audience, clientSecret.Substring(0,3)
            );
        }

        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        app.MapGet("/api/version", () => new {
            name = Assembly.GetExecutingAssembly().GetName().Name,
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        });

        app.Run();
    }

    public static void GetClientSecretFromKeyVault() {
        bool useMI = _configuration.GetValue("Entra:KeyVault:AuthMode", "ClientCreds").StartsWith("ManagedIdentity");
        var keyVaultUri = _configuration["KeyVault:VaultUri"]?.ToString()!;
        SecretClient? secretClient;
        if ( useMI ) {
            _logger.LogInformation("Accessing KeyVault via ManagedIdentity");
            secretClient = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
        } else {
            string tenantId = _configuration[$"Entra:TenantId"]?.ToString() ?? string.Empty;
            string clientId = _configuration[$"Entra:ClientId"]?.ToString() ?? string.Empty;
            string clientSecret = _configuration[$"Entra:ClientSecret"]?.ToString() ?? string.Empty;
            _logger.LogInformation("Accessing KeyVault via Client Credentials {ClientID}", clientId );
            secretClient = new SecretClient(new Uri(keyVaultUri), new ClientSecretCredential(tenantId, clientId, clientSecret) );
        }
        string graphClientId = _configuration[$"Entra:Graph:ClientId"]?.ToString() ?? string.Empty;
        KeyVaultSecret secret = secretClient.GetSecretAsync(graphClientId).Result;
        _configuration[$"Entra:Graph:ClientSecret"] = secret.Value;
    }
} // cls
