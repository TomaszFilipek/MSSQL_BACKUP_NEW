using Microsoft.EntityFrameworkCore;
using MssqlBackup.Api.Models;

namespace MssqlBackup.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<BackupRecord> BackupRecords { get; set; } = null!;
    public DbSet<BackupJob> BackupJobs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BackupRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EnvironmentName).HasMaxLength(100);
            entity.Property(e => e.InstanceName).HasMaxLength(200);
            entity.Property(e => e.DatabaseName).HasMaxLength(200);
            entity.Property(e => e.BackupType).HasMaxLength(50);
            entity.Property(e => e.OutputFilePath).HasMaxLength(500);
            entity.HasIndex(e => e.BackupDate);
            entity.HasIndex(e => new { e.EnvironmentName, e.DatabaseName });
        });

        modelBuilder.Entity<BackupJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EnvironmentName).HasMaxLength(100);
            entity.Property(e => e.InstanceName).HasMaxLength(200);
            entity.Property(e => e.HostName).HasMaxLength(200);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.ServerName).HasMaxLength(200);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UpdatedAt);
            entity.HasIndex(e => new { e.EnvironmentName, e.InstanceName });
        });
    }
}
