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

// ---- Chat ----
app.MapPost("/api/chat", (ChatRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest("Message is required.");

    return Results.Ok(new { reply = $"You said: {request.Message}" });
});

// ---- Documents: Upload ----
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

    await using var stream = File.Create(storedPath);
    await file.CopyToAsync(stream);

    return Results.Ok(new
    {
        id = docId,
        originalFileName,
        storedFileName = storedName,
        sizeBytes = file.Length
    });
})
.Accepts<IFormFile>("multipart/form-data")
.DisableAntiforgery();

app.MapGet("/api/documents", () =>
{
    var uploadsDir = Path.Combine(app.Environment.ContentRootPath, "Uploads");

    if (!Directory.Exists(uploadsDir))
        return Results.Ok(Array.Empty<object>());

    var files = Directory.GetFiles(uploadsDir);

    var result = files.Select(path =>
    {
        var fileInfo = new FileInfo(path);

        return new
        {
            storedFileName = fileInfo.Name,
            sizeBytes = fileInfo.Length
        };
    });

    return Results.Ok(result);
});


app.Run();

public sealed record ChatRequest(string Message);
