using DealDesk.Application.DTOs.Requests;
using DealDesk.Application.DTOs.Responses;
using DealDesk.Domain.Common;

namespace DealDesk.Application.Services
{
    public interface IDealService
    {
        Task<Result<CreateDealOutcome>> CreateAsync(
            CreateDealRequest request, string actorRole, string? idempotencyKey,
            CancellationToken cancellationToken = default);

        Task<Result<PagedDeals>> ListAsync(
            string? status, string? fundingType, int? page, int? pageSize,
            CancellationToken cancellationToken = default);

        Task<Result<DealResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<Result<DealResponse>> AttachDocumentAsync(
            Guid id, AttachDocumentRequest request, CancellationToken cancellationToken = default);

        Task<Result<DealResponse>> ReviewAsync(
            Guid id, string actorRole, CancellationToken cancellationToken = default);

        Task<Result<DealResponse>> ApproveAsync(
            Guid id, ApproveDealRequest request, string actorRole, CancellationToken cancellationToken = default);

        Task<Result<DealResponse>> DeclineAsync(
            Guid id, DeclineDealRequest request, string actorRole, CancellationToken cancellationToken = default);

        Task<Result<DealResponse>> FundAsync(
            Guid id, FundDealRequest request, string actorRole, CancellationToken cancellationToken = default);

        Task<Result<RepaymentResponse>> RecordRepaymentAsync(
            Guid id, RecordRepaymentRequest request, string actorRole, CancellationToken cancellationToken = default);

        Task<Result<StatementResponse>> GetStatementAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
