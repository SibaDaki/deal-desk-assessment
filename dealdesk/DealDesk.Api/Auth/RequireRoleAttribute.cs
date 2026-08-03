using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DealDesk.Api.Auth
{
    /// <summary>
    /// Gates an action to one role: applicants create deals and upload documents,
    /// analysts run the underwriting actions. The role comes from the caller's
    /// authenticated JWT role claim; for compatibility with the assessment
    /// contract, an unauthenticated caller may instead assert a role via the
    /// X-Actor-Role header unless Auth:AllowActorRoleHeader is disabled.
    /// Runs as an authorization filter so the gate fires before any
    /// state-machine or validation logic.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RequireRoleAttribute : Attribute, IAuthorizationFilter
    {
        private readonly string _role;

        public RequireRoleAttribute(string role)
        {
            _role = role;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var http = context.HttpContext;

            // An authenticated caller's role comes from the token, never the header.
            if (http.User.Identity?.IsAuthenticated == true)
            {
                if (http.User.IsInRole(_role))
                    return;

                context.Result = Error(StatusCodes.Status403Forbidden, "forbidden",
                    $"This action requires the '{_role}' role.");
                return;
            }

            // A presented-but-invalid token is an authentication failure, not a
            // role mismatch; never fall back to the header in that case.
            if (http.Request.Headers.ContainsKey("Authorization"))
            {
                context.Result = Error(StatusCodes.Status401Unauthorized, "unauthorized",
                    "The Bearer token is missing, expired or invalid.");
                return;
            }

            var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
            if (configuration.GetValue("Auth:AllowActorRoleHeader", true))
            {
                var role = http.Request.Headers[ActorRoleHeader.Name].ToString();
                if (string.Equals(role.Trim(), _role, StringComparison.OrdinalIgnoreCase))
                    return;

                context.Result = Error(StatusCodes.Status403Forbidden, "forbidden",
                    $"This action requires the '{_role}' role via the {ActorRoleHeader.Name} header.");
                return;
            }

            context.Result = Error(StatusCodes.Status401Unauthorized, "unauthorized",
                "Authentication required: obtain a Bearer token from POST /api/auth/login.");
        }

        private static ObjectResult Error(int statusCode, string code, string message) =>
            new(new { error = new { code, message } }) { StatusCode = statusCode };
    }
}
