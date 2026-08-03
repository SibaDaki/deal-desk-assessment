using DealDesk.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace DealDesk.Api.Controllers
{
    /// <summary>
    /// Shared transport concerns: translates typed domain errors into the HTTP
    /// contract (Validation → 422, NotFound → 404, Conflict → 409,
    /// Forbidden → 403, Unauthorized → 401) with a consistent error body.
    /// </summary>
    [ApiController]
    [Produces("application/json")]
    public abstract class ApiControllerBase : ControllerBase
    {
        protected ObjectResult FromError(Error error)
        {
            var body = new Dictionary<string, object?>
            {
                ["code"] = error.Code,
                ["message"] = error.Message
            };

            if (error.Details is not null)
            {
                foreach (var (key, value) in error.Details)
                    body[key] = value;
            }

            var statusCode = error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status500InternalServerError
            };

            return StatusCode(statusCode, new Dictionary<string, object?> { ["error"] = body });
        }
    }
}
