using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using System.Net.Http.Headers;

namespace WebApiUsingManagedIdentity;

public class OboFlowTokenProvider : IAccessTokenProvider {
    private readonly string _token;
    public OboFlowTokenProvider(string token) => _token = token;

    public Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = default, CancellationToken cancellationToken = default) {
        return Task.FromResult(_token);
    }
    public AllowedHostsValidator AllowedHostsValidator { get; } = new AllowedHostsValidator();
}

public interface IGraphClientService {
    public Task<List<Microsoft.Graph.Models.Organization>?> GetOrganizationAsync(CancellationToken cancellationToken = default);
    public string AuthMode();
}

public class GraphClientService : IGraphClientService {
    private readonly ITokenAcquisition? _tokenAcquisition;
    private readonly TokenCredential? _tokenCredential;
    private readonly ILogger<GraphClientService> _log;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private BaseBearerTokenAuthenticationProvider? _oboFlowTokenProvider;
    private const string _scope = "https://graph.microsoft.com/.default";
    private readonly string[] _scopes = new[] { _scope };
    private GraphServiceClient? _graphClient;
    private string? _authMode;

    public GraphClientService( IConfiguration configuration, ILogger<GraphClientService> log
                            , IHttpContextAccessor httpContextAccessor, ITokenAcquisition? tokenAcquisition = null) {
        _log = log;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _tokenAcquisition = tokenAcquisition;
        var authMode = configuration["Entra:Graph:AuthMode"];
        var clientId = configuration["Entra:Graph:ClientId"];
        if (null != authMode && authMode!.StartsWith("ManagedIdentity") && !IsUserToken() ) {
            // system-assigned vs user-assigned managed identity
            if ( authMode.Contains("User", StringComparison.OrdinalIgnoreCase)) {
                _tokenCredential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
                _authMode = $"ManagedIdentity:User:{clientId}";
            } else {
                _tokenCredential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
                _authMode = $"ManagedIdentity";
            }
        } else {
            _tokenAcquisition = tokenAcquisition ?? throw new InvalidOperationException( "ITokenAcquisition is required.");
            var options = new ClientSecretCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzurePublicCloud };
            _tokenCredential = new ClientSecretCredential(configuration["Entra:TenantId"], clientId, configuration["Entra:Graph:ClientSecret"], options);
            _authMode = $"ClientCreds:{clientId}";
        }
        _graphClient = new GraphServiceClient(_tokenCredential, _scopes);
    }
    public string AuthMode() {
        return _authMode!;
    }
    private bool IsUserToken() {
        if (_httpContextAccessor.HttpContext!.User == null) return false;
        var scopeClaim = _httpContextAccessor.HttpContext.User.FindFirst(c => c.Type == ClaimConstants.Scope || c.Type == ClaimConstants.Scp);
        if (scopeClaim == null) return false;
        return true;
    }
    private async Task OboExchangeTokenAsync(string[] graphScopes) {
        if (!IsUserToken()) return;
        try {
            string accessToken = await _tokenAcquisition!.GetAccessTokenForUserAsync(graphScopes);
            // if the next step throws an AADSTS65001 exception, be sure you haven't misspelled the scope (like Users.Read.All instead of User.Read.All)
            _oboFlowTokenProvider = new BaseBearerTokenAuthenticationProvider(new OboFlowTokenProvider(accessToken));
            _graphClient = new GraphServiceClient(_oboFlowTokenProvider);
        } catch (MicrosoftIdentityWebChallengeUserException ex) {
            _log.LogTrace("OboExchangeToken(): {MSG}", ex.Message);
            throw;
        }
    }

    public async Task<List<Microsoft.Graph.Models.Organization>?> GetOrganizationAsync( CancellationToken cancellationToken = default) {
        await OboExchangeTokenAsync(new[] { "Organization.Read.All" });
        var response = await _graphClient!.Organization.GetAsync(cancellationToken: cancellationToken);
        return response?.Value!;
    }
}
