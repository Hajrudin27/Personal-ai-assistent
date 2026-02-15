using Microsoft.EntityFrameworkCore;
using PersonalAiAssistant.Api.Models;

namespace PersonalAiAssistant.Api.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<DocumentEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OriginalFileName).IsRequired();
            e.Property(x => x.StoredFileName).IsRequired();
            e.Property(x => x.UploadedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ChunkEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).IsRequired();

            e.HasOne(x => x.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(x => x.Embedding).HasColumnType("vector");
        });
    }
}
