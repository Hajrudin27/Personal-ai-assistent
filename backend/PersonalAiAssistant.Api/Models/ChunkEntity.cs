using Pgvector;

namespace PersonalAiAssistant.Api.Models;

public sealed class ChunkEntity
{
    public Guid Id { get; set; }
    public string DocumentId { get; set; } = default!;
    public int Index { get; set; }
    public string Text { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }

    public Vector? Embedding { get; set; }

    public DocumentEntity Document { get; set; } = default!;
}
