namespace DealDesk.Application.DTOs.Requests
{
    public sealed class AttachDocumentRequest
    {
        public string? DocType { get; init; }
        public string? Filename { get; init; }
    }
}
