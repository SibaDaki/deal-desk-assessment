using DealDesk.Application.DTOs.Requests;
using DealDesk.Application.DTOs.Responses;
using DealDesk.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DealDesk.Api.Controllers
{
    [Route("api/auth")]
    public class AuthController : ApiControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>Registers a user with a role of "applicant" or "analyst".</summary>
        [HttpPost("register")]
        [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Register(
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RegisterUserRequest? request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.RegisterAsync(
                request ?? new RegisterUserRequest(), cancellationToken);
            return result.IsFailure
                ? FromError(result.Error!)
                : StatusCode(StatusCodes.Status201Created, result.Value);
        }

        /// <summary>Exchanges valid credentials for a Bearer token carrying the user's role.</summary>
        [HttpPost("login")]
        [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Login(
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LoginRequest? request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.LoginAsync(request ?? new LoginRequest(), cancellationToken);
            return result.IsFailure ? FromError(result.Error!) : Ok(result.Value);
        }
    }
}
