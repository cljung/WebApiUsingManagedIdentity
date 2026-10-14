using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace WebApiUsingManagedIdentity;

public class AuthorizationPolicyHandler {
    public static bool EvaluatePermission(AuthorizationHandlerContext context, string[] requiredPermissions) {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User == null) return false;
        if (null != context.User.Identity && !context.User.Identity.IsAuthenticated) return false;
        var rolesClaims = context.User.FindAll(c => string.Equals(c.Type, ClaimConstants.Roles, StringComparison.OrdinalIgnoreCase) || c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "roles");
        string[]? scopes = null;
        if (null != rolesClaims) {
            scopes = rolesClaims.Select(c => c.Value).ToList().ToArray();
        }
        if (null == rolesClaims || null == scopes || scopes.Length == 0) {
            var scopeClaim = context.User.FindFirst(c => c.Type == ClaimConstants.Scope || c.Type == ClaimConstants.Scp);
            if (scopeClaim != null) {
                scopes = scopeClaim.Value.Split(' ');
            }
        }
        if (scopes != null) {
            var requiredSet = new HashSet<string>(requiredPermissions, StringComparer.OrdinalIgnoreCase);
            return scopes.Any(s => requiredSet.Contains(s));
        }
        return false;
    }
}