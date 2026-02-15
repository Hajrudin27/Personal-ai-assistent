using System.Text.Json;

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

app.Run();

public sealed record ChatRequest(string Message);

public sealed record DocumentMeta(
    string Id,
    string OriginalFileName,
    string StoredFileName,
    long SizeBytes,
    DateTime UploadedAtUtc
);
