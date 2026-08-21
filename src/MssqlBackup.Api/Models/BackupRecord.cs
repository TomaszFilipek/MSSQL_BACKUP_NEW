using System.ComponentModel.DataAnnotations;

namespace MssqlBackup.Api.Models;

public class BackupRecord
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public required string EnvironmentName { get; set; }

    [Required]
    [MaxLength(200)]
    public required string InstanceName { get; set; }

    [Required]
    [MaxLength(200)]
    public required string DatabaseName { get; set; }

    [Required]
    [MaxLength(50)]
    public required string BackupType { get; set; }

    [Required]
    [MaxLength(500)]
    public required string OutputFilePath { get; set; }

    public long FileSize { get; set; }

    public DateTime BackupDate { get; set; }

    public bool Compress { get; set; }

    public bool Verify { get; set; }

    public TimeSpan Duration { get; set; }
}
