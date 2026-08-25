namespace MssqlBackup.Web.Models;

public class BackupJobDto
{
    public Guid Id { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int TotalDatabases { get; set; }
    public int CompletedCount { get; set; }
    public int FailedCount { get; set; }
    public string? CurrentDatabase { get; set; }
    public string? CurrentStep { get; set; }
    public string? Message { get; set; }
}
