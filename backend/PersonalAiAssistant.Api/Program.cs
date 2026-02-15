using System.Text.Json;
using UglyToad.PdfPig;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPost("/api/chat", (ChatRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest("Message is required.");

    return Results.Ok(new { reply = $"You said: {request.Message}" });
});

app.MapPost("/api/documents", async (IFormFile file) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest("File is required.");

    var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "Uploads");
    Directory.CreateDirectory(uploadsDir);

    var originalFileName = Path.GetFileName(file.FileName);
    var docId = Guid.NewGuid().ToString("N");
    var ext = Path.GetExtension(originalFileName);
    var storedName = $"{docId}{ext}";
    var storedPath = Path.Combine(uploadsDir, storedName);

    await using (var stream = File.Create(storedPath))
    {
        await file.CopyToAsync(stream);
    }

    var meta = new DocumentMeta(
        Id: docId,
        OriginalFileName: originalFileName,
        StoredFileName: storedName,
        SizeBytes: file.Length,
        UploadedAtUtc: DateTime.UtcNow
    );

    var metaPath = Path.Combine(uploadsDir, $"{docId}.json");
    await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta));

    return Results.Ok(meta);
})
.Accepts<IFormFile>("multipart/form-data")
.DisableAntiforgery();

app.MapGet("/api/documents", async () =>
{
    var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "Uploads");

    if (!Directory.Exists(uploadsDir))
        return Results.Ok(Array.Empty<DocumentMeta>());

    var metaFiles = Directory.GetFiles(uploadsDir, "*.json");
    var docs = new List<DocumentMeta>();

    foreach (var path in metaFiles)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var meta = JsonSerializer.Deserialize<DocumentMeta>(json);
            if (meta != null) docs.Add(meta);
        }
        catch
        {
        }
    }

    docs.Sort((a, b) => b.UploadedAtUtc.CompareTo(a.UploadedAtUtc));
    return Results.Ok(docs);
});

app.MapGet("/api/documents/{id}/download", (string id) =>
{
    var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "Uploads");
    if (!Directory.Exists(uploadsDir))
        return Results.NotFound("Uploads folder not found.");

    var metaPath = Path.Combine(uploadsDir, $"{id}.json");
    if (!File.Exists(metaPath))
        return Results.NotFound("Document not found.");

    var json = File.ReadAllText(metaPath);
    var meta = JsonSerializer.Deserialize<DocumentMeta>(json);
    if (meta == null)
        return Results.NotFound("Metadata not found.");

    var filePath = Path.Combine(uploadsDir, meta.StoredFileName);
    if (!File.Exists(filePath))
        return Results.NotFound("File missing.");

    return Results.File(filePath, "application/octet-stream", meta.OriginalFileName);
});

app.MapGet("/api/documents/{id}/text", async (string id) =>
{
    var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "Uploads");

    var metaPath = Path.Combine(uploadsDir, $"{id}.json");
    if (!File.Exists(metaPath))
        return Results.NotFound("Document not found.");

    var json = await File.ReadAllTextAsync(metaPath);
    var meta = JsonSerializer.Deserialize<DocumentMeta>(json);
    if (meta == null)
        return Results.NotFound("Metadata not found.");

    var filePath = Path.Combine(uploadsDir, meta.StoredFileName);
    if (!File.Exists(filePath))
        return Results.NotFound("File missing.");

    var ext = Path.GetExtension(meta.OriginalFileName).ToLowerInvariant();

    if (ext is ".txt" or ".md")
    {
        var text = await File.ReadAllTextAsync(filePath);
        return Results.Ok(new { id = meta.Id, text });
    }

    if (ext == ".pdf")
    {
        var sb = new System.Text.StringBuilder();
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine(pageText);
                sb.AppendLine();
            }
        }

        return Results.Ok(new { id = meta.Id, text = sb.ToString() });
    }

    return Results.BadRequest("Text extraction not supported for this file type.");
});

app.MapGet("/api/documents/{id}/chunks", async (string id, int? maxChars, int? overlap) =>
{
    var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "Uploads");

    var metaPath = Path.Combine(uploadsDir, $"{id}.json");
    if (!File.Exists(metaPath))
        return Results.NotFound("Document not found.");

    var json = await File.ReadAllTextAsync(metaPath);
    var meta = JsonSerializer.Deserialize<DocumentMeta>(json);
    if (meta == null)
        return Results.NotFound("Metadata not found.");

    var filePath = Path.Combine(uploadsDir, meta.StoredFileName);
    if (!File.Exists(filePath))
        return Results.NotFound("File missing.");

    var ext = Path.GetExtension(meta.OriginalFileName).ToLowerInvariant();

    string text;

    if (ext is ".txt" or ".md")
    {
        text = await File.ReadAllTextAsync(filePath);
    }
    else if (ext == ".pdf")
    {
        var sb = new System.Text.StringBuilder();
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine(pageText);
                sb.AppendLine();
            }
        }

        text = sb.ToString();
    }
    else
    {
        return Results.BadRequest("Chunking not supported for this file type.");
    }

    var chunkSize = maxChars.GetValueOrDefault(1200);
    var chunkOverlap = overlap.GetValueOrDefault(150);

    if (chunkSize < 200) chunkSize = 200;
    if (chunkOverlap < 0) chunkOverlap = 0;
    if (chunkOverlap >= chunkSize) chunkOverlap = Math.Max(0, chunkSize / 4);

    var cleaned = NormalizeText(text);
    var chunks = ChunkText(cleaned, chunkSize, chunkOverlap)
        .Select((t, i) => new ChunkDto(i, t))
        .ToArray();

    return Results.Ok(new { id = meta.Id, chunks });
});

app.Run();

static string NormalizeText(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return string.Empty;

    var s = input.Replace("\r\n", "\n").Replace("\r", "\n");

    s = s.Replace("|", " | ");
    s = s.Replace("•", " • ");

    s = s.Replace("\n\n", "__PARA__");
    s = s.Replace("\n", " ").Replace("\t", " ");

    s = Regex.Replace(s, @"\s{2,}", " ");

    s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ");
    s = Regex.Replace(s, @"(?<=[A-Za-z])(?=\d)", " ");
    s = Regex.Replace(s, @"(?<=\d)(?=[A-Za-z])", " ");

    s = Regex.Replace(s, @"([.,:;!?])(?=\S)", "$1 ");

    s = s.Replace("__PARA__", "\n\n");

    s = s.Replace(" \n\n", "\n\n").Replace("\n\n ", "\n\n");
    s = Regex.Replace(s, @"\s+\|", " |");
    s = Regex.Replace(s, @"\|\s+", "| ");

    return s.Trim();
}


static IEnumerable<string> ChunkText(string text, int maxChars, int overlap)
{
    if (string.IsNullOrWhiteSpace(text))
        yield break;

    var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Trim())
        .Where(p => p.Length > 0)
        .ToArray();

    var result = new List<string>();
    var current = new System.Text.StringBuilder();

    foreach (var p in paragraphs)
    {
        if (current.Length == 0)
        {
            if (p.Length <= maxChars)
            {
                current.Append(p);
                continue;
            }

            for (int i = 0; i < p.Length; i += (maxChars - overlap))
            {
                var len = Math.Min(maxChars, p.Length - i);
                var chunk = p.Substring(i, len).Trim();
                if (chunk.Length > 0) result.Add(chunk);
                if (len < maxChars) break;
            }

            continue;
        }

        if (current.Length + 2 + p.Length <= maxChars)
        {
            current.Append("\n\n").Append(p);
        }
        else
        {
            var prevChunk = current.ToString().Trim();
            if (prevChunk.Length > 0) result.Add(prevChunk);

            var ov = Math.Min(overlap, prevChunk.Length);
            var prefix = ov > 0 ? prevChunk.Substring(prevChunk.Length - ov) : string.Empty;

            current.Clear();
            if (!string.IsNullOrEmpty(prefix))
                current.Append(prefix).Append("\n\n");

            if (p.Length <= maxChars - current.Length)
            {
                current.Append(p);
            }
            else
            {
                var room = Math.Max(0, maxChars - current.Length);
                var take = Math.Min(room, p.Length);

                var first = (current.ToString() + p.Substring(0, take)).Trim();
                if (first.Length > 0) result.Add(first);

                var remaining = p.Substring(take);
                for (int i = 0; i < remaining.Length; i += (maxChars - overlap))
                {
                    var len = Math.Min(maxChars, remaining.Length - i);
                    var chunk = remaining.Substring(i, len).Trim();
                    if (chunk.Length > 0) result.Add(chunk);
                    if (len < maxChars) break;
                }

                current.Clear();
            }
        }
    }

    var last = current.ToString().Trim();
    if (last.Length > 0) result.Add(last);

    foreach (var c in result)
        yield return c;
}

public sealed record ChatRequest(string Message);

public sealed record DocumentMeta(
    string Id,
    string OriginalFileName,
    string StoredFileName,
    long SizeBytes,
    DateTime UploadedAtUtc
);

public sealed record ChunkDto(int Index, string Text);
