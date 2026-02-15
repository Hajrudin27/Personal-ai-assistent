using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using PersonalAiAssistant.Api.Data;
using PersonalAiAssistant.Api.Models;
using PersonalAiAssistant.Api.Services;
using Pgvector;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<GeminiEmbeddingService>();
builder.Services.AddHttpClient<OllamaChatService>(c =>
{
    c.BaseAddress = new Uri("http://localhost:11434");
});

var connStr = builder.Configuration.GetConnectionString("Db");
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connStr, o => o.UseVector()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapPost("/api/chat", async (ChatRequest request, AppDbContext db, GeminiEmbeddingService emb, OllamaChatService ollama) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest("Message is required.");

    var qVec = await emb.EmbedAsync(request.Message);

    var candidates = await db.Chunks
        .Where(c => c.Embedding != null)
        .OrderByDescending(c => c.CreatedAtUtc)
        .Take(500)
        .Select(c => new { c.Text, c.Embedding })
        .ToListAsync();

    static double Dot(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double sum = 0;
        for (var i = 0; i < n; i++) sum += (double)a[i] * b[i];
        return sum;
    }

    static double Norm(float[] a)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += (double)a[i] * a[i];
        return Math.Sqrt(sum);
    }

    var qNorm = Norm(qVec);

    var topContexts = candidates
        .Select(c =>
        {
            var vec = c.Embedding!.ToArray();
            var score = qNorm == 0 ? 0 : Dot(qVec, vec) / (qNorm * (Norm(vec) + 1e-12));
            return new { c.Text, score };
        })
        .OrderByDescending(x => x.score)
        .Take(5)
        .Select(x => x.Text)
        .ToList();

    var system = BuildSystemPrompt(topContexts);
    var user = BuildUserPrompt(request.Message);

    var answer = await ollama.ChatAsync(system, user);
    return Results.Ok(new { reply = answer, used = "ollama", context = topContexts });
});

app.MapPost("/api/documents", async (IFormFile file, AppDbContext db, IWebHostEnvironment env) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest("File is required.");

    var uploadsDir = Path.Combine(env.ContentRootPath, "Uploads");
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

    var doc = new DocumentEntity
    {
        Id = docId,
        OriginalFileName = originalFileName,
        StoredFileName = storedName,
        SizeBytes = file.Length,
        UploadedAtUtc = DateTime.UtcNow
    };

    db.Documents.Add(doc);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        id = doc.Id,
        originalFileName = doc.OriginalFileName,
        storedFileName = doc.StoredFileName,
        sizeBytes = doc.SizeBytes,
        uploadedAtUtc = doc.UploadedAtUtc
    });
})
.Accepts<IFormFile>("multipart/form-data")
.DisableAntiforgery();

app.MapGet("/api/documents", async (AppDbContext db) =>
{
    var docs = await db.Documents
        .OrderByDescending(d => d.UploadedAtUtc)
        .Select(d => new
        {
            id = d.Id,
            originalFileName = d.OriginalFileName,
            storedFileName = d.StoredFileName,
            sizeBytes = d.SizeBytes,
            uploadedAtUtc = d.UploadedAtUtc
        })
        .ToListAsync();

    return Results.Ok(docs);
});

app.MapGet("/api/documents/{id}/download", async (string id, AppDbContext db, IWebHostEnvironment env) =>
{
    var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
    if (doc == null)
        return Results.NotFound("Document not found.");

    var uploadsDir = Path.Combine(env.ContentRootPath, "Uploads");
    var filePath = Path.Combine(uploadsDir, doc.StoredFileName);

    if (!File.Exists(filePath))
        return Results.NotFound("File missing.");

    return Results.File(filePath, "application/octet-stream", doc.OriginalFileName);
});

app.MapGet("/api/documents/{id}/text", async (string id, AppDbContext db, IWebHostEnvironment env) =>
{
    var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
    if (doc == null)
        return Results.NotFound("Document not found.");

    var uploadsDir = Path.Combine(env.ContentRootPath, "Uploads");
    var filePath = Path.Combine(uploadsDir, doc.StoredFileName);

    if (!File.Exists(filePath))
        return Results.NotFound("File missing.");

    var ext = Path.GetExtension(doc.OriginalFileName).ToLowerInvariant();

    if (ext is ".txt" or ".md")
    {
        var text = await File.ReadAllTextAsync(filePath);
        return Results.Ok(new { id = doc.Id, text });
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

        return Results.Ok(new { id = doc.Id, text = sb.ToString() });
    }

    return Results.BadRequest("Text extraction not supported for this file type.");
});

app.MapGet("/api/documents/{id}/chunks", async (string id, int? maxChars, int? overlap, AppDbContext db, IWebHostEnvironment env) =>
{
    var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
    if (doc == null)
        return Results.NotFound("Document not found.");

    var uploadsDir = Path.Combine(env.ContentRootPath, "Uploads");
    var filePath = Path.Combine(uploadsDir, doc.StoredFileName);

    if (!File.Exists(filePath))
        return Results.NotFound("File missing.");

    var ext = Path.GetExtension(doc.OriginalFileName).ToLowerInvariant();

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

    return Results.Ok(new { id = doc.Id, chunks });
});

app.MapPost("/api/documents/{id}/ingest", async (string id, int? maxChars, int? overlap, AppDbContext db, IWebHostEnvironment env, GeminiEmbeddingService emb) =>
{
    var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
    if (doc == null)
        return Results.NotFound("Document not found.");

    var uploadsDir = Path.Combine(env.ContentRootPath, "Uploads");
    var filePath = Path.Combine(uploadsDir, doc.StoredFileName);

    if (!File.Exists(filePath))
        return Results.NotFound("File missing.");

    var ext = Path.GetExtension(doc.OriginalFileName).ToLowerInvariant();

    string text;

    if (ext is ".txt" or ".md")
    {
        text = await File.ReadAllTextAsync(filePath);
    }
    else if (ext == ".pdf")
    {
        var sb = new System.Text.StringBuilder();
        using var pdf = PdfDocument.Open(filePath);
        foreach (var page in pdf.GetPages())
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
        return Results.BadRequest("Ingest not supported for this file type.");
    }

    var chunkSize = maxChars.GetValueOrDefault(1200);
    var chunkOverlap = overlap.GetValueOrDefault(150);

    if (chunkSize < 200) chunkSize = 200;
    if (chunkOverlap < 0) chunkOverlap = 0;
    if (chunkOverlap >= chunkSize) chunkOverlap = Math.Max(0, chunkSize / 4);

    var cleaned = NormalizeText(text);
    var chunkTexts = ChunkText(cleaned, chunkSize, chunkOverlap).ToList();

    var existing = db.Chunks.Where(c => c.DocumentId == doc.Id);
    db.Chunks.RemoveRange(existing);

    var now = DateTime.UtcNow;

    for (var i = 0; i < chunkTexts.Count; i++)
    {
        var vector = await emb.EmbedAsync(chunkTexts[i]);

        db.Chunks.Add(new ChunkEntity
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Index = i,
            Text = chunkTexts[i],
            CreatedAtUtc = now,
            Embedding = new Vector(vector)
        });
    }

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        id = doc.Id,
        chunksStored = chunkTexts.Count
    });
});

app.MapGet("/api/documents/{id}/chunks/stored", async (string id, AppDbContext db) =>
{
    var docExists = await db.Documents.AnyAsync(d => d.Id == id);
    if (!docExists)
        return Results.NotFound("Document not found.");

    var chunks = await db.Chunks
        .Where(c => c.DocumentId == id)
        .OrderBy(c => c.Index)
        .Select(c => new { c.Index, c.Text })
        .ToListAsync();

    return Results.Ok(new { id, chunks });
});

app.MapGet("/api/documents/{id}/chunks/db", async (string id, AppDbContext db) =>
{
    var rows = await db.Chunks
        .Where(c => c.DocumentId == id)
        .OrderBy(c => c.Index)
        .Select(c => new { c.Index, hasEmbedding = c.Embedding != null, dims = c.Embedding == null ? 0 : c.Embedding.ToArray().Length })
        .Take(10)
        .ToListAsync();

    return Results.Ok(rows);
});

app.MapGet("/api/embeddings/test", async (GeminiEmbeddingService emb) =>
{
    var v = await emb.EmbedAsync("hello from embeddings test");
    return Results.Ok(new { dims = v.Length, first = v.Take(5).ToArray() });
});

app.MapGet("/api/search", async (string q, int? k, AppDbContext db, GeminiEmbeddingService emb) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest("Query parameter 'q' is required.");

    var topK = Math.Clamp(k ?? 5, 1, 20);

    var qVec = await emb.EmbedAsync(q);

    var candidates = await db.Chunks
        .Where(c => c.Embedding != null)
        .OrderByDescending(c => c.CreatedAtUtc)
        .Take(500)
        .Select(c => new { c.DocumentId, c.Index, c.Text, c.Embedding })
        .ToListAsync();

    static double Dot(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double sum = 0;
        for (var i = 0; i < n; i++) sum += (double)a[i] * b[i];
        return sum;
    }

    static double Norm(float[] a)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += (double)a[i] * a[i];
        return Math.Sqrt(sum);
    }

    var qNorm = Norm(qVec);
    if (qNorm == 0) return Results.Ok(Array.Empty<object>());

    var scored = candidates
        .Select(c =>
        {
            var vec = c.Embedding!.ToArray();
            var score = Dot(qVec, vec) / (qNorm * (Norm(vec) + 1e-12));
            return new { c.DocumentId, c.Index, c.Text, score };
        })
        .OrderByDescending(x => x.score)
        .Take(topK)
        .Select(x => new
        {
            x.DocumentId,
            x.Index,
            score = Math.Round(x.score, 4),
            preview = x.Text.Length > 240 ? x.Text.Substring(0, 240) + "…" : x.Text
        })
        .ToList();

    return Results.Ok(scored);
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

static string BuildSystemPrompt(IEnumerable<string> contexts)
{
    var ctxList = contexts
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select((s, i) => $"SOURCE {i + 1}:\n{s.Trim()}")
        .ToArray();

    var ctx = ctxList.Length == 0 ? "(no sources)" : string.Join("\n\n", ctxList);

    return
$@"You are a helpful assistant.
You must answer using ONLY the SOURCES below.
If the user asks what the document says, you should repeat/quote the most relevant source.
If the answer is not present, reply exactly: I don't know.

Example:
SOURCES:
SOURCE 1:
This is a test.

User: What does the document say?
Assistant: The document says: ""This is a test."" 
Sources used: SOURCE 1

Now follow the same pattern.

SOURCES:
{ctx}";
}


static string BuildUserPrompt(string userMessage)
{
    return
$@"Question: {userMessage}

Answer format:
- Answer in 1 sentence.
- Then: Sources used: SOURCE X";
}

public sealed record ChatRequest(string Message);
public sealed record ChunkDto(int Index, string Text);
