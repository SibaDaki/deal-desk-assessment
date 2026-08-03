namespace DealDesk.Domain.Common
{
    public enum ErrorType
    {
        Validation = 0,
        NotFound = 1,
        Conflict = 2,
        Forbidden = 3,
        Unauthorized = 4
    }

    /// <summary>
    /// A typed domain/application error. <see cref="Type"/> drives the HTTP status
    /// at the transport layer (Validation → 422, NotFound → 404, Conflict → 409,
    /// Forbidden → 403, Unauthorized → 401); <see cref="Details"/> carries
    /// structured extras such as the list of missing document types.
    /// </summary>
    public sealed record Error(
        ErrorType Type,
        string Code,
        string Message,
        IReadOnlyDictionary<string, object?>? Details = null)
    {
        public static Error Validation(string message, IReadOnlyDictionary<string, object?>? details = null) =>
            new(ErrorType.Validation, "validation_failed", message, details);

        public static Error Validation(string message, IReadOnlyList<string> errors) =>
            new(ErrorType.Validation, "validation_failed", message,
                new Dictionary<string, object?> { ["errors"] = errors });

        public static Error MissingDocuments(IReadOnlyList<string> missingDocumentTypes) =>
            new(ErrorType.Validation, "missing_documents",
                "Required documents are missing.",
                new Dictionary<string, object?> { ["missing_documents"] = missingDocumentTypes });

        public static Error NotFound(string message = "Deal not found.") =>
            new(ErrorType.NotFound, "not_found", message);

        public static Error Conflict(string message) =>
            new(ErrorType.Conflict, "illegal_transition", message);

        public static Error Forbidden(string message = "This action requires the 'analyst' role.") =>
            new(ErrorType.Forbidden, "forbidden", message);

        public static Error Unauthorized(string message = "Invalid username or password.") =>
            new(ErrorType.Unauthorized, "unauthorized", message);
    }
}
