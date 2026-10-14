using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace WebApiUsingManagedIdentity.Controllers; 
[ApiController]
//[Route("[controller]")]
public class ApiController : ControllerBase {

    private readonly ILogger<ApiController> _log;
    private readonly IConfiguration _config;
    private readonly IGraphClientService _graph;

    /// <summary>
    /// This API showcases using Managed Identity to call Graph API as downstream.
    /// The caller needs to have an access token with scopes/roles to access this API and as
    /// you can only declare those on an app (and MI is an SP), we have to have two more app regs
    /// 1. API-BackEnd that define the scope "access_as_user" and role "Api.Read"
    /// 2. API-FrontEnd that adds #1 as permissions so they can be acquired in the access token
    /// </summary>
    /// <param name="config"></param>
    /// <param name="secretClient"></param>
    /// <param name="logger"></param>
    /// <param name="graph"></param>

    public ApiController(IConfiguration config, ILogger<ApiController> logger, IGraphClientService graph) {
        _log = logger;
        _config = config;
        _graph = graph;
    }

    private async Task<string> GetManagedIdentityObjectIdAsync() {
        var credential = new DefaultAzureCredential();
        var tokenContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
        var tokenResult = await credential.GetTokenAsync(tokenContext);
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenResult.Token);
        var objectIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "oid")
                            ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "sub");
        if (objectIdClaim == null) {
            throw new InvalidOperationException("Could not find the Object ID claim in the token.");
        }
        return objectIdClaim.Value; // This is your Managed Identity's Object ID
    }
    /// <summary>
    /// Echo out what identity configuration we are currently running with
    /// </summary>
    /// <returns></returns>
    [HttpGet("api/identity")]
    public async Task<IActionResult> GetIdentityConfig() {
        try {
            string authMode = _config["Entra:Graph:AuthMode"]?.ToString() ?? string.Empty;
            string audience = _config["Entra:Audience"]?.ToString() ?? string.Empty;
            string graphClientId = _config["Entra:Graph:ClientId"]?.ToString() ?? string.Empty;
            string? graphClientSecret = null;
            string? managedIdentityId = null;
            if ( authMode.StartsWith("ManagedIdentity", StringComparison.OrdinalIgnoreCase)) {
                managedIdentityId = await GetManagedIdentityObjectIdAsync();
            } else {
                graphClientSecret = _config["Entra:Graph:ClientSecret"]?.ToString() ?? string.Empty;
                if ( !string.IsNullOrWhiteSpace(graphClientSecret)) {
                    graphClientSecret = graphClientSecret.Substring(0, 4) + "...";
                }
            }
            return Ok(new { authMode = authMode, audience = audience
                , graphClientId = graphClientId, graphClientSecret = graphClientSecret
                , managedIdentityId = managedIdentityId }
            );
        } catch (Exception ex) {
            _log.LogError(ex, "Error getting config. " + ex.Message);
            return StatusCode(500, "Could not retrieve config.");
        }
    }

    /// <summary>
    /// API Endpoint that can be called with both a user and an app token. It returns the Graph organization
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("api/organization")]
    [Authorize(Policy = "ScopeOrRolePolicy.Read")]
    [Produces("application/json")]
    public async Task<IActionResult> GetOrganization( CancellationToken cancellationToken) {
        try {

            var scopeClaim = HttpContext.User?.FindFirst(c => c.Type == ClaimConstants.Scope || c.Type == ClaimConstants.Scp);
            string tokenType = scopeClaim == null ? "app token" : "user token";
            string? username = HttpContext.User?.Identity?.Name;
            var oid = HttpContext.User?.Claims.FirstOrDefault(c => c.Type == "oid")
                ?? HttpContext.User?.Claims.FirstOrDefault(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier");

            List<Microsoft.Graph.Models.Organization>? orgs = await _graph.GetOrganizationAsync( cancellationToken);

            string? authMode = _graph.AuthMode();

            if ( null != orgs ) {
                Microsoft.Graph.Models.Organization org = orgs[0];
                List<string> domains = new List<string>();
                foreach( var verifiedDomain in org.VerifiedDomains!) {
                    if ( true == verifiedDomain.IsDefault! ) 
                         domains.Add(verifiedDomain.Name! + " (default)" );
                    else domains.Add(verifiedDomain.Name!);
                }
                var resp = new {
                    caller = new {
                        tokenType = tokenType,
                        oid = oid?.Value,
                        name = username,
                        authMode = authMode
                    },
                    organization = new {
                        Id = org.Id,
                        DisplayName = org.DisplayName,
                        TenantType = org.TenantType,
                        Country = org.CountryLetterCode,
                        VerifiedDomains = domains
                    }
                };
                return Content(JsonSerializer.Serialize(resp), "application/json");
            } else {
                return NotFound();
            }
        } catch (Exception ex) {
            _log.LogError(ex, "Error querying Graph. " + ex.Message);
            return StatusCode(500, "Could not get organization.");
        }

    }
} // cls
