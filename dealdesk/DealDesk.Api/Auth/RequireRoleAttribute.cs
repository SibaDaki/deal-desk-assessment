using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DealDesk.Api.Auth
{
    /// <summary>
    /// Rejects an action with 403 when the X-Actor-Role header is missing or not
    /// the required role: applicants create deals and upload documents, analysts
    /// run the underwriting actions. Runs as an authorization filter so the role
    /// gate fires before any state-machine or validation logic.
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
            var role = context.HttpContext.Request.Headers[ActorRoleHeader.Name].ToString();
            if (string.Equals(role.Trim(), _role, StringComparison.OrdinalIgnoreCase))
                return;

            context.Result = new ObjectResult(new
            {
                error = new
                {
                    code = "forbidden",
                    message = $"This action requires the '{_role}' role via the {ActorRoleHeader.Name} header."
                }
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
