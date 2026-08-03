using DealDesk.Api.Auth;
using DealDesk.Application.DTOs.Requests;
using DealDesk.Application.DTOs.Responses;
using DealDesk.Application.Services;
using DealDesk.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DealDesk.Api.Controllers
{
    [ApiController]
    [Route("api/deals")]
    [Produces("application/json")]
    public class DealsController : ControllerBase
    {
        private const string IdempotencyKeyHeader = "Idempotency-Key";

        private readonly IDealService _dealService;

        public DealsController(IDealService dealService)
        {
            _dealService = dealService;
        }

        /// <summary>Creates a deal in status SUBMITTED and assigns a unique SF-{year}-{seq} reference.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(DealResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Create(
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateDealRequest? request,
            CancellationToken cancellationToken)
        {
            var idempotencyKey = Request.Headers[IdempotencyKeyHeader].ToString();
            var result = await _dealService.CreateAsync(
                request ?? new CreateDealRequest(),
                ActorRoleHeader.Resolve(Request),
                string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
                cancellationToken);

            if (result.IsFailure)
                return FromError(result.Error!);

            if (result.Value.Replayed)
                Response.Headers["Idempotency-Replayed"] = "true";

            return CreatedAtAction(nameof(GetById), new { id = result.Value.Deal.Id }, result.Value.Deal);
        }

        /// <summary>Lists deals with optional ?status= and ?funding_type= filters and optional pagination.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<DealResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> List(
            [FromQuery] string? status,
            [FromQuery(Name = "funding_type")] string? fundingType,
            [FromQuery] int? page,
            [FromQuery(Name = "page_size")] int? pageSize,
            CancellationToken cancellationToken)
        {
            var result = await _dealService.ListAsync(status, fundingType, page, pageSize, cancellationToken);
            if (result.IsFailure)
                return FromError(result.Error!);

            Response.Headers["X-Total-Count"] = result.Value.TotalCount.ToString();
            if (result.Value.Page is not null)
            {
                Response.Headers["X-Page"] = result.Value.Page.Value.ToString();
                Response.Headers["X-Page-Size"] = result.Value.PageSize!.Value.ToString();
            }

            return Ok(result.Value.Items);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(DealResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _dealService.GetByIdAsync(id, cancellationToken);
            return result.IsFailure ? FromError(result.Error!) : Ok(result.Value);
        }

        /// <summary>Attaches document metadata ({doc_type, filename}) to a deal.</summary>
        [HttpPost("{id:guid}/documents")]
        [ProducesResponseType(typeof(DealResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> AttachDocument(
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AttachDocumentRequest? request,
            CancellationToken cancellationToken)
        {
            var result = await _dealService.AttachDocumentAsync(
                id, request ?? new AttachDocumentRequest(), cancellationToken);
            return result.IsFailure
                ? FromError(result.Error!)
                : CreatedAtAction(nameof(GetById), new { id }, result.Value);
        }

        /// <summary>Analyst-only: SUBMITTED → UNDER_REVIEW.</summary>
        [HttpPost("{id:guid}/review")]
        [RequireAnalyst]
        [ProducesResponseType(typeof(DealResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
        {
            var result = await _dealService.ReviewAsync(id, ActorRoleHeader.Resolve(Request), cancellationToken);
            return result.IsFailure ? FromError(result.Error!) : Ok(result.Value);
        }

        /// <summary>Analyst-only: UNDER_REVIEW → APPROVED, gated on required documents and valid terms.</summary>
        [HttpPost("{id:guid}/approve")]
        [RequireAnalyst]
        [ProducesResponseType(typeof(DealResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Approve(
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveDealRequest? request,
            CancellationToken cancellationToken)
        {
            var result = await _dealService.ApproveAsync(
                id, request ?? new ApproveDealRequest(), ActorRoleHeader.Resolve(Request), cancellationToken);
            return result.IsFailure ? FromError(result.Error!) : Ok(result.Value);
        }

        /// <summary>Analyst-only: SUBMITTED or UNDER_REVIEW → DECLINED.</summary>
        [HttpPost("{id:guid}/decline")]
        [RequireAnalyst]
        [ProducesResponseType(typeof(DealResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Decline(
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeclineDealRequest? request,
            CancellationToken cancellationToken)
        {
            var result = await _dealService.DeclineAsync(
                id, request ?? new DeclineDealRequest(), ActorRoleHeader.Resolve(Request), cancellationToken);
            return result.IsFailure ? FromError(result.Error!) : Ok(result.Value);
        }

        /// <summary>Analyst-only: APPROVED → FUNDED; computes the advance from the approved terms.</summary>
        [HttpPost("{id:guid}/fund")]
        [RequireAnalyst]
        [ProducesResponseType(typeof(DealResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Fund(
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] FundDealRequest? request,
            CancellationToken cancellationToken)
        {
            var result = await _dealService.FundAsync(
                id, request ?? new FundDealRequest(), ActorRoleHeader.Resolve(Request), cancellationToken);
            return result.IsFailure ? FromError(result.Error!) : Ok(result.Value);
        }

        /// <summary>Analyst-only: records a repayment against a FUNDED deal; settles it at zero balance.</summary>
        [HttpPost("{id:guid}/repayments")]
        [RequireAnalyst]
        [ProducesResponseType(typeof(RepaymentResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> RecordRepayment(
            Guid id,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RecordRepaymentRequest? request,
            CancellationToken cancellationToken)
        {
            var result = await _dealService.RecordRepaymentAsync(
                id, request ?? new RecordRepaymentRequest(), ActorRoleHeader.Resolve(Request), cancellationToken);
            return result.IsFailure
                ? FromError(result.Error!)
                : CreatedAtAction(nameof(GetStatement), new { id }, result.Value);
        }

        [HttpGet("{id:guid}/statement")]
        [ProducesResponseType(typeof(StatementResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStatement(Guid id, CancellationToken cancellationToken)
        {
            var result = await _dealService.GetStatementAsync(id, cancellationToken);
            return result.IsFailure ? FromError(result.Error!) : Ok(result.Value);
        }

        private ObjectResult FromError(Error error)
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
                _ => StatusCodes.Status500InternalServerError
            };

            return StatusCode(statusCode, new Dictionary<string, object?> { ["error"] = body });
        }
    }
}
