using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Solace.LauncherUI.Models.Db;

namespace Solace.LauncherUI.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
{
    public DbSet<DbBuildplatePreview> BuildplatePreviews { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DbBuildplatePreview>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.HasIndex(e => new { e.PlayerId, e.BuildplateId })
                .HasDatabaseName("IX_Player_Buildplate")
                .IsUnique();

            entity.Property(e => e.PreviewData)
                .IsRequired()
                .HasColumnType("BLOB");
        });
    }
}
