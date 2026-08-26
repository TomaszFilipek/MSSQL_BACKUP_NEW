namespace MssqlBackup.Api.Models;

public class RegisteredDatabase
{
    public int Id { get; set; }

    public string EnvironmentName { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Stable ID = Env|Instance|Database lowercased - for linking</summary>
    public string DatabaseKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public bool IsActive { get; set; } = true;
}

public class DatabaseSyncRequest
{
    public string EnvironmentName { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public List<string> DatabaseNames { get; set; } = [];
}

public class DatabaseWithBackupDto
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

    // Last backup info (left join)
    public DateTime? LastBackupDate { get; set; }
    public string? LastBackupType { get; set; }
    public long? LastFileSize { get; set; }
    public TimeSpan? LastDuration { get; set; }
    public int? LastBackupId { get; set; }
}
