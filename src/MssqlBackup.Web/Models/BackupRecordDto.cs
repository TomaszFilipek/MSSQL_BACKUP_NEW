namespace MssqlBackup.Web.Models;

public class BackupRecordDto
{
    public int Id { get; set; }
    public string EnvironmentName { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long FileSizeBeforeCompression { get; set; }
    public long FileSizeAfterCompression { get; set; }
    public DateTime BackupDate { get; set; }
    public bool Compress { get; set; }
    public bool Verify { get; set; }
    public TimeSpan Duration { get; set; }
}

public class BackupFilter
{
    public string? Environment { get; set; }
    public string? Instance { get; set; }
    public string? Database { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}
