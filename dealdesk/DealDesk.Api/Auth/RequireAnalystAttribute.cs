using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DealDesk.Api.Auth
{
    /// <summary>
    /// Rejects analyst-only actions with 403 when the X-Actor-Role header is
    /// missing or not "analyst". Runs as an authorization filter so the role gate
    /// fires before any state-machine or validation logic.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RequireAnalystAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var role = context.HttpContext.Request.Headers[ActorRoleHeader.Name].ToString();
            if (string.Equals(role.Trim(), ActorRoleHeader.Analyst, StringComparison.OrdinalIgnoreCase))
                return;

            context.Result = new ObjectResult(new
            {
                error = new
                {
                    code = "forbidden",
                    message = $"This action requires the '{ActorRoleHeader.Analyst}' role via the {ActorRoleHeader.Name} header."
                }
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
