using DealDesk.Domain.Enums;

namespace DealDesk.Domain.Entities
{
    /// <summary>Document metadata attached to a deal (no real file storage).</summary>
    public sealed record DealDocument(DocumentType DocType, string Filename);
}
