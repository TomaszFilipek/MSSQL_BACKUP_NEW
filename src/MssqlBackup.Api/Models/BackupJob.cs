namespace MssqlBackup.Api.Models;

public class BackupJobDatabaseInfo
{
    public string DatabaseName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed
    public long FileSize { get; set; }
    public string? ErrorMessage { get; set; }
    public double DurationSeconds { get; set; }
}

public class BackupJob
{
    public Guid Id { get; set; }

    public string EnvironmentName { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;

    public string HostName { get; set; } = string.Empty;

    public string Status { get; set; } = "Running"; // Running, Completed, CompletedWithErrors, Failed

    public DateTime StartedAt { get; set; } // UTC

    public DateTime? FinishedAt { get; set; } // UTC

    public DateTime UpdatedAt { get; set; } // UTC

    public int TotalDatabases { get; set; }

    public int CompletedCount { get; set; }

    public int FailedCount { get; set; }

    public string? CurrentDatabase { get; set; }

    public string? CurrentStep { get; set; }

    public string? Message { get; set; }

    public string BackupType { get; set; } = "Full"; // Full | Differential

    // Multi-server run info (x / TotalServers)
    public string? ServerName { get; set; }

    public int TotalServers { get; set; } = 1;

    public int ServerIndex { get; set; } = 1;

    public List<BackupJobDatabaseInfo> Databases { get; set; } = [];
}
