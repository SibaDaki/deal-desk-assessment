using DealDesk.Application.DTOs.Requests;
using DealDesk.Application.DTOs.Responses;
using DealDesk.Domain.Common;

namespace DealDesk.Application.Services
{
    public interface IAuthService
    {
        Task<Result<UserResponse>> RegisterAsync(
            RegisterUserRequest request, CancellationToken cancellationToken = default);

        Task<Result<TokenResponse>> LoginAsync(
            LoginRequest request, CancellationToken cancellationToken = default);
    }
}
