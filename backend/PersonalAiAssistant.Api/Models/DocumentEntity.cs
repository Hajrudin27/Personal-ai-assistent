namespace PersonalAiAssistant.Api.Models;

public sealed class DocumentEntity
{
    public string Id { get; set; } = default!;
    public string OriginalFileName { get; set; } = default!;
    public string StoredFileName { get; set; } = default!;
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; }

    public List<ChunkEntity> Chunks { get; set; } = new();
}
