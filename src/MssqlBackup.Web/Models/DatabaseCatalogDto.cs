namespace MssqlBackup.Web.Models;

public class DatabaseCatalogDto
{
    public int Id { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public bool IsActive { get; set; }

    public DateTime? LastBackupDate { get; set; }
    public string? LastBackupType { get; set; }
    public long? LastFileSize { get; set; }
    public TimeSpan? LastDuration { get; set; }
    public int? LastBackupId { get; set; }
}

public class DatabaseCatalogFilter
{
    public string? Environment { get; set; }
    public string? Server { get; set; }
    public string? Instance { get; set; }
    public string? Database { get; set; }
    public bool? IsActive { get; set; }
}
