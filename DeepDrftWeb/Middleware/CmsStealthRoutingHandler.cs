using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace DeepDrftWeb.Middleware;

/// <summary>
/// Returns 404 for any /cms/* request that fails authorization.
/// This prevents the CMS from acknowledging its own existence to unauthorized callers
/// (a redirect to /account/login would reveal that the route exists).
/// CMS-PLAN §3.4 stealth-routing constraint.
/// </summary>
public class CmsStealthRoutingHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // For /cms/* routes (including an exact /cms hit), map any authorization
        // failure to 404 regardless of cause (unauthenticated, wrong role, or any
        // future policy failure). This prevents the CMS from acknowledging its
        // own existence to callers outside the Admin hierarchy.
        if (context.Request.Path.StartsWithSegments("/cms") && !authorizeResult.Succeeded)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
