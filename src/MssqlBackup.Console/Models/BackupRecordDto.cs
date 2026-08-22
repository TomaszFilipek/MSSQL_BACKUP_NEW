namespace MssqlBackup.Console.Models;

public class BackupRecordDto
{
    public string EnvironmentName { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime BackupDate { get; set; }
    public bool Compress { get; set; }
    public bool Verify { get; set; }
    public TimeSpan Duration { get; set; }
}
